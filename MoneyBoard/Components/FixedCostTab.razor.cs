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
        if (!Svc.ActiveAccounts.Any()) { ShowNoAccountWarn = true; return; }
        if (IsMobile)
        {
            _draft = new FixedCost
            {
                Name = "",
                AccountId = Svc.ActiveAccounts.First().Id,
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

    // ── 追加ダイアログ ──────────────────────────────
    private bool ShowAddDialog = false;
    private string NewName = "";
    private string NewAccountId = "";
    private decimal NewAmount = 0;
    private bool NewIsVariable = false;
    private string AddError = "";

    private bool ShowNoAccountWarn = false;

    private void OpenAddDialog()
    {
        if (!Svc.ActiveAccounts.Any())
        {
            ShowNoAccountWarn = true;
            return;
        }
        NewName      = "";
        NewAccountId = Svc.ActiveAccounts.FirstOrDefault()?.Id ?? "";
        NewAmount    = 0;
        NewIsVariable = false;
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

    private void AddBonus(FixedCost fc)    { fc.BonusSettings.Add(new BonusSetting { Month = 6 }); SaveWithReload(); }
    private void RemoveBonus(FixedCost fc, string bid) { fc.BonusSettings.RemoveAll(b => b.Id == bid); SaveWithReload(); }

    // ── 年月ヘルパー ────────────────────────────────
    // StartYm/EndYm は null / "yyyy"（年のみ）/ "yyyyMM" の3形態。
    // 解析・組み立て・表示整形は FixedCostPeriod（純粋ロジック・テスト対象）へ委譲する。
    private static IEnumerable<int> YearRange() => Enumerable.Range(DateTime.Today.Year - 5, 31);

    private static string StartYear(FixedCost fc)  => FixedCostPeriod.YearPart(fc.StartYm);
    private static string StartMonth(FixedCost fc) => FixedCostPeriod.MonthPart(fc.StartYm);
    private static string EndYear(FixedCost fc)    => FixedCostPeriod.YearPart(fc.EndYm);
    private static string EndMonth(FixedCost fc)   => FixedCostPeriod.MonthPart(fc.EndYm);

    private void SetStartYear(FixedCost fc, string? y)  { fc.StartYm = FixedCostPeriod.ComposeYm(y, StartMonth(fc)); SaveWithReload(); }
    private void SetStartMonth(FixedCost fc, string? m) { fc.StartYm = FixedCostPeriod.ComposeYm(StartYear(fc), m);  SaveWithReload(); }
    private void SetEndYear(FixedCost fc, string? y)    { fc.EndYm   = FixedCostPeriod.ComposeYm(y, EndMonth(fc));    SaveWithReload(); }
    private void SetEndMonth(FixedCost fc, string? m)   { fc.EndYm   = FixedCostPeriod.ComposeYm(EndYear(fc), m);     SaveWithReload(); }

    private static string SummaryText(FixedCost fc) => FixedCostPeriod.Summary(fc);

    // ═══════════════════════════════════════════════════
    // ── 収入固定費（#95）────────────────────────────
    // 支出の固定費（上記）と同じ操作感（スマホ=編集シート／PC=追加ダイアログ+インライン編集・
    // 口座フィルター・期限切れ折りたたみ）。ボーナス払い（BonusSettings）のみ対象外
    // （カード等のボーナス払いを想定した機能で収入側に自然な対応概念が無いため）。
    // ═══════════════════════════════════════════════════
    private string? _editIncomeId;
    private bool _isNewIncome;
    private FixedIncome? _draftIncome;
    private FixedIncome? EditingIncome => _isNewIncome ? _draftIncome : (_editIncomeId is null ? null : Svc.State.FixedIncomes.FirstOrDefault(f => f.Id == _editIncomeId));
    private void OpenEditIncome(string id) { _editIncomeId = id; _isNewIncome = false; _draftIncome = null; }

    // 期限切れ中は名前・口座・金額を編集不可（期間を復活させると自動的に編集可能へ戻る）。
    private bool EditingIncomeExpired => EditingIncome is { } fi && FixedCostPeriod.IsExpired(fi, CurrentCycleStart);

    private void CloseEditIncome()
    {
        if (_isNewIncome)
        {
            var fi = _draftIncome;
            _isNewIncome = false; _draftIncome = null; _editIncomeId = null;
            if (fi is not null && !string.IsNullOrWhiteSpace(fi.Name)) { Svc.State.FixedIncomes.Add(fi); SaveIncomeWithReload(); }
            return;
        }
        _editIncomeId = null;
    }

    private void DeleteEditingIncome()
    {
        if (_isNewIncome) { _isNewIncome = false; _draftIncome = null; _editIncomeId = null; return; }
        var id = _editIncomeId;
        _editIncomeId = null;
        if (id is not null) RemoveFixedIncome(id);
    }

    // ── 口座フィルター（Excel風・表示のみ・永続化しない。フィルター中は D&D 無効）──
    // _checkedIncome == null は「全口座表示（フィルターなし）」。部分選択時のみ set を保持する。
    private HashSet<string>? _checkedIncome;
    private bool _filterIncomeOpen;

    private bool IsCheckedIncome(string accountId) => _checkedIncome == null || _checkedIncome.Contains(accountId);
    private bool IsFilterIncomeActive => _checkedIncome != null;
    private bool IsManualIncomeOrder => !IsFilterIncomeActive;

    private void ToggleFilterIncomeMenu() => _filterIncomeOpen = !_filterIncomeOpen;

    private void ToggleAccountIncome(string accountId)
    {
        _checkedIncome ??= Svc.ActiveAccounts.Select(a => a.Id).ToHashSet();
        if (!_checkedIncome.Remove(accountId)) _checkedIncome.Add(accountId);
        if (Svc.ActiveAccounts.All(a => _checkedIncome.Contains(a.Id))) _checkedIncome = null;
    }

    private void ClearIncomeFilter() { _checkedIncome = null; _filterIncomeOpen = false; }

    // 期限切れ（EndYm が当月サイクルより前）は折りたたみグループに分離（#100 と同じ扱い）。既定は閉じた状態・非永続。
    private bool _expiredIncomeOpen;
    private void ToggleExpiredIncomeGroup() => _expiredIncomeOpen = !_expiredIncomeOpen;

    private IEnumerable<FixedIncome> FilteredIncomeByAccount =>
        _checkedIncome == null
            ? Svc.State.FixedIncomes
            : Svc.State.FixedIncomes.Where(f => _checkedIncome.Contains(f.AccountId));

    // 有効な収入固定費（口座フィルター適用・手動順のまま）。D&D／▲▼ 並べ替えの対象。
    private IEnumerable<FixedIncome> DisplayedFixedIncomes =>
        FilteredIncomeByAccount.Where(f => !FixedCostPeriod.IsExpired(f, CurrentCycleStart));

    // 期限切れの収入固定費。終了年月の降順（新しく切れたものが先頭）で表示。並べ替え不可。
    private List<FixedIncome> ExpiredFixedIncomes =>
        FilteredIncomeByAccount.Where(f => FixedCostPeriod.IsExpired(f, CurrentCycleStart))
            .OrderByDescending(f => f.EndBound())
            .ToList();

    // スマホ＝編集シートのドラフト、PC＝追加ダイアログ（支出の固定費と同じ操作感に統一）。
    private void AddIncomeClicked()
    {
        if (!Svc.ActiveAccounts.Any()) { ShowNoAccountWarn = true; return; }
        if (IsMobile)
        {
            var fi = new FixedIncome
            {
                Name = "",
                AccountId = Svc.ActiveAccounts.First().Id,
                SortOrder = Svc.State.FixedIncomes.Count
            };
            _draftIncome = fi; _editIncomeId = fi.Id; _isNewIncome = true;
        }
        else OpenAddIncomeDialog();
    }

    // ── 追加ダイアログ（PC・#95）──────────────────────
    private bool ShowAddIncomeDialog = false;
    private string NewIncomeName = "";
    private string NewIncomeAccountId = "";
    private decimal NewIncomeAmount = 0;
    private bool NewIncomeIsVariable = false;
    private string AddIncomeError = "";

    private void OpenAddIncomeDialog()
    {
        if (!Svc.ActiveAccounts.Any())
        {
            ShowNoAccountWarn = true;
            return;
        }
        NewIncomeName       = "";
        NewIncomeAccountId  = Svc.ActiveAccounts.FirstOrDefault()?.Id ?? "";
        NewIncomeAmount     = 0;
        NewIncomeIsVariable = false;
        AddIncomeError      = "";
        ShowAddIncomeDialog = true;
    }

    private void CloseAddIncomeDialog() => ShowAddIncomeDialog = false;

    private void ExecuteAddIncome()
    {
        if (string.IsNullOrWhiteSpace(NewIncomeName))      { AddIncomeError = "項目名を入力してください。"; return; }
        if (string.IsNullOrWhiteSpace(NewIncomeAccountId)) { AddIncomeError = "口座を選択してください。"; return; }

        Svc.State.FixedIncomes.Add(new FixedIncome
        {
            Name       = NewIncomeName.Trim(),
            AccountId  = NewIncomeAccountId,
            Amount     = NewIncomeIsVariable ? 0 : NewIncomeAmount,
            IsVariable = NewIncomeIsVariable,
            SortOrder  = Svc.State.FixedIncomes.Count
        });
        SaveIncomeWithReload();
        ShowAddIncomeDialog = false;
    }

    private void SaveIncomeWithReload() { if (_isNewIncome) return; Svc.OnFixedIncomeChanged(); _ = Svc.SaveAsync(); }
    private void RequestSaveIncomeWithReload() { if (_isNewIncome) return; Svc.OnFixedIncomeChanged(); Svc.RequestSave(); }

    private void SetIncomeVariable(FixedIncome fi, bool value) { fi.IsVariable = value; SaveIncomeWithReload(); }

    private readonly HashSet<string> ExpandedIncomeIds = new();
    private void ToggleExpandIncome(string id) { if (!ExpandedIncomeIds.Remove(id)) ExpandedIncomeIds.Add(id); }

    // スマホ：▲▼ で並べ替え（フィルター無し時のみ＝IsManualIncomeOrder）。表示中（有効）の収入固定費同士で
    // 隣を入れ替える（期限切れは並べ替え対象外のため、間に挟まっていても無視して隣接を判定する）。
    private void MoveIncome(FixedIncome fi, int dir)
    {
        var active = DisplayedFixedIncomes.ToList();
        int ai = active.IndexOf(fi);
        int aj = ai + dir;
        if (ai < 0 || aj < 0 || aj >= active.Count) return;

        var list = Svc.State.FixedIncomes;
        int i = list.IndexOf(active[ai]);
        int j = list.IndexOf(active[aj]);
        (list[i], list[j]) = (list[j], list[i]);
        for (int k = 0; k < list.Count; k++) list[k].SortOrder = k;
        SaveIncomeWithReload();
    }

    // ── ドラッグ＆ドロップ並び替え（PC）──────────────
    private string? DragSourceIncomeId;
    private string? DragOverIncomeId;

    private void OnIncomeDragStart(string id) { if (IsManualIncomeOrder) DragSourceIncomeId = id; }
    private void OnIncomeDragOver(string id) { if (IsManualIncomeOrder) DragOverIncomeId = id; }

    private void OnIncomeDrop(string targetId)
    {
        if (!IsManualIncomeOrder) return;
        if (DragSourceIncomeId == null || DragSourceIncomeId == targetId) return;

        var list = Svc.State.FixedIncomes;
        var srcIdx = list.FindIndex(f => f.Id == DragSourceIncomeId);
        var tgtIdx = list.FindIndex(f => f.Id == targetId);
        if (srcIdx < 0 || tgtIdx < 0) return;

        var item = list[srcIdx];
        list.RemoveAt(srcIdx);
        list.Insert(tgtIdx, item);

        // SortOrder を連番に振り直す
        for (int i = 0; i < list.Count; i++) list[i].SortOrder = i;

        SaveIncomeWithReload();
    }

    private void OnIncomeDragEnd()
    {
        DragSourceIncomeId = null;
        DragOverIncomeId = null;
    }

    // ── 削除 ────────────────────────────────────────
    private string? PendingDeleteIncomeId;
    private bool ShowIncomeConfirm;

    private void RemoveFixedIncome(string id) { PendingDeleteIncomeId = id; ShowIncomeConfirm = true; }

    private void ExecuteIncomeDelete()
    {
        if (PendingDeleteIncomeId != null) { Svc.State.FixedIncomes.RemoveAll(f => f.Id == PendingDeleteIncomeId); SaveIncomeWithReload(); }
        PendingDeleteIncomeId = null;
        ShowIncomeConfirm = false;
    }

    private void CancelIncomeDelete() { PendingDeleteIncomeId = null; ShowIncomeConfirm = false; }

    // ── 年月ヘルパー（FixedCostPeriod を再利用・FixedIncome 版のオーバーロード）──
    private static string StartYear(FixedIncome fi)  => FixedCostPeriod.YearPart(fi.StartYm);
    private static string StartMonth(FixedIncome fi) => FixedCostPeriod.MonthPart(fi.StartYm);
    private static string EndYear(FixedIncome fi)    => FixedCostPeriod.YearPart(fi.EndYm);
    private static string EndMonth(FixedIncome fi)   => FixedCostPeriod.MonthPart(fi.EndYm);

    private void SetStartYear(FixedIncome fi, string? y)  { fi.StartYm = FixedCostPeriod.ComposeYm(y, StartMonth(fi)); SaveIncomeWithReload(); }
    private void SetStartMonth(FixedIncome fi, string? m) { fi.StartYm = FixedCostPeriod.ComposeYm(StartYear(fi), m);  SaveIncomeWithReload(); }
    private void SetEndYear(FixedIncome fi, string? y)    { fi.EndYm   = FixedCostPeriod.ComposeYm(y, EndMonth(fi));    SaveIncomeWithReload(); }
    private void SetEndMonth(FixedIncome fi, string? m)   { fi.EndYm   = FixedCostPeriod.ComposeYm(EndYear(fi), m);     SaveIncomeWithReload(); }
}
