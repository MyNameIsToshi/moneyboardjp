namespace MoneyBoard.Pages;

using ApexCharts;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MoneyBoard.Components;
using MoneyBoard.Services;
using MoneyBoardShared;
using static MoneyBoard.MoneyFormat;

// GraphPage.razor の code-behind。markup・ディレクティブ(@page/@inject/@using)は .razor 側に残し、
// 統計の集計・期間処理・ドリルダウン状態をこの partial class に集約する。
public partial class GraphPage
{
    private string SelectedPeriod = "3";
    private Dictionary<string, string> Periods = new()
    {
        { "current", "当月" }, { "3", "3ヶ月" }, { "6", "6ヶ月" }, { "12", "12ヶ月" }, { "all", "全期間" }
    };

    // カテゴリ別/カード別 ドーナツ＋一覧の集計軸（#105）。"billing"=請求月（月次ドキュメント所属・既定）／
    // "usage"=利用月（CardDetail.Date の年月）。(B)「カテゴリ別 月別利用推移」はこのトグルの影響を受けず常に利用月固定。
    private string SpendAxis = "billing";

    private List<ChartPoint> MonthlyDebitData = new();
    // 系列順が色割当順（BalancePalette）と対応するため、順序が保証される List で保持する（#157）。
    private List<(string Name, List<ChartPoint> Data)> BalanceSeriesData = new();
    private List<ChartPoint> SalaryData = new();
    private List<ChartPoint> BonusData = new();
    private List<ChartPoint> IncomeData = new();
    private List<ChartPoint> FixedCostData = new();
    // メイン・コンボの収支折れ線（月別 収入−支出。支出は MonthlyDebitData と同義で、棒2本と整合）
    private List<ChartPoint> NetData = new();

    // ── 要約バンド（ヒーロー＋指標4枚）。既存の月別系列を期間合算した派生値（新ロジックなし）──
    // 収入合計＝給料+ボーナス+臨時。支出合計＝Debits全件（固定費は月初展開で既にDebitsに記帳済み＝固定費込み）。
    // FixedTotal も Debits 内の IsFixed 分の合計（マスタ再計算ではない＝ExpenseTotal と同一ソースなので必ず ExpenseTotal 以下）。
    // ExpenseTotal 内訳の表示・比率算出にのみ使い、ExpenseTotal には加算しない（二重計上防止）。
    private decimal IncomeTotal => IncomeData.Sum(p => p.Value);
    private decimal FixedTotal => FixedCostData.Sum(p => p.Value);
    private decimal ExpenseTotal => MonthlyDebitData.Sum(p => p.Value);
    private decimal NetTotal => IncomeTotal - ExpenseTotal;
    private bool IsSurplus => NetTotal >= 0;
    // 貯蓄率＝期間収支 / 収入合計（収入0なら null＝「—」表示）
    private double? SavingsRate => IncomeTotal == 0 ? null : (double)(NetTotal / IncomeTotal);
    // 固定費が支出に占める割合（補助行）
    private int FixedPctOfExpense => ExpenseTotal == 0 ? 0 : (int)Math.Round((double)(FixedTotal / ExpenseTotal) * 100);

    // ③ 収入の内訳系列（給料・ボーナス・各臨時収入名）。積み上げ棒で表示。
    private List<IncomeSeries> IncomeBreakdown = new();
    private record IncomeSeries(string Name, List<ChartPoint> Data);

    [CascadingParameter(Name = "IsMasked")] public bool IsMasked { get; set; }

    // いずれかのダイアログ表示中は背面スクロールをロック（明細ドリルダウン/内訳ダイアログ）。
    private bool AnyDialogOpen => _detail is not null || _breakdown is not null;
    private bool _scrollLocked;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (AnyDialogOpen != _scrollLocked)
        {
            _scrollLocked = AnyDialogOpen;
            await JS.InvokeVoidAsync("moneyboardViewport.setBodyScrollLock", _scrollLocked);
            Overlay.SetOpen(nameof(GraphPage), AnyDialogOpen);
        }
    }

    public void Dispose()
    {
        if (_scrollLocked) _ = JS.InvokeVoidAsync("moneyboardViewport.setBodyScrollLock", false);
        Overlay.SetOpen(nameof(GraphPage), false);
    }

    // マスク切替のたびにインクリメント → チャート @key に含めて強制再生成。
    private int _maskRev;
    private bool _prevMasked;

    // Y軸・ツールチップのフォーマッタ（マスク時は ¥****）。YFmt＝ApexCharts の型名 YAxis との衝突回避。
    private string YFmt => IsMasked ? "function(v){return '¥****'}" : MoneyFormat.ChartYenMan;
    private string YTip => IsMasked ? "function(v){return '¥****'}" : MoneyFormat.ChartYenFull;

    // ApexChartOptions はチャート固有の状態を書き込むため、1インスタンスを複数の
    // <ApexChart> で共有すると最初の1つしか描画されない。チャートごとに専用インスタンスを持つ。
    // IsMasked 変化時に RebuildChartOptions() で差し替えるため readonly を外す。
    private ApexChartOptions<ChartPoint> ComboOptions = default!;
    private ApexChartOptions<ChartPoint> BalanceLineOptions = default!;
    private ApexChartOptions<ChartPoint> IncomeBreakdownOptions = default!;
    private ApexChartOptions<ChartPoint> CategoryTrendOptions = default!;
    private ApexChartOptions<ChartPoint> CardTrendOptions = default!;

    private static Grid SoftGrid() => new() { BorderColor = "#f0eee9" };
    private ApexChartOptions<ChartPoint> NewLineOptions() => new()
    {
        Chart = new Chart { Height = 240, Toolbar = new Toolbar { Show = false } },
        Stroke = new Stroke { Curve = Curve.Smooth },
        Grid = SoftGrid(),
        Tooltip = new Tooltip { Y = new TooltipY { Formatter = YTip } },
        Yaxis = new List<YAxis> { new() { Labels = new YAxisLabels { Formatter = YFmt } } }
    };

    // sharedTooltip=true：積み上げ棒でホバー時に系列（要素）単位ではなく、その月全体を1つとして
    // ハイライト・ツールチップ表示する（#105・カテゴリ別/カード別月別推移で使用。タップ時のドリルダウンが
    // 系列に関わらず「その月全体」の内容を開く仕様のため、ホバーの見た目もそれに合わせる。要望で追加）。
    private ApexChartOptions<ChartPoint> NewBarOptions(bool stacked = false, bool sharedTooltip = false) => new()
    {
        Chart = new Chart { Height = 240, Stacked = stacked, Toolbar = new Toolbar { Show = false } },
        Grid = SoftGrid(),
        // sharedTooltip=false のときは Shared/Intersect を未設定のままにして ApexCharts 既定
        // （shared=true・intersect=false）を保つ（この共通関数を使う既存チャート＝収入の内訳推移の
        // ツールチップ挙動を変えないため）。sharedTooltip=true のときだけ shared=true を明示する
        // （intersect は既定 false のままでよい＝shared=true と両立できる。両方 true だと ApexCharts が例外）。
        Tooltip = sharedTooltip
            ? new Tooltip { Shared = true, Intersect = false, Y = new TooltipY { Formatter = YTip } }
            : new Tooltip { Y = new TooltipY { Formatter = YTip } },
        Yaxis = new List<YAxis> { new() { Labels = new YAxisLabels { Formatter = YFmt } } },
        // shared 時は states.hover の既定ダークンフィルタ（要素＝系列単位のハイライト）が shared の
        // グレー帯（月単位のハイライト）と同時に効いてちらつくため無効化する（実機確認で発覚・要望対応）。
        States = sharedTooltip
            ? new States { Hover = new StatesHover { Filter = new StatesFilter { Type = StatesFilterType.none } } }
            : null
    };

    // メイン：収入(棒)・支出(棒)＋収支(折れ線)のコンボ。色は既存トークン（収入=緑/支出=赤/収支線=navy）。
    private ApexChartOptions<ChartPoint> NewComboOptions() => new()
    {
        Chart = new Chart { Height = 340, Toolbar = new Toolbar { Show = false } },
        Colors = new List<string> { "#0f6e56", "#a3261f", "#1f3a5f" },
        Stroke = new Stroke { Width = new List<int> { 0, 0, 3 }, Curve = Curve.Smooth },
        Grid = SoftGrid(),
        Tooltip = new Tooltip { Y = new TooltipY { Formatter = YTip } },
        Yaxis = new List<YAxis> { new() { Labels = new YAxisLabels { Formatter = YFmt } } }
    };

    private void RebuildChartOptions()
    {
        // 色を持つチャート（ドーナツ・月別推移）は BuildChartData でしか色を再設定しない一方、
        // マスク切替ではこのメソッドだけが呼ばれ BuildChartData は呼ばれないため、再生成前に
        // 旧インスタンスの Colors を退避し、再生成後に引き継ぐ（引き継がないとマスク切替で
        // カテゴリ色/カードパレットが ApexCharts 既定色に戻り、ドーナツと不整合になる）。
        var catColors = DonutOptions?.Colors;
        var cardColors = CardDonutOptions?.Colors;
        var catTrendColors = CategoryTrendOptions?.Colors;
        var cardTrendColors = CardTrendOptions?.Colors;

        ComboOptions = NewComboOptions();
        BalanceLineOptions = NewLineOptions();
        IncomeBreakdownOptions = NewBarOptions(stacked: true);
        CategoryTrendOptions = NewBarOptions(stacked: true, sharedTooltip: true);
        CardTrendOptions = NewBarOptions(stacked: true, sharedTooltip: true);
        DonutOptions = NewDonutOptions();
        CardDonutOptions = NewDonutOptions();

        if (catColors != null) DonutOptions.Colors = catColors;
        if (cardColors != null) CardDonutOptions.Colors = cardColors;
        if (catTrendColors != null) CategoryTrendOptions.Colors = catTrendColors;
        if (cardTrendColors != null) CardTrendOptions.Colors = cardTrendColors;
        _maskRev++;
    }

    protected override void OnInitialized()
    {
        // OnInitializedAsync より先に実行 → Load() が使う前にオプションを確保する。
        RebuildChartOptions();
        _maskRev = 0; // 初回は rev を 0 に戻す（RebuildChartOptions が ++するため）
    }

    protected override void OnParametersSet()
    {
        if (IsMasked != _prevMasked)
        {
            _prevMasked = IsMasked;
            RebuildChartOptions();
        }
    }

    private List<SpendSlice> CategorySpendData = new();
    private decimal CategoryTotal => CategorySpendData.Sum(s => s.Value);

    // ドリルダウン（カテゴリ別／カード別 共通モーダル）。明細は日付降順で保持する。
    private Dictionary<string, List<DetailDialog.DetailRow>> CategoryDetails = new();   // カテゴリキー → 明細（3列目=カード名）
    private Dictionary<string, List<DetailDialog.DetailRow>> CardDetails = new();       // カードキー   → 明細（3列目=カテゴリ名）
    private DetailModal? _detail;   // 開いているモーダル（null=閉）
    // 現在開いているダイアログが (B)(C) 月別推移の棒タップ由来か（そのymを保持。null=別の起動元）。
    // 由来の場合のみダイアログ内に「集計別/明細別」トグル（ToolbarExtra）を出す（#105）。
    private string? _trendYm;

    private void OpenCatDetail(string key)
    {
        var s = CategorySpendData.FirstOrDefault(x => x.Key == key);
        _trendYm = null;
        // 未分類（CategoryId 空）のドリルダウンのみ「カテゴリ設定」操作を出す
        if (s != null) _detail = new(s.Label, s.Color, s.Count, s.Value, CategoryDetails.GetValueOrDefault(key) ?? new(), string.IsNullOrEmpty(key));
    }
    private void OpenCardDetail(string key)
    {
        var s = CardSpendData.FirstOrDefault(x => x.Key == key);
        _trendYm = null;
        if (s != null) _detail = new(s.Label, s.Color, s.Count, s.Value, CardDetails.GetValueOrDefault(key) ?? new(), false);
    }
    private void CloseDetail() { _detail = null; _trendYm = null; }

    // 「選択してカテゴリ設定」（#118）：DetailRow.Id → 実体（CardDetail/Debit）へ反映するデリゲート。
    // 月をまたいだ複数の CardDetail/Debit を直接書き換えるため、BuildCategorySpend の再構築ごとに作り直す。
    private Dictionary<string, Action<string?>> _categorizeTargets = new();

    // DetailDialog から選択された行 Id 群へまとめてカテゴリを反映し、保存・再集計する。
    private void ApplyCategorize((HashSet<string> Ids, string? CategoryId) req)
    {
        foreach (var id in req.Ids)
            if (_categorizeTargets.TryGetValue(id, out var setter))
                setter(req.CategoryId);
        _ = Svc.SaveAsync();
        BuildChartData();
        RefreshCategorizeDialog();
    }

    // 反映後、開いている「未分類」ドリルダウンを最新の集計で更新する（対象が0件になった場合は閉じる）。
    private void RefreshCategorizeDialog()
    {
        _trendYm = null;
        var s = CategorySpendData.FirstOrDefault(x => x.Key == "");
        _detail = s != null
            ? new(s.Label, s.Color, s.Count, s.Value, CategoryDetails.GetValueOrDefault("") ?? new(), true)
            : null;
    }

    // ドーナツのスライス選択でも同じモーダルを開く（スライス順=各 SpendData 順）
    private void OnSliceSelected(SelectedData<SpendSlice> sel)
    {
        if (sel.DataPointIndex >= 0 && sel.DataPointIndex < CategorySpendData.Count)
            OpenCatDetail(CategorySpendData[sel.DataPointIndex].Key);
    }
    private void OnCardSliceSelected(SelectedData<SpendSlice> sel)
    {
        if (sel.DataPointIndex >= 0 && sel.DataPointIndex < CardSpendData.Count)
            OpenCardDetail(CardSpendData[sel.DataPointIndex].Key);
    }

    // SubLabel は既定 ""＝呼び出し元は RangeLabel（期間全体）を表示する既存パターンのまま。
    // 単月ドリルダウン（(B) の月別利用推移タップ等）は明示的に月ラベルを渡して上書きする。
    private record DetailModal(string Title, string Color, int Count, decimal Total, List<DetailDialog.DetailRow> Rows, bool ShowCategorize, string SubLabel = "");

    // ── 収入/支出の項目別内訳モーダル（④・⑤から起動＝期間合計。コンボ棒タップ＝タップした月のみ）──
    private record BreakdownModal(string Title, string SubLabel, decimal Total, List<BreakdownDialog.BreakdownItem> Items);
    private BreakdownModal? _breakdown;
    private void CloseBreakdown() { _breakdown = null; _trendYm = null; }

    // メインコンボの収入棒→収入内訳、支出棒→支出内訳（系列0=収入, 1=支出, 2=収支線=ドリルダウンなし）。
    // DataPointIndex＝タップした月（GetTargetYms() の並びと一致）で対象月を1つだけに絞る。
    private void OnIncomeVsExpenseSelected(SelectedData<ChartPoint> sel)
    {
        var yms = GetTargetYms();
        if (sel.DataPointIndex < 0 || sel.DataPointIndex >= yms.Count) return;
        var ym = yms[sel.DataPointIndex];
        var label = LedgerService.Label(ym);
        if (sel.SeriesIndex == 0) OpenIncomeBreakdown(new List<string> { ym }, $"{label}の収入内訳", $"{label}（1ヶ月）");
        else if (sel.SeriesIndex == 1) OpenExpenseBreakdown(new List<string> { ym }, $"{label}の支出内訳", $"{label}（1ヶ月）");
    }

    // ④の「収入 合計」ボタン用（対象期間全体を集計）
    private void OpenIncomeBreakdown() => OpenIncomeBreakdown(GetTargetYms(), "収入の内訳", RangeLabel);

    // 指定 yms の収入を項目（給料/ボーナス/各臨時収入名）で合算
    private void OpenIncomeBreakdown(List<string> yms, string title, string subLabel)
    {
        _trendYm = null;
        var items = new List<BreakdownDialog.BreakdownItem>
        {
            new("給料", yms.Sum(ym => MonthSum(ym, l => l.Salary))),
            new("ボーナス", yms.Sum(ym => MonthSum(ym, l => l.Bonus))),
        };
        items.AddRange(LedgersIn(yms)
            .SelectMany(l => l.Incomes)
            .GroupBy(IncomeName)
            .Select(g => new BreakdownDialog.BreakdownItem(g.Key, g.Sum(i => i.Amount))));

        items = items.Where(x => x.Amount != 0).OrderByDescending(x => x.Amount).ToList();
        _breakdown = new(title, subLabel, items.Sum(x => x.Amount), items);
    }

    // ⑤の「支出 合計」ボタン用（対象期間全体を集計）
    private void OpenExpenseBreakdown() => OpenExpenseBreakdown(GetTargetYms(), "支出の内訳", RangeLabel);

    // 指定 yms の支出を項目（月次の Debit 名。カードはカード名で1項目・ATMは対象外）で合算
    private void OpenExpenseBreakdown(List<string> yms, string title, string subLabel)
    {
        _trendYm = null;
        var items = LedgersIn(yms)
            .SelectMany(l => l.Debits)
            .GroupBy(d => string.IsNullOrWhiteSpace(d.Name) ? "（名称なし）" : d.Name)
            .Select(g => new BreakdownDialog.BreakdownItem(g.Key, g.Sum(d => d.Amount)))
            .Where(x => x.Amount != 0)
            .OrderByDescending(x => x.Amount)
            .ToList();
        _breakdown = new(title, subLabel, items.Sum(x => x.Amount), items);
    }

    // 対象期間の固定費を項目（固定費マスタID）ごとに合算。マスタの現在値を再計算するのではなく、
    // 各月生成時に Debits へ実際に記帳された金額（IsFixed）を合計する（月次管理の表示と一致させる。
    // マスタ変更後は過去月の Debits は据え置きのため、再計算するとマスタ変更前後で二重計上・不整合が生じる）。
    private void OpenFixedBreakdown()
    {
        _trendYm = null;
        var yms = GetTargetYms();
        var items = LedgersIn(yms)
            .SelectMany(l => l.Debits)
            .Where(d => d.IsFixed)
            .GroupBy(d => d.FixedCostId ?? d.Name)
            .Select(g => new BreakdownDialog.BreakdownItem(g.First().Name, g.Sum(d => d.Amount)))
            .Where(x => x.Amount != 0)
            .OrderByDescending(x => x.Amount)
            .ToList();
        _breakdown = new("固定費の内訳", RangeLabel, items.Sum(x => x.Amount), items);
    }

    private List<SpendSlice> CardSpendData = new();
    private decimal CardTotal => CardSpendData.Sum(s => s.Value);

    // カードは色設定を持たないため、表示順に割り当てる固定パレット。
    private static readonly string[] CardPalette = MoneyFormat.DonutPalette;

    // ② 口座別月末残高推移の線色（spec §5：青/橙/赤/緑をローテ。紫/青緑を追加し4→6色に拡張・#157。
    // 口座が5件以上あると4色では循環して同色が発生していたため）。
    private static readonly string[] BalancePalette =
        { "#3a52c0", "#b86a18", "#a3261f", "#2c7a52", "#6b4c9a", "#1f7d7a" };
    // ③ 収入内訳：給料=navy／ボーナス=緑／臨時収入=ゴールド（spec §5）。
    private const string IncomeGold = "#c9a23a";

    // ドーナツ共通設定（カテゴリ/カードで別インスタンスにする。1インスタンスを
    // 複数の <ApexChart> で共有すると最初の1つしか描画されないため）。
    // 既定のホバー効果(lighten)だと薄い色(未分類のグレー)が白飛びするため、わずかに暗くする
    private ApexChartOptions<SpendSlice> NewDonutOptions() => new()
    {
        Chart = new Chart { Height = 300, Toolbar = new Toolbar { Show = false } },
        Legend = new Legend { Position = LegendPosition.Bottom },
        Tooltip = new Tooltip { Y = new TooltipY { Formatter = YTip } },
        States = new States
        {
            Hover = new StatesHover { Filter = new StatesFilter { Type = StatesFilterType.darken, Value = 0.12 } },
            Active = new StatesActive { Filter = new StatesFilter { Type = StatesFilterType.darken, Value = 0.12 } }
        }
    };

    private ApexChartOptions<SpendSlice> DonutOptions = default!;
    private ApexChartOptions<SpendSlice> CardDonutOptions = default!;

    // 読み込み完了まで操作不可（/graph を直接リロードしたケースに対応）
    private bool Loaded;
    private bool LoadFailed;

    // 期間指定（月単位）の開始・終了 ym
    private string _customStart = "";
    private string _customEnd = "";

    private List<string> AllYmsAsc => Svc.State.Months.Keys.OrderBy(x => x).ToList();

    // チャート @key 用。"custom" は SelectedPeriod だけでは変化を検出できないため _customStart/_customEnd を含める。
    private string PeriodKey => SelectedPeriod == "custom" ? $"custom-{_customStart}-{_customEnd}" : SelectedPeriod;

    protected override async Task OnInitializedAsync() => await Load();

    private async Task Load()
    {
        Loaded = false;
        LoadFailed = false;
        // State はアプリ起動時(Home)にメモリへ読込済み。未ロード（直接リロード等）のときだけ取得。
        if (Svc.IsLoaded || await Svc.LoadAsync())
            Loaded = true;
        else
        {
            LoadFailed = true;
            return;
        }
        BuildChartData();
    }

    private void SetPeriod(string p)
    {
        SelectedPeriod = p;
        _detail = null;
        _breakdown = null;
        _trendYm = null;
        // 期間指定に切替時、未設定なら全期間の端を初期値にする
        if (p == "custom" && string.IsNullOrEmpty(_customStart))
        {
            var yms = AllYmsAsc;
            if (yms.Count > 0) { _customStart = yms.First(); _customEnd = yms.Last(); }
        }
        BuildChartData();
    }

    private void OnCustomChanged() { _detail = null; _breakdown = null; _trendYm = null; BuildChartData(); }

    // 集計軸トグル（#105）：カテゴリ別/カード別 ドーナツ+一覧の期間メンバーシップを切り替える。
    private void SetSpendAxis(string axis)
    {
        if (SpendAxis == axis) return;
        SpendAxis = axis;
        _detail = null;
        _breakdown = null;
        _trendYm = null;
        BuildChartData();
    }

    // 期間選択→対象 ym（昇順）。計算本体は StatsMath（純粋ロジック・テスト対象）へ委譲する。
    // 「当月」は未来月を先行作成済みでも実際の給料サイクル(15日〜14日)を指すよう、
    // 現在時刻に依存する起点計算だけ LedgerService（呼び出し側）から渡す。
    private List<string> GetTargetYms() =>
        StatsMath.SelectPeriodYms(AllYmsAsc, SelectedPeriod, _customStart, _customEnd, LedgerService.CurrentCycleStartYm());

    // 現在の対象期間を実際の月で明記する（例: 2026年3月 〜 2026年6月（4ヶ月））
    private string RangeLabel
    {
        get
        {
            var yms = GetTargetYms();
            if (yms.Count == 0) return "対象データなし";
            var first = LedgerService.Label(yms.First());
            var last = LedgerService.Label(yms.Last());
            return first == last ? $"{first}（1ヶ月）" : $"{first} 〜 {last}（{yms.Count}ヶ月）";
        }
    }

    private void BuildChartData()
    {
        var yms = GetTargetYms();

        // 支出合計は Debits のみ（ATM出金は AtmWithdraw フィールドで別管理＝統計から自動除外）
        MonthlyDebitData = BuildSeries(yms, ym => MonthSum(ym, l => l.Debits.Sum(d => d.Amount)));
        SalaryData       = BuildSeries(yms, ym => MonthSum(ym, l => l.Salary));
        BonusData        = BuildSeries(yms, ym => MonthSum(ym, l => l.Bonus));
        // 収入総額は給料＋ボーナス＋臨時収入（ATM入金は資産移動のため含めない）
        IncomeData       = BuildSeries(yms, ym => MonthSum(ym, l => l.Salary + l.Bonus + l.Incomes.Sum(i => i.Amount)));
        BuildIncomeBreakdown(yms);

        // 残高は一意な Id で解決する（#157：口座名で引くと同名口座が破綻する）。並び順＝ActiveAccounts 順。
        BalanceSeriesData = StatsMath.BuildAccountSeries(
            Svc.ActiveAccounts.Select(a => (a.Id, a.Name)),
            id => BuildSeries(yms, ym => Svc.CloseOf(ym, id)));
        // ② 口座線色を規定パレットでローテ（spec §5。系列順＝口座順）。ActiveAccounts を再列挙せず
        // 系列リスト自身から導出することで、i 番目の系列と Colors[i] の対応を構造的に保証する。
        BalanceLineOptions.Colors = BalanceSeriesData
            .Select((_, i) => BalancePalette[i % BalancePalette.Length]).ToList();

        // マスタの現在値ではなく実際に記帳された固定費 Debit（IsFixed）を合計（OpenFixedBreakdown と同じ理由）
        FixedCostData = BuildSeries(yms, ym => MonthSum(ym, l => l.Debits.Where(d => d.IsFixed).Sum(d => d.Amount)));

        // メイン・コンボの収支線（収入−支出。支出は MonthlyDebitData と同義で棒2本に整合）
        NetData = yms.Select((ym, i) => new ChartPoint
        {
            Label = LedgerService.Label(ym),
            Value = IncomeData[i].Value - MonthlyDebitData[i].Value
        }).ToList();

        // カード色（CardPalette 割当）を先に確定し、カテゴリ明細のカードバッジ色に流用する
        BuildCardSpend(yms);
        BuildCategorySpend(yms);
        BuildCategoryTrend(yms);
        BuildCardTrend(yms);
    }

    // cardId → ドーナツ/バッジで使う色（BuildCardSpend で確定）
    private Dictionary<string, string> _cardColors = new();

    // 期間中の全カード明細＋財布の現金支出（#77）を CategoryId で集計
    // （未分類＝カードの CategoryId 空欄、現金支出の CategoryId 未設定/空欄、および参照切れ
    //  （削除済み等で解決できない CategoryId）は、すべて空キー "" に正規化して1つの「未分類」に
    //  集約する（#117）。現金支出はカードと同じく未分類のものも集計対象に含める（#118フォローアップ。
    //  当初 CategoryId が null/空の現金支出を集計から除外していたが、新規追加した現金支出は
    //  カテゴリ未選択のまま CategoryId=null になる＝実質すべての「未分類」現金支出が統計に
    //  一切反映されない不具合だったため、CashDebitsIn 側で財布口座かどうかで絞り込む方式に変更した）。
    private void BuildCategorySpend(List<string> yms)
    {
        // 解決できない CategoryId（空・参照切れ）は "" に正規化してグルーピングキーを統一する（#117）。
        var knownCategoryIds = Svc.State.Categories.Select(c => c.Id).ToHashSet();

        // 月をまたいで明細を集める（ドリルダウン表示用に日付降順で保持）
        var cardDetails = CardDetailsIn(yms);
        var cashDebits = CashDebitsIn(yms).ToList();

        var cardGroups = cardDetails.GroupBy(d => StatsMath.NormalizeCategoryKey(d.CategoryId, knownCategoryIds)).ToDictionary(g => g.Key, g => g.ToList());
        var cashGroups = cashDebits.GroupBy(x => StatsMath.NormalizeCategoryKey(x.Debit.CategoryId, knownCategoryIds)).ToDictionary(g => g.Key, g => g.ToList());
        var allKeys = cardGroups.Keys.Union(cashGroups.Keys).ToList();

        CategorySpendData = allKeys
            .Select(key =>
            {
                var cat = Svc.CategoryById(key);
                var cardList = cardGroups.GetValueOrDefault(key, new());
                var cashList = cashGroups.GetValueOrDefault(key, new());
                return new SpendSlice
                {
                    Key = key,
                    Label = cat?.Name ?? "未分類",
                    Value = cardList.Sum(d => d.Amount) + cashList.Sum(x => x.Debit.Amount),
                    Color = cat?.Color ?? "#bdbdbd",
                    Count = cardList.Count + cashList.Count
                };
            })
            .OrderByDescending(s => s.Value)
            .ToList();

        // ドリルダウン用：カテゴリごとの明細（日付降順）。補足列＝カード名/口座名・色はカードドーナツと共有
        // （現金支出は利用日を持たないため所属月の1日を合成日として使う＝StatsMath.MonthStartDate。
        //  カード明細と同じ実日付として扱えるため、日付降順ソートも破綻しない・#171）。
        // 併せて DetailRow.Id → 実体への setter を記録する（「選択してカテゴリ設定」#118 用）。
        _categorizeTargets = new();
        CategoryDetails = allKeys.ToDictionary(
            key => key,
            key =>
            {
                var cardRows = cardGroups.GetValueOrDefault(key, new())
                    .Select(d =>
                    {
                        _categorizeTargets[d.Id] = catId => d.CategoryId = catId;
                        return new DetailDialog.DetailRow(
                            d.Date, d.Name, Svc.CardById(d.CardId)?.Name ?? "", d.Amount,
                            _cardColors.GetValueOrDefault(d.CardId ?? "", "#bdbdbd"), d.Id);
                    });
                var cashRows = cashGroups.GetValueOrDefault(key, new())
                    .Select(x =>
                    {
                        _categorizeTargets[x.Debit.Id] = catId => x.Debit.CategoryId = catId;
                        // 現金・電子マネー支出は実日付を持たないため、所属月の1日を合成してカード明細と
                        // 同じ M/d 書式・日付順ソートに乗せる（DateIsSynthesized=true で ⓘ 注記を出す・#171）。
                        return new DetailDialog.DetailRow(
                            StatsMath.MonthStartDate(x.Ym), string.IsNullOrWhiteSpace(x.Debit.Name) ? "（名称なし）" : x.Debit.Name,
                            Svc.AccountName(x.AccountId) ?? "現金", x.Debit.Amount, "#bdbdbd", x.Debit.Id, DateIsSynthesized: true);
                    });
                return cardRows.Concat(cashRows).OrderByDescending(r => r.Date).ToList();
            });

        // スライス色をカテゴリ設定色に合わせる（データ並びと同順）
        DonutOptions.Colors = CategorySpendData.Select(s => s.Color).ToList();
    }

    // 期間中の全カード明細を CardId で集計。削除済みカードもソフト削除でレコードが
    // 残るため名前を引けて、自身のスライスとして表示される。
    private void BuildCardSpend(List<string> yms)
    {
        var details = CardDetailsIn(yms);

        var groups = details.GroupBy(d => d.CardId ?? "").ToList();

        CardSpendData = groups
            .Select(g => new SpendSlice
            {
                Key = g.Key,
                Label = Svc.CardById(g.Key)?.Name is { Length: > 0 } n ? n : "（不明）",
                Value = g.Sum(d => d.Amount),
                Count = g.Count()
            })
            .OrderByDescending(s => s.Value)
            .ToList();

        // ドリルダウン用：カードごとの明細（日付降順・補足列＝カテゴリ名・色はカテゴリ設定色）
        CardDetails = groups.ToDictionary(
            g => g.Key,
            g => g.OrderByDescending(d => d.Date)
                  .Select(d => new DetailDialog.DetailRow(
                      d.Date, d.Name, Svc.CategoryById(d.CategoryId)?.Name ?? "未分類", d.Amount,
                      Svc.CategoryById(d.CategoryId)?.Color ?? "#bdbdbd"))
                  .ToList());

        // 表示順にパレット色を割り当て、スライス色と一覧ドットを揃える。
        for (int i = 0; i < CardSpendData.Count; i++)
            CardSpendData[i].Color = CardPalette[i % CardPalette.Length];
        CardDonutOptions.Colors = CardSpendData.Select(s => s.Color).ToList();
        // カテゴリ明細のカードバッジ色に使う cardId→色 を確定
        _cardColors = CardSpendData.ToDictionary(s => s.Key, s => s.Color);
    }

    // ③ 給料・ボーナス・臨時収入（合算）の3系列固定（#89）。臨時収入は月ごとに入力名の
    // 顔ぶれが変わり個別系列だと色が乱立して判別不能になるため合算1系列にまとめ、
    // 内訳（名称別）は棒タップで OnIncomeBreakdownSelected → 既存の内訳ダイアログに委譲する。
    private void BuildIncomeBreakdown(List<string> yms)
    {
        var otherIncome = BuildSeries(yms, ym => MonthSum(ym, l => l.Incomes.Sum(i => i.Amount)));
        var series = new List<IncomeSeries>
        {
            new("給料", SalaryData),
            new("ボーナス", BonusData),
            new("臨時収入", otherIncome),
        };

        // すべて 0 の系列しかない（=収入が一切ない）場合は空にしてプレースホルダ表示
        IncomeBreakdown = series.Any(s => s.Data.Any(p => p.Value != 0)) ? series : new();

        // 色：給料=navy／ボーナス=緑／臨時収入=ゴールド（系列が3本固定になったためローテ不要。spec §5）
        IncomeBreakdownOptions.Colors = new List<string> { "#1f3a5f", "#0f6e56", IncomeGold };
    }

    // 収入内訳推移の棒タップ→その月の収入内訳（給料/ボーナス/臨時収入の各入力名）を
    // 既存の内訳ダイアログ（OpenIncomeBreakdown）で表示する（コンボ棒タップと同じ導線）。
    private void OnIncomeBreakdownSelected(SelectedData<ChartPoint> sel)
    {
        var yms = GetTargetYms();
        if (sel.DataPointIndex < 0 || sel.DataPointIndex >= yms.Count) return;
        var ym = yms[sel.DataPointIndex];
        var label = LedgerService.Label(ym);
        OpenIncomeBreakdown(new List<string> { ym }, $"{label}の収入内訳", $"{label}（1ヶ月）");
    }

    private static string IncomeName(IncomeItem i) => string.IsNullOrWhiteSpace(i.Name) ? "その他収入" : i.Name;

    // ── (B)(C) カテゴリ別／カード別 月別推移（#105）─────────────────────
    // 横軸(x軸=月)の各バケットに属するカード明細は SpendAxis トグル（ドーナツ+一覧用）と連動する
    // （ユーザー要望で「請求月/利用月」トグルを推移にも連動させる方針に変更）：
    // billing=請求月（ドキュメント所属＝ CardDetailsByBillingMonth と同じ定義）／
    // usage=利用月（CardDetail.Date の年月＝ CardDetailsByExactUsageYm）。
    // 現金支出は利用日を持たないため対象外（カード明細のみ）。
    private List<CategoryTrendSeries> CategoryTrend = new();
    private record CategoryTrendSeries(string Name, string Color, List<ChartPoint> Data);
    private List<CardTrendSeries> CardTrend = new();
    private record CardTrendSeries(string Name, string Color, List<ChartPoint> Data);

    // 月別推移の1バケット（x軸1点＝ym）に属するカード明細。SpendAxis に連動。
    private List<CardDetail> CardDetailsAtBucket(string ym) =>
        SpendAxis == "usage"
            ? CardDetailsByExactUsageYm(ym).ToList()
            : Svc.State.Months.GetValueOrDefault(ym)?.CardDetails ?? new List<CardDetail>();

    // 月別推移の共通骨組み：各 ym バケット（CardDetailsAtBucket）を keyOf でグルーピングし、
    // キー別・月別の金額系列を「合計降順」で返す。名前・色の解決だけをカテゴリ別/カード別で差し替える。
    // extraByYm を渡すと、その ym バケットのキー別金額をカード明細分に合算する（StatsMath.MergeAmounts・
    // #171・カテゴリ別推移の現金/電子マネー支出加算用。カード別推移は extraByYm を渡さないため
    // 今までどおりカードのみ）。
    private List<(string Key, List<ChartPoint> Data)> BuildTrendSeries(
        List<string> yms, Func<CardDetail, string> keyOf, Func<string, Dictionary<string, decimal>>? extraByYm = null)
    {
        // ym → キー → 金額合計
        var amountsByYm = yms.ToDictionary(
            ym => ym,
            ym =>
            {
                var amounts = CardDetailsAtBucket(ym).GroupBy(keyOf).ToDictionary(g => g.Key, g => g.Sum(d => d.Amount));
                return extraByYm != null ? StatsMath.MergeAmounts(amounts, extraByYm(ym)) : amounts;
            });

        return amountsByYm.Values.SelectMany(d => d.Keys).Distinct()
            .Select(key => (Key: key, Data: yms.Select(ym => new ChartPoint
            {
                Label = LedgerService.Label(ym),
                Value = amountsByYm[ym].GetValueOrDefault(key, 0)
            }).ToList()))
            .OrderByDescending(x => x.Data.Sum(p => p.Value))
            .ToList();
    }

    private void BuildCategoryTrend(List<string> yms)
    {
        var knownCategoryIds = Svc.State.Categories.Select(c => c.Id).ToHashSet();
        // 現金・電子マネー支出（利用日を持たないため常に所属月バケット）をカテゴリ別月別推移に加算する。
        // ドーナツ・カテゴリ別合計（BuildCategorySpend の CashDebitsIn）には既に反映済みだったが、
        // この推移だけカード明細のみを対象にしていたため出ていなかった（#171）。
        // 同じ集計（CashCategoryAmounts）を棒タップの内訳ドリルダウン（OpenCategoryTrendSummary）でも
        // 使い、棒の高さと内訳合計を一致させる（#171フォローアップ：コードレビューで発覚した不整合の修正）。
        CategoryTrend = BuildTrendSeries(yms, d => StatsMath.NormalizeCategoryKey(d.CategoryId, knownCategoryIds),
                ym => CashCategoryAmounts(ym, knownCategoryIds))
            .Select(x =>
            {
                var cat = Svc.CategoryById(x.Key);
                return new CategoryTrendSeries(cat?.Name ?? "未分類", cat?.Color ?? "#bdbdbd", x.Data);
            })
            .ToList();
        CategoryTrendOptions.Colors = CategoryTrend.Select(s => s.Color).ToList();
    }

    // 指定 ym（1ヶ月分）の現金・電子マネー支出を、カテゴリキー別の金額合計にする（#171フォローアップ）。
    // BuildCategoryTrend（棒の高さ）と OpenCategoryTrendSummary（棒タップの内訳合計）の両方から使い、
    // 常に同じ集計結果になることで「棒の高さ ≠ 内訳合計」という不整合を構造的に防ぐ。
    private Dictionary<string, decimal> CashCategoryAmounts(string ym, IReadOnlyCollection<string> knownCategoryIds) =>
        CashDebitsIn(new[] { ym })
            .GroupBy(x => StatsMath.NormalizeCategoryKey(x.Debit.CategoryId, knownCategoryIds))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Debit.Amount));

    private void BuildCardTrend(List<string> yms)
    {
        // 表示順にパレット色を割り当てる（BuildCardSpend と同じ方針）
        CardTrend = BuildTrendSeries(yms, d => d.CardId ?? "")
            .Select((x, i) => new CardTrendSeries(
                Svc.CardById(x.Key)?.Name is { Length: > 0 } n ? n : "（不明）",
                CardPalette[i % CardPalette.Length],
                x.Data))
            .ToList();
        CardTrendOptions.Colors = CardTrend.Select(s => s.Color).ToList();
    }

    // 棒タップ時にどちらを開くか（"summary"=カテゴリ別/カード別の集計内訳／"detail"=明細別）。
    // カテゴリ別推移・カード別推移の両チャートで共通の1つのモード。ダイアログ自体に埋め込んだ
    // トグル（TrendModeToggle・ToolbarExtra 経由）で開いたまま切替できる（実装後のユーザー要望で
    // グラフ上の外置きトグルから移設）。
    private string _trendDrilldownMode = "summary";
    // 対象月・どちらのチャートから開いたか（集計内訳の軸に必要）を覚えておき、
    // ダイアログを開いたまま SetTrendDrilldownMode でモード切替できるようにする。
    private bool _trendIsCategoryChart;

    private void OnCategoryTrendSelected(SelectedData<ChartPoint> sel) => HandleTrendSelected(sel, isCategoryChart: true);
    private void OnCardTrendSelected(SelectedData<ChartPoint> sel) => HandleTrendSelected(sel, isCategoryChart: false);

    // 棒（月）タップ→対象月とどちらのチャートかを覚えて、現在のモードでドリルダウンを開く。
    private void HandleTrendSelected(SelectedData<ChartPoint> sel, bool isCategoryChart)
    {
        var yms = GetTargetYms();
        if (sel.DataPointIndex < 0 || sel.DataPointIndex >= yms.Count) return;
        _trendYm = yms[sel.DataPointIndex];
        _trendIsCategoryChart = isCategoryChart;
        OpenTrendDrilldown();
    }

    // _trendYm/_trendIsCategoryChart/_trendDrilldownMode の現在値に応じて表示するダイアログを（再）構築する。
    // ダイアログ内トグルからのモード切替時、ダイアログを閉じずに中身だけ差し替えるために分離。
    // _detail・_breakdown はコンポーネントが別（DetailDialog/BreakdownDialog）のため、切替先を
    // 設定する前に反対側を明示的に null にしておかないと両方同時に表示されてしまう。
    private void OpenTrendDrilldown()
    {
        if (_trendYm is not { } ym) return;
        if (_trendDrilldownMode == "detail")
        {
            _breakdown = null;
            OpenTrendDetail(ym);
        }
        else
        {
            _detail = null;
            if (_trendIsCategoryChart) OpenCategoryTrendSummary(ym);
            else OpenCardTrendSummary(ym);
        }
    }

    // ダイアログ内トグル（TrendModeToggle）から呼ばれる。_trendYm が立っている間はダイアログを
    // 閉じずに表示だけ切り替える。
    private void SetTrendDrilldownMode(string mode)
    {
        _trendDrilldownMode = mode;
        if (_trendYm is not null) OpenTrendDrilldown();
    }

    // 集計別モード（カテゴリ別推移）：項目（カテゴリ）ごとの合計（既存の内訳ダイアログ導線に倣う）。
    // カード明細の金額に現金・電子マネー支出（CashCategoryAmounts＝BuildCategoryTrend と同じ集計）を
    // 合算し、合計値が棒の高さ（CategoryTrend）と一致するようにする（#171フォローアップ）。
    private void OpenCategoryTrendSummary(string ym)
    {
        var knownCategoryIds = Svc.State.Categories.Select(c => c.Id).ToHashSet();
        var cardAmounts = CardDetailsAtBucket(ym)
            .GroupBy(d => StatsMath.NormalizeCategoryKey(d.CategoryId, knownCategoryIds))
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Amount));
        var amounts = StatsMath.MergeAmounts(cardAmounts, CashCategoryAmounts(ym, knownCategoryIds));

        var items = amounts
            .Select(kv => new BreakdownDialog.BreakdownItem(Svc.CategoryById(kv.Key)?.Name ?? "未分類", kv.Value))
            .Where(x => x.Amount != 0)
            .OrderByDescending(x => x.Amount)
            .ToList();
        var label = LedgerService.Label(ym);
        _breakdown = new($"{label}のカテゴリ別内訳", $"{label}（1ヶ月）", items.Sum(x => x.Amount), items);
    }

    // 集計別モード（カード別推移）：項目（カード）ごとの合計
    private void OpenCardTrendSummary(string ym)
    {
        var items = CardDetailsAtBucket(ym)
            .GroupBy(d => d.CardId ?? "")
            .Select(g => new BreakdownDialog.BreakdownItem(
                Svc.CardById(g.Key)?.Name is { Length: > 0 } n ? n : "（不明）", g.Sum(d => d.Amount)))
            .Where(x => x.Amount != 0)
            .OrderByDescending(x => x.Amount)
            .ToList();
        var label = LedgerService.Label(ym);
        _breakdown = new($"{label}のカード別内訳", $"{label}（1ヶ月）", items.Sum(x => x.Amount), items);
    }

    // 明細別モード：その月（バケット）の明細を1行ずつ表示（既存の DetailDialog を再利用。
    // ドーナツ側の「選択してカテゴリ設定」はスコープ外＝閲覧専用で ShowCategorize=false）。
    // カテゴリ別推移（_trendIsCategoryChart=true）から開いた場合のみ、カード明細に加えて現金・電子マネー
    // 支出も含める（OpenCategoryTrendSummary の合計＝棒の高さと一致させるため・#171フォローアップ。
    // 当初「どちらのチャートから開いても同一内容」としていたが、カテゴリ別推移の棒には現金・電子マネー分が
    // 含まれるため、明細側も揃えないと合計が一致しない不整合になっていた）。
    // カード別推移（false）から開いた場合は現金・電子マネーを持たない＝従来どおりカード明細のみ。
    private void OpenTrendDetail(string ym)
    {
        var knownCategoryIds = Svc.State.Categories.Select(c => c.Id).ToHashSet();
        var cardRows = CardDetailsAtBucket(ym)
            .Select(d =>
            {
                var cat = Svc.CategoryById(StatsMath.NormalizeCategoryKey(d.CategoryId, knownCategoryIds));
                return new DetailDialog.DetailRow(d.Date, d.Name, cat?.Name ?? "未分類", d.Amount, cat?.Color ?? "#bdbdbd");
            });

        var rows = _trendIsCategoryChart
            ? cardRows.Concat(CashDebitsIn(new[] { ym }).Select(x =>
              {
                  var cat = Svc.CategoryById(StatsMath.NormalizeCategoryKey(x.Debit.CategoryId, knownCategoryIds));
                  // 現金・電子マネーは実日付を持たないため、ドーナツ側ドリルダウンと同じく所属月の1日を
                  // 合成日として使う（StatsMath.MonthStartDate・DateIsSynthesized=true で ⓘ 注記が出る）。
                  return new DetailDialog.DetailRow(
                      StatsMath.MonthStartDate(ym), string.IsNullOrWhiteSpace(x.Debit.Name) ? "（名称なし）" : x.Debit.Name,
                      cat?.Name ?? "未分類", x.Debit.Amount, cat?.Color ?? "#bdbdbd", DateIsSynthesized: true);
              })).ToList()
            : cardRows.ToList();

        rows = rows.OrderByDescending(r => r.Date).ToList();
        var label = LedgerService.Label(ym);
        _detail = new(label, "", rows.Count, rows.Sum(r => r.Amount), rows, false, $"{label}（1ヶ月）");
    }

    // 期間中の各月について Label/Value のチャート点を作る共通処理
    private static List<ChartPoint> BuildSeries(List<string> yms, Func<string, decimal> valueOf) =>
        yms.Select(ym => new ChartPoint { Label = LedgerService.Label(ym), Value = valueOf(ym) }).ToList();

    // 指定月の全口座台帳にセレクタを適用して合計（月が無ければ 0）
    private decimal MonthSum(string ym, Func<Ledger, decimal> selector) =>
        Svc.State.Months.GetValueOrDefault(ym)?.Ledgers.Values.Sum(selector) ?? 0;

    // 期間中の全台帳／全カード明細をまとめて列挙（月が無ければスキップ）。内訳ダイアログ・ドーナツ集計で共有する。
    private IEnumerable<Ledger> LedgersIn(IEnumerable<string> yms) =>
        yms.SelectMany(ym => Svc.State.Months.GetValueOrDefault(ym)?.Ledgers.Values ?? Enumerable.Empty<Ledger>());

    // カテゴリ別/カード別 ドーナツ+一覧が対象とするカード明細（#105・SpendAxis トグルで切替）。
    // 現金支出（CashDebitsIn）はこのトグルの影響を受けず常に請求月バケットのまま（Debit は利用日を持たないため）。
    private List<CardDetail> CardDetailsIn(IEnumerable<string> yms) =>
        SpendAxis == "usage" ? CardDetailsByUsageMonth(yms) : CardDetailsByBillingMonth(yms);

    // 請求月＝月次ドキュメント所属（既存の集計方式）。指定 yms のドキュメントに計上済みの明細をそのまま集める。
    private List<CardDetail> CardDetailsByBillingMonth(IEnumerable<string> yms) =>
        yms.SelectMany(ym => Svc.State.Months.GetValueOrDefault(ym)?.CardDetails ?? Enumerable.Empty<CardDetail>()).ToList();

    // 利用月＝CardDetail.Date の年月。ドキュメント所属月とは無関係に、全月のカード明細を横断して
    // 利用月が対象期間 yms に含まれるものだけを集める（全月データはクライアント常駐のため低コスト）。
    private List<CardDetail> CardDetailsByUsageMonth(IEnumerable<string> yms)
    {
        var target = yms.ToHashSet();
        return Svc.State.Months.Values
            .SelectMany(mo => mo.CardDetails)
            .Where(d => StatsMath.UsageYmOf(d.Date) is { } uym && target.Contains(uym))
            .ToList();
    }

    // 利用月がちょうど ym と一致するカード明細を全月横断で列挙する（(B) の月別集計・ドリルダウン共通で使用）。
    private IEnumerable<CardDetail> CardDetailsByExactUsageYm(string ym) =>
        Svc.State.Months.Values.SelectMany(mo => mo.CardDetails).Where(d => StatsMath.UsageYmOf(d.Date) == ym);

    // 財布・電子マネーの手入力支出（#77・#148）を ym・口座つきで列挙する。カテゴリ付き支出口座
    // （過去に該当種別だった口座も含め Svc.State.Accounts から HasCategorizedSpending で判定・
    // ソフト削除済みでも過去月の参照のため対象に含める）の Debits はすべて手入力の支出のみ
    // （固定費・カード由来の Debit は #124/#148 でこれらの口座を引き落とし口座として選べないため
    // ledgerには載らない）なので、口座で絞り込めば取りこぼしなく列挙できる。
    // 当初は CategoryId が空でない Debit だけに絞っていたが、新規追加した現金支出はカテゴリ未選択のまま
    // CategoryId=null になる（#118フォローアップで判明・#77起因の不具合）ため、未分類の支出も
    // 集計対象に含むよう口座ベースの判定に変更した（NormalizeCategoryKey が null/""/参照切れを
    // まとめて「未分類」キーへ正規化するため、ここでは絞り込まず全件渡せばよい）。
    private IEnumerable<(string Ym, string AccountId, Debit Debit)> CashDebitsIn(IEnumerable<string> yms)
    {
        var categorizedAccountIds = Svc.State.Accounts.Where(a => a.Type.HasCategorizedSpending()).Select(a => a.Id).ToHashSet();
        return yms.SelectMany(ym => Svc.State.Months.GetValueOrDefault(ym)?.Ledgers
            .Where(kv => categorizedAccountIds.Contains(kv.Key))
            .SelectMany(kv => kv.Value.Debits.Select(d => (Ym: ym, AccountId: kv.Key, Debit: d)))
            ?? Enumerable.Empty<(string, string, Debit)>());
    }

    public class ChartPoint
    {
        public string Label { get; set; } = "";
        public decimal Value { get; set; }
    }
}
