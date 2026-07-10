namespace MoneyBoard.Components;

using Microsoft.AspNetCore.Components;
using MoneyBoard.Services;
using MoneyBoardShared;

// FixedCostTab.razor の code-behind。markup・ディレクティブ(@inject)は .razor 側に残し、
// 編集シート・口座フィルター・D&D並べ替え・追加/削除・年月設定の UI ロジックを集約する。
// StartYm/EndYm の解析・整形は Shared の FixedCostPeriod（テスト済み）へ委譲する。
public partial class FixedCostTab
{
    [CascadingParameter(Name = "IsMobile")] public bool IsMobile { get; set; }
    [CascadingParameter(Name = "IsMasked")] public bool IsMasked { get; set; }

    // スマホ：編集シートで開いている固定費。既存は State の実体を直接編集（即時保存）、
    // ＋追加は未コミットのドラフトを編集し、決定（完了）時にだけ State へ追加・保存する。
    private string? _editId;
    private bool _isNew;        // ＋追加のドラフトを編集中か
    private FixedCost? _draft;  // ＋追加のドラフト（決定するまで State に入れない）
    private FixedCost? Editing => _isNew ? _draft : (_editId is null ? null : Svc.State.FixedCosts.FirstOrDefault(f => f.Id == _editId));
    private void OpenEdit(string id) { _editId = id; _isNew = false; _draft = null; }

    // 期限切れ中は名前・口座・金額を編集不可（期間を復活させると自動的に編集可能へ戻る）。
    private bool EditingExpired => Editing is { } fc && FixedCostPeriod.IsExpired(fc, CurrentCycleStart);

    private void CloseEdit()
    {
        // ＋追加のドラフトは「決定」時にだけコミット。項目名が空なら破棄（State には何も作らない）。
        if (_isNew)
        {
            var fc = _draft;
            _isNew = false; _draft = null; _editId = null;
            if (fc is not null && !string.IsNullOrWhiteSpace(fc.Name)) { Svc.State.FixedCosts.Add(fc); SaveWithReload(); }
            return;
        }
        _editId = null;
    }
    private string AccountName(string id) => Svc.ActiveAccounts.FirstOrDefault(a => a.Id == id)?.Name ?? "口座未選択";

    // シートから削除：ドラフトは破棄、既存はシートを閉じてから確認ダイアログを出す（重なり順の都合）。
    private void DeleteEditing()
    {
        if (_isNew) { _isNew = false; _draft = null; _editId = null; return; }
        var id = _editId;
        _editId = null;
        if (id is not null) RemoveFixedCost(id);
    }

    // ＋追加：スマホはドラフトを作って編集シートを開く（State には未追加）。PCは従来の追加ダイアログ。
    private void AddClicked()
    {
        if (!Svc.NonWalletAccounts.Any()) { ShowNoAccountWarn = true; return; }
        if (IsMobile)
        {
            _draft = new FixedCost
            {
                Name = "",
                AccountId = Svc.NonWalletAccounts.First().Id,
                Amount = 0,
                SortOrder = Svc.State.FixedCosts.Count
            };
            _editId = _draft.Id;
            _isNew = true;
        }
        else OpenAddDialog();
    }

    // ── 口座フィルター（Excel風・表示のみ・永続化しない。フィルター中は D&D 無効）──
    // _checked == null は「全口座表示（フィルターなし）」。部分選択時のみ set を保持する。
    private HashSet<string>? _checked;
    private bool _filterOpen;

    private bool IsChecked(string accountId) => _checked == null || _checked.Contains(accountId);
    private bool IsFilterActive => _checked != null;
    // フィルターしていないときだけ手動 D&D 並べ替えを許可する
    private bool IsManualOrder => !IsFilterActive;

    private void ToggleFilterMenu() => _filterOpen = !_filterOpen;

    private void ToggleAccount(string accountId)
    {
        _checked ??= Svc.ActiveAccounts.Select(a => a.Id).ToHashSet();
        if (!_checked.Remove(accountId)) _checked.Add(accountId);
        // 全口座が選択された状態に戻ったらフィルター解除（null 化）
        if (Svc.ActiveAccounts.All(a => _checked.Contains(a.Id))) _checked = null;
    }

    private void ClearFilter() { _checked = null; _filterOpen = false; }

    // 期限切れ（EndYm が当月サイクルより前）は折りたたみグループに分離（#100）。既定は閉じた状態・非永続。
    private bool _expiredOpen = false;
    private void ToggleExpiredGroup() => _expiredOpen = !_expiredOpen;

    private static Ym CurrentCycleStart => Ym.Parse(LedgerService.CurrentCycleStartYm());

    private IEnumerable<FixedCost> FilteredByAccount =>
        _checked == null
            ? Svc.State.FixedCosts                                        // 全表示（手動順）
            : Svc.State.FixedCosts.Where(f => _checked.Contains(f.AccountId));

    // 有効な固定費（口座フィルター適用・手動順のまま）。D&D／▲▼ 並べ替えの対象。
    private IEnumerable<FixedCost> DisplayedFixedCosts =>
        FilteredByAccount.Where(f => !FixedCostPeriod.IsExpired(f, CurrentCycleStart));

    // 期限切れの固定費。終了年月の降順（新しく切れたものが先頭）で表示。並べ替え不可。
    private List<FixedCost> ExpiredFixedCosts =>
        FilteredByAccount.Where(f => FixedCostPeriod.IsExpired(f, CurrentCycleStart))
            .OrderByDescending(f => f.EndBound())
            .ToList();

    private HashSet<string> ExpandedIds = new();
    private void Save() => _ = Svc.SaveAsync();
    // ドラフト編集中（_isNew）は永続化しない（決定時に State へ追加してから保存）。
    // 構造変更・単発イベント用（即時保存）
    private void SaveWithReload() { if (_isNew) return; Svc.OnFixedCostChanged(); _ = Svc.SaveAsync(); }
    // 金額入力など高頻度の編集用（再展開は即時・メモリ内、保存はデバウンス）
    private void RequestSaveWithReload() { if (_isNew) return; Svc.OnFixedCostChanged(); Svc.RequestSave(); }
    private void ToggleExpand(string id) { if (!ExpandedIds.Remove(id)) ExpandedIds.Add(id); }

    // スマホ：▲▼ で並べ替え（フィルター無し時のみ＝IsManualOrder）。表示中（有効）の固定費同士で
    // 隣を入れ替える（期限切れは並べ替え対象外のため、間に挟まっていても無視して隣接を判定する）。
    private void Move(FixedCost fc, int dir)
    {
        var active = DisplayedFixedCosts.ToList();
        int ai = active.IndexOf(fc);
        int aj = ai + dir;
        if (ai < 0 || aj < 0 || aj >= active.Count) return;

        var list = Svc.State.FixedCosts;
        int i = list.IndexOf(active[ai]);
        int j = list.IndexOf(active[aj]);
        (list[i], list[j]) = (list[j], list[i]);
        for (int k = 0; k < list.Count; k++) list[k].SortOrder = k;
        SaveWithReload();
    }

    // ── 追加ダイアログ（#128：スマホの編集シートに合わせ、期間・ボーナス払いも追加時に設定可能）──
    // 期間・ボーナスは NewPeriod（未コミットの FixedCost）上で、既存項目編集と同じ Apply* 純粋ロジックを使って組み立て、
    // ExecuteAdd で確定するまで保存しない（Svc.State には未追加のため SaveWithReload は呼ばない）。
    private bool ShowAddDialog = false;
    private string NewName = "";
    private string NewAccountId = "";
    private decimal NewAmount = 0;
    private bool NewIsVariable = false;
    private FixedCost NewPeriod = new();
    private string AddError = "";

    private bool ShowNoAccountWarn = false;

    private void OpenAddDialog()
    {
        if (!Svc.NonWalletAccounts.Any())
        {
            ShowNoAccountWarn = true;
            return;
        }
        NewName      = "";
        NewAccountId = Svc.NonWalletAccounts.FirstOrDefault()?.Id ?? "";
        NewAmount    = 0;
        NewIsVariable = false;
        NewPeriod    = new();
        AddError     = "";
        ShowAddDialog = true;
    }

    private void CloseAddDialog() => ShowAddDialog = false;

    private void ExecuteAdd()
    {
        if (string.IsNullOrWhiteSpace(NewName))    { AddError = "項目名を入力してください。"; return; }
        if (string.IsNullOrWhiteSpace(NewAccountId)) { AddError = "口座を選択してください。"; return; }

        Svc.State.FixedCosts.Add(new FixedCost
        {
            Name       = NewName.Trim(),
            AccountId  = NewAccountId,
            Amount     = NewAmount,
            IsVariable = NewIsVariable,
            StartYm    = NewPeriod.StartYm,
            EndYm      = NewPeriod.EndYm,
            BonusSettings = NewPeriod.BonusSettings,
            SortOrder  = Svc.State.FixedCosts.Count
        });
        SaveWithReload();
        ShowAddDialog = false;
    }

    // 変動費フラグの切替。既存 FixedCost を直接編集して即時反映（再展開＋保存）。
    private void SetVariable(FixedCost fc, bool value) { fc.IsVariable = value; SaveWithReload(); }

    // ── ドラッグ＆ドロップ並び替え ──────────────────
    private string? DragSourceId = null;
    private string? DragOverId   = null;

    private void OnDragStart(string id) { if (IsManualOrder) DragSourceId = id; }
    private void OnDragOver(string id)  { if (IsManualOrder) DragOverId   = id; }

    private void OnDrop(string targetId)
    {
        if (!IsManualOrder) return;
        if (DragSourceId == null || DragSourceId == targetId) return;

        var list = Svc.State.FixedCosts;
        var srcIdx = list.FindIndex(f => f.Id == DragSourceId);
        var tgtIdx = list.FindIndex(f => f.Id == targetId);
        if (srcIdx < 0 || tgtIdx < 0) return;

        var item = list[srcIdx];
        list.RemoveAt(srcIdx);
        list.Insert(tgtIdx, item);

        // SortOrder を連番に振り直す
        for (int i = 0; i < list.Count; i++) list[i].SortOrder = i;

        SaveWithReload();
    }

    private void OnDragEnd()
    {
        DragSourceId = null;
        DragOverId   = null;
    }

    // ── 削除 ────────────────────────────────────────
    private string? PendingDeleteId = null;
    private bool ShowConfirm = false;

    private void RemoveFixedCost(string id) { PendingDeleteId = id; ShowConfirm = true; }

    private void ExecuteDelete()
    {
        if (PendingDeleteId != null) { Svc.State.FixedCosts.RemoveAll(f => f.Id == PendingDeleteId); SaveWithReload(); }
        PendingDeleteId = null;
        ShowConfirm = false;
    }

    private void CancelDelete() { PendingDeleteId = null; ShowConfirm = false; }

    // 書き換えのみ（保存はしない）。追加ダイアログの未コミットドラフト（NewPeriod）と、
    // 保存を伴う既存項目編集（下の Set*/AddBonus/RemoveBonus）の両方から使う純粋ロジック。
    private static void ApplyAddBonus(FixedCost fc) => fc.BonusSettings.Add(new BonusSetting { Month = 6 });
    private static void ApplyRemoveBonus(FixedCost fc, string bid) => fc.BonusSettings.RemoveAll(b => b.Id == bid);

    private void AddBonus(FixedCost fc)    { ApplyAddBonus(fc);       SaveWithReload(); }
    private void RemoveBonus(FixedCost fc, string bid) { ApplyRemoveBonus(fc, bid); SaveWithReload(); }

    // ── 年月ヘルパー ────────────────────────────────
    // StartYm/EndYm は null / "yyyy"（年のみ）/ "yyyyMM" の3形態。
    // 解析・組み立て・表示整形は FixedCostPeriod（純粋ロジック・テスト対象）へ委譲する。
    private static IEnumerable<int> YearRange() => Enumerable.Range(DateTime.Today.Year - 5, 31);

    private static string StartYear(FixedCost fc)  => FixedCostPeriod.YearPart(fc.StartYm);
    private static string StartMonth(FixedCost fc) => FixedCostPeriod.MonthPart(fc.StartYm);
    private static string EndYear(FixedCost fc)    => FixedCostPeriod.YearPart(fc.EndYm);
    private static string EndMonth(FixedCost fc)   => FixedCostPeriod.MonthPart(fc.EndYm);

    private static void ApplyStartYear(FixedCost fc, string? y)  => fc.StartYm = FixedCostPeriod.ComposeYm(y, StartMonth(fc));
    private static void ApplyStartMonth(FixedCost fc, string? m) => fc.StartYm = FixedCostPeriod.ComposeYm(StartYear(fc), m);
    private static void ApplyEndYear(FixedCost fc, string? y)    => fc.EndYm   = FixedCostPeriod.ComposeYm(y, EndMonth(fc));
    private static void ApplyEndMonth(FixedCost fc, string? m)   => fc.EndYm   = FixedCostPeriod.ComposeYm(EndYear(fc), m);

    private void SetStartYear(FixedCost fc, string? y)  { ApplyStartYear(fc, y);   SaveWithReload(); }
    private void SetStartMonth(FixedCost fc, string? m) { ApplyStartMonth(fc, m);  SaveWithReload(); }
    private void SetEndYear(FixedCost fc, string? y)    { ApplyEndYear(fc, y);     SaveWithReload(); }
    private void SetEndMonth(FixedCost fc, string? m)   { ApplyEndMonth(fc, m);    SaveWithReload(); }

    private static string SummaryText(FixedCost fc) => FixedCostPeriod.Summary(fc);
}
