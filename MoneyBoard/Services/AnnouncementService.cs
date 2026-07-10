using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MoneyBoardShared;

namespace MoneyBoard.Services;

/// <summary>お知らせ1件（wwwroot/announcements.json の1要素）。id は追記のたび単調増加させる運用。</summary>
public record Announcement(int Id, string Date, string Version, string Type, string Title, string Body);

/// <summary>
/// お知らせ（repo同梱 wwwroot/announcements.json・デプロイ配信）の読込・既読管理。
/// 既読は localStorage の「最後に見た id」で保持（端末ごと独立）。WASM では Scoped＝実質シングルトン。
/// </summary>
public class AnnouncementService
{
    private const string StorageKey = "mb_announce_last_seen";

    private readonly HttpClient _http;
    private readonly IJSRuntime _js;

    /// <summary>新しい順（id 降順）。</summary>
    public IReadOnlyList<Announcement> Items { get; private set; } = Array.Empty<Announcement>();
    public int UnreadCount { get; private set; }

    /// <summary>更新後の初回表示で提示する未読分（新しい順）。無ければ空。Dismiss で空になる。</summary>
    public IReadOnlyList<Announcement> UnreadItems { get; private set; } = Array.Empty<Announcement>();

    /// <summary>お知らせ一覧ダイアログの表示状態（ベル押下で true）。</summary>
    public bool ShowList { get; private set; }

    /// <summary>
    /// 一覧ダイアログを開いた瞬間の未読件数・既読基準idのスナップショット（#130）。
    /// OpenListAsync は開くと同時に実既読化（lastSeen 更新）するため、UnreadCount 自体は
    /// 直後に 0 になる。ダイアログ側の「未読 N」ピル・各バージョンの未読ドット表示は、
    /// 既読化前のこのスナップショットを見て描画する（未読判定ロジック自体は変更しない）。
    /// </summary>
    public int UnreadCountAtOpen { get; private set; }
    public int LastSeenIdAtOpen { get; private set; }

    private int _lastSeenId;

    /// <summary>件数・UnreadItems・ShowList が変化したとき。購読側は InvokeAsync(StateHasChanged) すること。</summary>
    public event Action? Changed;

    // wwwroot 配下の静的ファイルを取得するため、API 用（apiBaseUrl）とは別に
    // 常にホスト自身（NavigationManager.BaseUri）をベースにした専用クライアントを使う。
    public AnnouncementService(NavigationManager nav, IJSRuntime js)
    {
        _http = new HttpClient { BaseAddress = new Uri(nav.BaseUri) };
        _js = js;
    }

    private bool _loaded;

    // announcements.json は人手で書く前提のため小文字キー（id/date/…）。
    // Blazor既定の JsonSerializerOptions は大文字小文字を区別するため一致させる（JSON シリアライズの申し送り事項）。
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task InitAsync()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var items = await _http.GetFromJsonAsync<List<Announcement>>("announcements.json", JsonOptions);
            Items = (items ?? new List<Announcement>()).OrderByDescending(a => a.Id).ToList();
        }
        catch
        {
            Items = Array.Empty<Announcement>();
        }

        var lastSeen = await ReadLastSeenAsync();
        _lastSeenId = lastSeen;
        var ids = Items.Select(a => a.Id).ToList();
        UnreadCount = AnnouncementMath.CountUnread(ids, lastSeen);
        // Items は新しい順（id降順）のため、Where で絞ってもその順序のまま保たれる。
        UnreadItems = Items.Where(a => AnnouncementMath.IsUnread(a.Id, lastSeen)).ToList();
        Changed?.Invoke();
    }

    /// <summary>お知らせ一覧を開く（ベル押下）。閲覧＝既読として未読バッジを消す。
    /// What's New も同時に閲覧済み扱いにする（一覧とWhat's Newが同時に出ない前提を
    /// 描画上の重なりだけに頼らず、状態としても保証する）。</summary>
    public async Task OpenListAsync()
    {
        UnreadCountAtOpen = UnreadCount;
        LastSeenIdAtOpen = _lastSeenId;
        ShowList = true;
        UnreadItems = Array.Empty<Announcement>();
        await MarkAllReadAsync();
        Changed?.Invoke();
    }

    public void CloseList()
    {
        ShowList = false;
        Changed?.Invoke();
    }

    /// <summary>お知らせ一覧を開いた等、閲覧＝既読として未読バッジを消す。</summary>
    private async Task MarkAllReadAsync()
    {
        if (UnreadCount == 0) return;
        if (AnnouncementMath.LatestId(Items.Select(a => a.Id).ToList()) is { } latest)
        {
            await WriteLastSeenAsync(latest);
            _lastSeenId = latest;
        }
        UnreadCount = 0;
    }

    /// <summary>What's New モーダルを閉じる（閲覧済み扱いで既読化）。</summary>
    public async Task DismissWhatsNewAsync()
    {
        UnreadItems = Array.Empty<Announcement>();
        await MarkAllReadAsync();
        Changed?.Invoke();
    }

    private async Task<int> ReadLastSeenAsync()
    {
        try
        {
            var v = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            return int.TryParse(v, out var id) ? id : 0;
        }
        catch { return 0; }
    }

    private async Task WriteLastSeenAsync(int id)
    {
        try { await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, id.ToString()); }
        catch { /* JS 未準備時は無視 */ }
    }
}
