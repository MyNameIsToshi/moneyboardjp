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
            return new OkObjectResult(item.Resource.Data);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
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
            var state = JsonSerializer.Deserialize<AppState>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (state == null) return new BadRequestResult();

            var doc = new CosmosDoc { Id = UserId, UserId = UserId, Data = state };
            await GetContainer().UpsertItemAsync(doc, new PartitionKey(UserId));
            return new OkResult();
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
