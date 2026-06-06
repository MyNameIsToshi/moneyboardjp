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
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var state = JsonSerializer.Deserialize<AppState>(body, JsonOptions);
            if (state == null) return new BadRequestResult();

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
}

public class CosmosDoc
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public AppState Data { get; set; } = new();
}
