using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace MoneyBoardApi;

/// <summary>検証済みトークンから取り出したユーザー情報。</summary>
public record AuthPrincipal(string Uid, string? Email, string? Name, bool EmailVerified);

/// <summary>
/// Firebase Authentication の ID トークン（JWT・RS256）を検証して uid を取り出す。
/// 署名鍵は securetoken の OIDC 構成から取得し ConfigurationManager がキャッシュ＋自動更新する。
/// AuthBypass=true（ローカル開発）のときは検証せず固定 userId を返す。
/// </summary>
public class FirebaseAuth
{
    private readonly ILogger<FirebaseAuth> _logger;
    private readonly string _projectId;
    private readonly string _issuer;
    private readonly bool _bypass;
    private readonly string _bypassUserId;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _config;

    public bool IsBypass => _bypass;

    // 直近の認証失敗理由（デバッグ用。Singleton 共有なので厳密なスレッド安全性は割り切り）。
    public string? LastError { get; private set; }

    public FirebaseAuth(ILogger<FirebaseAuth> logger)
    {
        _logger = logger;
        _projectId = Environment.GetEnvironmentVariable("Firebase__ProjectId") ?? "";
        _issuer = $"https://securetoken.google.com/{_projectId}";
        _bypass = string.Equals(Environment.GetEnvironmentVariable("AuthBypass"), "true", StringComparison.OrdinalIgnoreCase);
        _bypassUserId = Environment.GetEnvironmentVariable("AuthBypassUserId") ?? "default";

        if (!_bypass)
        {
            _config = new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{_issuer}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever());
        }
    }

    /// <summary>認証済みユーザーの情報を返す。未認証・検証失敗は null（理由は LastError）。</summary>
    public async Task<AuthPrincipal?> GetPrincipalAsync(HttpRequest req)
    {
        if (_bypass) return new AuthPrincipal(_bypassUserId, "dev@local", "ローカル開発", true);
        LastError = null;

        if (string.IsNullOrEmpty(_projectId))
        {
            LastError = "projectId-empty";
            _logger.LogWarning("Firebase auth: Firebase__ProjectId is empty");
            return null;
        }

        string header = req.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            LastError = "no-bearer-header";
            return null;
        }
        var token = header["Bearer ".Length..].Trim();

        try
        {
            // ConfigurationManager を TVP に渡すと、kid 不一致時に JWKS を自動リフレッシュ＆再試行する。
            // Firebase の securetoken 署名鍵はほぼ毎日ローテーションするため、鍵スナップショットの固定は不可。
            var parameters = new TokenValidationParameters
            {
                ValidIssuer = _issuer,
                ValidAudience = _projectId,
                ConfigurationManager = _config!,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
            };
            var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
            if (!result.IsValid)
            {
                LastError = await BuildDiagAsync(header, token, result.Exception);
                _logger.LogWarning("Firebase token invalid: {Err}", LastError);
                return null;
            }

            // Firebase の uid は user_id / sub クレーム（既定マッピングで NameIdentifier にもなる）。
            var id = result.ClaimsIdentity;
            var uid = id.FindFirst("user_id")?.Value
                      ?? id.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? id.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(uid))
            {
                LastError = "no-uid-claim";
                _logger.LogWarning("Firebase token has no uid claim");
                return null;
            }

            var email = id.FindFirst("email")?.Value;
            var name = id.FindFirst("name")?.Value;
            var emailVerified = string.Equals(id.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            return new AuthPrincipal(uid, email, name, emailVerified);
        }
        catch (Exception ex)
        {
            LastError = await BuildDiagAsync(header, token, ex);
            _logger.LogError(ex, "Firebase token validation threw");
            return null;
        }
    }

    // 検証失敗時の精密診断（原因特定の一時計測）。関数が実際に受け取った Authorization ヘッダー／
    // トークン文字列の形（長さ・セグメント数・デコードした1セグメント目＝JWTヘッダー）と、握っている
    // 署名鍵の kid 一覧・期待 iss/aud・内側例外を1行にまとめる。原因確定後に簡素化する。
    private async Task<string> BuildDiagAsync(string rawHeader, string token, Exception? inner)
    {
        string tokenKid = "?";
        try { tokenKid = new JsonWebToken(token).Kid; } catch { /* ignore */ }

        string hdr0 = "?";
        try { hdr0 = Base64UrlEncoder.Decode(token.Split('.')[0]); }
        catch (Exception e) { hdr0 = $"decode-failed:{e.Message}"; }

        string cfgKids;
        try
        {
            var c = await _config!.GetConfigurationAsync(CancellationToken.None);
            cfgKids = string.Join(",", c.SigningKeys.Select(k => k.KeyId));
        }
        catch (Exception e)
        {
            cfgKids = $"cfg-fetch-failed: {e.GetType().Name}: {e.Message}";
        }

        string rawPrefix = rawHeader.Length >= 15 ? rawHeader[..15] : rawHeader;
        return $"rawLen={rawHeader.Length}; rawPrefix='{rawPrefix}'; tokLen={token.Length}; segs={token.Split('.').Length}; "
             + $"hdr0={hdr0}; tokenKidProp={tokenKid}; expIss={_issuer}; expAud={_projectId}; cfgKids=[{cfgKids}]; "
             + $"inner={inner?.GetType().Name}: {inner?.Message}";
    }
}
