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

            // 株価・為替（Yahoo）と投信基準価額（投信協会）を並行取得（少数前提）。
            var priceTasks = symbols.Select(async s => (Key: s.ToUpperInvariant(), Price: await FetchPriceAsync(s))).ToList();
            var fundTasks = funds.Select(async f => (Key: f.AssocFundCd.Trim().ToUpperInvariant(), Price: await FetchFundPriceAsync(f.Isin?.Trim() ?? "", f.AssocFundCd.Trim()))).ToList();
            var rateTask = FetchPriceAsync(RateSymbol);
            await Task.WhenAll(priceTasks.Cast<Task>().Concat(fundTasks.Cast<Task>()).Append(rateTask));

            foreach (var t in priceTasks)
            {
                var (key, price) = t.Result;
                if (price is > 0) result.Prices[key] = price.Value;
            }
            foreach (var t in fundTasks)
            {
                var (key, price) = t.Result;
                if (price is > 0) result.FundPrices[key] = price.Value;
            }
            if (rateTask.Result is > 0) result.UsdJpyRate = rateTask.Result!.Value;

            return new OkObjectResult(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetQuote failed");
            return new StatusCodeResult(StatusCodes.Status502BadGateway);
        }
    }

    // Yahoo Finance chart API から現在値を取得。取得不能は null。
    private static async Task<decimal?> FetchPriceAsync(string symbol)
    {
        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&range=1d";
            using var resp = await QuoteHttp.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;
            if (!root.TryGetProperty("chart", out var chart)) return null;
            if (!chart.TryGetProperty("result", out var arr)
                || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) return null;
            if (!arr[0].TryGetProperty("meta", out var meta)) return null;
            if (meta.TryGetProperty("regularMarketPrice", out var p) && p.ValueKind == JsonValueKind.Number)
                return p.GetDecimal();
            return null;
        }
        catch
        {
            return null;   // 1銘柄の失敗で全体を落とさない
        }
    }

    // 投信協会（投信総合検索ライブラリ）の基準価額CSV から最新の基準価額（円・1万口あたり）を取得。
    // CSV は Shift-JIS だが日付列以外は ASCII 数字。Shift-JIS の2バイト目にカンマ(0x2C)は現れないため
    // Latin1（バイト1:1）でデコードしてカンマ分割すれば基準価額列(index 1)を安全に取り出せる。
    private static async Task<decimal?> FetchFundPriceAsync(string isin, string assocFundCd)
    {
        try
        {
            // ISIN未指定（協会コードのみ登録）の銘柄は既知の有効ISINで代替（価格は協会コードで決まる）。
            if (string.IsNullOrWhiteSpace(isin)) isin = FallbackIsin;
            var url = $"https://toushin-lib.fwg.ne.jp/FdsWeb/FDST030000/csv-file-download" +
                      $"?isinCd={Uri.EscapeDataString(isin)}&associFundCd={Uri.EscapeDataString(assocFundCd)}";
            using var resp = await QuoteHttp.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var text = System.Text.Encoding.Latin1.GetString(bytes);

            decimal? latest = null;   // CSVは古い順なので最後の有効行を採用
            bool first = true;
            foreach (var line in text.Split('\n'))
            {
                if (first) { first = false; continue; }   // ヘッダ
                var cols = line.Trim().Split(',');
                if (cols.Length < 2) continue;
                if (decimal.TryParse(cols[1].Trim(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0)
                    latest = v;
            }
            return latest;
        }
        catch
        {
            return null;
        }
    }
}
