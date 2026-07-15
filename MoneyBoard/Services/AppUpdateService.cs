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
/// アプリ使用中の更新検知は軽量バージョンマーカー（wwwroot/version.json・ビルド時に&lt;Version&gt;から
/// 自動生成）を3分間隔でポーリングし、実行中バージョンと異なれば検知する。取得はキャッシュバスティング用の
/// クエリ付きで行うため、SWのプリキャッシュやブラウザHTTPキャッシュにキャッシュ済み内容があっても
/// 影響されず、常に最新を取得できる。
/// 検知後の通知はPWA限定のバッジ（#132）から、Web版も含めた更新ダイアログに変更した（#141）。
/// ポーリング自体は常時実行（IsStandaloneゲート撤去）。ダイアログは「操作中でない」ことを
/// OverlayRegistryService（モーダル/シート/ビジー状態の中央レジストリ）と AppStateStore.HasPendingChanges
/// （未保存のインライン編集）で確認してから表示し、操作中ならそれらの Changed イベントで再評価する
/// （一度きりのチェックではなくイベント駆動）。「あとで」で閉じたら以降はこのセッション（再起動まで）
/// 再表示しない。
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
    private readonly AppStateStore _appState;
    private readonly OverlayRegistryService _overlay;

    /// <summary>今回検知した更新後バージョン（"vX.Y.Z"）。通知不要ならnull。</summary>
    public string? UpdatedToVersion { get; private set; }

    /// <summary>ホーム画面追加後のスタンドアロン起動か（#132）。常設リロードボタンは通常のブラウザタブ
    /// （既にブラウザ自身のリロード操作がある）では冗長なため、PWAとして起動している時のみ表示する。</summary>
    public bool IsStandalone { get; private set; }

    /// <summary>更新ダイアログ（#141）を表示すべきか。ポーリングで検知しても、操作中は
    /// 安全なタイミングまで立たない。</summary>
    public bool ShowUpdateDialog { get; private set; }

    // ポーリングで検知したが、操作中のためダイアログをまだ出せていない更新後バージョン。
    private string? _pendingVersion;
    // 「あとで」で閉じたら、以降このセッション（アプリ再起動＝再読込まで）は再表示しない（一度きり）。
    private bool _dismissedOnce;

    public event Action? Changed;

    public AppUpdateService(IJSRuntime js, NavigationManager nav, AppStateStore appState, OverlayRegistryService overlay)
    {
        _js = js;
        _http = new HttpClient { BaseAddress = new Uri(nav.BaseUri) };
        _appState = appState;
        _overlay = overlay;
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

        // 更新ダイアログ（#141）はWeb版も含めて全ユーザーに出すため、常時ポーリングする
        // （#132時点ではPWAのバッジ表示にしか使い道が無くIsStandalone限定だったが、その制約は無くなった）。
        _appState.Changed += TryShowPendingDialog;
        _overlay.Changed += TryShowPendingDialog;
        _ = PollForUpdateAsync(currentVersion);
    }

    /// <summary>
    /// version.json を3分間隔でポーリングし、実行中バージョンと異なれば検知する。
    /// 一度検知したら以降ポーリングを続ける意味がないため終了する。取得失敗（オフライン等）はその回だけ
    /// スキップし、次回の間隔で再試行する。
    /// </summary>
    private async Task PollForUpdateAsync(string currentVersion)
    {
        while (_pendingVersion == null)
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
                _pendingVersion = deployedVersion;
                TryShowPendingDialog();
            }
        }
    }

    /// <summary>
    /// 検知済みの更新（_pendingVersion）があり、かつ「操作中でない」（未保存のインライン編集も
    /// 中央レジストリ上のモーダル/シート/ビジー状態も無い）なら更新ダイアログを表示する。
    /// 操作中で出せなかった場合は AppStateStore.Changed / OverlayRegistryService.Changed
    /// （＝保存完了／オーバーレイのclose）で再度呼ばれ、安全になったタイミングで再評価する。
    /// </summary>
    private void TryShowPendingDialog()
    {
        if (_pendingVersion == null || _dismissedOnce || ShowUpdateDialog) return;
        if (_appState.HasPendingChanges || _overlay.IsAnyOpen) return;

        ShowUpdateDialog = true;
        Changed?.Invoke();
    }

    /// <summary>「あとで」：ダイアログを閉じ、以降このセッション（再起動＝再読込まで）は再表示しない。</summary>
    public void DismissUpdateDialog()
    {
        ShowUpdateDialog = false;
        _dismissedOnce = true;
        Changed?.Invoke();
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
