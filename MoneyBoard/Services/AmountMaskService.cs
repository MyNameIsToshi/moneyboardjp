using Microsoft.JSInterop;

namespace MoneyBoard.Services;

/// <summary>金額マスク状態（アプリ全体トグル）を保持する。localStorage で永続化。WASM では Scoped＝実質シングルトン。</summary>
public class AmountMaskService(IJSRuntime js)
{
    private const string StorageKey = "mb_masked";

    public bool IsMasked { get; private set; }

    /// <summary>マスク状態が変化したとき。購読側は InvokeAsync(StateHasChanged) すること。</summary>
    public event Action? Changed;

    public async Task InitAsync()
    {
        try
        {
            var v = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            IsMasked = v == "1";
        }
        catch { /* SSR や JS 未準備時は無視 */ }
    }

    public async Task ToggleAsync()
    {
        IsMasked = !IsMasked;
        try { await js.InvokeVoidAsync("localStorage.setItem", StorageKey, IsMasked ? "1" : "0"); }
        catch { }
        Changed?.Invoke();
    }
}
