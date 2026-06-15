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

    // GET /api/portfolio → ポートフォリオ全体を返す（無ければ空）。
    [Function("GetPortfolio")]
    public async Task<IActionResult> GetPortfolio(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "portfolio")] HttpRequest req)
    {
        try
        {
            var container = GetContainer();
            var (userId, _, authError) = await AuthorizeAsync(container, req);
            if (authError is not null) return authError;
            var pk = new PartitionKey(userId!);
            var env = new PortfolioEnvelope();

            try
            {
                var r = await container.ReadItemAsync<PortfolioDoc>(PortfolioId, pk);
                env.Etag = r.ETag;
                env.Data = new PortfolioData
                {
                    SchemaVersion = r.Resource.SchemaVersion,
                    Holdings = r.Resource.Holdings,
                    Buys = r.Resource.Buys,
                    Sells = r.Resource.Sells,
                    Dividends = r.Resource.Dividends,
                    Snapshots = r.Resource.Snapshots,
                    CurrentPrices = r.Resource.CurrentPrices,
                    UsdJpyRate = r.Resource.UsdJpyRate,
                    PricedAt = r.Resource.PricedAt
                };
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
            if (req.ContentLength > MaxBodyBytes)
                return new StatusCodeResult(StatusCodes.Status413RequestEntityTooLarge);
            var body = await ReadBodyCappedAsync(req.Body);
            if (body == null)
                return new StatusCodeResult(StatusCodes.Status413RequestEntityTooLarge);

            var env = JsonSerializer.Deserialize<PortfolioEnvelope>(body, JsonOptions);
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

            var doc = new PortfolioDoc
            {
                Id = PortfolioId, UserId = userId!, Type = "portfolio",
                SchemaVersion = d.SchemaVersion,
                Holdings = d.Holdings, Buys = d.Buys, Sells = d.Sells,
                Dividends = d.Dividends, Snapshots = d.Snapshots,
                CurrentPrices = d.CurrentPrices, UsdJpyRate = d.UsdJpyRate, PricedAt = d.PricedAt
            };
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
