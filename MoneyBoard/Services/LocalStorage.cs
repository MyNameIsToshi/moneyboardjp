using Microsoft.JSInterop;

namespace MoneyBoard.Services;

/// <summary>localStorageの読み書きを例外握りつぶし付き（JS未準備時は無視）で行う共通ヘルパー。</summary>
public static class LocalStorage
{
    public static async Task<string?> GetItemAsync(IJSRuntime js, string key)
    {
        try { return await js.InvokeAsync<string?>("localStorage.getItem", key); }
        catch { return null; }
    }

    public static async Task SetItemAsync(IJSRuntime js, string key, string value)
    {
        try { await js.InvokeVoidAsync("localStorage.setItem", key, value); }
        catch { /* JS 未準備時は無視 */ }
    }

    public static async Task RemoveItemAsync(IJSRuntime js, string key)
    {
        try { await js.InvokeVoidAsync("localStorage.removeItem", key); }
        catch { /* JS 未準備時は無視 */ }
    }
}
