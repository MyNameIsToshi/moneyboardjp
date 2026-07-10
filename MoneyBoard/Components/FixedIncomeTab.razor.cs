namespace MoneyBoard.Components;

using Microsoft.AspNetCore.Components;
using MoneyBoard.Services;
using MoneyBoardShared;

// FixedIncomeTab.razor の code-behind。markup・ディレクティブ(@inject)は .razor 側に残し、
// 編集シート・口座フィルター・D&D並べ替え・追加/削除・年月設定の UI ロジックを集約する。
// StartYm/EndYm の解析・整形は Shared の FixedCostPeriod（テスト済み）へ委譲する。
// #125 で FixedCostTab から分割（元は固定費（支出）と同居していた収入固定費・#95 のロジック）。
public partial class FixedIncomeTab
{
    [CascadingParameter(Name = "IsMobile")] public bool IsMobile { get; set; }
    [CascadingParameter(Name = "IsMasked")] public bool IsMasked { get; set; }

    private static Ym CurrentCycleStart => Ym.Parse(LedgerService.CurrentCycleStartYm());
    private static IEnumerable<int> YearRange() => Enumerable.Range(DateTime.Today.Year - 5, 31);
    private string AccountName(string id) => Svc.ActiveAccounts.FirstOrDefault(a => a.Id == id)?.Name ?? "口座未選択";

    private bool ShowNoAccountWarn = false;

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

    // スマホ＝編集シートのドラフト、PC＝追加ダイアログ（固定費（支出）と同じ操作感に統一）。
    private void AddIncomeClicked()
    {
        if (!Svc.NonWalletAccounts.Any()) { ShowNoAccountWarn = true; return; }
        if (IsMobile)
        {
            var fi = new FixedIncome
            {
                Name = "",
                AccountId = Svc.NonWalletAccounts.First().Id,
                SortOrder = Svc.State.FixedIncomes.Count
            };
            _draftIncome = fi; _editIncomeId = fi.Id; _isNewIncome = true;
        }
        else OpenAddIncomeDialog();
    }

    // ── 追加ダイアログ（PC・#95。#128でスマホの編集シートに合わせ期間設定も追加時に対応）──
    // 期間は NewIncomePeriod（未コミットの FixedIncome）上で、既存項目編集と同じ Apply* 純粋ロジックを使って組み立て、
    // ExecuteAddIncome で確定するまで保存しない（Svc.State には未追加のため SaveIncomeWithReload は呼ばない）。
    private bool ShowAddIncomeDialog = false;
    private string NewIncomeName = "";
    private string NewIncomeAccountId = "";
    private decimal NewIncomeAmount = 0;
    private bool NewIncomeIsVariable = false;
    private FixedIncome NewIncomePeriod = new();
    private string AddIncomeError = "";

    private void OpenAddIncomeDialog()
    {
        if (!Svc.NonWalletAccounts.Any())
        {
            ShowNoAccountWarn = true;
            return;
        }
        NewIncomeName       = "";
        NewIncomeAccountId  = Svc.NonWalletAccounts.FirstOrDefault()?.Id ?? "";
        NewIncomeAmount     = 0;
        NewIncomeIsVariable = false;
        NewIncomePeriod     = new();
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
            StartYm    = NewIncomePeriod.StartYm,
            EndYm      = NewIncomePeriod.EndYm,
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

    // 書き換えのみ（保存はしない）。追加ダイアログの未コミットドラフト（NewIncomePeriod）と、
    // 保存を伴う既存項目編集（下の Set*）の両方から使う純粋ロジック。
    private static void ApplyStartYear(FixedIncome fi, string? y)  => fi.StartYm = FixedCostPeriod.ComposeYm(y, StartMonth(fi));
    private static void ApplyStartMonth(FixedIncome fi, string? m) => fi.StartYm = FixedCostPeriod.ComposeYm(StartYear(fi), m);
    private static void ApplyEndYear(FixedIncome fi, string? y)    => fi.EndYm   = FixedCostPeriod.ComposeYm(y, EndMonth(fi));
    private static void ApplyEndMonth(FixedIncome fi, string? m)   => fi.EndYm   = FixedCostPeriod.ComposeYm(EndYear(fi), m);

    private void SetStartYear(FixedIncome fi, string? y)  { ApplyStartYear(fi, y);  SaveIncomeWithReload(); }
    private void SetStartMonth(FixedIncome fi, string? m) { ApplyStartMonth(fi, m); SaveIncomeWithReload(); }
    private void SetEndYear(FixedIncome fi, string? y)    { ApplyEndYear(fi, y);    SaveIncomeWithReload(); }
    private void SetEndMonth(FixedIncome fi, string? m)   { ApplyEndMonth(fi, m);   SaveIncomeWithReload(); }
}
