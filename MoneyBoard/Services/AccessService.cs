using System.Net.Http.Json;

namespace MoneyBoard.Services;

public record PendingUserDto(string Uid, string? Email, string? Name, string RequestedAt);
public record AccessUserDto(string Uid, string? Email, string? Name);
public record AccessInfo(List<AccessUserDto> Approved, List<PendingUserDto> Pending, List<string>? TsmcEmployees = null);

/// <summary>オーナー向けのアクセス承認管理 API（/api/access）クライアント。</summary>
public class AccessService(HttpClient http, AuthService auth)
{
    private const string Path = "api/access";

    public async Task<AccessInfo?> GetAsync()
    {
        await auth.ApplyTokenAsync(http);
        var resp = await http.GetAsync(Path);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<AccessInfo>() : null;
    }

    // action = approve / reject / revoke。成功時は更新後の承認情報を返す。
    public async Task<AccessInfo?> ActAsync(string action, string uid)
    {
        await auth.ApplyTokenAsync(http);
        var resp = await http.PostAsJsonAsync(Path, new { action, uid });
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<AccessInfo>() : null;
    }

    // TSMC 社員フラグの ON/OFF。成功時は更新後の承認情報を返す。
    public async Task<AccessInfo?> SetTsmcAsync(string uid, bool value)
    {
        await auth.ApplyTokenAsync(http);
        var resp = await http.PostAsJsonAsync(Path, new { action = "tsmc", uid, value });
        return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<AccessInfo>() : null;
    }
}
