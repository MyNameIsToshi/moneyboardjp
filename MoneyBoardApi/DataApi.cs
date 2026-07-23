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
    private const int MaxFixedIncomes = 500;
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
                // 同名プロパティを機械的にコピー（ObjectSync）。フィールド追加時にここを更新し忘れる事故を防ぐ（#134で2度発生・#136）。
                // 権威フィールド（Etag）はコピーの後に設定する（#138：将来 SettingsDoc に同名プロパティが増えても上書きされない順序）。
                var part = ObjectSync.CopyMatchingProperties(r.Resource, new SettingsPart());
                part.Etag = r.ETag;
                env.Settings = part;
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
                    // 同名プロパティを機械的にコピー（ObjectSync）。フィールド追加時にここを更新し忘れる事故を防ぐ（#134/#136 と同型・#137）。
                    env.Months[d.Ym] = ObjectSync.CopyMatchingProperties(d, new MonthPart());
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

            if (env.Settings != null)
            {
                // 版数フロア（#155・stage2=#174）：保存済み設定docの SchemaVersion より低い、または
                // 欠落した ClientSchemaVersion での上書きを拒否する。読み取りが1回増える（RUトレードオフはADR参照）。
                int? storedSchemaVersion = null;
                try
                {
                    var r = await container.ReadItemAsync<SettingsDoc>(SettingsId, pk);
                    storedSchemaVersion = r.Resource.SchemaVersion;
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound) { /* 新規ユーザー：フロアなし */ }

                if (ViolatesSchemaFloor(env.ClientSchemaVersion, storedSchemaVersion))
                {
                    logger.LogWarning("SaveData rejected: schema floor violation (clientSchemaVersion={Csv}, stored={Stored})",
                        env.ClientSchemaVersion, storedSchemaVersion);
                    return new StatusCodeResult(StatusCodes.Status409Conflict);
                }
            }

            var batch = container.CreateTransactionalBatch(pk);
            var ops = new List<(string kind, string ym)>();

            if (env.Settings != null)
            {
                // 同名プロパティを機械的にコピー（ObjectSync）。フィールド追加時にここを更新し忘れる事故を防ぐ（#134で2度発生・#136）。
                // 権威フィールド（Id/UserId/Type）はコピーの後に設定する（#138：将来 SettingsPart に同名プロパティが増えても上書きされない順序）。
                var doc = ObjectSync.CopyMatchingProperties(env.Settings, new SettingsDoc());
                doc.Id = SettingsId;
                doc.UserId = userId!;
                doc.Type = "settings";
                // 口座種別をサーバー側で正規化してから永続化する（#154）。ObjectSync はリストを参照ごと
                // コピーするため、これは env.Settings.Accounts も書き換える点に注意（以降 env 側は読まない）。
                NormalizeAccountTypes(doc.Accounts, doc.SchemaVersion);
                batch.UpsertItem(doc, BatchOptions(env.Settings.Etag));
                ops.Add(("settings", ""));
            }
            foreach (var (ym, m) in env.Months)
            {
                // 同名プロパティを機械的にコピー（ObjectSync）。フィールド追加時にここを更新し忘れる事故を防ぐ（#134/#136 と同型・#137）。
                // 権威フィールド（Id/UserId/Type/Ym）はコピーの後に設定する（#138：将来 MonthPart に同名プロパティが増えても上書きされない順序）。
                var doc = ObjectSync.CopyMatchingProperties(m, new MonthDoc());
                doc.Id = MonthId(ym);
                doc.UserId = userId!;
                doc.Type = "month";
                doc.Ym = ym;
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

    // 口座種別の正規化（#154）。サーバー側で Type/IsWallet の矛盾を収束させ、スキーマの権威を
    // クライアント版数から切り離す。internal=MoneyBoardApi.Tests から検証。
    //
    // 旧フラグからの復元は **v11 未満のクライアントが送ったデータに限る**。Type==Normal は
    // 「通常口座」と「Type を知らないクライアントが送らなかった」の区別が付かないため、
    // v11 以降にも適用すると Wallet→通常口座 の変更を保存のたびに巻き戻してしまう（#148 で
    // 種別変更 UI が入ると顕在化する）。書き戻し（IsWallet を Type に合わせる）は版数に依らず行う。
    internal static void NormalizeAccountTypes(List<Account> accounts, int schemaVersion)
    {
        if (schemaVersion < SchemaMigration.AccountTypeVersion)
            SchemaMigration.RestoreWalletTypeFromLegacyFlag(accounts);

        // 旧クライアント（Type を知らず IsWallet だけを見る）が同じデータを開いても財布判定を
        // 落とさないよう、後方互換フィールドを Type と矛盾しない値へ揃える。
        foreach (var a in accounts) a.IsWallet = a.Type == AccountType.Wallet;
    }

    // 版数フロアの純粋判定（#155・stage2=#174）。保存済み doc が無い（新規ユーザー）場合はフロアなし。
    // 保存済み doc がある場合、ClientSchemaVersion 欠落（null＝旧クライアント）は拒否する
    // （ロールアウト第2段。第1段では許可していたが、旧クライアント一掃後に切り替え済み）。
    // internal=MoneyBoardApi.Tests から検証。
    internal static bool ViolatesSchemaFloor(int? clientSchemaVersion, int? storedSchemaVersion)
    {
        if (storedSchemaVersion is not int stored) return false;
        if (clientSchemaVersion is not int csv) return true;
        return csv < stored;
    }

    // 異常に巨大なコレクションを拒否（DoS / 破損データ対策）。internal=MoneyBoardApi.Tests から検証。
    internal static bool IsStructurallyValid(DataEnvelope env, out string reason)
    {
        if (env.Settings != null)
        {
            if (env.Settings.Accounts.Count > MaxAccounts) { reason = $"accounts={env.Settings.Accounts.Count}"; return false; }
            if (env.Settings.FixedCosts.Count > MaxFixedCosts) { reason = $"fixedCosts={env.Settings.FixedCosts.Count}"; return false; }
            if (env.Settings.FixedIncomes.Count > MaxFixedIncomes) { reason = $"fixedIncomes={env.Settings.FixedIncomes.Count}"; return false; }
            if (env.Settings.Categories.Count > MaxCategories) { reason = $"categories={env.Settings.Categories.Count}"; return false; }
            if (env.Settings.Cards.Count > MaxCards) { reason = $"cards={env.Settings.Cards.Count}"; return false; }
            // AccountType は整数で永続化され、未定義値も素通しでキャストされる（#164・#147からの申し送り）。
            // 述語（AccountTypePredicates）はswitchを使わないため未定義値でも例外にはならないが、
            // 保存データに未定義値を残さないよう入口で拒否する。
            foreach (var a in env.Settings.Accounts)
            {
                if (!Enum.IsDefined(typeof(AccountType), a.Type)) { reason = $"accountType={(int)a.Type}"; return false; }
            }
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
    public List<FixedIncome> FixedIncomes { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public List<Card> Cards { get; set; } = new();
    public Dictionary<string, string> CategoryRules { get; set; } = new();
    public Dictionary<string, string> CategoryPrefixRules { get; set; } = new();
    public int TutorialSeenVersion { get; set; } = 0;
    public List<int> BonusMonths { get; set; } = new() { 6, 12 };
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
