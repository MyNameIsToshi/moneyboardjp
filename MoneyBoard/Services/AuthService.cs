using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MoneyBoard.Services;

public record AuthUser(string Uid, string? Email, string? Name);

/// <summary>
/// Firebase Authentication（Googleログイン）のクライアント側ラッパー。
/// localhost では Firebase を介さず固定テストユーザーでバイパスする（開発の手間削減）。
/// `?auth=real` を付けると localhost でも実 Firebase を使う。
/// </summary>
public class AuthService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<AuthService>? _ref;

    public bool IsBypass { get; }
    public AuthUser? User { get; private set; }
    public bool Ready { get; private set; }     // 初回の認証状態判定が済んだか
    public event Action? StateChanged;

    public AuthService(IJSRuntime js, NavigationManager nav)
    {
        _js = js;
        var host = new Uri(nav.BaseUri).Host;
        var forceReal = nav.Uri.Contains("auth=real");
        IsBypass = (host is "localhost" or "127.0.0.1") && !forceReal;
    }

    public async Task InitAsync()
    {
        if (IsBypass)
        {
            User = new AuthUser("dev-user", "dev@local", "ローカル開発");
            Ready = true;
            StateChanged?.Invoke();
            return;
        }
        _ref = DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("mbAuth.start", _ref);
    }

    [JSInvokable]
    public void OnAuthStateChanged(AuthUser? user)
    {
        User = user;
        Ready = true;
        StateChanged?.Invoke();
    }

    public Task SignInAsync() => IsBypass ? Task.CompletedTask : _js.InvokeVoidAsync("mbAuth.signInGoogle").AsTask();
    public Task SignOutAsync() => IsBypass ? Task.CompletedTask : _js.InvokeVoidAsync("mbAuth.signOut").AsTask();

    // API 呼び出しに添付する ID トークン（JWT）。バイパス時は null（バックエンドも開発バイパス）。
    public async Task<string?> GetTokenAsync()
        => IsBypass ? null : await _js.InvokeAsync<string?>("mbAuth.getToken");

    // ID トークンを API に添付（バイパス時はヘッダーなし）。
    // SWA(マネージド関数)は Authorization ヘッダーを自前トークンで上書きするため、Firebase トークンは
    // 独自ヘッダー X-Firebase-Token で渡す。Authorization も残す（ローカル等 SWA を経由しない経路のフォールバック）。
    public async Task ApplyTokenAsync(HttpClient http)
    {
        var token = await GetTokenAsync();
        http.DefaultRequestHeaders.Remove("X-Firebase-Token");
        http.DefaultRequestHeaders.Authorization =
            token is null ? null : new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (token is not null)
            http.DefaultRequestHeaders.Add("X-Firebase-Token", token);
    }

    public ValueTask DisposeAsync()
    {
        _ref?.Dispose();
        return ValueTask.CompletedTask;
    }
}
