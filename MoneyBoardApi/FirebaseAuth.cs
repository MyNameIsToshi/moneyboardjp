using System.Security.Claims;
using Microsoft.AspNetCore.Http;
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
    private readonly string _projectId;
    private readonly string _issuer;
    private readonly bool _bypass;
    private readonly string _bypassUserId;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _config;

    public bool IsBypass => _bypass;

    public FirebaseAuth()
    {
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

    /// <summary>認証済みユーザーの情報を返す。未認証・検証失敗は null。</summary>
    public async Task<AuthPrincipal?> GetPrincipalAsync(HttpRequest req)
    {
        if (_bypass) return new AuthPrincipal(_bypassUserId, "dev@local", "ローカル開発", true);

        string header = req.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        var token = header["Bearer ".Length..].Trim();

        try
        {
            var config = await _config!.GetConfigurationAsync(CancellationToken.None);
            var parameters = new TokenValidationParameters
            {
                ValidIssuer = _issuer,
                ValidAudience = _projectId,
                IssuerSigningKeys = config.SigningKeys,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
            };
            var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
            if (!result.IsValid) return null;

            // Firebase の uid は user_id / sub クレーム（既定マッピングで NameIdentifier にもなる）。
            var id = result.ClaimsIdentity;
            var uid = id.FindFirst("user_id")?.Value
                      ?? id.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? id.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(uid)) return null;

            var email = id.FindFirst("email")?.Value;
            var name = id.FindFirst("name")?.Value;
            var emailVerified = string.Equals(id.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            return new AuthPrincipal(uid, email, name, emailVerified);
        }
        catch
        {
            return null;
        }
    }
}
