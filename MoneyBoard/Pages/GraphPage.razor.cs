namespace MoneyBoard.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ApexCharts;
using Microsoft.AspNetCore.Components;
using MoneyBoard.Components;
using MoneyBoard.Services;
using MoneyBoardShared;

// GraphPage.razor の code-behind。markup・ディレクティブ(@page/@inject/@using)は .razor 側に残し、
// 統計の集計・期間処理・ドリルダウン状態をこの partial class に集約する。
public partial class GraphPage
{
    private string SelectedPeriod = "3";
    private Dictionary<string, string> Periods = new()
    {
        { "3", "3ヶ月" }, { "6", "6ヶ月" }, { "12", "12ヶ月" }, { "all", "全期間" }
    };

    private List<ChartPoint> MonthlyDebitData = new();
    private Dictionary<string, List<ChartPoint>> BalanceSeriesData = new();
    private List<ChartPoint> SalaryData = new();
    private List<ChartPoint> BonusData = new();
    private List<ChartPoint> IncomeData = new();
    private List<ChartPoint> FixedCostData = new();
    // メイン・コンボの収支折れ線（月別 収入−支出。支出は MonthlyDebitData と同義で、棒2本と整合）
    private List<ChartPoint> NetData = new();

    // ── 要約バンド（ヒーロー＋指標4枚）。既存の月別系列を期間合算した派生値（新ロジックなし）──
    // 収入合計＝給料+ボーナス+臨時。支出合計＝変動(Debits)＋固定費（spec §2 で固定費込みと定義）。
    private decimal IncomeTotal => IncomeData.Sum(p => p.Value);
    private decimal VariableExpenseTotal => MonthlyDebitData.Sum(p => p.Value);
    private decimal FixedTotal => FixedCostData.Sum(p => p.Value);
    private decimal ExpenseTotal => VariableExpenseTotal + FixedTotal;
    private decimal NetTotal => IncomeTotal - ExpenseTotal;
    private bool IsSurplus => NetTotal >= 0;
    // 貯蓄率＝期間収支 / 収入合計（収入0なら null＝「—」表示）
    private double? SavingsRate => IncomeTotal == 0 ? null : (double)(NetTotal / IncomeTotal);
    // 固定費が支出に占めるおおよその割合（補助行）
    private int FixedPctOfExpense => ExpenseTotal == 0 ? 0 : (int)Math.Round((double)(FixedTotal / ExpenseTotal) * 100);

    // ③ 収入の内訳系列（給料・ボーナス・各臨時収入名）。積み上げ棒で表示。
    private List<IncomeSeries> IncomeBreakdown = new();
    private record IncomeSeries(string Name, List<ChartPoint> Data);

    // Y軸は万単位の概略表示、tooltip はフル円表示（spec §3）。グリッド線・軸ラベルも淡色で統一。
    private const string YMan = "function(v){return '¥'+Math.round(v/10000)+'万'}";
    private const string YFull = "function(v){return '¥'+v.toLocaleString()}";

    private static YAxis ManAxis() => new() { Labels = new YAxisLabels { Formatter = YMan } };
    private static Grid SoftGrid() => new() { BorderColor = "#f0eee9" };
    private static Tooltip FullYenTip() => new() { Y = new TooltipY { Formatter = YFull } };

    // ApexChartOptions はチャート固有の状態を書き込むため、1インスタンスを複数の
    // <ApexChart> で共有すると最初の1つしか描画されない。チャートごとに専用インスタンスを持つ。
    private static ApexChartOptions<ChartPoint> NewLineOptions() => new()
    {
        Chart = new Chart { Height = 240, Toolbar = new Toolbar { Show = false } },
        Stroke = new Stroke { Curve = Curve.Smooth },
        Grid = SoftGrid(),
        Tooltip = FullYenTip(),
        Yaxis = new List<YAxis> { ManAxis() }
    };

    private static ApexChartOptions<ChartPoint> NewBarOptions(bool stacked = false) => new()
    {
        Chart = new Chart { Height = 240, Stacked = stacked, Toolbar = new Toolbar { Show = false } },
        Grid = SoftGrid(),
        Tooltip = FullYenTip(),
        Yaxis = new List<YAxis> { ManAxis() }
    };

    // メイン：収入(棒)・支出(棒)＋収支(折れ線)のコンボ。色は既存トークン（収入=緑/支出=赤/収支線=navy）。
    private static ApexChartOptions<ChartPoint> NewComboOptions() => new()
    {
        Chart = new Chart { Height = 340, Toolbar = new Toolbar { Show = false } },
        Colors = new List<string> { "#0f6e56", "#a3261f", "#1f3a5f" },
        Stroke = new Stroke { Width = new List<int> { 0, 0, 3 }, Curve = Curve.Smooth },
        Grid = SoftGrid(),
        Tooltip = FullYenTip(),
        Yaxis = new List<YAxis> { ManAxis() }
    };

    private readonly ApexChartOptions<ChartPoint> ComboOptions = NewComboOptions();
    private readonly ApexChartOptions<ChartPoint> BalanceLineOptions = NewLineOptions();
    private readonly ApexChartOptions<ChartPoint> IncomeBreakdownOptions = NewBarOptions(stacked: true);

    private List<SpendSlice> CategorySpendData = new();
    private decimal CategoryTotal => CategorySpendData.Sum(s => s.Value);

    // ドリルダウン（カテゴリ別／カード別 共通モーダル）。明細は日付降順で保持する。
    private Dictionary<string, List<DetailDialog.DetailRow>> CategoryDetails = new();   // カテゴリキー → 明細（3列目=カード名）
    private Dictionary<string, List<DetailDialog.DetailRow>> CardDetails = new();       // カードキー   → 明細（3列目=カテゴリ名）
    private DetailModal? _detail;   // 開いているモーダル（null=閉）

    private void OpenCatDetail(string key)
    {
        var s = CategorySpendData.FirstOrDefault(x => x.Key == key);
        // 未分類（CategoryId 空）のドリルダウンのみ「カテゴリ設定」操作を出す
        if (s != null) _detail = new(s.Label, s.Color, s.Count, s.Value, CategoryDetails.GetValueOrDefault(key) ?? new(), string.IsNullOrEmpty(key));
    }
    private void OpenCardDetail(string key)
    {
        var s = CardSpendData.FirstOrDefault(x => x.Key == key);
        if (s != null) _detail = new(s.Label, s.Color, s.Count, s.Value, CardDetails.GetValueOrDefault(key) ?? new(), false);
    }
    private void CloseDetail() => _detail = null;

    // 未分類明細の「カテゴリ設定」操作。複数選択→一括更新の実処理は別 issue（dialog-spec §8 スコープ外）。
    // 本リデザインでは UI の枠（ボタン表示）までとし、ここはプレースホルダにとどめる。
    private void OnCategorize() { }

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

    private record DetailModal(string Title, string Color, int Count, decimal Total, List<DetailDialog.DetailRow> Rows, bool ShowCategorize);

    // ── 収入/支出の項目別内訳モーダル（④・⑤から起動。期間合計で集計）──
    private record BreakdownModal(string Title, decimal Total, List<BreakdownDialog.BreakdownItem> Items);
    private BreakdownModal? _breakdown;
    private void CloseBreakdown() => _breakdown = null;

    // メインコンボの収入棒→収入内訳、支出棒→支出内訳（系列0=収入, 1=支出, 2=収支線=ドリルダウンなし）
    private void OnIncomeVsExpenseSelected(SelectedData<ChartPoint> sel)
    {
        if (sel.SeriesIndex == 0) OpenIncomeBreakdown();
        else if (sel.SeriesIndex == 1) OpenExpenseBreakdown();
    }

    // 対象期間の収入を項目（給料/ボーナス/各臨時収入名）で合算
    private void OpenIncomeBreakdown()
    {
        var yms = GetTargetYms();
        var items = new List<BreakdownDialog.BreakdownItem>
        {
            new("給料", yms.Sum(ym => MonthSum(ym, l => l.Salary))),
            new("ボーナス", yms.Sum(ym => MonthSum(ym, l => l.Bonus))),
        };
        items.AddRange(yms
            .SelectMany(ym => Svc.State.Months.GetValueOrDefault(ym)?.Ledgers.Values ?? Enumerable.Empty<Ledger>())
            .SelectMany(l => l.Incomes)
            .GroupBy(IncomeName)
            .Select(g => new BreakdownDialog.BreakdownItem(g.Key, g.Sum(i => i.Amount))));

        items = items.Where(x => x.Amount != 0).OrderByDescending(x => x.Amount).ToList();
        _breakdown = new("収入の内訳", items.Sum(x => x.Amount), items);
    }

    // 対象期間の支出を項目（月次の Debit 名。カードはカード名で1項目・ATMは対象外）で合算
    private void OpenExpenseBreakdown()
    {
        var yms = GetTargetYms();
        var items = yms
            .SelectMany(ym => Svc.State.Months.GetValueOrDefault(ym)?.Ledgers.Values ?? Enumerable.Empty<Ledger>())
            .SelectMany(l => l.Debits)
            .GroupBy(d => string.IsNullOrWhiteSpace(d.Name) ? "（名称なし）" : d.Name)
            .Select(g => new BreakdownDialog.BreakdownItem(g.Key, g.Sum(d => d.Amount)))
            .Where(x => x.Amount != 0)
            .OrderByDescending(x => x.Amount)
            .ToList();
        _breakdown = new("支出の内訳", items.Sum(x => x.Amount), items);
    }

    // 対象期間の固定費を項目（固定費マスタ）ごとに合算（各月の有効分・ボーナス払い込み）
    private void OpenFixedBreakdown()
    {
        var yms = GetTargetYms();
        var byId = new Dictionary<string, (string Name, decimal Amount)>();
        foreach (var ym in yms)
        {
            var month = Ym.Parse(ym).Month;
            foreach (var fc in Svc.State.FixedCosts.Where(fc => LedgerService.IsFixedCostActive(fc, ym)))
            {
                var cur = byId.GetValueOrDefault(fc.Id);
                byId[fc.Id] = (fc.Name, cur.Amount + LedgerService.GetFixedCostAmount(fc, month));
            }
        }
        var items = byId.Values
            .Select(v => new BreakdownDialog.BreakdownItem(v.Name, v.Amount))
            .Where(x => x.Amount != 0)
            .OrderByDescending(x => x.Amount)
            .ToList();
        _breakdown = new("固定費の内訳", items.Sum(x => x.Amount), items);
    }

    private List<SpendSlice> CardSpendData = new();
    private decimal CardTotal => CardSpendData.Sum(s => s.Value);

    // カードは色設定を持たないため、表示順に割り当てる固定パレット。
    private static readonly string[] CardPalette =
    {
        "#5b8def", "#34c3a3", "#f6a609", "#ef6a6a", "#9b7ede",
        "#4db3d6", "#e879b9", "#7ac74f", "#f08a4b", "#6c7a89"
    };

    // ② 口座別月末残高推移の線色（spec §5：青/橙/赤/緑をローテ）。
    private static readonly string[] BalancePalette = { "#3a52c0", "#b86a18", "#a3261f", "#2c7a52" };
    // ③ 収入内訳：給料=navy／ボーナス=緑／臨時収入=ゴールド（spec §5）。
    private const string IncomeGold = "#c9a23a";

    // ドーナツ共通設定（カテゴリ/カードで別インスタンスにする。1インスタンスを
    // 複数の <ApexChart> で共有すると最初の1つしか描画されないため）。
    private static ApexChartOptions<T> NewDonutOptions<T>() where T : class => new()
    {
        Chart = new Chart { Height = 300, Toolbar = new Toolbar { Show = false } },
        Legend = new Legend { Position = LegendPosition.Bottom },
        Tooltip = new Tooltip { Y = new TooltipY { Formatter = "function(v){return '¥'+v.toLocaleString()}" } },
        // 既定のホバー効果(lighten)だと薄い色(未分類のグレー)が白飛びするため、わずかに暗くする
        States = new States
        {
            Hover = new StatesHover { Filter = new StatesFilter { Type = StatesFilterType.darken, Value = 0.12 } },
            Active = new StatesActive { Filter = new StatesFilter { Type = StatesFilterType.darken, Value = 0.12 } }
        }
    };

    private readonly ApexChartOptions<SpendSlice> DonutOptions = NewDonutOptions<SpendSlice>();
    private readonly ApexChartOptions<SpendSlice> CardDonutOptions = NewDonutOptions<SpendSlice>();

    // 読み込み完了まで操作不可（/graph を直接リロードしたケースに対応）
    private bool Loaded;
    private bool LoadFailed;

    // 期間指定（月単位）の開始・終了 ym
    private string _customStart = "";
    private string _customEnd = "";

    private List<string> AllYmsAsc => Svc.State.Months.Keys.OrderBy(x => x).ToList();

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
        // 期間指定に切替時、未設定なら全期間の端を初期値にする
        if (p == "custom" && string.IsNullOrEmpty(_customStart))
        {
            var yms = AllYmsAsc;
            if (yms.Count > 0) { _customStart = yms.First(); _customEnd = yms.Last(); }
        }
        BuildChartData();
    }

    private void OnCustomChanged() { _detail = null; _breakdown = null; BuildChartData(); }

    // 期間選択→対象 ym（昇順）。計算本体は StatsMath（純粋ロジック・テスト対象）へ委譲する。
    private List<string> GetTargetYms() =>
        StatsMath.SelectPeriodYms(AllYmsAsc, SelectedPeriod, _customStart, _customEnd);

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

        BalanceSeriesData = Svc.ActiveAccounts.ToDictionary(
            a => a.Name,
            a => BuildSeries(yms, ym => Svc.CloseOf(ym, a.Id)));
        // ② 口座線色を規定パレットでローテ（spec §5。系列順＝口座順）
        BalanceLineOptions.Colors = Svc.ActiveAccounts
            .Select((_, i) => BalancePalette[i % BalancePalette.Length]).ToList();

        FixedCostData = BuildSeries(yms, ym =>
        {
            var month = Ym.Parse(ym).Month;
            return Svc.State.FixedCosts
                .Where(fc => LedgerService.IsFixedCostActive(fc, ym))
                .Sum(fc => LedgerService.GetFixedCostAmount(fc, month));
        });

        // メイン・コンボの収支線（収入−支出。支出は MonthlyDebitData と同義で棒2本に整合）
        NetData = yms.Select((ym, i) => new ChartPoint
        {
            Label = LedgerService.Label(ym),
            Value = IncomeData[i].Value - MonthlyDebitData[i].Value
        }).ToList();

        // カード色（CardPalette 割当）を先に確定し、カテゴリ明細のカードバッジ色に流用する
        BuildCardSpend(yms);
        BuildCategorySpend(yms);
    }

    // cardId → ドーナツ/バッジで使う色（BuildCardSpend で確定）
    private Dictionary<string, string> _cardColors = new();

    // 期間中の全カード明細を CategoryId で集計（未分類はまとめて末尾の色なし扱い）
    private void BuildCategorySpend(List<string> yms)
    {
        // 月をまたいで明細を集める（ドリルダウン表示用に日付降順で保持）
        var details = yms
            .SelectMany(ym => Svc.State.Months.GetValueOrDefault(ym)?.CardDetails ?? Enumerable.Empty<CardDetail>())
            .ToList();

        var groups = details.GroupBy(d => d.CategoryId ?? "").ToList();

        CategorySpendData = groups
            .Select(g =>
            {
                var cat = Svc.CategoryById(g.Key);
                return new SpendSlice
                {
                    Key = g.Key,
                    Label = cat?.Name ?? "未分類",
                    Value = g.Sum(d => d.Amount),
                    Color = cat?.Color ?? "#bdbdbd",
                    Count = g.Count()
                };
            })
            .OrderByDescending(s => s.Value)
            .ToList();

        // ドリルダウン用：カテゴリごとの明細（日付降順）。補足列＝カード名・色はカードドーナツと共有
        CategoryDetails = groups.ToDictionary(
            g => g.Key,
            g => g.OrderByDescending(d => d.Date)
                  .Select(d => new DetailDialog.DetailRow(
                      d.Date, d.Name, Svc.CardById(d.CardId)?.Name ?? "", d.Amount,
                      _cardColors.GetValueOrDefault(d.CardId ?? "", "#bdbdbd")))
                  .ToList());

        // スライス色をカテゴリ設定色に合わせる（データ並びと同順）
        DonutOptions.Colors = CategorySpendData.Select(s => s.Color).ToList();
    }

    // 期間中の全カード明細を CardId で集計。削除済みカードもソフト削除でレコードが
    // 残るため名前を引けて、自身のスライスとして表示される。
    private void BuildCardSpend(List<string> yms)
    {
        var details = yms
            .SelectMany(ym => Svc.State.Months.GetValueOrDefault(ym)?.CardDetails ?? Enumerable.Empty<CardDetail>())
            .ToList();

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

    // ③ 給料・ボーナスに加え、期間中に登場する臨時収入を入力名ごとの系列にする。
    private void BuildIncomeBreakdown(List<string> yms)
    {
        var series = new List<IncomeSeries>
        {
            new("給料", SalaryData),
            new("ボーナス", BonusData),
        };

        // 期間中の臨時収入の入力名（空名は「その他収入」にまとめる）を出現順で収集
        var names = yms
            .SelectMany(ym => Svc.State.Months.GetValueOrDefault(ym)?.Ledgers.Values ?? Enumerable.Empty<Ledger>())
            .SelectMany(l => l.Incomes)
            .Select(IncomeName)
            .Distinct()
            .ToList();

        foreach (var name in names)
        {
            series.Add(new(name, BuildSeries(yms, ym =>
                MonthSum(ym, l => l.Incomes.Where(i => IncomeName(i) == name).Sum(i => i.Amount)))));
        }

        // すべて 0 の系列しかない（=収入が一切ない）場合は空にしてプレースホルダ表示
        IncomeBreakdown = series.Any(s => s.Data.Any(p => p.Value != 0)) ? series : new();

        // 色：給料=navy／ボーナス=緑／以降の臨時収入=ゴールド（spec §5）
        IncomeBreakdownOptions.Colors = IncomeBreakdown
            .Select((_, i) => i == 0 ? "#1f3a5f" : i == 1 ? "#0f6e56" : IncomeGold).ToList();
    }

    private static string IncomeName(IncomeItem i) => string.IsNullOrWhiteSpace(i.Name) ? "その他収入" : i.Name;

    // 期間中の各月について Label/Value のチャート点を作る共通処理
    private static List<ChartPoint> BuildSeries(List<string> yms, Func<string, decimal> valueOf) =>
        yms.Select(ym => new ChartPoint { Label = LedgerService.Label(ym), Value = valueOf(ym) }).ToList();

    // 指定月の全口座台帳にセレクタを適用して合計（月が無ければ 0）
    private decimal MonthSum(string ym, Func<Ledger, decimal> selector) =>
        Svc.State.Months.GetValueOrDefault(ym)?.Ledgers.Values.Sum(selector) ?? 0;

    private static string Yen(decimal v) => "¥" + v.ToString("#,0");
    // 符号付き（要約ヒーロー用。±を必ず明示）
    private static string SignedYen(decimal v) => (v >= 0 ? "+¥" : "−¥") + Math.Abs(v).ToString("#,0");

    public class ChartPoint
    {
        public string Label { get; set; } = "";
        public decimal Value { get; set; }
    }
}
