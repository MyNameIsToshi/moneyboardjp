using Microsoft.JSInterop;

namespace Seikei.Services;

// IndexedDB への薄いラッパー（wwwroot/js/storage.js を呼ぶ）
public class StorageService(IJSRuntime js)
{
    public async Task<string?> GetAsync(string key)
        => await js.InvokeAsync<string?>("seikeiStorage.get", key);

    public async Task SetAsync(string key, string value)
        => await js.InvokeVoidAsync("seikeiStorage.set", key, value);
}
