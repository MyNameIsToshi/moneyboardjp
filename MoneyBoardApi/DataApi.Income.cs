using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using System.Net;
using System.Text.Json;
using MoneyBoardShared;

namespace MoneyBoardApi;

// 収入（給与）記録の CRUD（家計簿データとは別ドキュメント income・別ルート /api/income）。
// portfolio と同じ流儀（1ユーザー1ドキュメント・ETag で楽観的並行制御・全体を差分なしで保存）。
// 総支給（額面）は記録専用で、家計簿（settings/month）とは一切連動しない（#107）。
public partial class DataApi
{
    private const string IncomeId = "income";
    private const int MaxIncomeMonths = 1200;   // 100年分あれば十分な上限（異常データ対策）

    // GET /api/income → 収入（給与）記録を返す（無ければ空）。
    [Function("GetIncome")]
    public async Task<IActionResult> GetIncome(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "income")] HttpRequest req)
    {
        try
        {
            var container = GetContainer();
            var (userId, _, authError) = await AuthorizeAsync(container, req);
            if (authError is not null) return authError;
            var pk = new PartitionKey(userId!);
            var env = new IncomeEnvelope();

            try
            {
                var r = await container.ReadItemAsync<IncomeDoc>(IncomeId, pk);
                env.Etag = r.ETag;
                env.Data = new IncomeData { SchemaVersion = r.Resource.SchemaVersion, Records = r.Resource.Records };
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                // 空のまま返す（新規ユーザー）
            }

            return new OkObjectResult(env);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetIncome failed");
            return new StatusCodeResult(500);
        }
    }

    // POST /api/income → 収入（給与）記録を保存（If-Match で楽観的並行制御・競合は 412）。
    [Function("SaveIncome")]
    public async Task<IActionResult> SaveIncome(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "income")] HttpRequest req)
    {
        try
        {
            var (body, bodyError) = await ReadCappedBodyAsync(req);
            if (bodyError is not null) return bodyError;

            var env = JsonSerializer.Deserialize<IncomeEnvelope>(body!, JsonOptions);
            if (env == null) return new BadRequestResult();
            if (env.Data.Records.Count > MaxIncomeMonths)
            {
                logger.LogWarning("SaveIncome rejected: oversize collection ({Count})", env.Data.Records.Count);
                return new BadRequestResult();
            }

            var container = GetContainer();
            var (userId, _, authError) = await AuthorizeAsync(container, req);
            if (authError is not null) return authError;
            var pk = new PartitionKey(userId!);

            var doc = new IncomeDoc
            {
                Id = IncomeId, UserId = userId!, Type = "income",
                SchemaVersion = env.Data.SchemaVersion, Records = env.Data.Records
            };
            var opt = new ItemRequestOptions { EnableContentResponseOnWrite = false };
            if (!string.IsNullOrEmpty(env.Etag)) opt.IfMatchEtag = env.Etag;

            try
            {
                var resp = await container.UpsertItemAsync(doc, pk, opt);
                return new OkObjectResult(new IncomeSaveResponse { Etag = resp.ETag });
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return new StatusCodeResult(StatusCodes.Status412PreconditionFailed);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SaveIncome failed");
            return new StatusCodeResult(500);
        }
    }
}

// Cosmos ドキュメント（同一パーティション /userId 内で type="income"）。
public class IncomeDoc
{
    [Newtonsoft.Json.JsonProperty("id")] public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Type { get; set; } = "income";
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, IncomeMonthRecord> Records { get; set; } = new();
}
