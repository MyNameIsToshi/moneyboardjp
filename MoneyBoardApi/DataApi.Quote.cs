using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using MoneyBoardShared;

namespace MoneyBoardApi;

// 価格取得プロキシ（Yahoo Finance）。承認ユーザーのみ通す。株価＋USD/JPYレートを返す。
// クライアントから直接叩くと CORS で弾かれるためサーバー側でプロキシする。
// （旧 Stooq は JavaScript ボット検証を挟むようになりサーバー取得不可になったため Yahoo に移行）
public partial class DataApi
{
    // Functions Isolated ではソケット枯渇を避けるため HttpClient は使い回す。
    // User-Agent 無しだと弾かれることがあるためブラウザ風 UA を付ける。
    private static readonly HttpClient QuoteHttp = CreateQuoteHttp();
    private const string RateSymbol = "JPY=X";   // Yahoo の USD/JPY（1ドル＝何円）
    // 投信協会CSVの基準価額は「協会コード」だけで一意に決まる。ただしISINは有効な実在値が必須
    // （ダミーだと500）。協会コードが分かればこの既知の有効ISINを補完用に使う（オルカン・検証済み）。
    private const string FallbackIsin = "JP90C000H1T1";

    private static HttpClient CreateQuoteHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        return c;
    }

    // 市場指標バー（#26）の確定5本に準拠（表示用の定義は Portfolio.razor.cs の MarketIndices）。
    // ⚠️ TOPIX は対象外：Yahoo v8 が TOPIX 指数を配信していないため（詳細は同箇所のコメント参照）。
    private static readonly (string Symbol, string Label)[] MarketIndices =
    {
        ("^DJI", "NYダウ"),
        ("^IXIC", "ナスダック"),
        ("^GSPC", "S&P500"),
        ("^N225", "日経平均"),
        ("^KS11", "KOSPI"),
    };

    // GET /api/market-summary → 公的な市場データ（指数＋USD/JPY）のみを返す（個人ポートフォリオは含まない＝#48 で分離）。
    // 認証はユーザー JWT ゲートとは別の共有シークレット（ヘッダー X-Internal-Secret）。日報スキル等の外部連携専用。
    [Function("GetMarketSummary")]
    public async Task<IActionResult> GetMarketSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "market-summary")] HttpRequest req)
    {
        if (!IsAuthorizedSharedSecret(Environment.GetEnvironmentVariable("InternalApi__SharedSecret"), req.Headers["X-Internal-Secret"]))
            return new UnauthorizedResult();

        try
        {
            var indexTasks = MarketIndices.Select(async ix => (ix, Q: await FetchPriceAsync(ix.Symbol))).ToList();
            var rateTask = FetchPriceAsync(RateSymbol);
            await Task.WhenAll(indexTasks.Cast<Task>().Append(rateTask));

            var result = new MarketSummaryResponse { At = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") };
            foreach (var t in indexTasks)
            {
                var (ix, q) = t.Result;
                if (q.Price is not > 0) continue;
                result.Indices.Add(new MarketIndexInfo { Symbol = ix.Symbol, Label = ix.Label, Value = q.Price.Value, PrevClose = q.Prev });
            }
            if (rateTask.Result.Price is > 0) result.UsdJpyRate = rateTask.Result.Price.Value;

            return new OkObjectResult(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetMarketSummary failed");
            return new StatusCodeResult(StatusCodes.Status502BadGateway);
        }
    }

    // 共有シークレットの比較（定数時間比較でタイミング攻撃を避ける）。internal=テストから検証。
    // #37/#48 も同じ仕組み（環境変数 InternalApi__SharedSecret・ヘッダー X-Internal-Secret）を再利用する想定。
    internal static bool IsAuthorizedSharedSecret(string? expected, string? provided)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided)) return false;
        var a = System.Text.Encoding.UTF8.GetBytes(expected);
        var b = System.Text.Encoding.UTF8.GetBytes(provided);
        return a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    // POST /api/quote → シンボル一覧の現在値（ネイティブ通貨）＋USD/JPYレートを返す。
    // シンボルは Yahoo 形式（日本株 "7203.T" / 米国株 "AAPL" / 為替 "JPY=X"）。
    [Function("GetQuote")]
    public async Task<IActionResult> GetQuote(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "quote")] HttpRequest req)
    {
        try
        {
            var container = GetContainer();
            var (_, _, authError) = await AuthorizeAsync(container, req);
            if (authError is not null) return authError;

            var body = await ReadBodyCappedAsync(req.Body);
            if (body == null) return new StatusCodeResult(StatusCodes.Status413RequestEntityTooLarge);
            var reqData = JsonSerializer.Deserialize<QuoteRequest>(body, JsonOptions) ?? new QuoteRequest();

            var symbols = reqData.Symbols
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxHoldings)
                .ToList();
            var funds = reqData.Funds
                .Where(f => !string.IsNullOrWhiteSpace(f.AssocFundCd))
                .GroupBy(f => f.AssocFundCd.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(MaxHoldings)
                .ToList();

            var result = new QuoteResponse { At = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") };

            // 株価・為替（Yahoo）と投信基準価額（投信協会）を並行取得（少数前提）。現在値＋前日終値を併せて取る。
            var priceTasks = symbols.Select(async s => (Key: s.ToUpperInvariant(), Q: await FetchPriceAsync(s))).ToList();
            var fundTasks = funds.Select(async f => (Key: f.AssocFundCd.Trim().ToUpperInvariant(), Q: await FetchFundPriceAsync(f.Isin?.Trim() ?? "", f.AssocFundCd.Trim()))).ToList();
            var rateTask = FetchPriceAsync(RateSymbol);
            await Task.WhenAll(priceTasks.Cast<Task>().Concat(fundTasks.Cast<Task>()).Append(rateTask));

            foreach (var t in priceTasks)
            {
                var (key, q) = t.Result;
                if (q.Price is > 0) result.Prices[key] = q.Price.Value;
                if (q.Prev is > 0) result.PrevClose[key] = q.Prev.Value;
            }
            foreach (var t in fundTasks)
            {
                var (key, q) = t.Result;
                if (q.Latest is > 0) result.FundPrices[key] = q.Latest.Value;
                if (q.Prev is > 0) result.FundPrevClose[key] = q.Prev.Value;
            }
            if (rateTask.Result.Price is > 0) result.UsdJpyRate = rateTask.Result.Price.Value;

            return new OkObjectResult(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetQuote failed");
            return new StatusCodeResult(StatusCodes.Status502BadGateway);
        }
    }

    // Yahoo Finance chart API から現在値＋前日終値を取得。取得不能は null。
    private static async Task<(decimal? Price, decimal? Prev)> FetchPriceAsync(string symbol)
    {
        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&range=1d";
            using var resp = await QuoteHttp.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return (null, null);
            var json = await resp.Content.ReadAsStringAsync();
            return ParseYahooQuote(json);
        }
        catch
        {
            return (null, null);   // 1銘柄の失敗で全体を落とさない
        }
    }

    // Yahoo chart レスポンス(JSON)から現在値＋前日終値を抽出する純粋ロジック。internal=テストから検証。
    // 前日終値は chartPreviousClose（無ければ previousClose）。欠落・不正JSONは null。
    internal static (decimal? Price, decimal? Prev) ParseYahooQuote(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("chart", out var chart)) return (null, null);
            if (!chart.TryGetProperty("result", out var arr)
                || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) return (null, null);
            if (!arr[0].TryGetProperty("meta", out var meta)) return (null, null);
            decimal? price = null, prev = null;
            if (meta.TryGetProperty("regularMarketPrice", out var p) && p.ValueKind == JsonValueKind.Number)
                price = p.GetDecimal();
            if (meta.TryGetProperty("chartPreviousClose", out var pc) && pc.ValueKind == JsonValueKind.Number)
                prev = pc.GetDecimal();
            else if (meta.TryGetProperty("previousClose", out var pc2) && pc2.ValueKind == JsonValueKind.Number)
                prev = pc2.GetDecimal();
            return (price, prev);
        }
        catch
        {
            return (null, null);
        }
    }

    // 投信協会（投信総合検索ライブラリ）の基準価額CSV から最新の基準価額（円・1万口あたり）を取得。
    // CSV は Shift-JIS だが日付列以外は ASCII 数字。Shift-JIS の2バイト目にカンマ(0x2C)は現れないため
    // Latin1（バイト1:1）でデコードしてカンマ分割すれば基準価額列(index 1)を安全に取り出せる。
    private static async Task<(decimal? Latest, decimal? Prev)> FetchFundPriceAsync(string isin, string assocFundCd)
    {
        try
        {
            // ISIN未指定（協会コードのみ登録）の銘柄は既知の有効ISINで代替（価格は協会コードで決まる）。
            if (string.IsNullOrWhiteSpace(isin)) isin = FallbackIsin;
            var url = $"https://toushin-lib.fwg.ne.jp/FdsWeb/FDST030000/csv-file-download" +
                      $"?isinCd={Uri.EscapeDataString(isin)}&associFundCd={Uri.EscapeDataString(assocFundCd)}";
            using var resp = await QuoteHttp.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return (null, null);
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var text = System.Text.Encoding.Latin1.GetString(bytes);
            return ParseFundCsv(text);
        }
        catch
        {
            return (null, null);
        }
    }

    // 投信協会CSV(Latin1デコード済みテキスト)から最新＋前営業日の基準価額を抽出する純粋ロジック。internal=テストから検証。
    // CSVは古い順・1行目はヘッダ。基準価額は列[1]。最後の有効行＝最新、その1つ前＝前営業日。
    internal static (decimal? Latest, decimal? Prev) ParseFundCsv(string text)
    {
        decimal? latest = null, prev = null;
        bool first = true;
        foreach (var line in text.Split('\n'))
        {
            if (first) { first = false; continue; }   // ヘッダ
            var cols = line.Trim().Split(',');
            if (cols.Length < 2) continue;
            if (decimal.TryParse(cols[1].Trim(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0)
            {
                prev = latest;
                latest = v;
            }
        }
        return (latest, prev);
    }
}

/// <summary>GET /api/market-summary のレスポンス。公的データのみ（個人ポートフォリオは含まない）。</summary>
public class MarketSummaryResponse
{
    public string At { get; set; } = "";   // 取得時刻（UTC・"yyyy-MM-dd HH:mm"）
    public decimal UsdJpyRate { get; set; }
    public List<MarketIndexInfo> Indices { get; set; } = new();
}

/// <summary>市場指数1本の現況。取得失敗分は Indices に含まれない。</summary>
public class MarketIndexInfo
{
    public string Symbol { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
    public decimal? PrevClose { get; set; }
}
