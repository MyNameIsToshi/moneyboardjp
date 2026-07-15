using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MoneyBoardShared;

namespace MoneyBoard.Services;

/// <summary>
/// アプリ更新の事後通知（#86）。iOSのスタンドアロンPWAは service worker のライフサイクルイベント
/// （installed/waiting/controllerchange。pwa.js 参照）が信頼できず、無反応のままサイレントに新バージョンへ
/// 切り替わることがあるため、それらに依存せず「起動時にlocalStorageへ保存した前回バージョンと現在の
/// AppVersionを比較する」方式にした。差異があれば「vX.Y.Zに更新されました」と事後報告するのみで、
/// 強制リロードは行わない（他タブへ影響しない・未保存データを壊さない、という#76の設計方針を踏襲）。
/// アプリ使用中の更新検知（#132）：起動時比較だけでは長時間使い続けた場合に気づけないため、
/// 軽量バージョンマーカー（wwwroot/version.json・ビルド時に&lt;Version&gt;から自動生成）を3分間隔で
/// ポーリングし、実行中バージョンと異なれば UpdateAvailable を立てる（リロードボタンのバッジ表示のみ・
/// 強制リロードはしない）。取得はキャッシュバスティング用のクエリ付きで行うため、SWのプリキャッシュや
/// ブラウザHTTPキャッシュにキャッシュ済み内容があっても影響されず、常に最新を取得できる。
/// </summary>
public class AppUpdateService
{
    private const string LastSeenKey = "mb_app_last_seen_version";
    // pwa.js の「更新する」クリック（SKIP_WAITING→自タブ即リロード）はユーザーが既に更新を認識済みのため、
    // 直後の再読込でこちらの事後通知が二重に出ないよう、pwa.js がセットしたこのフラグ（クリック時刻）を見て
    // 短い時間枠内のみ抑止する。時間枠を設けるのは、reloadが完了しなかった場合にフラグが残り続けて
    // 無関係な将来の更新まで誤って抑止してしまうのを防ぐため。タイトル横の常設リロードボタン（#132）の
    // クリック時も同じフラグを使うため、pwa.js 側で共通化してある（moneyboardPwa.checkForUpdateAndReload）。
    private const string ManualReloadKey = "mb_pwa_manual_reload";
    private static readonly TimeSpan ManualReloadWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(3);
    private static readonly JsonSerializerOptions VersionJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IJSRuntime _js;
    // version.json は wwwroot 直下の静的ファイルのため、API用（apiBaseUrl）とは別に
    // 常にホスト自身（NavigationManager.BaseUri）をベースにした専用クライアントを使う（AnnouncementServiceと同じ理由）。
    private readonly HttpClient _http;

    /// <summary>今回検知した更新後バージョン（"vX.Y.Z"）。通知不要ならnull。</summary>
    public string? UpdatedToVersion { get; private set; }

    /// <summary>ポーリングでデプロイ済みバージョンとの差異を検知した場合 true（#132）。
    /// リロードボタンのバッジ表示にのみ使う。</summary>
    public bool UpdateAvailable { get; private set; }

    /// <summary>ホーム画面追加後のスタンドアロン起動か（#132）。常設リロードボタンは通常のブラウザタブ
    /// （既にブラウザ自身のリロード操作がある）では冗長なため、PWAとして起動している時のみ表示する。</summary>
    public bool IsStandalone { get; private set; }

    public event Action? Changed;

    public AppUpdateService(IJSRuntime js, NavigationManager nav)
    {
        _js = js;
        _http = new HttpClient { BaseAddress = new Uri(nav.BaseUri) };
    }

    private bool _initialized;

    public async Task InitAsync(string currentVersion)
    {
        if (_initialized || string.IsNullOrEmpty(currentVersion)) return;
        _initialized = true;

        IsStandalone = await _js.InvokeAsync<bool>("moneyboardPwa.isStandalone");

        var lastSeen = await LocalStorage.GetItemAsync(_js, LastSeenKey);
        var manualReload = await ConsumeManualReloadFlagAsync();

        if (!manualReload && AppVersionMath.ShouldNotifyUpdate(lastSeen, currentVersion))
        {
            UpdatedToVersion = currentVersion;
            Changed?.Invoke();
        }

        if (lastSeen != currentVersion)
            await LocalStorage.SetItemAsync(_js, LastSeenKey, currentVersion);

        // ポーリング結果（UpdateAvailable）はリロードボタンのバッジ＝AppTitle/SideNav とも IsStandalone
        // ゲート下でしか描画されない。通常ブラウザタブでは出す先が無いため、無駄な定期取得を避けて
        // PWA起動時のみポーリングを開始する。
        if (IsStandalone)
            _ = PollForUpdateAsync(currentVersion);
    }

    /// <summary>
    /// version.json を3分間隔でポーリングし、実行中バージョンと異なれば UpdateAvailable を立てる。
    /// 一度検知したら以降ポーリングを続ける意味がないため終了する。取得失敗（オフライン等）はその回だけ
    /// スキップし、次回の間隔で再試行する。
    /// </summary>
    private async Task PollForUpdateAsync(string currentVersion)
    {
        while (!UpdateAvailable)
        {
            await Task.Delay(PollInterval);

            string? deployedVersion;
            try
            {
                // 毎回クエリを変えてキャッシュバスティングする。version.json はビルド状態次第で SW の
                // プリキャッシュ対象に含まれ得る（生成タイミングと wwwroot 静的アセット列挙の順序に依存し
                // クリーン／再ビルドで挙動が変わる）ため、素のURLだと SW やブラウザHTTPキャッシュが
                // キャッシュ済み内容を返して新バージョンを検知できないおそれがある。
                var payload = await _http.GetFromJsonAsync<VersionPayload>(
                    $"version.json?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", VersionJsonOptions);
                deployedVersion = payload?.Version;
            }
            catch
            {
                continue;
            }

            if (deployedVersion != null && AppVersionMath.ShouldNotifyUpdate(currentVersion, deployedVersion))
            {
                UpdateAvailable = true;
                Changed?.Invoke();
            }
        }
    }

    private record VersionPayload(string? Version);

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
