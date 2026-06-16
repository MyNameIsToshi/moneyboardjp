using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using System.Net;
using System.Text.Json;

namespace MoneyBoardApi;

// DataApi の認証＋アクセス承認まわり（データ CRUD とは責務を分離）。
public partial class DataApi
{
    private const string SystemPartition = "__system__";
    private const string AccessDocId = "access-control";

    // 認証＋承認。許可なら (userId, isOwner)、未認証は401、未承認は pending 記録＋403 を返す。
    private async Task<(string? userId, bool isOwner, IActionResult? error)> AuthorizeAsync(Container container, HttpRequest req)
    {
        var principal = await auth.GetPrincipalAsync(req);
        if (principal is null) return (null, false, new UnauthorizedResult());
        if (auth.IsBypass) return (principal.Uid, true, null);   // ローカル開発はオーナー扱い

        // オーナー（OwnerEmail と一致・メール確認済み）は常に許可。
        var ownerEmail = Environment.GetEnvironmentVariable("OwnerEmail");
        if (!string.IsNullOrEmpty(ownerEmail) && principal.EmailVerified
            && string.Equals(principal.Email, ownerEmail, StringComparison.OrdinalIgnoreCase))
            return (principal.Uid, true, null);

        var access = await ReadAccessAsync(container);
        if (access.Approved.Any(a => a.Uid == principal.Uid)) return (principal.Uid, false, null);

        // 未承認：pending に未登録なら記録して保存（オーナーが後で承認）。
        if (!access.Pending.Any(p => p.Uid == principal.Uid))
        {
            access.Pending.Add(new PendingUser
            {
                Uid = principal.Uid,
                Email = principal.Email,
                Name = principal.Name,
                RequestedAt = DateTime.UtcNow.ToString("o")
            });
            await container.UpsertItemAsync(access, new PartitionKey(SystemPartition));
        }
        return (null, false, new ObjectResult(new { status = "pending" }) { StatusCode = StatusCodes.Status403Forbidden });
    }

    private async Task<AccessDoc> ReadAccessAsync(Container container)
    {
        try
        {
            var r = await container.ReadItemAsync<AccessDoc>(AccessDocId, new PartitionKey(SystemPartition));
            return r.Resource;
        }
        catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return new AccessDoc();
        }
        catch (Newtonsoft.Json.JsonException e)
        {
            // 旧スキーマ（approved が文字列配列）等で解析できない場合は空にリセット。
            logger.LogWarning(e, "AccessDoc parse failed; resetting");
            return new AccessDoc();
        }
    }

    // GET /api/access → 承認管理データ（オーナーのみ）
    [Function("GetAccess")]
    public async Task<IActionResult> GetAccess(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "access")] HttpRequest req)
    {
        try
        {
            var container = GetContainer();
            var (_, isOwner, error) = await AuthorizeAsync(container, req);
            if (error is not null) return error;
            if (!isOwner) return new StatusCodeResult(StatusCodes.Status403Forbidden);

            var access = await ReadAccessAsync(container);
            return new OkObjectResult(new { approved = access.Approved, pending = access.Pending, tsmcEmployees = access.TsmcEmployees });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetAccess failed");
            return new StatusCodeResult(500);
        }
    }

    // POST /api/access → 承認/拒否/解除（オーナーのみ）。本文 { action, uid }
    [Function("PostAccess")]
    public async Task<IActionResult> PostAccess(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "access")] HttpRequest req)
    {
        try
        {
            var container = GetContainer();
            var (_, isOwner, error) = await AuthorizeAsync(container, req);
            if (error is not null) return error;
            if (!isOwner) return new StatusCodeResult(StatusCodes.Status403Forbidden);

            var body = await ReadBodyCappedAsync(req.Body);
            if (body == null) return new BadRequestResult();
            var action = JsonSerializer.Deserialize<AccessAction>(body, JsonOptions);
            if (action is null || string.IsNullOrEmpty(action.Uid)) return new BadRequestResult();

            var access = await ReadAccessAsync(container);
            switch (action.Action)
            {
                case "approve":
                    if (!access.Approved.Any(a => a.Uid == action.Uid))
                    {
                        var pend = access.Pending.FirstOrDefault(p => p.Uid == action.Uid);
                        access.Approved.Add(new AccessUser { Uid = action.Uid, Email = pend?.Email, Name = pend?.Name });
                    }
                    access.Pending.RemoveAll(p => p.Uid == action.Uid);
                    break;
                case "reject":
                    access.Pending.RemoveAll(p => p.Uid == action.Uid);
                    break;
                case "revoke":
                    access.Approved.RemoveAll(a => a.Uid == action.Uid);
                    access.TsmcEmployees.RemoveAll(u => u == action.Uid);   // 解除時は社員フラグも除去
                    break;
                case "tsmc":   // TSMC 社員フラグの ON/OFF（value=true で付与）
                    access.TsmcEmployees.RemoveAll(u => u == action.Uid);
                    if (action.Value) access.TsmcEmployees.Add(action.Uid);
                    break;
                default:
                    return new BadRequestResult();
            }
            await container.UpsertItemAsync(access, new PartitionKey(SystemPartition));
            return new OkObjectResult(new { approved = access.Approved, pending = access.Pending, tsmcEmployees = access.TsmcEmployees });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PostAccess failed");
            return new StatusCodeResult(500);
        }
    }
}

// アクセス承認の管理ドキュメント（partition=__system__・id=access-control）。
public class AccessDoc
{
    [Newtonsoft.Json.JsonProperty("id")] public string Id { get; set; } = "access-control";
    public string UserId { get; set; } = "__system__";
    public string Type { get; set; } = "access";
    public List<AccessUser> Approved { get; set; } = new();   // 承認済み（uid＋メール/名前）
    public List<PendingUser> Pending { get; set; } = new();   // 承認待ち
    public List<string> TsmcEmployees { get; set; } = new();  // TSMC 社員フラグを付けた uid（ESPP UI 表示可。Owner は常に許可）
}

public class AccessUser
{
    public string Uid { get; set; } = "";
    public string? Email { get; set; }
    public string? Name { get; set; }
}

public class PendingUser
{
    public string Uid { get; set; } = "";
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string RequestedAt { get; set; } = "";
}

// POST /api/access のリクエスト本文。action = approve / reject / revoke / tsmc（value で ON/OFF）。
public class AccessAction
{
    public string Action { get; set; } = "";
    public string Uid { get; set; } = "";
    public bool Value { get; set; }
}
