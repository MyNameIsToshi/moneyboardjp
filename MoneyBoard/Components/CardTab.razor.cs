namespace MoneyBoard.Components;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MoneyBoard.Services;
using MoneyBoardShared;
using static MoneyBoard.MoneyFormat;

// CardTab.razor の code-behind。markup・ディレクティブ(@inject)は .razor 側に残し、
// 明細編集・折りたたみ/ソート・CSV取込・一括カテゴリの UI ロジックをこの partial class に集約する。
// CSV パース(CardCsvParser)・重複除外(LedgerEngine) は Shared でテスト済みの純粋ロジックを利用する。
public partial class CardTab
{
    [CascadingParameter(Name = "IsMobile")] public bool IsMobile { get; set; }

    private MonthData Mo = new();

    // スマホ：明細編集シートで開いている明細（直接編集・即時保存）
    private string? _editDetailId;
    private bool _isNewDetail;   // ＋追加で開いた新規明細か（未入力で閉じたら破棄）
    private CardDetail? EditingDetail => _editDetailId is null ? null : Mo.CardDetails.FirstOrDefault(d => d.Id == _editDetailId);
    private void OpenDetail(string id) { _editDetailId = id; _isNewDetail = false; }
    private string CatName(string? catId) => Svc.CategoryById(catId)?.Name ?? "未分類";

    private void CloseDetail()
    {
        // 利用先が空・金額0のまま閉じた新規明細は追加しない（破棄）。
        if (_isNewDetail && EditingDetail is { } d && string.IsNullOrWhiteSpace(d.Name) && d.Amount == 0)
        {
            var id = _editDetailId;
            _editDetailId = null;
            _isNewDetail = false;
            if (id != null) RemoveDetail(id);
            return;
        }
        _editDetailId = null;
        _isNewDetail = false;
    }

    // シートから削除：シートを閉じてから削除（明細は確認なしで即削除＝従来挙動）
    private void DeleteEditingDetail()
    {
        var id = _editDetailId;
        _editDetailId = null;
        if (id != null) RemoveDetail(id);
    }

    protected override void OnInitialized() => Mo = Svc.EnsureMonth(Svc.CardMonth);

    // 月次タブから遷移したとき：対象カードだけ展開し他は畳んで、その位置までスクロールする。
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var target = Svc.ScrollToCardId;
        if (target == null) return;
        Svc.ScrollToCardId = null;            // 一度きりで消費

        // 既定は折りたたみ。_toggled は「展開した集合」なので、対象だけ入れれば対象のみ展開になる。
        _toggled.Clear();
        _toggled.Add(target);
        StateHasChanged();

        await JS.InvokeVoidAsync("scrollToElementId", $"card-{target}");
    }

    private List<CardDetail> DetailsFor(string cardId)
    {
        var q = Mo.CardDetails.Where(d => d.CardId == cardId);
        return (_sortAsc ? q.OrderBy(SortKey) : q.OrderByDescending(SortKey)).ToList();
    }

    // ── カードパネルの折りたたみ ──
    // 既定は PC・スマホとも折りたたみ。_toggled は「ユーザーが展開した集合」。
    // 月変更・カード増減でも自然に既定（折りたたみ）へ戻る。
    private readonly HashSet<string> _toggled = new();
    private bool IsCollapsed(string cardId) => !_toggled.Contains(cardId);
    private void ToggleCollapse(string cardId)
    {
        if (!_toggled.Add(cardId)) _toggled.Remove(cardId);
    }

    // ヒーローの「先月比」用：指定月の全カード利用額合計（未ロードの月は 0。月は新規作成しない）。
    private decimal CardTotalOf(string ym) =>
        Svc.State.Months.TryGetValue(ym, out var mo) ? mo.CardDetails.Sum(d => d.Amount) : 0m;

    // ── 明細の並び替え（利用日/利用先/カテゴリ/金額）。全カードパネル共通 ──
    private string _sortKey = "date";
    private bool _sortAsc = true;

    private object SortKey(CardDetail d) => _sortKey switch
    {
        "name" => d.Name,
        "category" => CategorySortKey(d.CategoryId),
        "amount" => d.Amount,
        _ => d.Date,
    };

    private void SetSort(string key)
    {
        if (_sortKey == key) _sortAsc = !_sortAsc;
        else { _sortKey = key; _sortAsc = true; }
    }

    // ラベル＋並び替え矢印をまとめて返す（@の直前が文字だとRazorがメール扱いするのを回避）
    private MarkupString SortHead(string key, string label) =>
        new(label + (_sortKey == key ? (_sortAsc ? " ▲" : " ▼") : ""));

    // カテゴリ設定の並び順（未分類・不明は末尾）
    private int CategorySortKey(string? catId) =>
        Svc.CategoryById(catId)?.SortOrder ?? int.MaxValue;

    // カテゴリ設定の色（未分類・不明は透明＝空リング）
    private string DotColor(string? catId) =>
        Svc.CategoryById(catId)?.Color ?? "transparent";

    // 手入力で利用先を確定したとき、未分類なら記憶済みルールで自動分類する
    // （手動でカテゴリを選んだ行は上書きしない）。
    private void OnNameChanged(CardDetail d)
    {
        if (string.IsNullOrEmpty(d.CategoryId)
            && Svc.State.CategoryRules.TryGetValue(d.Name, out var catId))
            d.CategoryId = catId;
        Save();
    }

    private void Prev() { Svc.CardMonth = LedgerService.PrevYm(Svc.CardMonth); Mo = Svc.EnsureMonth(Svc.CardMonth); }
    private void Next() { Svc.CardMonth = LedgerService.NextYm(Svc.CardMonth); Mo = Svc.EnsureMonth(Svc.CardMonth); }

    private void Save() => _ = Svc.SaveAsync();

    private void OnDateChange(CardDetail d, object? v) { d.Date = v?.ToString() ?? ""; Save(); }

    // 金額変更時のみカード Debit 合計が変わるため再計算（デバウンス保存）
    private void OnAmountChanged() { Svc.RecalcCards(Svc.CardMonth); Svc.RequestSave(); }

    // ── 今月の請求額（リボ・分割で利用額≠引き落とし額のとき補正）──
    private bool HasBilled(string cardId) => Svc.CardBilledOf(Svc.CardMonth, cardId) != null;
    private decimal? BilledOf(string cardId) => Svc.CardBilledOf(Svc.CardMonth, cardId);

    // トグルON時は既定で利用額（明細合計）を初期請求額に。OFFで解除し一括払いへ戻す。
    private void ToggleBilled(string cardId, bool on, decimal used)
    {
        Svc.SetCardBilled(Svc.CardMonth, cardId, on ? used : null);
        Svc.RequestSave();
    }

    private void OnBilledChanged(string cardId, decimal v)
    {
        Svc.SetCardBilled(Svc.CardMonth, cardId, v);
        Svc.RequestSave();
    }

    private void AddDetail(string cardId)
    {
        var d = new CardDetail { CardId = cardId, Date = DateTime.Today.ToString("yyyy-MM-dd") };
        Mo.CardDetails.Add(d);
        Svc.RecalcCards(Svc.CardMonth);
        Save();
        if (IsMobile) { _editDetailId = d.Id; _isNewDetail = true; }   // スマホは作成後すぐ編集シートを開く
    }

    private void RemoveDetail(string id)
    {
        Mo.CardDetails.RemoveAll(d => d.Id == id);
        Svc.RecalcCards(Svc.CardMonth);
        Save();
    }

    // ── カード明細 CSV 取込（取込ボタン→種別選択→ファイル選択。表示中の月へ・そのカードを全置換）──
    private string? ImportMessage;
    private string? _importCardId;   // 取込対象カード（種別選択ダイアログ表示中は非null）

    private void OpenImport(string cardId) => _importCardId = cardId;

    private async Task OnCsvSelected(InputFileChangeEventArgs e, CardCsvFormat format)
    {
        var cardId = _importCardId;
        var file = e.File;
        _importCardId = null;   // 種別選択ダイアログを閉じる
        if (cardId == null || file == null) return;
        try
        {
            using var stream = file.OpenReadStream(maxAllowedSize: 5_000_000);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var spec = CardCsvParser.Specs[format];
            // PayPay と楽天は UTF-8(BOM可)、その他(JCB/三井住友/au PAY)は Shift-JIS。種別ごとにデコードを切り替える。
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var text = spec.IsUtf8
                ? System.Text.Encoding.UTF8.GetString(hasBom ? bytes[3..] : bytes)
                : await JS.InvokeAsync<string>("decodeShiftJis", bytes);

            var parsed = CardCsvParser.Parse(format, text, cardId);
            Svc.ApplyCategoryRules(parsed);   // 記憶済みの店名→カテゴリを自動適用

            // リボ/分割で過去月に既出の明細（再掲）は除外し、二重計上を防ぐ
            var (kept, excluded) = Svc.DedupAgainstEarlierMonths(Svc.CardMonth, cardId, parsed);

            // 全置換：このカードの当月明細を消して入れ直す
            Mo.CardDetails.RemoveAll(d => d.CardId == cardId);
            Mo.CardDetails.AddRange(kept);
            Svc.RecalcCards(Svc.CardMonth);
            Save();

            var name = Svc.CardById(cardId)?.Name;
            var label = LedgerService.Label(Svc.CardMonth);
            ImportMessage = excluded > 0
                ? $"「{name}」に {spec.Label} の明細 {kept.Count} 件を取り込みました（{label}・全置換／過去月と重複の {excluded} 件を除外）"
                : $"「{name}」に {spec.Label} の明細 {kept.Count} 件を取り込みました（{label}・全置換）";
        }
        catch (Exception ex)
        {
            ImportMessage = $"取込に失敗しました: {ex.Message}";
        }
    }

    // ── カード明細スクショ読み取り（Claude Vision）。当月へ増分追加（CSVと違い全置換しない）──
    // X(旧Twitter)風：選択/貼り付けした画像をプレビューで溜め置き（ステージング）し、
    // 「読み取り開始」でまとめて読み取る。複数回の貼り付け・追加選択ができる。
    private string? _shotCardId;            // スクショ取込対象カード（ダイアログ表示中は非null）
    private ElementReference _shotInput;    // 複数選択ファイル input
    private bool _shotBusy;                 // 読み取り中（多重実行・閉じる操作を抑止）
    private int _shotTotal, _shotDone;      // 進捗表示用
    private DotNetObjectReference<CardTab>? _shotRef;   // 貼り付けコールバック用
    private readonly List<ProcessedImage> _shotStaged = new();   // 溜め置き中の画像
    private const int ShotMaxDim = 1600;    // 長辺の上限（縮小して本文上限内＋トークン削減）
    private const double ShotQuality = 0.85;
    private const int ShotMaxCount = 10;    // 一度に読み取れる枚数の上限

    private async Task OpenShot(string cardId)
    {
        _shotCardId = cardId;
        _shotBusy = false; _shotTotal = 0; _shotDone = 0;
        _shotStaged.Clear();
        if (!IsMobile)   // PC は Ctrl+V 貼り付けを購読（スマホは貼り付け非対応）
        {
            _shotRef ??= DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("cardImage.attachPaste", _shotRef, ShotMaxDim, ShotQuality);
        }
    }

    private async Task CloseShot()
    {
        if (_shotBusy) return;
        if (!IsMobile) await JS.InvokeVoidAsync("cardImage.detachPaste");
        _shotCardId = null;
        _shotStaged.Clear();
    }

    // ファイル選択（複数可）→ JS で縮小 → ステージングへ追加
    private async Task OnShotFilesSelected()
    {
        if (_shotCardId == null || _shotBusy) return;
        var imgs = await JS.InvokeAsync<ProcessedImage[]>("cardImage.fromInput", _shotInput, ShotMaxDim, ShotQuality);
        AddToStaging(imgs);
    }

    // Ctrl+V 貼り付け（JS から呼ばれる）→ ステージングへ追加
    [JSInvokable]
    public Task OnPastedImages(ProcessedImage[] imgs)
    {
        if (_shotCardId == null || _shotBusy) return Task.CompletedTask;
        AddToStaging(imgs);
        return Task.CompletedTask;
    }

    // 縮小済み画像を溜め置きに加える（上限 ShotMaxCount まで。超過分は捨てて通知）。
    private void AddToStaging(ProcessedImage[] imgs)
    {
        if (imgs == null || imgs.Length == 0) return;
        int space = ShotMaxCount - _shotStaged.Count;
        int added = 0;
        foreach (var im in imgs)
        {
            if (added >= space) break;
            _shotStaged.Add(im);
            added++;
        }
        ImportMessage = added < imgs.Length
            ? $"画像は一度に最大 {ShotMaxCount} 枚までです（{imgs.Length - added} 枚は追加できませんでした）"
            : null;
        StateHasChanged();
    }

    // プレビュー用の data URL（縮小済み base64 をそのまま表示）。
    private static string ThumbSrc(ProcessedImage im) => $"data:{im.MediaType};base64,{im.Data}";

    private void RemoveStaged(int index)
    {
        if (_shotBusy) return;
        if (index >= 0 && index < _shotStaged.Count) _shotStaged.RemoveAt(index);
    }

    // 溜め置きした画像をまとめて読み取る。
    private async Task StartShotRead()
    {
        if (_shotCardId == null || _shotBusy || _shotStaged.Count == 0) return;
        await ProcessShotImages(_shotStaged.ToArray());
    }

    // 各画像を /api/extract-card で読み取り、当月へ増分追加する。
    private async Task ProcessShotImages(ProcessedImage[] imgs)
    {
        var cardId = _shotCardId;
        if (cardId == null || imgs == null || imgs.Length == 0) return;

        _shotBusy = true; _shotTotal = imgs.Length; _shotDone = 0; StateHasChanged();

        var all = new List<CardDetail>();
        string? error = null;
        try
        {
            foreach (var im in imgs)   // 1枚ずつ呼んで集約
            {
                all.AddRange(await Store.ExtractCardImageAsync(cardId, im.Data, im.MediaType));
                _shotDone++; StateHasChanged();
            }
        }
        catch (AccessPendingException) { error = "アクセス承認待ちのため利用できません。"; }
        catch (Exception ex) { error = $"スクショの読み取りに失敗しました: {ex.Message}"; }

        if (error != null)
        {
            _shotBusy = false; _shotCardId = null; _shotStaged.Clear(); ImportMessage = error;
            if (!IsMobile) await JS.InvokeVoidAsync("cardImage.detachPaste");
            StateHasChanged();
            return;
        }

        // 当月へ増分追加：カテゴリ自動分類 → 過去月の重複（再掲）除外 → 当月の完全一致を除外 → 追加
        foreach (var r in all) r.CardId = cardId;
        Svc.ApplyCategoryRules(all);
        var (kept, excludedEarlier) = Svc.DedupAgainstEarlierMonths(Svc.CardMonth, cardId, all);

        int excludedHere = 0;
        var toAdd = new List<CardDetail>();
        foreach (var r in kept)
        {
            bool dup = Mo.CardDetails.Any(d => d.CardId == cardId && d.Date == r.Date && d.Name == r.Name && d.Amount == r.Amount)
                       || toAdd.Any(d => d.Date == r.Date && d.Name == r.Name && d.Amount == r.Amount);
            if (dup) { excludedHere++; continue; }   // 再実行・複数枚の重複追加を防ぐ
            toAdd.Add(r);
        }

        Mo.CardDetails.AddRange(toAdd);
        Svc.RecalcCards(Svc.CardMonth);
        Save();

        var name = Svc.CardById(cardId)?.Name;
        var label = LedgerService.Label(Svc.CardMonth);
        int totalExcluded = excludedEarlier + excludedHere;
        ImportMessage = totalExcluded > 0
            ? $"「{name}」にスクショから明細 {toAdd.Count} 件を追加しました（{label}・{_shotTotal} 枚／重複 {totalExcluded} 件を除外）"
            : $"「{name}」にスクショから明細 {toAdd.Count} 件を追加しました（{label}・{_shotTotal} 枚）";

        _shotBusy = false; _shotCardId = null; _shotStaged.Clear();
        if (!IsMobile) await JS.InvokeVoidAsync("cardImage.detachPaste");
        StateHasChanged();
    }

    public void Dispose() => _shotRef?.Dispose();

    public sealed class ProcessedImage
    {
        public string Data { get; set; } = "";          // base64（data: プレフィックス無し）
        public string MediaType { get; set; } = "image/jpeg";
    }

    // ── 一括カテゴリ（利用先ごと・当月適用＋ルール記憶）──
    private const string KeepSentinel = "__keep__";
    private bool ShowBulk;
    private record StoreGroup(string Store, int Count, decimal Total);
    private List<StoreGroup> BulkGroups = new();
    private Dictionary<string, string> BulkSelection = new();

    // 複数の利用先を選んでまとめて同じカテゴリを設定する（チェック＋一括設定）
    private const string AllFilter = "__all__";
    private HashSet<string> _bulkChecked = new();
    private string _batchCat = "";          // 一括設定で選ぶカテゴリ（""=未分類）
    private string _bulkCatFilter = AllFilter;   // 表示の絞り込み（カテゴリ。AllFilter=すべて）
    private bool _bulkDirty;                // 未適用の変更があるか（キャンセル時の警告用）
    private bool _bulkConfirmCancel;

    // カテゴリで絞り込んだ表示用の利用先一覧（チェック状態はフィルターと独立に保持）
    private List<StoreGroup> DisplayedBulkGroups =>
        _bulkCatFilter == AllFilter
            ? BulkGroups
            : BulkGroups.Where(g => BulkSelection.GetValueOrDefault(g.Store, KeepSentinel) == _bulkCatFilter).ToList();

    private bool AllChecked => DisplayedBulkGroups.Count > 0 && DisplayedBulkGroups.All(g => _bulkChecked.Contains(g.Store));
    private void ToggleAll()
    {
        var disp = DisplayedBulkGroups;
        if (disp.All(g => _bulkChecked.Contains(g.Store)))
            foreach (var g in disp) _bulkChecked.Remove(g.Store);
        else
            foreach (var g in disp) _bulkChecked.Add(g.Store);
    }
    private void ToggleBulkCheck(string store)
    {
        if (!_bulkChecked.Remove(store)) _bulkChecked.Add(store);
    }
    // 行ごとのカテゴリ変更（未適用の編集として記録）
    private void SetRowCategory(string store, string? val)
    {
        BulkSelection[store] = val ?? KeepSentinel;
        _bulkDirty = true;
    }
    // チェックした利用先に _batchCat をまとめて設定し、設定後はチェックを外す
    private void ApplyBatchToChecked()
    {
        foreach (var store in _bulkChecked)
            BulkSelection[store] = _batchCat;
        _bulkChecked.Clear();
        _bulkDirty = true;
    }

    // キャンセル/オーバーレイ閉じ：未適用の編集があれば破棄確認を出す
    private void RequestCloseBulk()
    {
        if (_bulkDirty) _bulkConfirmCancel = true;
        else ShowBulk = false;
    }
    private void DiscardBulk() { _bulkConfirmCancel = false; ShowBulk = false; }
    private void KeepEditingBulk() => _bulkConfirmCancel = false;

    private void OpenBulk()
    {
        BulkGroups = Mo.CardDetails
            .GroupBy(d => d.Name)
            .Select(g => new StoreGroup(g.Key, g.Count(), g.Sum(d => d.Amount)))
            .ToList();

        _bulkChecked.Clear();
        _batchCat = "";
        _bulkCatFilter = AllFilter;
        _bulkDirty = false;
        _bulkConfirmCancel = false;
        BulkSelection = new();
        foreach (var g in BulkGroups)
        {
            // 当該店名の明細カテゴリが揃っていれば初期選択、混在なら「変更しない」
            var cats = Mo.CardDetails.Where(d => d.Name == g.Store)
                .Select(d => d.CategoryId ?? "").Distinct().ToList();
            BulkSelection[g.Store] = cats.Count == 1 ? cats[0] : KeepSentinel;
        }
        SortBulk();   // BulkSelection 構築後に現在の並び順（既定は金額降順）を適用
        ShowBulk = true;
    }

    // ── 一括ダイアログの並び替え（利用先/件数/金額）。既定は金額の降順 ──
    private string _bulkSort = "total";
    private bool _bulkAsc = false;

    private void SetBulkSort(string key)
    {
        if (_bulkSort == key) _bulkAsc = !_bulkAsc;
        else { _bulkSort = key; _bulkAsc = true; }   // 初回クリックは昇順で統一
        SortBulk();
    }

    private void SortBulk()
    {
        Func<StoreGroup, object> key = _bulkSort switch
        {
            "store" => g => g.Store,
            "count" => g => g.Count,
            "category" => g => CategorySortKey(BulkSelection.GetValueOrDefault(g.Store)),
            _ => g => g.Total,
        };
        BulkGroups = (_bulkAsc ? BulkGroups.OrderBy(key) : BulkGroups.OrderByDescending(key)).ToList();
    }

    private MarkupString BulkHead(string key, string label) =>
        new(label + (_bulkSort == key ? (_bulkAsc ? " ▲" : " ▼") : ""));

    private void ApplyBulk()
    {
        foreach (var g in BulkGroups)
        {
            var sel = BulkSelection.GetValueOrDefault(g.Store, KeepSentinel);
            if (sel == KeepSentinel) continue;   // 変更しない

            foreach (var d in Mo.CardDetails.Where(d => d.Name == g.Store))
                d.CategoryId = string.IsNullOrEmpty(sel) ? null : sel;

            // 店名→カテゴリのルールを記憶（未分類選択時は削除）
            if (string.IsNullOrEmpty(sel)) Svc.State.CategoryRules.Remove(g.Store);
            else Svc.State.CategoryRules[g.Store] = sel;
        }
        _bulkDirty = false;
        ShowBulk = false;
        Save();
    }
}
