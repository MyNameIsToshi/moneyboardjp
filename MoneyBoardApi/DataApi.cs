using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using System.Net;
using System.Text.Json;
using MoneyBoardShared;

namespace MoneyBoardApi;

public partial class DataApi(ILogger<DataApi> logger, CosmosClient cosmos, FirebaseAuth auth)
{
    private const string SettingsId = "settings";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // 本文サイズ上限（巨大ペイロードによる RU 暴発・DoS を防ぐ）
    private const long MaxBodyBytes = 1_900_000;
    // 構造上の健全性チェック
    private const int MaxAccounts = 100;
    private const int MaxFixedCosts = 500;
    private const int MaxCategories = 100;
    private const int MaxCards = 100;
    private const int MaxMonthsPerSave = 600;
    private const int MaxDebitsPerLedger = 1000;
    private const int MaxCardDetailsPerMonth = 5000;

    private Container GetContainer() =>
        cosmos.GetContainer(Environment.GetEnvironmentVariable("CosmosDb__DatabaseName"), "userdata");

    private static string MonthId(string ym) => $"month:{ym}";

    // GET /api/data → 設定ドキュメント＋全月次ドキュメントを集約して返す
    [Function("GetData")]
    public async Task<IActionResult> GetData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "data")] HttpRequest req)
    {
        try
        {
            var container = GetContainer();
            var (userId, isOwner, authError) = await AuthorizeAsync(container, req);
            if (authError is not null) return authError;
            var pk = new PartitionKey(userId!);
            var env = new DataEnvelope { Settings = new SettingsPart(), IsOwner = isOwner };

            // 設定（ポイント読み取り）。無ければ新規ユーザーとして空の設定。
            try
            {
                var r = await container.ReadItemAsync<SettingsDoc>(SettingsId, pk);
                env.Settings = new SettingsPart
                {
                    Etag = r.ETag,
                    SchemaVersion = r.Resource.SchemaVersion,
                    Accounts = r.Resource.Accounts,
                    FixedCosts = r.Resource.FixedCosts,
                    Categories = r.Resource.Categories,
                    Cards = r.Resource.Cards,
                    CategoryRules = r.Resource.CategoryRules
                };
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                // 空の設定のまま返す
            }

            // 月次（クエリ）。_etag はドキュメント本文から取得する。
            var query = new QueryDefinition("SELECT * FROM c WHERE c.userId = @u AND c.type = 'month'")
                .WithParameter("@u", userId);
            using var it = container.GetItemQueryIterator<MonthReadDoc>(query,
                requestOptions: new QueryRequestOptions { PartitionKey = pk });
            while (it.HasMoreResults)
            {
                foreach (var d in await it.ReadNextAsync())
                {
                    if (string.IsNullOrEmpty(d.Ym)) continue;
                    env.Months[d.Ym] = new MonthPart
                    {
                        Etag = d.Etag,
                        Ledgers = d.Ledgers ?? new(),
                        Transfers = d.Transfers ?? new(),
                        CardDetails = d.CardDetails ?? new(),
                        CardBilled = d.CardBilled ?? new()
                    };
                }
            }

            return new OkObjectResult(env);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetData failed");
            return new StatusCodeResult(500);
        }
    }

    // POST /api/data → 変更された設定/月次のみを TransactionalBatch で原子的に保存
    [Function("SaveData")]
    public async Task<IActionResult> SaveData(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "data")] HttpRequest req)
    {
        try
        {
            var (body, bodyError) = await ReadCappedBodyAsync(req);
            if (bodyError is not null) return bodyError;

            var env = JsonSerializer.Deserialize<DataEnvelope>(body!, JsonOptions);
            if (env == null) return new BadRequestResult();
            if (!IsStructurallyValid(env, out var reason))
            {
                logger.LogWarning("SaveData rejected: {Reason}", reason);
                return new BadRequestResult();
            }

            var container = GetContainer();
            var (userId, _, authError) = await AuthorizeAsync(container, req);
            if (authError is not null) return authError;
            var pk = new PartitionKey(userId!);
            var batch = container.CreateTransactionalBatch(pk);
            var ops = new List<(string kind, string ym)>();

            if (env.Settings != null)
            {
                var doc = new SettingsDoc
                {
                    Id = SettingsId, UserId = userId!, Type = "settings",
                    SchemaVersion = env.Settings.SchemaVersion,
                    Accounts = env.Settings.Accounts,
                    FixedCosts = env.Settings.FixedCosts,
                    Categories = env.Settings.Categories,
                    Cards = env.Settings.Cards,
                    CategoryRules = env.Settings.CategoryRules
                };
                batch.UpsertItem(doc, BatchOptions(env.Settings.Etag));
                ops.Add(("settings", ""));
            }
            foreach (var (ym, m) in env.Months)
            {
                var doc = new MonthDoc
                {
                    Id = MonthId(ym), UserId = userId!, Type = "month", Ym = ym,
                    Ledgers = m.Ledgers, Transfers = m.Transfers, CardDetails = m.CardDetails, CardBilled = m.CardBilled
                };
                batch.UpsertItem(doc, BatchOptions(m.Etag));
                ops.Add(("month", ym));
            }

            if (ops.Count == 0) return new OkObjectResult(new SaveResponse());

            using var resp = await batch.ExecuteAsync();
            if (!resp.IsSuccessStatusCode)
            {
                for (int i = 0; i < resp.Count; i++)
                {
                    if (resp[i].StatusCode == HttpStatusCode.PreconditionFailed)
                        return new StatusCodeResult(StatusCodes.Status412PreconditionFailed);
                }
                logger.LogError("SaveData batch failed: {Status}", resp.StatusCode);
                return new StatusCodeResult(500);
            }

            var result = new SaveResponse();
            for (int i = 0; i < ops.Count; i++)
            {
                var (kind, ym) = ops[i];
                if (kind == "settings") result.SettingsEtag = resp[i].ETag;
                else result.MonthEtags[ym] = resp[i].ETag;
            }
            return new OkObjectResult(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SaveData failed");
            return new StatusCodeResult(500);
        }
    }

    // クライアントが保持する etag があれば条件付き更新（競合検出）。
    private static TransactionalBatchItemRequestOptions BatchOptions(string? etag)
    {
        var opt = new TransactionalBatchItemRequestOptions { EnableContentResponseOnWrite = false };
        if (!string.IsNullOrEmpty(etag)) opt.IfMatchEtag = etag;
        return opt;
    }

    // Content-Length 事前チェック＋本文の上限読み取りをまとめて行う（書き込み系エンドポイント共通）。
    // 超過時は 413 を error に入れて返す（body は null）。正常時は (body, null)。
    private async Task<(string? body, IActionResult? error)> ReadCappedBodyAsync(HttpRequest req)
    {
        if (req.ContentLength > MaxBodyBytes)
            return (null, new StatusCodeResult(StatusCodes.Status413RequestEntityTooLarge));
        var body = await ReadBodyCappedAsync(req.Body);
        if (body == null)
            return (null, new StatusCodeResult(StatusCodes.Status413RequestEntityTooLarge));
        return (body, null);
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

    // 異常に巨大なコレクションを拒否（DoS / 破損データ対策）。internal=MoneyBoardApi.Tests から検証。
    internal static bool IsStructurallyValid(DataEnvelope env, out string reason)
    {
        if (env.Settings != null)
        {
            if (env.Settings.Accounts.Count > MaxAccounts) { reason = $"accounts={env.Settings.Accounts.Count}"; return false; }
            if (env.Settings.FixedCosts.Count > MaxFixedCosts) { reason = $"fixedCosts={env.Settings.FixedCosts.Count}"; return false; }
            if (env.Settings.Categories.Count > MaxCategories) { reason = $"categories={env.Settings.Categories.Count}"; return false; }
            if (env.Settings.Cards.Count > MaxCards) { reason = $"cards={env.Settings.Cards.Count}"; return false; }
        }
        if (env.Months.Count > MaxMonthsPerSave) { reason = $"months={env.Months.Count}"; return false; }
        foreach (var (ym, m) in env.Months)
        {
            if (m.CardDetails.Count > MaxCardDetailsPerMonth) { reason = $"cardDetails in {ym}={m.CardDetails.Count}"; return false; }
            foreach (var (accId, l) in m.Ledgers)
            {
                if (l.Debits.Count > MaxDebitsPerLedger)
                {
                    reason = $"debits in {ym}/{accId}={l.Debits.Count}";
                    return false;
                }
            }
        }
        reason = "";
        return true;
    }
}

// ── Cosmos ドキュメント（同一パーティション /userId 内で type により分割）──
public class SettingsDoc
{
    [Newtonsoft.Json.JsonProperty("id")] public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Type { get; set; } = "settings";
    public int SchemaVersion { get; set; } = 1;
    public List<Account> Accounts { get; set; } = new();
    public List<FixedCost> FixedCosts { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public List<Card> Cards { get; set; } = new();
    public Dictionary<string, string> CategoryRules { get; set; } = new();
}

public class MonthDoc
{
    [Newtonsoft.Json.JsonProperty("id")] public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Type { get; set; } = "month";
    public string Ym { get; set; } = "";
    public Dictionary<string, Ledger> Ledgers { get; set; } = new();
    public List<Transfer> Transfers { get; set; } = new();
    public List<CardDetail> CardDetails { get; set; } = new();
    public Dictionary<string, decimal> CardBilled { get; set; } = new();
}

// 月次クエリ読み取り専用（_etag を本文から取得するため）
public class MonthReadDoc
{
    public string? Ym { get; set; }
    public Dictionary<string, Ledger>? Ledgers { get; set; }
    public List<Transfer>? Transfers { get; set; }
    public List<CardDetail>? CardDetails { get; set; }
    public Dictionary<string, decimal>? CardBilled { get; set; }
    [Newtonsoft.Json.JsonProperty("_etag")] public string? Etag { get; set; }
}
