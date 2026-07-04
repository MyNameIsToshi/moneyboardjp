using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using System.Net;
using System.Text.Json;
using MoneyBoardShared;

namespace MoneyBoardApi;

// 証券ポートフォリオの CRUD（家計簿データとは別ドキュメント portfolio・別ルート /api/portfolio）。
// 認証は家計簿と同じ AuthorizeAsync ゲートを通す（承認ユーザーのみ）。
public partial class DataApi
{
    private const string PortfolioId = "portfolio";

    // 構造上の健全性チェック（巨大ペイロード対策。本文サイズ上限とは別の保険）。
    private const int MaxHoldings = 1000;
    private const int MaxLots = 100_000;

    // PortfolioDoc ⇔ PortfolioData の詰め替え（GET/保存/スナップショットAPI で共有し、フィールド追加時のドリフトを防ぐ）。
    // PrevPrices は意図的に非永続（前日終値は価格更新のたびに取り直す）のためどちらにも含めない。
    private static PortfolioData ToData(PortfolioDoc doc) => new()
    {
        SchemaVersion = doc.SchemaVersion,
        Holdings = doc.Holdings, Buys = doc.Buys, Sells = doc.Sells,
        Dividends = doc.Dividends, Snapshots = doc.Snapshots,
        CurrentPrices = doc.CurrentPrices, UsdJpyRate = doc.UsdJpyRate, PricedAt = doc.PricedAt
    };

    private static PortfolioDoc ToDoc(PortfolioData d, string userId) => new()
    {
        Id = PortfolioId, UserId = userId, Type = "portfolio",
        SchemaVersion = d.SchemaVersion,
        Holdings = d.Holdings, Buys = d.Buys, Sells = d.Sells,
        Dividends = d.Dividends, Snapshots = d.Snapshots,
        CurrentPrices = d.CurrentPrices, UsdJpyRate = d.UsdJpyRate, PricedAt = d.PricedAt
    };

    // GET /api/portfolio → ポートフォリオ全体を返す（無ければ空）。
    [Function("GetPortfolio")]
    public async Task<IActionResult> GetPortfolio(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "portfolio")] HttpRequest req)
    {
        try
        {
            var container = GetContainer();
            var (userId, isOwner, authError) = await AuthorizeAsync(container, req);
            if (authError is not null) return authError;
            var pk = new PartitionKey(userId!);
            var env = new PortfolioEnvelope();

            // ESPP UI の表示可否：Owner は常に true、それ以外は access の社員リストに含まれるか。本人ぶんのみ返す。
            if (isOwner) env.IsTsmcEmployee = true;
            else
            {
                var access = await ReadAccessAsync(container);
                env.IsTsmcEmployee = access.TsmcEmployees.Contains(userId!);
            }

            try
            {
                var r = await container.ReadItemAsync<PortfolioDoc>(PortfolioId, pk);
                env.Etag = r.ETag;
                env.Data = ToData(r.Resource);
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                // 空のまま返す（新規ユーザー）
            }

            return new OkObjectResult(env);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetPortfolio failed");
            return new StatusCodeResult(500);
        }
    }

    // POST /api/portfolio → ポートフォリオ全体を保存（If-Match で楽観的並行制御・競合は 412）。
    [Function("SavePortfolio")]
    public async Task<IActionResult> SavePortfolio(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "portfolio")] HttpRequest req)
    {
        try
        {
            var (body, bodyError) = await ReadCappedBodyAsync(req);
            if (bodyError is not null) return bodyError;

            var env = JsonSerializer.Deserialize<PortfolioEnvelope>(body!, JsonOptions);
            if (env == null) return new BadRequestResult();
            var d = env.Data;
            if (d.Holdings.Count > MaxHoldings
                || d.Buys.Count > MaxLots || d.Sells.Count > MaxLots
                || d.Dividends.Count > MaxLots || d.Snapshots.Count > MaxLots)
            {
                logger.LogWarning("SavePortfolio rejected: oversize collections");
                return new BadRequestResult();
            }

            var container = GetContainer();
            var (userId, _, authError) = await AuthorizeAsync(container, req);
            if (authError is not null) return authError;
            var pk = new PartitionKey(userId!);

            var doc = ToDoc(d, userId!);
            var opt = new ItemRequestOptions { EnableContentResponseOnWrite = false };
            if (!string.IsNullOrEmpty(env.Etag)) opt.IfMatchEtag = env.Etag;

            try
            {
                var resp = await container.UpsertItemAsync(doc, pk, opt);
                return new OkObjectResult(new PortfolioSaveResponse { Etag = resp.ETag });
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return new StatusCodeResult(StatusCodes.Status412PreconditionFailed);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SavePortfolio failed");
            return new StatusCodeResult(500);
        }
    }

    // GET /api/portfolio-snapshot-current → 最新価格を取得・当日スナップショットを記録し、
    // 現況（保有銘柄・評価額・含み損益）と推移履歴を返す（日報スキル等の内部 API 専用）。
    // 認証は共有シークレット（X-Internal-Secret）。OwnerUserId 環境変数でオーナーの userId を特定。
    [Function("GetPortfolioSnapshotCurrent")]
    public async Task<IActionResult> GetPortfolioSnapshotCurrent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "portfolio-snapshot-current")] HttpRequest req)
    {
        if (!IsAuthorizedSharedSecret(Environment.GetEnvironmentVariable("InternalApi__SharedSecret"), req.Headers["X-Internal-Secret"]))
            return new UnauthorizedResult();

        var ownerUserId = Environment.GetEnvironmentVariable("OwnerUserId");
        if (string.IsNullOrEmpty(ownerUserId))
        {
            logger.LogError("GetPortfolioSnapshotCurrent: OwnerUserId not configured");
            return new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var container = GetContainer();
            var pk = new PartitionKey(ownerUserId);

            // ポートフォリオドキュメントを読む（存在しなければ空のデータで続行）。
            PortfolioData data = new();
            string? etag = null;
            try
            {
                var r = await container.ReadItemAsync<PortfolioDoc>(PortfolioId, pk);
                etag = r.ETag;
                data = ToData(r.Resource);
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound) { }

            // 株・投信・USD/JPY を並行取得。
            var active = data.Holdings.Where(h => !h.IsDeleted).ToList();
            var stockTasks = active
                .Where(h => h.Class != AssetClass.Fund && !string.IsNullOrEmpty(h.Symbol))
                .Select(async h => (h, P: await FetchPriceAsync(PortfolioMath.YahooSymbol(h))))
                .ToList();
            var fundTasks = active
                .Where(h => h.Class == AssetClass.Fund && !string.IsNullOrEmpty(h.AssocFundCd))
                .Select(async h => (h, P: await FetchFundPriceAsync(h.Isin, h.AssocFundCd)))
                .ToList();
            var rateTask = FetchPriceAsync(RateSymbol);
            await Task.WhenAll(stockTasks.Cast<Task>().Concat(fundTasks.Cast<Task>()).Append(rateTask));

            foreach (var t in stockTasks)
            {
                var (h, p) = t.Result;
                if (p.Price is > 0) data.CurrentPrices[h.Id] = p.Price.Value;
            }
            foreach (var t in fundTasks)
            {
                var (h, p) = t.Result;
                if (p.Latest is > 0) data.CurrentPrices[h.Id] = p.Latest.Value;
            }
            if (rateTask.Result.Price is > 0) data.UsdJpyRate = rateTask.Result.Price.Value;

            var at = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
            data.PricedAt = at;

            // 当日スナップショット記録（同日上書き＝フロントの価格更新と同じ規則）。
            var snap = PortfolioMath.BuildSnapshot(data, at);
            if (snap != null) PortfolioMath.UpsertSnapshot(data, snap);

            // Cosmos に保存。ETag 競合（フロントと同時操作）はスキップ。
            await TrySavePortfolioAsync(container, ownerUserId, data, etag, nameof(GetPortfolioSnapshotCurrent));

            // レスポンス構築。
            var today2 = at[..10];
            decimal totalValuation = 0m;
            decimal totalCost = PortfolioMath.CostBasisJpyAsOf(data, today2, data.UsdJpyRate);
            var holdingInfos = new List<HoldingCurrentInfo>();
            foreach (var h in active)
            {
                var sum = PortfolioMath.Summarize(h, data.Buys, data.Sells, data.Dividends);
                if (sum.Quantity == 0) continue;
                var price = data.CurrentPrices.GetValueOrDefault(h.Id);
                var vJpy = PortfolioMath.ValuationJpy(h, sum.Quantity, price, data.UsdJpyRate);
                if (!vJpy.HasValue) continue;
                var costJpy = PortfolioMath.HoldingCostBasisJpyAsOf(data, h, today2, data.UsdJpyRate);
                totalValuation += vJpy.Value;
                holdingInfos.Add(new HoldingCurrentInfo
                {
                    Name = h.Name,
                    Account = h.Account,
                    Quantity = sum.Quantity,
                    PriceNative = price,
                    ValuationJpy = vJpy.Value,
                    CostBasisJpy = costJpy,
                    UnrealizedPnlJpy = vJpy.Value - costJpy
                });
            }

            var history = data.Snapshots
                .OrderBy(s => s.At)
                .Select(s =>
                {
                    var sDate = s.At.Length >= 10 ? s.At[..10] : s.At;
                    var totalJpy = s.Values.Sum(v => v.ValuationJpy);
                    var costJpy = PortfolioMath.CostBasisJpyAsOf(data, sDate, s.UsdJpyRate);
                    return new SnapshotPnlPoint
                    {
                        At = s.At,
                        UsdJpyRate = s.UsdJpyRate,
                        TotalValuationJpy = totalJpy,
                        CostBasisJpy = costJpy,
                        PnlJpy = totalJpy - costJpy
                    };
                })
                .ToList();

            return new OkObjectResult(new PortfolioCurrentResponse
            {
                PricedAt = at,
                UsdJpyRate = data.UsdJpyRate,
                TotalValuationJpy = totalValuation,
                CostBasisJpy = totalCost,
                UnrealizedPnlJpy = totalValuation - totalCost,
                Holdings = holdingInfos,
                History = history
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetPortfolioSnapshotCurrent failed");
            return new StatusCodeResult(500);
        }
    }

    // POST /api/record-snapshots → 全ユーザーの portfolio ドキュメントに当日スナップショットを1点ずつ記録する。
    // GitHub Actions の cron から毎営業日呼ばれ、画面を開かない日の推移欠測を防ぐ（#37）。SWA Free は
    // managed Functions が HTTP トリガーのみのため Timer トリガーではなくこの方式を採る（ADR 参照）。
    // 認証は共有シークレット（X-Internal-Secret、market-summary/portfolio-snapshot-current と共通）。
    // 同日上書き（PortfolioMath.UpsertSnapshot）のため、手動で画面を開いた記録と二重にはならない。
    [Function("RecordSnapshots")]
    public async Task<IActionResult> RecordSnapshots(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "record-snapshots")] HttpRequest req)
    {
        if (!IsAuthorizedSharedSecret(Environment.GetEnvironmentVariable("InternalApi__SharedSecret"), req.Headers["X-Internal-Secret"]))
            return new UnauthorizedResult();

        try
        {
            var container = GetContainer();

            // 全ユーザーの portfolio ドキュメントを列挙（クロスパーティション）。
            var docs = new List<PortfolioReadDoc>();
            var query = new QueryDefinition("SELECT * FROM c WHERE c.type = 'portfolio'");
            using (var it = container.GetItemQueryIterator<PortfolioReadDoc>(query))
            {
                while (it.HasMoreResults)
                    docs.AddRange(await it.ReadNextAsync());
            }
            if (docs.Count == 0) return new OkObjectResult(new RecordSnapshotsResponse());

            // 価格は全ユーザー分をまとめて重複排除して取得（Yahoo/投信協会への呼び出し回数を抑える）。
            var stockSymbols = docs
                .SelectMany(d => d.Holdings.Where(h => !h.IsDeleted && h.Class != AssetClass.Fund && !string.IsNullOrEmpty(h.Symbol)))
                .Select(PortfolioMath.YahooSymbol)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var fundHoldings = docs
                .SelectMany(d => d.Holdings.Where(h => !h.IsDeleted && h.Class == AssetClass.Fund && !string.IsNullOrEmpty(h.AssocFundCd)))
                .GroupBy(h => h.AssocFundCd.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var stockTasks = stockSymbols.Select(async s => (Symbol: s, Q: await FetchPriceAsync(s))).ToList();
            var fundTasks = fundHoldings.Select(async h => (Key: h.AssocFundCd.Trim(), Q: await FetchFundPriceAsync(h.Isin, h.AssocFundCd))).ToList();
            var rateTask = FetchPriceAsync(RateSymbol);
            await Task.WhenAll(stockTasks.Cast<Task>().Concat(fundTasks.Cast<Task>()).Append(rateTask));

            var stockPrices = stockTasks.Where(t => t.Result.Q.Price is > 0)
                .ToDictionary(t => t.Result.Symbol, t => t.Result.Q.Price!.Value, StringComparer.OrdinalIgnoreCase);
            var fundPrices = fundTasks.Where(t => t.Result.Q.Latest is > 0)
                .ToDictionary(t => t.Result.Key, t => t.Result.Q.Latest!.Value, StringComparer.OrdinalIgnoreCase);
            var usdJpyRate = rateTask.Result.Price;

            var at = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
            int recorded = 0;
            foreach (var doc in docs)
            {
                var data = new PortfolioData
                {
                    SchemaVersion = doc.SchemaVersion,
                    Holdings = doc.Holdings, Buys = doc.Buys, Sells = doc.Sells,
                    Dividends = doc.Dividends, Snapshots = doc.Snapshots,
                    CurrentPrices = doc.CurrentPrices, UsdJpyRate = doc.UsdJpyRate, PricedAt = doc.PricedAt
                };

                foreach (var h in data.Holdings.Where(h => !h.IsDeleted))
                {
                    if (h.Class == AssetClass.Fund)
                    {
                        if (!string.IsNullOrEmpty(h.AssocFundCd) && fundPrices.TryGetValue(h.AssocFundCd.Trim(), out var fp))
                            data.CurrentPrices[h.Id] = fp;
                    }
                    else if (!string.IsNullOrEmpty(h.Symbol) && stockPrices.TryGetValue(PortfolioMath.YahooSymbol(h), out var sp))
                    {
                        data.CurrentPrices[h.Id] = sp;
                    }
                }
                if (usdJpyRate is > 0) data.UsdJpyRate = usdJpyRate.Value;
                data.PricedAt = at;

                // 保有評価額0件（価格未取得含む）は既存 RecordSnapshot と同じく記録しない。
                var snap = PortfolioMath.BuildSnapshot(data, at);
                if (snap == null) continue;
                PortfolioMath.UpsertSnapshot(data, snap);

                // 競合（フロントが同時に価格更新等）はそのユーザーだけスキップ。次回 cron で吸収される。
                if (await TrySavePortfolioAsync(container, doc.UserId, data, doc.Etag, nameof(RecordSnapshots)))
                    recorded++;
            }

            return new OkObjectResult(new RecordSnapshotsResponse { UserCount = docs.Count, RecordedCount = recorded });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RecordSnapshots failed");
            return new StatusCodeResult(500);
        }
    }

    // portfolio ドキュメントを ETag 付きで保存する（GetPortfolioSnapshotCurrent / RecordSnapshots 共通）。
    // 競合(412)はログのみでスキップ可＝戻り値 false（呼び出し元のリトライ方針に委ねる）。
    private async Task<bool> TrySavePortfolioAsync(Container container, string userId, PortfolioData data, string? etag, string logContext)
    {
        var saveDoc = ToDoc(data, userId);
        var saveOpt = new ItemRequestOptions { EnableContentResponseOnWrite = false };
        if (!string.IsNullOrEmpty(etag)) saveOpt.IfMatchEtag = etag;
        try
        {
            await container.UpsertItemAsync(saveDoc, new PartitionKey(userId), saveOpt);
            return true;
        }
        catch (CosmosException e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            logger.LogWarning("{Context}: etag conflict for user {UserId}, skipping", logContext, userId);
            return false;
        }
    }
}

// Cosmos ドキュメント（同一パーティション /userId 内で type="portfolio"）。
public class PortfolioDoc
{
    [Newtonsoft.Json.JsonProperty("id")] public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Type { get; set; } = "portfolio";
    public int SchemaVersion { get; set; } = 1;
    public List<Holding> Holdings { get; set; } = new();
    public List<BuyLot> Buys { get; set; } = new();
    public List<SellLot> Sells { get; set; } = new();
    public List<Dividend> Dividends { get; set; } = new();
    public List<PriceSnapshot> Snapshots { get; set; } = new();
    public Dictionary<string, decimal> CurrentPrices { get; set; } = new();
    public decimal UsdJpyRate { get; set; }
    public string PricedAt { get; set; } = "";
}

/// <summary>GET /api/portfolio-snapshot-current のレスポンス（日報スキル等の内部 API 専用）。</summary>
public class PortfolioCurrentResponse
{
    public string PricedAt { get; set; } = "";          // 価格取得日時（UTC・"yyyy-MM-dd HH:mm"）
    public decimal UsdJpyRate { get; set; }              // 評価額算出に使った USD/JPY レート
    public decimal TotalValuationJpy { get; set; }       // 総資産（評価額合計・円）
    public decimal CostBasisJpy { get; set; }            // 取得原価合計（円）
    public decimal UnrealizedPnlJpy { get; set; }        // 含み損益合計（円）
    public List<HoldingCurrentInfo> Holdings { get; set; } = new();
    public List<SnapshotPnlPoint> History { get; set; } = new();
}

/// <summary>保有銘柄の現況（1件）。</summary>
public class HoldingCurrentInfo
{
    public string Name { get; set; } = "";
    public AccountKind Account { get; set; }
    public decimal Quantity { get; set; }
    public decimal PriceNative { get; set; }       // 現在価格（建て通貨）
    public decimal ValuationJpy { get; set; }       // 評価額（円）
    public decimal CostBasisJpy { get; set; }       // 取得原価（円）
    public decimal UnrealizedPnlJpy { get; set; }  // 含み損益（円）
}

/// <summary>スナップショット時系列の1点（評価損益付き）。</summary>
public class SnapshotPnlPoint
{
    public string At { get; set; } = "";           // "yyyy-MM-dd HH:mm"
    public decimal UsdJpyRate { get; set; }
    public decimal TotalValuationJpy { get; set; }
    public decimal CostBasisJpy { get; set; }
    public decimal PnlJpy { get; set; }
}

// 全ユーザー横断の portfolio クエリ読み取り専用（_etag を本文から取得するため。#37）。
public class PortfolioReadDoc
{
    public string UserId { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public List<Holding> Holdings { get; set; } = new();
    public List<BuyLot> Buys { get; set; } = new();
    public List<SellLot> Sells { get; set; } = new();
    public List<Dividend> Dividends { get; set; } = new();
    public List<PriceSnapshot> Snapshots { get; set; } = new();
    public Dictionary<string, decimal> CurrentPrices { get; set; } = new();
    public decimal UsdJpyRate { get; set; }
    public string PricedAt { get; set; } = "";
    [Newtonsoft.Json.JsonProperty("_etag")] public string? Etag { get; set; }
}

/// <summary>POST /api/record-snapshots のレスポンス（GitHub Actions cron からの呼び出し用）。</summary>
public class RecordSnapshotsResponse
{
    public int UserCount { get; set; }      // portfolio ドキュメントを持つユーザー数
    public int RecordedCount { get; set; }  // 実際にスナップショットを記録できたユーザー数
}
