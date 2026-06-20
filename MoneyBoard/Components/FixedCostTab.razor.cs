namespace MoneyBoard.Components;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Components;
using MoneyBoard.Services;
using MoneyBoardShared;

// FixedCostTab.razor の code-behind。markup・ディレクティブ(@inject)は .razor 側に残し、
// 編集シート・口座フィルター・D&D並べ替え・追加/削除・年月設定の UI ロジックを集約する。
// StartYm/EndYm の解析・整形は Shared の FixedCostPeriod（テスト済み）へ委譲する。
public partial class FixedCostTab
{
    [CascadingParameter(Name = "IsMobile")] public bool IsMobile { get; set; }

    // スマホ：編集シートで開いている固定費（id）。直接その固定費を編集（即時保存）。
    private string? _editId;
    private bool _isNew;   // ＋追加で開いた新規項目か（未入力のまま閉じたら破棄する）
    private FixedCost? Editing => _editId is null ? null : Svc.State.FixedCosts.FirstOrDefault(f => f.Id == _editId);
    private void OpenEdit(string id) { _editId = id; _isNew = false; }

    private void CloseEdit()
    {
        // 値未入力（項目名が空）のまま閉じた新規項目は追加しない（破棄）。
        if (_isNew && Editing is { } fc && string.IsNullOrWhiteSpace(fc.Name))
        {
            Svc.State.FixedCosts.RemoveAll(f => f.Id == _editId);
            SaveWithReload();
        }
        _editId = null;
        _isNew = false;
    }
    private string AccountName(string id) => Svc.ActiveAccounts.FirstOrDefault(a => a.Id == id)?.Name ?? "口座未選択";

    // シートから削除：シートを閉じてから確認ダイアログを出す（重なり順の都合）。
    private void DeleteEditing()
    {
        var id = _editId;
        _editId = null;
        if (id is not null) RemoveFixedCost(id);
    }

    // ＋追加：スマホは空の固定費を作ってそのまま編集シートを開く。PCは従来の追加ダイアログ。
    private void AddClicked()
    {
        if (!Svc.ActiveAccounts.Any()) { ShowNoAccountWarn = true; return; }
        if (IsMobile)
        {
            var fc = new FixedCost
            {
                Name = "",
                AccountId = Svc.ActiveAccounts.First().Id,
                Amount = 0,
                SortOrder = Svc.State.FixedCosts.Count
            };
            Svc.State.FixedCosts.Add(fc);
            SaveWithReload();
            _editId = fc.Id;
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

    private IEnumerable<FixedCost> DisplayedFixedCosts =>
        _checked == null
            ? Svc.State.FixedCosts                                        // 全表示（手動順）
            : Svc.State.FixedCosts.Where(f => _checked.Contains(f.AccountId));

    private HashSet<string> ExpandedIds = new();
    private void Save() => _ = Svc.SaveAsync();
    // 構造変更・単発イベント用（即時保存）
    private void SaveWithReload() { Svc.OnFixedCostChanged(); _ = Svc.SaveAsync(); }
    // 金額入力など高頻度の編集用（再展開は即時・メモリ内、保存はデバウンス）
    private void RequestSaveWithReload() { Svc.OnFixedCostChanged(); Svc.RequestSave(); }
    private void ToggleExpand(string id) { if (!ExpandedIds.Remove(id)) ExpandedIds.Add(id); }

    // スマホ：▲▼ で並べ替え（フィルター無し時のみ＝IsManualOrder）。SortOrder を 0..n に振り直す。
    private void Move(FixedCost fc, int dir)
    {
        var list = Svc.State.FixedCosts;
        int i = list.IndexOf(fc);
        int j = i + dir;
        if (i < 0 || j < 0 || j >= list.Count) return;
        (list[i], list[j]) = (list[j], list[i]);
        for (int k = 0; k < list.Count; k++) list[k].SortOrder = k;
        SaveWithReload();
    }

    // ── 追加ダイアログ ──────────────────────────────
    private bool ShowAddDialog = false;
    private string NewName = "";
    private string NewAccountId = "";
    private decimal NewAmount = 0;
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
            Name      = NewName.Trim(),
            AccountId = NewAccountId,
            Amount    = NewAmount,
            SortOrder = Svc.State.FixedCosts.Count
        });
        SaveWithReload();
        ShowAddDialog = false;
    }

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
}
