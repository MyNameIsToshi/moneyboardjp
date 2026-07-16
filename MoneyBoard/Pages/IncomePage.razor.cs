namespace MoneyBoard.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MoneyBoard.Services;
using MoneyBoardShared;

// IncomePage.razor の code-behind。年×月の収入（給与）記録の読み取り・編集・年収サマリー算出を担う。
// 集計本体は IncomeMath（純粋ロジック・テスト対象）へ委譲する。
//
// データの持ち方（#107 実装後の見直し）:
// - 総支給（額面）は月次（settings/month）とは完全に独立した Cosmos ドキュメント income（IncomeStore）
//   に保存する。月次の Ledger には一切書き込まない＝過去・現在・未来のどの月でも自由に記録できる。
// - 手取りは3状態:
//   ①現在月/未来月                     → Ledger.Salary/Bonus を編集・同期（月次管理タブと共有）
//   ②過去月・月次データが既に存在する   → Ledger の凍結値を表示するだけ（編集不可）
//   ③過去月・月次データが存在しない     → income 側の独立フィールド(SalaryNet/BonusNet)を編集
//                                        （月次とは一切連携しない。後から月次にその月ができても同期しない）
//   ①②は Ledger、③は income のみを見る。②③とも Svc.EnsureMonth は呼ばない（月次を新規作成しない）。
public partial class IncomePage
{
    [CascadingParameter(Name = "IsMobile")] public bool IsMobile { get; set; }
    [CascadingParameter(Name = "IsMasked")] public bool IsMasked { get; set; }

    private bool Loaded;
    private bool LoadFailed;

    private int _year;
    private int _month;   // 1-12：フォーカス中の月

    // 手取りの記録先口座。既存の Salary/Bonus は口座ごとに入力可能な設計だが、この画面は
    // 「1年分を1画面で見る」ため代表口座を1つに固定する（#107・issue想定変更「給与受取口座 or 既定口座」）。
    // 給料＝既定口座（並び順の先頭の非財布口座）／賞与＝ボーナス受取口座（#134）が無ければ給料と同じ口座。
    private string? _salaryAccountId;
    private string? _bonusAccountId;
    private bool HasAccount => _salaryAccountId != null;

    private enum NetMode
    {
        SyncedEditable,      // 現在月/未来月：Ledger を編集・同期
        FrozenReadOnly,      // 過去月・月次データあり：Ledger の凍結値を表示のみ
        IndependentEditable, // 過去月・月次データなし：income 側の独立フィールドを編集
    }

    // 年内12ヶ月分の読み取り専用スナップショット（ヒーロー・内訳・月チップ表示用）。
    private List<IncomeMath.MonthIncome> _months = new();
    private IncomeMath.YearSummary _summary = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    // フォーカス中の月・現在月/未来月または月次データが既にある過去月のときだけ、実体の Ledger を保持する。
    private Ledger? _focusSalaryLedger;
    private Ledger? _focusBonusLedger;

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        Loaded = false;
        LoadFailed = false;
        var ledgerOk = Svc.IsLoaded || await Svc.LoadAsync();
        var incomeOk = await IncomeStore.LoadAsync();
        if (ledgerOk && incomeOk)
        {
            Loaded = true;
            var current = Ym.Parse(LedgerService.CurrentCycleStartYm());
            _year = current.Year;
            _month = current.Month;
            ResolveAccounts();
            BuildYear();
            EnsureFocusEditable();
        }
        else if (!IncomeStore.IsPending)
        {
            LoadFailed = true;
        }
    }

    private void ResolveAccounts()
    {
        var accounts = Svc.NonWalletAccounts;
        _salaryAccountId = accounts.FirstOrDefault()?.Id;
        _bonusAccountId = accounts.FirstOrDefault(a => a.IsBonusAccount)?.Id ?? _salaryAccountId;
    }

    private bool MonthExists(string ym) => Svc.State.Months.ContainsKey(ym);

    private NetMode NetModeOf(string ym) =>
        LedgerService.IsCurrentOrFutureCycle(ym) ? NetMode.SyncedEditable :
        MonthExists(ym) ? NetMode.FrozenReadOnly :
        NetMode.IndependentEditable;

    private NetMode FocusNetMode => NetModeOf(FocusYm);
    private bool FocusNetEditable => FocusNetMode != NetMode.FrozenReadOnly;

    private string NetNoteText => FocusNetMode switch
    {
        NetMode.SyncedEditable => "月次と共有",
        NetMode.FrozenReadOnly => "月次の記録値（編集不可）",
        _ => "月次と非連携",
    };

    // 手取りは月が既に存在する場合のみ読む（GraphPage.MonthSum と同じ流儀）。EnsureMonth は呼ばない
    // （このページの表示・読み取りだけで月次展開の副作用を起こさないため）。
    private Ledger? SalaryLedgerOf(string ym) => _salaryAccountId is null ? null
        : Svc.State.Months.GetValueOrDefault(ym)?.Ledgers.GetValueOrDefault(_salaryAccountId);
    private Ledger? BonusLedgerOf(string ym) => _bonusAccountId is null ? null
        : Svc.State.Months.GetValueOrDefault(ym)?.Ledgers.GetValueOrDefault(_bonusAccountId);

    private IncomeMonthRecord? RecordOf(string ym) => IncomeStore.Data.Records.GetValueOrDefault(ym);

    // 手取り（給料）の実効値。SyncedEditable/FrozenReadOnly は Ledger、IndependentEditable は income 側。
    private decimal NetSalaryOf(string ym) => NetModeOf(ym) == NetMode.IndependentEditable
        ? (RecordOf(ym)?.SalaryNet ?? 0)
        : (SalaryLedgerOf(ym)?.Salary ?? 0);
    private decimal NetBonusOf(string ym) => NetModeOf(ym) == NetMode.IndependentEditable
        ? (RecordOf(ym)?.BonusNet ?? 0)
        : (BonusLedgerOf(ym)?.Bonus ?? 0);

    // 賞与の年内回数（IncomeMath.Summarize の bonusMonthsPerYear）。Svc.State.BonusMonths は List<int> のため、
    // 保存経路（BonusMonthSettings.ConfirmBonusMonths）を経ていない古いデータに重複が残っている可能性がある
    // （List.Count は重複をそのまま数えてしまう＝実際のボーナス月数より多く出て想定年収が過大計算されるバグの元）。
    // Distinct してから数えることで、保存データの重複有無に関わらず常に正しい月数になるようにする。
    private int BonusMonthsPerYear => Svc.State.BonusMonths.Distinct().Count();

    private void BuildYear()
    {
        _months = Enumerable.Range(1, 12).Select(m =>
        {
            var ym = new Ym(_year, m).ToString();
            var rec = RecordOf(ym);
            var isBonusMonth = Svc.State.BonusMonths.Contains(m);
            return new IncomeMath.MonthIncome(m, rec?.SalaryGross ?? 0, NetSalaryOf(ym), isBonusMonth, rec?.BonusGross ?? 0, NetBonusOf(ym));
        }).ToList();
        _summary = IncomeMath.Summarize(_months, BonusMonthsPerYear);
    }

    private IncomeMath.MonthIncome FocusMonth => _months[_month - 1];
    private string FocusYm => new Ym(_year, _month).ToString();
    private bool FocusIsBonusMonth => FocusMonth.IsBonusMonth;

    // 手取り編集の実体を用意する。SyncedEditable のときだけ Svc.EnsureMonth を呼ぶ（MonthlyTab の
    // Prev/Next と同じ流儀）。それ以外（FrozenReadOnly/IndependentEditable）は EnsureMonth を呼ばず、
    // 既存データがあれば読むだけ（無ければ null＝0表示。IndependentEditable は income 側で編集する）。
    private void EnsureFocusEditable()
    {
        if (!HasAccount) return;
        if (FocusNetMode == NetMode.SyncedEditable)
        {
            var mo = Svc.EnsureMonth(FocusYm);
            _focusSalaryLedger = _salaryAccountId != null ? mo.Ledgers.GetValueOrDefault(_salaryAccountId) : null;
            _focusBonusLedger = _bonusAccountId != null ? mo.Ledgers.GetValueOrDefault(_bonusAccountId) : null;
        }
        else
        {
            _focusSalaryLedger = SalaryLedgerOf(FocusYm);
            _focusBonusLedger = BonusLedgerOf(FocusYm);
        }
    }

    private void SelectYear(int delta)
    {
        _year += delta;
        BuildYear();
        EnsureFocusEditable();
    }

    private void SelectMonth(int m)
    {
        _month = m;
        EnsureFocusEditable();
    }

    private void PrevMonth()
    {
        if (_month == 1) { _month = 12; _year--; } else _month--;
        BuildYear();
        EnsureFocusEditable();
    }

    private void NextMonth()
    {
        if (_month == 12) { _month = 1; _year++; } else _month++;
        BuildYear();
        EnsureFocusEditable();
    }

    // 入力の都度、月チップ・ヒーロー・内訳の集計を最新化する。
    private void RefreshFocusSnapshot()
    {
        var ym = FocusYm;
        var rec = RecordOf(ym);
        _months[_month - 1] = new IncomeMath.MonthIncome(_month, rec?.SalaryGross ?? 0, NetSalaryOf(ym), FocusMonth.IsBonusMonth, rec?.BonusGross ?? 0, NetBonusOf(ym));
        _summary = IncomeMath.Summarize(_months, BonusMonthsPerYear);
    }

    private IncomeMonthRecord GetOrCreateRecord(string ym)
    {
        if (!IncomeStore.Data.Records.TryGetValue(ym, out var r))
        {
            r = new IncomeMonthRecord();
            IncomeStore.Data.Records[ym] = r;
        }
        return r;
    }

    // 総支給（額面）：月次とは無関係の記録専用データ。過去・現在・未来どの月でも自由に編集できる。
    private void SetSalaryGross(decimal v) { GetOrCreateRecord(FocusYm).SalaryGross = v; IncomeStore.RequestSave(); RefreshFocusSnapshot(); }
    private void SetBonusGross(decimal v) { GetOrCreateRecord(FocusYm).BonusGross = v; IncomeStore.RequestSave(); RefreshFocusSnapshot(); }

    // 手取り：状態に応じて書き込み先を切り替える。FrozenReadOnly は入力欄が Disabled のため到達しない。
    private void SetSalaryNet(decimal v)
    {
        switch (FocusNetMode)
        {
            case NetMode.SyncedEditable:
                if (_focusSalaryLedger != null) { _focusSalaryLedger.Salary = v; Svc.RequestSave(); }
                break;
            case NetMode.IndependentEditable:
                GetOrCreateRecord(FocusYm).SalaryNet = v;
                IncomeStore.RequestSave();
                break;
            case NetMode.FrozenReadOnly:
                return;
        }
        RefreshFocusSnapshot();
    }

    private void SetBonusNet(decimal v)
    {
        switch (FocusNetMode)
        {
            case NetMode.SyncedEditable:
                if (_focusBonusLedger != null) { _focusBonusLedger.Bonus = v; Svc.RequestSave(); }
                break;
            case NetMode.IndependentEditable:
                GetOrCreateRecord(FocusYm).BonusNet = v;
                IncomeStore.RequestSave();
                break;
            case NetMode.FrozenReadOnly:
                return;
        }
        RefreshFocusSnapshot();
    }

    // フォーカス中の手取り入力欄に出す値（モードにより参照元を切り替え）。
    private decimal FocusSalaryNetValue => FocusNetMode == NetMode.IndependentEditable
        ? (RecordOf(FocusYm)?.SalaryNet ?? 0)
        : (_focusSalaryLedger?.Salary ?? 0);
    private decimal FocusBonusNetValue => FocusNetMode == NetMode.IndependentEditable
        ? (RecordOf(FocusYm)?.BonusNet ?? 0)
        : (_focusBonusLedger?.Bonus ?? 0);

    private static bool IsFilled(IncomeMath.MonthIncome m) => m.GrossSalary > 0 || (m.IsBonusMonth && m.GrossBonus > 0);
    private static decimal ChipTotal(IncomeMath.MonthIncome m) => m.GrossSalary + (m.IsBonusMonth ? m.GrossBonus : 0);

    // 想定年収の内訳バー（給料/賞与の積み上げ比率）。予測0時は0%均等（ゼロ割回避）。
    private int SalaryBarPct => _summary.ProjectedGross == 0 ? 0 : (int)Math.Round(_summary.SalaryEstimate / _summary.ProjectedGross * 100);
    private int BonusBarPct => _summary.ProjectedGross == 0 ? 0 : 100 - SalaryBarPct;

    // ── PC専用「この月の確認」カード：選択月の集計＋前月比／前回賞与比（IncomeMath.BuildMonthCheck へ委譲）。
    // 年をまたいだ比較のため、対象年12ヶ月分の _months ではなく全期間の IncomeStore.Data.Records を渡す。
    private IncomeMath.MonthCheck FocusMonthCheck =>
        IncomeMath.BuildMonthCheck(IncomeStore.Data.Records, FocusYm, FocusIsBonusMonth, NetSalaryOf(FocusYm), NetBonusOf(FocusYm));

    // ── PC専用「月別総支給 ミニ棒グラフ」：対象年内の最大月総支給を基準に高さ%を正規化。
    private decimal MaxMonthlyGrossInYear => _months.Count == 0 ? 0 : _months.Max(ChipTotal);
    private int SalaryBarHeightPct(IncomeMath.MonthIncome m) => MaxMonthlyGrossInYear == 0 ? 0 : (int)Math.Round(m.GrossSalary / MaxMonthlyGrossInYear * 100);
    private int BonusBarHeightPct(IncomeMath.MonthIncome m) => MaxMonthlyGrossInYear == 0 ? 0 : (int)Math.Round((m.IsBonusMonth ? m.GrossBonus : 0) / MaxMonthlyGrossInYear * 100);

    // ── ボーナス月設定ダイアログ（マイページ・口座設定と共有の BonusMonthSettings を使い回す）。
    // 背面スクロールロックは他ページ（AccountsTab等）と同じ流儀。内部の確認ダイアログが開いている間も
    // ロックし続けたいので、_showBonusMonthDialog（外枠）だけで判定すれば十分（内側は必ず外枠の内側）。
    private bool _showBonusMonthDialog;
    private bool AnyDialogOpen => _showBonusMonthDialog;
    private bool _scrollLocked;

    // ボーナス月設定を変更した可能性があるため、閉じた時点で年内サマリーを作り直す
    // （BonusMonthSettings は Svc.State.BonusMonths を直接書き換えるが、_months は BuildYear 時点の
    // IsBonusMonth を保持したスナップショットのため、変更を反映するには明示的な再構築が必要）。
    private void CloseBonusMonthDialog()
    {
        _showBonusMonthDialog = false;
        BuildYear();
        EnsureFocusEditable();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (AnyDialogOpen != _scrollLocked)
        {
            _scrollLocked = AnyDialogOpen;
            await JS.InvokeVoidAsync("moneyboardViewport.setBodyScrollLock", _scrollLocked);
            Overlay.SetOpen(nameof(IncomePage), AnyDialogOpen);
        }
    }

    public void Dispose()
    {
        if (_scrollLocked) _ = JS.InvokeVoidAsync("moneyboardViewport.setBodyScrollLock", false);
        Overlay.SetOpen(nameof(IncomePage), false);
    }
}
