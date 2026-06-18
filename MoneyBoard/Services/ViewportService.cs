using Microsoft.JSInterop;

namespace MoneyBoard.Services;

/// <summary>
/// 画面幅（スマホ判定）を保持する。viewport.js の matchMedia 監視を購読し、
/// 変化時に <see cref="Changed"/> を発火する。WASM では Scoped＝実質シングルトン。
/// ブレークポイント（640px）はここを単一の真実とする。
/// </summary>
public class ViewportService : IAsyncDisposable
{
    private const string MobileQuery = "(max-width: 640px)";

    private readonly IJSRuntime _js;
    private DotNetObjectReference<ViewportService>? _ref;

    public bool IsMobile { get; private set; }
    public bool Initialized { get; private set; }

    /// <summary>スマホ⇔PC が切り替わったとき。購読側は InvokeAsync(StateHasChanged) すること。</summary>
    public event Action? Changed;

    public ViewportService(IJSRuntime js) => _js = js;

    public async Task InitAsync()
    {
        if (Initialized) return;
        _ref = DotNetObjectReference.Create(this);
        IsMobile = await _js.InvokeAsync<bool>("moneyboardViewport.init", _ref, MobileQuery);
        Initialized = true;
    }

    [JSInvokable]
    public void OnViewportChanged(bool isMobile)
    {
        if (IsMobile == isMobile) return;
        IsMobile = isMobile;
        Changed?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        _ref?.Dispose();
        return ValueTask.CompletedTask;
    }
}
