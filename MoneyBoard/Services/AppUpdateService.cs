using Microsoft.JSInterop;
using MoneyBoardShared;

namespace MoneyBoard.Services;

/// <summary>
/// アプリ更新の事後通知（#86）。iOSのスタンドアロンPWAは service worker のライフサイクルイベント
/// （installed/waiting/controllerchange。pwa.js 参照）が信頼できず、無反応のままサイレントに新バージョンへ
/// 切り替わることがあるため、それらに依存せず「起動時にlocalStorageへ保存した前回バージョンと現在の
/// AppVersionを比較する」方式にした。差異があれば「vX.Y.Zに更新されました」と事後報告するのみで、
/// 強制リロードは行わない（他タブへ影響しない・未保存データを壊さない、という#76の設計方針を踏襲）。
/// </summary>
public class AppUpdateService
{
    private const string LastSeenKey = "mb_app_last_seen_version";
    // pwa.js の「更新する」クリック（SKIP_WAITING→自タブ即リロード）はユーザーが既に更新を認識済みのため、
    // 直後の再読込でこちらの事後通知が二重に出ないよう、pwa.js がセットしたこのフラグ（クリック時刻）を見て
    // 短い時間枠内のみ抑止する。時間枠を設けるのは、reloadが完了しなかった場合にフラグが残り続けて
    // 無関係な将来の更新まで誤って抑止してしまうのを防ぐため。
    private const string ManualReloadKey = "mb_pwa_manual_reload";
    private static readonly TimeSpan ManualReloadWindow = TimeSpan.FromSeconds(30);

    private readonly IJSRuntime _js;

    /// <summary>今回検知した更新後バージョン（"vX.Y.Z"）。通知不要ならnull。</summary>
    public string? UpdatedToVersion { get; private set; }

    public event Action? Changed;

    public AppUpdateService(IJSRuntime js) => _js = js;

    private bool _initialized;

    public async Task InitAsync(string currentVersion)
    {
        if (_initialized || string.IsNullOrEmpty(currentVersion)) return;
        _initialized = true;

        var lastSeen = await LocalStorage.GetItemAsync(_js, LastSeenKey);
        var manualReload = await ConsumeManualReloadFlagAsync();

        if (!manualReload && AppVersionMath.ShouldNotifyUpdate(lastSeen, currentVersion))
        {
            UpdatedToVersion = currentVersion;
            Changed?.Invoke();
        }

        if (lastSeen != currentVersion)
            await LocalStorage.SetItemAsync(_js, LastSeenKey, currentVersion);
    }

    public void Dismiss()
    {
        UpdatedToVersion = null;
        Changed?.Invoke();
    }

    private async Task<bool> ConsumeManualReloadFlagAsync()
    {
        var flag = await LocalStorage.GetItemAsync(_js, ManualReloadKey);
        if (flag == null) return false;

        await LocalStorage.RemoveItemAsync(_js, ManualReloadKey);
        if (!long.TryParse(flag, out var setAtMs)) return false;

        var setAt = DateTimeOffset.FromUnixTimeMilliseconds(setAtMs);
        return AppVersionMath.IsWithinManualReloadWindow(setAt, DateTimeOffset.UtcNow, ManualReloadWindow);
    }
}
