using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using System.Text.Json;
using MoneyBoardShared;

namespace MoneyBoardApi;

public class DataApi(ILogger<DataApi> logger, CosmosClient cosmos)
{
    private const string UserId = "default"; // Google認証実装後にヘッダーから取得
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // 本文サイズ上限。Cosmos のドキュメント上限(2MB)を超えさせず、
    // 巨大ペイロードによる RU 暴発・DoS を防ぐ。
    private const long MaxBodyBytes = 1_900_000;

    // 構造上の健全性チェック（異常に巨大なコレクションを拒否）
    private const int MaxAccounts = 100;
    private const int MaxFixedCosts = 500;
    private const int MaxMonths = 600;          // 約50年分
    private const int MaxDebitsPerLedger = 1000;

    private Container GetContainer() =>
        cosmos.GetContainer(Environment.GetEnvironmentVariable("CosmosDb__DatabaseName"), "userdata");

    // GET /api/data → AppState を返す
    [Function("GetData")]
    public async Task<IActionResult> GetData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "data")] HttpRequest req)
    {
        try
        {
            var item = await GetContainer().ReadItemAsync<CosmosDoc>(UserId, new PartitionKey(UserId));
            // 楽観的並行制御用に ETag をヘッダーで返す（保存時に If-Match で送り返す）
            req.HttpContext.Response.Headers.ETag = item.ETag;
            return new OkObjectResult(item.Resource.Data);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // 新規ユーザー。ETag なし → 初回保存は If-Match なしで作成される。
            return new OkObjectResult(new AppState());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetData failed");
            return new StatusCodeResult(500);
        }
    }

    // POST /api/data → AppState を保存
    [Function("SaveData")]
    public async Task<IActionResult> SaveData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "data")] HttpRequest req)
    {
        try
        {
            // Content-Length での先行チェック（存在すれば即拒否）
            if (req.ContentLength > MaxBodyBytes)
                return new StatusCodeResult(StatusCodes.Status413RequestEntityTooLarge);

            var body = await ReadBodyCappedAsync(req.Body);
            if (body == null) // 上限超過（Content-Length が無い/不正なケースも捕捉）
                return new StatusCodeResult(StatusCodes.Status413RequestEntityTooLarge);

            var state = JsonSerializer.Deserialize<AppState>(body, JsonOptions);
            if (state == null) return new BadRequestResult();

            if (!IsStructurallyValid(state, out var reason))
            {
                logger.LogWarning("SaveData rejected: {Reason}", reason);
                return new BadRequestResult();
            }

            var doc = new CosmosDoc { Id = UserId, UserId = UserId, Data = state };
            var options = new ItemRequestOptions { EnableContentResponseOnWrite = false };

            // クライアントが保持する ETag を渡された場合は条件付き更新。
            // サーバー側が新しければ Cosmos が 412 を返し、黙った上書きを防ぐ。
            var ifMatch = req.Headers.IfMatch.ToString();
            if (!string.IsNullOrEmpty(ifMatch)) options.IfMatchEtag = ifMatch;

            var response = await GetContainer().UpsertItemAsync(doc, new PartitionKey(UserId), options);
            req.HttpContext.Response.Headers.ETag = response.ETag;
            return new OkResult();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
        {
            // 別タブ/別端末が先に更新済み。クライアントに再読込を促す。
            logger.LogInformation("SaveData conflict (412): client etag is stale");
            return new StatusCodeResult(StatusCodes.Status412PreconditionFailed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SaveData failed");
            return new StatusCodeResult(500);
        }
    }

    // 上限までを読み、超過したら null を返す（Content-Length が無い/偽装の場合の保険）。
    private static async Task<string?> ReadBodyCappedAsync(Stream body)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await body.ReadAsync(buffer)) > 0)
        {
            total += read;
            if (total > MaxBodyBytes) return null;
            ms.Write(buffer, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    // 異常に巨大なコレクションを拒否（DoS / 破損データ対策）。業務的な厳密検証ではない。
    private static bool IsStructurallyValid(AppState state, out string reason)
    {
        if (state.Accounts.Count > MaxAccounts) { reason = $"accounts={state.Accounts.Count}"; return false; }
        if (state.FixedCosts.Count > MaxFixedCosts) { reason = $"fixedCosts={state.FixedCosts.Count}"; return false; }
        if (state.Months.Count > MaxMonths) { reason = $"months={state.Months.Count}"; return false; }
        foreach (var (ym, mo) in state.Months)
        {
            foreach (var (accId, ledger) in mo.Ledgers)
            {
                if (ledger.Debits.Count > MaxDebitsPerLedger)
                {
                    reason = $"debits in {ym}/{accId}={ledger.Debits.Count}";
                    return false;
                }
            }
        }
        reason = "";
        return true;
    }
}

public class CosmosDoc
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public AppState Data { get; set; } = new();
}
