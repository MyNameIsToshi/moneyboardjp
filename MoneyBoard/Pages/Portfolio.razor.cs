namespace MoneyBoard.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ApexCharts;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MoneyBoard.Components;
using MoneyBoard.Services;
using MoneyBoardShared;

// Portfolio.razor の code-behind。markup・ディレクティブ(@page/@inject/@implements)は .razor 側に残し、
// ロジックのみをこの partial class に集約する（@inject は .razor が生成・ここでは Store/Quote/Nav として参照）。
public partial class Portfolio
{
    [CascadingParameter(Name = "IsMobile")] public bool IsMobile { get; set; }

    private bool Loaded;
    private bool LoadFailed;

    // 表示するカテゴリ（順番＝日本株→米国株→投資信託）
    private static readonly (AssetClass Class, string Label)[] Groups =
    {
        (AssetClass.JpStock, "日本株"),
        (AssetClass.UsStock, "米国株"),
        (AssetClass.Fund, "投資信託"),
    };

    private IEnumerable<Holding> Ordered =>
        Store.Data.Holdings.Where(h => !h.IsDeleted).OrderBy(h => h.SortOrder);

    private HoldingSummary Summary(Holding h) =>
        PortfolioMath.Summarize(h, Store.Data.Buys, Store.Data.Sells, Store.Data.Dividends);

    protected override async Task OnInitializedAsync()
    {
        Store.StateReloadedExternally += OnReload;
        await Load();
    }

    public void Dispose()
    {
        Store.StateReloadedExternally -= OnReload;
        // ページ離脱時にロックが残らないよう解除（開いたまま遷移した場合の保険）。
        if (_scrollLocked) _ = JS.InvokeVoidAsync("moneyboardViewport.setBodyScrollLock", false);
    }
    private void OnReload() => InvokeAsync(StateHasChanged);

    // いずれかのダイアログ表示中は背面スクロールをロック（取引/新規登録/削除確認/破棄確認）。
    private bool AnyDialogOpen => _addOpen || _txHoldingId != null || ShowConfirm || _txCloseWarn;
    private bool _scrollLocked;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (AnyDialogOpen != _scrollLocked)
        {
            _scrollLocked = AnyDialogOpen;
            await JS.InvokeVoidAsync("moneyboardViewport.setBodyScrollLock", _scrollLocked);
        }
    }

    private async Task Load()
    {
        Loaded = false;
        LoadFailed = false;
        if (await Store.LoadAsync())
        {
            Loaded = true;
            // 既存データ（前回の価格・スナップショット）でまずグラフを描く（自動更新失敗時もここまでは出る）。
            BuildComposition();
            BuildTimeSeries();
            // 画面を開いたら現在価格を自動更新（失敗・一部未取得のメッセージは出さない）。
            await UpdatePrices(manual: false);
        }
        else if (!Store.IsPending) LoadFailed = true;
    }

    private void Save() => Store.RequestSave();

    // ── 表示ヘルパ ──
    private static string Qty(decimal v) => v.ToString("#,0.####");
    private static string CurSym(Holding h) => h.CostCurrency == Currency.Usd ? "$" : "¥";
    private static string Money(Holding h, decimal v) =>
        CurSym(h) + (h.CostCurrency == Currency.Usd ? v.ToString("#,0.##") : v.ToString("#,0"));
    private static string MoneySigned(Holding h, decimal v) => (v > 0 ? "+" : "") + Money(h, v);
    // 価格（単価・平均取得単価・現在価格）の通貨。米国株はドルで値が付くので常に $（円建てでも）。日本株・投信は ¥。
    private static string PriceCcySym(Holding h) => h.Class == AssetClass.UsStock ? "$" : "¥";
    private static string Price(Holding h, decimal v) => PriceCcySym(h) + v.ToString("#,0.##");
    // 評価額・評価損益は現在価格が無いと算出できないため null 許容。未取得は "—"。
    private static string MoneyOpt(Holding h, decimal? v) => v.HasValue ? Money(h, v.Value) : "—";
    private static string MoneySignedOpt(Holding h, decimal? v) => v.HasValue ? MoneySigned(h, v.Value) : "—";
    private static string PnlClass(decimal v) => v > 0 ? "pf-pos" : v < 0 ? "pf-neg" : "";

    // ── 評価額・評価損益の表示通貨（米国株グループのみ円/ドル切替。日本株・投信は常に円）──
    // 元本は建て通貨のまま（CostCurrency）。ここで切り替えるのは評価額・評価損益・前日比の表示通貨のみ。
    private Currency _usCcy = Currency.Jpy;   // 既定は円表示（総資産・元本サマリーと揃える）
    private void SetUsCcy(Currency c) => _usCcy = c;
    private Currency DispCcy(Holding h) => h.Class == AssetClass.UsStock ? _usCcy : Currency.Jpy;
    private static string CcySym(Currency c) => c == Currency.Usd ? "$" : "¥";
    // 建て通貨(native)額→表示通貨への換算係数。換算が要るのに為替未取得なら 0（呼び出し側で「—」）。
    private decimal CcyFactor(Holding h)
    {
        if (h.Class != AssetClass.UsStock || _usCcy == h.CostCurrency) return 1m;
        decimal fx = Store.Data.UsdJpyRate;
        if (fx <= 0) return 0m;
        return _usCcy == Currency.Jpy ? fx : 1m / fx;
    }
    private string MoneyDisp(Holding h, decimal v)
    {
        var c = DispCcy(h);
        return CcySym(c) + (c == Currency.Usd ? v.ToString("#,0.##") : v.ToString("#,0"));
    }
    private string MoneyDispSigned(Holding h, decimal v) => (v > 0 ? "+" : "") + MoneyDisp(h, v);
    private string MoneyDispOpt(Holding h, decimal? v) => v.HasValue ? MoneyDisp(h, v.Value) : "—";
    private string MoneyDispSignedOpt(Holding h, decimal? v) => v.HasValue ? MoneyDispSigned(h, v.Value) : "—";
    // 表示通貨での評価額・評価損益（米国株は _usCcy に従う・換算不可は null）。
    private decimal? ValDisp(Holding h, decimal qty)
    {
        var v = Valuation(h, qty);
        if (!v.HasValue) return null;
        var f = CcyFactor(h);
        return f <= 0 ? (decimal?)null : v.Value * f;
    }
    private decimal? UpnlDisp(Holding h, HoldingSummary s)
    {
        var v = UnrealizedPnl(h, s);
        if (!v.HasValue) return null;
        var f = CcyFactor(h);
        return f <= 0 ? (decimal?)null : v.Value * f;
    }
    // 評価損益率＝評価損益 ÷ 取得原価 ×100。金額の後ろに " (+12.3%)" として併記。価格未取得・原価0は空。
    private static string PnlPct(decimal? upnl, decimal costBasis)
    {
        if (!upnl.HasValue || costBasis == 0) return "";
        decimal p = upnl.Value / costBasis * 100m;
        return " (" + (p >= 0 ? "+" : "") + p.ToString("0.0") + "%)";
    }
    private static string UnitLabel(Holding h) => h.Class == AssetClass.Fund ? "基準価額" : h.Class == AssetClass.UsStock ? "単価($)" : "単価";
    private static string AccountLabel(AccountKind a) => a switch
    {
        AccountKind.NisaGrowth => "NISA成長",
        AccountKind.NisaTsumitate => "NISAつみたて",
        AccountKind.Nisa => "NISA",
        AccountKind.Tokutei => "特定",
        _ => "一般",
    };
    private static string DisplayName(Holding h) => string.IsNullOrEmpty(h.Name) ? "(名称未設定)" : h.Name;
    // 価格取得先と遅延の注記（クラス見出しの右に表示）
    private static string ClassDelayNote(AssetClass c) => c switch
    {
        AssetClass.JpStock => "Yahoo Finance・約20分遅延",
        AssetClass.UsStock => "Yahoo Finance・約15分遅延",
        AssetClass.Fund => "投信協会・基準価額は約1営業日遅れ",
        _ => "",
    };
    private Holding? HoldingById(string id) => Store.Data.Holdings.FirstOrDefault(h => h.Id == id);

    // ── 現在価格・評価額 ──
    private decimal CurPrice(string holdingId) => Store.Data.CurrentPrices.GetValueOrDefault(holdingId);
    private decimal PrevPrice(string holdingId) => Store.Data.PrevPrices.GetValueOrDefault(holdingId);
    private static string CurPriceLabel(Holding h) => h.Class == AssetClass.Fund ? "現在基準価額" : "現在価格";
    // 一覧の現在価格（単価と同じ価格通貨：米国株=$ / 日本株・投信=¥）。未取得は「—」。
    private string CurPriceDisp(Holding h) { var p = CurPrice(h.Id); return p > 0 ? Price(h, p) : "—"; }

    // ── 前日比（銘柄の値動き。現在/前日のどちらかが無ければ非表示）──
    // %はネイティブ価格ベース（為替を含まない銘柄の値動き）。金額は評価額の表示通貨に一致させ、
    // 現在/前日の評価額の差＝銘柄の値動きぶん（為替は現在レートで固定）として出す。
    private decimal? DayChangePct(Holding h)
    {
        decimal cur = CurPrice(h.Id), prev = PrevPrice(h.Id);
        return (cur > 0 && prev > 0) ? (cur - prev) / prev * 100m : (decimal?)null;
    }
    // 前日比（銘柄の値動き％・表示通貨に依らない）。PC は専用「前日比」列に素の％、スマホは損益の下に「前日 …」。
    private static string DayPct(decimal pct) => (pct >= 0 ? "+" : "") + pct.ToString("0.0") + "%";
    // 前日比の金額（評価額の表示通貨に合わせる）＝現在/前日の評価額の差。現在/前日のどちらかが無ければ null。
    private decimal? DayChangeAmount(Holding h, decimal qty)
    {
        decimal cur = CurPrice(h.Id), prev = PrevPrice(h.Id);
        if (cur <= 0 || prev <= 0) return null;
        var vNow = PortfolioMath.Valuation(h, qty, cur, Store.Data.UsdJpyRate);
        var vPrev = PortfolioMath.Valuation(h, qty, prev, Store.Data.UsdJpyRate);
        if (!vNow.HasValue || !vPrev.HasValue) return null;
        var f = CcyFactor(h);
        return f <= 0 ? (decimal?)null : (vNow.Value - vPrev.Value) * f;
    }

    private decimal? Valuation(Holding h, decimal qty) =>
        PortfolioMath.Valuation(h, qty, CurPrice(h.Id), Store.Data.UsdJpyRate);
    private decimal? ValuationJpy(Holding h, decimal qty) =>
        PortfolioMath.ValuationJpy(h, qty, CurPrice(h.Id), Store.Data.UsdJpyRate);
    private decimal? UnrealizedPnl(Holding h, HoldingSummary s)
    {
        var v = Valuation(h, s.Quantity);
        return v.HasValue ? v.Value - s.CostBasis : (decimal?)null;
    }

    // 総資産（円）。数量0以外で価格が取れている銘柄だけを合計。一部未取得なら印を付ける。
    private (decimal Total, bool Missing) TotalAssets()
    {
        decimal total = 0m;
        bool anyMissing = false;
        foreach (var h in Ordered)
        {
            var qty = Summary(h).Quantity;
            if (qty == 0) continue;
            var v = ValuationJpy(h, qty);
            if (v.HasValue) total += v.Value;
            else anyMissing = true;
        }
        return (total, anyMissing);
    }

    private string TotalJpyDisplay
    {
        get { var (total, missing) = TotalAssets(); return "¥" + total.ToString("#,0") + (missing ? "（一部未取得）" : ""); }
    }

    // 元本（取得原価合計・円換算）＝現存保有分。価格に依存しないので常に表示できる。
    private decimal TotalCostJpy => CostBasisJpyAsOf(DateTime.Today.ToString("yyyy-MM-dd"), 0m);
    // 評価損益（合計・円）＝総資産 − 元本（一部未取得時は概算）。
    private decimal TotalPnlJpy => TotalAssets().Total - TotalCostJpy;

    // ヒーロー（ダーク地）の損益色クラス：益＝淡緑(up)／損＝淡赤(down)／0＝既定（白）。
    private static string HeroVClass(decimal v) => v > 0 ? "up" : v < 0 ? "down" : "";

    // 含み益/含み損（評価損益の符号）と、その率（評価損益 ÷ 元本・"+36.8%"）。元本0は空。
    private bool HeroIsGain => TotalPnlJpy >= 0;
    private string HeroGainPct
    {
        get
        {
            if (TotalCostJpy == 0) return "";
            decimal p = TotalPnlJpy / TotalCostJpy * 100m;
            return (p >= 0 ? "+" : "") + p.ToString("0.0") + "%";
        }
    }

    // 当日損益率＝当日損益 ÷ 前日終値時点の総資産（＝現在総資産 − 当日損益）。前日総資産≦0は空。" (+1.2%)"
    private string TotalDayPct(decimal dayJpy)
    {
        var prev = TotalAssets().Total - dayJpy;
        if (prev <= 0) return "";
        var p = dayJpy / prev * 100m;
        return " (" + (p >= 0 ? "+" : "") + p.ToString("0.0") + "%)";
    }

    // 当日損益（全銘柄の前日比合計・円）。現在/前日のどちらも取れている銘柄だけ合算。1件も無ければ null。
    private decimal? TotalDayPnlJpy()
    {
        decimal sum = 0m;
        bool any = false;
        foreach (var h in Ordered)
        {
            decimal cur = CurPrice(h.Id), prev = PrevPrice(h.Id);
            if (cur <= 0 || prev <= 0) continue;
            var qty = Summary(h).Quantity;
            if (qty == 0) continue;
            var vNow = PortfolioMath.ValuationJpy(h, qty, cur, Store.Data.UsdJpyRate);
            var vPrev = PortfolioMath.ValuationJpy(h, qty, prev, Store.Data.UsdJpyRate);
            if (!vNow.HasValue || !vPrev.HasValue) continue;
            sum += vNow.Value - vPrev.Value;
            any = true;
        }
        return any ? sum : (decimal?)null;
    }

    // ── 資産クラスごとのグループ小計（保有銘柄の各グループ見出し右）──
    // 評価額（円・価格未取得は除外、全件未取得は null）／取得原価（円・銘柄単位の総和）／評価損益。
    private decimal? GroupValueJpy(AssetClass c)
    {
        decimal sum = 0m;
        bool any = false;
        foreach (var h in Ordered.Where(h => h.Class == c))
        {
            var v = ValuationJpy(h, Summary(h).Quantity);
            if (v.HasValue) { sum += v.Value; any = true; }
        }
        return any ? sum : (decimal?)null;
    }
    private decimal GroupCostJpy(AssetClass c) =>
        Ordered.Where(h => h.Class == c)
               .Sum(h => PortfolioMath.HoldingCostBasisJpyAsOf(Store.Data, h, DateTime.Today.ToString("yyyy-MM-dd"), 0m));
    private decimal? GroupPnlJpy(AssetClass c)
    {
        var v = GroupValueJpy(c);
        return v.HasValue ? v.Value - GroupCostJpy(c) : (decimal?)null;
    }
    // 円換算額をグループ表示通貨へ（米国株×ドル表示なら /fx でドル化・他は円）。換算不可は "—"。
    private string GroupMoney(AssetClass c, decimal jpy)
    {
        if (c == AssetClass.UsStock && _usCcy == Currency.Usd)
        {
            decimal fx = Store.Data.UsdJpyRate;
            return fx > 0 ? "$" + (jpy / fx).ToString("#,0.##") : "—";
        }
        return "¥" + jpy.ToString("#,0");
    }
    private string GroupValueDisp(AssetClass c) { var v = GroupValueJpy(c); return v.HasValue ? GroupMoney(c, v.Value) : "—"; }
    private string GroupPnlDisp(AssetClass c) { var v = GroupPnlJpy(c); return v.HasValue ? (v.Value > 0 ? "+" : "") + GroupMoney(c, v.Value) : "—"; }
    private decimal GroupPnlSign(AssetClass c) => GroupPnlJpy(c) ?? 0m;

    // 保有カード（スマホ）の補助指標（淡色サブ行）＝元本・数量のみ（PC は表形式の各列で表示）。
    private static string UnitSuffix(Holding h) => h.Class == AssetClass.Fund ? "口" : "株";
    private string MetricsLineMobile(Holding h, HoldingSummary s) =>
        $"元本 {Money(h, s.CostBasis)} ・ {Qty(s.Quantity)}{UnitSuffix(h)}";

    // ── 価格更新（Yahoo Finance から株価＋USD/JPY を取得し現在価格に反映）──
    private bool _updating;
    private string? _updateMsg;

    // 日本株は証券コードに .T を付与、米国株はティッカーそのまま。計算本体は PortfolioMath（純粋ロジック・テスト対象）。
    private static string YahooSymbol(Holding h) => PortfolioMath.YahooSymbol(h);

    // ── 市場指標バー（固定5本・/portfolio 上部。日報用スクショに総資産と同じ帯で収める。AI不要・既存 /api/quote 再利用）──
    // 値はポートフォリオ価格取得に相乗りで更新（非永続＝当日の表示専用）。先頭 ^ は API 側で URL エンコードされる。
    // ⚠️ TOPIX は対象外：Yahoo v8 は TOPIX 指数を配信しておらず（^TOPX/998405.T は空・^TPX は別物=米国OPRA指数/USD）、
    //    ETF(1306.T 等)は絶対値が指数値と桁違いになるため、誤表示を避けて除外。日本株は日経平均で代表させる（issue #26）。
    private static readonly (string Symbol, string Label, int Decimals)[] MarketIndices =
    {
        ("^DJI", "NYダウ", 0),
        ("^IXIC", "ナスダック", 0),
        ("^GSPC", "S&P500", 2),
        ("^N225", "日経平均", 0),
        ("^KS11", "KOSPI", 0),
    };
    private readonly Dictionary<string, decimal> _indexCur = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, decimal> _indexPrev = new(StringComparer.OrdinalIgnoreCase);

    // 指標の現在値（桁区切り・小数桁は指標ごと）。未取得・休場は「—」で画面を壊さない。
    private string IndexValueDisp(string sym, int decimals) =>
        _indexCur.TryGetValue(sym, out var c) && c > 0
            ? c.ToString(decimals == 0 ? "#,0" : "#,0.00")
            : "—";
    // 前日比％（現在/前日終値のどちらかが無ければ非表示）。色・記号は前日比列と同流儀（DayPct/PnlClass を再利用）。
    private decimal? IndexPct(string sym) =>
        _indexCur.TryGetValue(sym, out var c) && _indexPrev.TryGetValue(sym, out var p) && c > 0 && p > 0
            ? (c - p) / p * 100m
            : (decimal?)null;

    private async Task UpdatePrices(bool manual = true)
    {
        if (_updating) return;
        _updating = true;
        _updateMsg = null;
        try
        {
            var stocks = Ordered
                .Where(h => h.Class != AssetClass.Fund && !string.IsNullOrWhiteSpace(h.Symbol))
                .ToList();
            var fundHoldings = Ordered
                .Where(h => h.Class == AssetClass.Fund && !string.IsNullOrWhiteSpace(h.AssocFundCd))
                .ToList();

            var symbols = stocks.Select(YahooSymbol).Distinct().ToList();
            // 市場指標（固定6本）を相乗りで取得。保有銘柄の取得件数カウントには含めない。
            foreach (var ix in MarketIndices)
                if (!symbols.Contains(ix.Symbol)) symbols.Add(ix.Symbol);
            var funds = fundHoldings
                .Select(h => new FundRef { Isin = h.Isin.Trim(), AssocFundCd = h.AssocFundCd.Trim() })
                .ToList();

            var res = await Quote.FetchAsync(symbols, funds);
            if (res == null) { if (manual) _updateMsg = "価格の取得に失敗しました。時間をおいて再度お試しください。"; return; }

            if (res.UsdJpyRate > 0) Store.Data.UsdJpyRate = res.UsdJpyRate;
            int hit = 0, target = stocks.Count + fundHoldings.Count;
            foreach (var h in stocks)
            {
                var key = YahooSymbol(h).ToUpperInvariant();
                if (res.Prices.TryGetValue(key, out var p) && p > 0)
                {
                    Store.Data.CurrentPrices[h.Id] = p;
                    hit++;
                }
                if (res.PrevClose.TryGetValue(key, out var pv) && pv > 0)
                    Store.Data.PrevPrices[h.Id] = pv;
            }
            foreach (var h in fundHoldings)
            {
                var key = h.AssocFundCd.Trim().ToUpperInvariant();
                if (res.FundPrices.TryGetValue(key, out var p) && p > 0)
                {
                    Store.Data.CurrentPrices[h.Id] = p;
                    hit++;
                }
                if (res.FundPrevClose.TryGetValue(key, out var pv) && pv > 0)
                    Store.Data.PrevPrices[h.Id] = pv;
            }
            // 市場指標（保有とは独立。取得件数のメッセージには関与しない）。
            foreach (var ix in MarketIndices)
            {
                var key = ix.Symbol.ToUpperInvariant();
                if (res.Prices.TryGetValue(key, out var p) && p > 0) _indexCur[ix.Symbol] = p;
                if (res.PrevClose.TryGetValue(key, out var pv) && pv > 0) _indexPrev[ix.Symbol] = pv;
            }
            Store.Data.PricedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            RecordSnapshot();
            BuildComposition();
            BuildTimeSeries();
            if (manual && target > 0 && hit < target)
                _updateMsg = $"一部の銘柄は価格を取得できませんでした（{hit}/{target}）。コード（証券コード/ティッカー/協会コード）をご確認ください。";
            Save();
        }
        catch
        {
            if (manual) _updateMsg = "価格の取得に失敗しました。時間をおいて再度お試しください。";
        }
        finally
        {
            _updating = false;
        }
    }

    // 価格更新のたびに、その時点の銘柄別評価額（円）と USD/JPY を時系列の1点として記録する。
    // 評価額が1件も取れなければ記録しない。同じ日付の点は最新で上書き（1日1点・自動更新でも履歴が育つ）。
    private void RecordSnapshot()
    {
        var values = new List<HoldingValue>();
        foreach (var h in Ordered)
        {
            var qty = Summary(h).Quantity;
            if (qty == 0) continue;
            var vJpy = ValuationJpy(h, qty);
            if (!vJpy.HasValue) continue;   // 価格未取得の銘柄は除外
            values.Add(new HoldingValue { HoldingId = h.Id, PriceNative = CurPrice(h.Id), ValuationJpy = vJpy.Value });
        }
        if (values.Count == 0) return;

        var now = DateTime.Now;
        var today = now.ToString("yyyy-MM-dd");
        Store.Data.Snapshots.RemoveAll(s => s.At.StartsWith(today));   // 同日は上書き
        Store.Data.Snapshots.Add(new PriceSnapshot
        {
            At = now.ToString("yyyy-MM-dd HH:mm"),
            UsdJpyRate = Store.Data.UsdJpyRate,
            Values = values
        });
        Store.Data.Snapshots.Sort((a, b) => string.CompareOrdinal(a.At, b.At));
    }

    // ── 資産構成（クラス別・銘柄別のドーナツ。PC＝両方を横並び／スマホ＝トグルで1つ）──
    private string _compMode = "class";   // スマホのトグル: "class" | "holding"
    private int _compRev;                 // 再描画キー（データ更新ごとにインクリメント）
    private List<SpendSlice> CompClassData = new();
    private List<SpendSlice> CompHoldingData = new();
    private List<SpendSlice> CompMobileData => _compMode == "class" ? CompClassData : CompHoldingData;
    private static decimal SlicesTotal(List<SpendSlice> data) => data.Sum(s => s.Value);
    private static string CompPct(decimal v, decimal total) => total == 0 ? "0%" : (v / total * 100).ToString("0.0") + "%";

    // クラス別の固定色／銘柄別の循環パレット（家計簿のカード別と同様）
    private static readonly Dictionary<AssetClass, string> ClassColors = new()
    {
        [AssetClass.JpStock] = "#5b8def",
        [AssetClass.UsStock] = "#34c3a3",
        [AssetClass.Fund] = "#f6a609",
    };
    private static readonly string[] CompPalette =
    {
        "#5b8def", "#34c3a3", "#f6a609", "#ef6a6a", "#9b7ede",
        "#4db3d6", "#e879b9", "#7ac74f", "#f08a4b", "#6c7a89"
    };

    // ドーナツ設定（中央に総資産 total・万単位／凡例は一覧へ集約しOFF／白2pxストロークでスライスを分離）。
    // クラス別・銘柄別で Colors が異なるため別インスタンス（PC は両方を同時描画する）。
    private static ApexChartOptions<SpendSlice> NewCompDonut() => new()
    {
        Chart = new Chart { Height = 260, Toolbar = new Toolbar { Show = false } },
        Legend = new Legend { Show = false },
        DataLabels = new DataLabels { Enabled = false },
        Stroke = new Stroke { Width = 2, Colors = new List<string> { "#fff" } },
        PlotOptions = new PlotOptions
        {
            Pie = new PlotOptionsPie
            {
                Donut = new PlotOptionsDonut
                {
                    Size = "70%",
                    Labels = new DonutLabels
                    {
                        Show = true,
                        Name = new DonutLabelName { Show = true, Color = "#8f8b84", FontSize = "12px", OffsetY = -2 },
                        Value = new DonutLabelValue { Show = true, Color = "#21262e", FontSize = "19px", FontWeight = 700, OffsetY = 4, Formatter = "function(v){return '¥'+Math.round(v/10000)+'万'}" },
                        Total = new DonutLabelTotal { Show = true, Label = "総資産", Color = "#8f8b84", FontSize = "12px", Formatter = "function(w){var t=w.globals.seriesTotals.reduce(function(a,b){return a+b},0);return '¥'+Math.round(t/10000)+'万'}" }
                    }
                }
            }
        },
        Tooltip = new Tooltip { Y = new TooltipY { Formatter = "function(v){return '¥'+v.toLocaleString()}" } },
        States = new States
        {
            Hover = new StatesHover { Filter = new StatesFilter { Type = StatesFilterType.darken, Value = 0.12 } },
            Active = new StatesActive { Filter = new StatesFilter { Type = StatesFilterType.darken, Value = 0.12 } }
        }
    };
    private readonly ApexChartOptions<SpendSlice> CompDonutClassOpt = NewCompDonut();
    private readonly ApexChartOptions<SpendSlice> CompDonutHoldingOpt = NewCompDonut();
    private ApexChartOptions<SpendSlice> CompMobileOpt => _compMode == "class" ? CompDonutClassOpt : CompDonutHoldingOpt;

    // 両データは BuildComposition で同時に作る。スマホのトグルは表示切替のみ（再ビルド不要）。
    private void SetCompMode(string mode) => _compMode = mode;

    // 現在の評価額（円換算）から構成比スライスを作る（クラス別・銘柄別の両方）。価格未取得・評価額0は除外。
    private void BuildComposition()
    {
        // クラス別（日本株/米国株/投資信託の固定色）
        var cls = new List<SpendSlice>();
        foreach (var grp in Groups)
        {
            decimal sum = 0m;
            foreach (var h in Ordered.Where(h => h.Class == grp.Class))
            {
                var v = ValuationJpy(h, Summary(h).Quantity);
                if (v.HasValue) sum += v.Value;
            }
            if (sum > 0) cls.Add(new SpendSlice { Key = grp.Class.ToString(), Label = grp.Label, Value = sum, Color = ClassColors[grp.Class] });
        }
        // 銘柄別（評価額の降順・循環パレット）
        var hold = new List<SpendSlice>();
        foreach (var h in Ordered)
        {
            var v = ValuationJpy(h, Summary(h).Quantity);
            if (v.HasValue && v.Value > 0) hold.Add(new SpendSlice { Key = h.Id, Label = DisplayName(h), Value = v.Value });
        }
        hold = hold.OrderByDescending(s => s.Value).ToList();
        for (int i = 0; i < hold.Count; i++) hold[i].Color = CompPalette[i % CompPalette.Length];

        CompClassData = cls;
        CompHoldingData = hold;
        CompDonutClassOpt.Colors = cls.Select(s => s.Color).ToList();
        CompDonutHoldingOpt.Colors = hold.Select(s => s.Color).ToList();
        _compRev++;
    }

    // ── 並べ替え（同一クラス内のドラッグ＆ドロップ）──
    private string? _dragId;       // ドラッグ中の銘柄
    private string? _dragOverId;   // ドロップ先のハイライト対象

    private void OnDragLeave(string id) { if (_dragOverId == id) _dragOverId = null; }
    private void OnDragEnd() { _dragId = null; _dragOverId = null; }

    // スマホ：▲▼ で同一クラス内を並べ替え（dir=-1 上 / +1 下）。クラスの SortOrder スロットを振り直す。
    private void MoveHolding(Holding h, int dir)
    {
        var list = Ordered.Where(x => x.Class == h.Class).ToList();
        int i = list.IndexOf(h);
        int j = i + dir;
        if (i < 0 || j < 0 || j >= list.Count) return;
        var slots = list.Select(x => x.SortOrder).OrderBy(x => x).ToList();
        (list[i], list[j]) = (list[j], list[i]);
        for (int k = 0; k < list.Count; k++) list[k].SortOrder = slots[k];
        Save();
    }

    private void OnDrop(Holding target)
    {
        var dragId = _dragId;
        _dragId = null;
        _dragOverId = null;
        if (dragId == null || dragId == target.Id) return;
        var dragged = HoldingById(dragId);
        if (dragged == null || dragged.Class != target.Class) return;   // 別クラスへの移動は無効

        // 同一クラス内の現在順でドラッグ要素をターゲット位置へ差し込む
        var list = Ordered.Where(h => h.Class == dragged.Class).ToList();
        list.Remove(dragged);
        int idx = list.IndexOf(target);
        if (idx < 0) return;
        list.Insert(idx, dragged);

        // そのクラスが現在占めている SortOrder 値の集合を昇順で振り直す（他クラスは不変）
        var slots = Ordered.Where(h => h.Class == dragged.Class).Select(h => h.SortOrder).OrderBy(x => x).ToList();
        for (int i = 0; i < list.Count; i++) list[i].SortOrder = slots[i];

        // 銘柄別ドーナツは評価額の降順で並ぶため SortOrder 変更の影響なし → グラフは再構築しない
        // （BuildComposition で _compRev を上げると全チャートが再マウントし画面が再描画されてしまう）
        Save();
    }

    // ── 推移（時系列）──
    // Label＝カテゴリ軸用ラベル（評価損益・配当）／XDate＝日時軸用の実日付（総資産・元本）。
    public class ChartPoint { public string Label { get; set; } = ""; public DateTime XDate { get; set; } public decimal Value { get; set; } }

    private int _trendRev;                          // 推移チャートの再描画キー（BuildTimeSeries 時のみ更新＝構成切替では再描画しない）
    private List<ChartPoint> AssetSeries = new();   // 総資産（Σ評価額・円。スナップショット以降）
    private List<ChartPoint> CostSeries = new();    // 元本（取得原価合計・円。取引履歴から全期間算出）
    private List<ChartPoint> PnlSeries = new();     // 評価損益（総資産 − 取得原価・円。スナップショット以降）

    // 推移の表示期間（1W/1M/3M/6M/1Y/ALL）。
    private string _trendPeriod = "ALL";
    private static readonly (string Key, string Label)[] TrendPeriods =
    {
        ("1W", "1W"), ("1M", "1M"), ("3M", "3M"), ("6M", "6M"), ("1Y", "1Y"), ("ALL", "ALL"),
    };
    private DateTime? PeriodStart() => _trendPeriod switch
    {
        "1W" => DateTime.Today.AddDays(-7),
        "1M" => DateTime.Today.AddMonths(-1),
        "3M" => DateTime.Today.AddMonths(-3),
        "6M" => DateTime.Today.AddMonths(-6),
        "1Y" => DateTime.Today.AddYears(-1),
        _ => null,   // ALL
    };
    private void SetTrendPeriod(string p) { _trendPeriod = p; BuildTimeSeries(); }

    // 折れ線設定（家計簿の NewLineOptions と同流儀）。チャートごとに専用インスタンス。
    private static ApexChartOptions<ChartPoint> NewLineOpt() => new()
    {
        Chart = new Chart { Height = 220, Toolbar = new Toolbar { Show = false } },
        Stroke = new Stroke { Curve = Curve.Smooth },
        Yaxis = new List<YAxis> { new YAxis { Labels = new YAxisLabels { Formatter = "function(v){return '¥'+Math.round(v).toLocaleString()}" } } }
    };
    // 総資産・元本は実日付で重ねるため日時軸（総資産=今日以降／元本=全期間でも時間軸で正しく整列）。軸ラベルは yy/MM。
    private static ApexChartOptions<ChartPoint> NewDateLineOpt()
    {
        var o = NewLineOpt();
        o.Xaxis = new XAxis
        {
            Type = XAxisType.Datetime,
            Labels = new XAxisLabels { Format = "yy/MM", DatetimeUTC = false }
        };
        return o;
    }
    private readonly ApexChartOptions<ChartPoint> AssetLineOpt = NewDateLineOpt();
    private readonly ApexChartOptions<ChartPoint> PnlLineOpt = NewLineOpt();

    // スナップショットの "yyyy-MM-dd HH:mm" → 軸ラベル "M/d"
    private static string SnapLabel(string at) =>
        DateTime.TryParse(at, out var d) ? d.ToString("M/d") : at;

    private void BuildTimeSeries()
    {
        var start = PeriodStart();                                   // null=ALL
        string? startStr = start?.ToString("yyyy-MM-dd");
        bool InRange(string ymd) => startStr == null || string.CompareOrdinal(ymd, startStr) >= 0;
        string DatePart(string at) => at.Length >= 10 ? at[..10] : at;

        var snaps = Store.Data.Snapshots
            .Where(s => DateTime.TryParse(s.At, out _) && InRange(DatePart(s.At)))
            .OrderBy(s => s.At, StringComparer.Ordinal).ToList();

        // 総資産＝スナップショット（過去価格が無いので記録以降のみ）。日時軸。
        AssetSeries = snaps
            .Select(s => new ChartPoint { XDate = DateTime.Parse(s.At), Value = s.Values.Sum(v => v.ValuationJpy) })
            .ToList();

        // 元本＝取引履歴（買付/売却/配当再投資の各日付＋今日）から算出。価格不要なので最初の購入時点まで遡れる。
        // 期間指定時は期間開始日にアンカー点を置き、その時点の積み上がり額から線を始める。
        var costDates = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var b in Store.Data.Buys) if (b.Date.Length >= 10) costDates.Add(b.Date[..10]);
        foreach (var sl in Store.Data.Sells) if (sl.Date.Length >= 10) costDates.Add(sl.Date[..10]);
        foreach (var d in Store.Data.Dividends) if (d.Quantity != 0 && d.Date.Length >= 10) costDates.Add(d.Date[..10]);
        costDates.Add(DateTime.Today.ToString("yyyy-MM-dd"));   // 直近まで線を伸ばす
        var costDateList = costDates.Where(InRange).ToList();
        if (startStr != null) costDateList.Insert(0, startStr);     // 期間開始のアンカー
        CostSeries = costDateList
            .Where(dt => DateTime.TryParse(dt, out _))
            .Distinct()
            .OrderBy(dt => dt, StringComparer.Ordinal)
            .Select(dt => new ChartPoint { XDate = DateTime.Parse(dt), Value = CostBasisJpyAsOf(dt, 0m) })
            .ToList();

        // 評価損益＝総資産 − 元本。価格が要るのでスナップショット以降のみ（カテゴリ軸）。
        PnlSeries = snaps.Select(s =>
        {
            decimal val = s.Values.Sum(v => v.ValuationJpy);
            decimal cost = CostBasisJpyAsOf(DatePart(s.At), s.UsdJpyRate);
            return new ChartPoint { Label = SnapLabel(s.At), Value = val - cost };
        }).ToList();

        _trendRev++;
    }

    // 指定日時点の取得原価合計（円換算）。計算本体は PortfolioMath（純粋ロジック・テスト対象）へ委譲する。
    private decimal CostBasisJpyAsOf(string date, decimal snapRate) =>
        PortfolioMath.CostBasisJpyAsOf(Store.Data, date, snapRate);

    // ── 新規銘柄登録 ──
    private bool _addOpen;
    private string _nName = "";
    private AssetClass _nClass = AssetClass.Fund;
    private AccountKind _nAccount = AccountKind.NisaTsumitate;
    private Currency _nCurrency = Currency.Jpy;
    private string _nSymbol = "";
    private string _nIsin = "";
    private string _nAssocFundCd = "";
    private string _nFundPick = "";
    private string _bDate = "";
    private decimal _bQty;
    private decimal _bPrice;

    private string BuyPriceLabel => _nClass == AssetClass.Fund ? "基準価額" : _nClass == AssetClass.UsStock ? "単価($)" : "単価";

    private bool CanAdd => !string.IsNullOrWhiteSpace(_nName) && _bQty != 0;

    private void OpenAdd()
    {
        _nName = ""; _nClass = AssetClass.Fund; _nAccount = AccountKind.NisaTsumitate;
        _nCurrency = Currency.Jpy; _nSymbol = ""; _nIsin = ""; _nAssocFundCd = ""; _nFundPick = "";
        _bDate = DateTime.Today.ToString("yyyy-MM-dd");
        _bQty = 0; _bPrice = 0;
        _addOpen = true;
    }
    private void CloseAdd() => _addOpen = false;

    private async Task SubmitAdd()
    {
        if (!CanAdd) return;
        var name = _nName.Trim();
        // 同一銘柄（名前＋クラス＋口座）が既にあれば合算、無ければ新規作成
        var existing = Store.Data.Holdings.FirstOrDefault(h =>
            !h.IsDeleted && h.Name == name && h.Class == _nClass && h.Account == _nAccount);
        string holdingId;
        if (existing != null)
        {
            holdingId = existing.Id;
        }
        else
        {
            var nh = new Holding
            {
                Name = name,
                Class = _nClass,
                Account = _nAccount,
                CostCurrency = _nClass == AssetClass.UsStock ? _nCurrency : Currency.Jpy,
                Symbol = _nClass == AssetClass.Fund ? "" : _nSymbol.Trim(),
                Isin = _nClass == AssetClass.Fund ? _nIsin.Trim() : "",
                AssocFundCd = _nClass == AssetClass.Fund ? _nAssocFundCd.Trim() : "",
                SortOrder = Store.Data.Holdings.Count,
            };
            Store.Data.Holdings.Add(nh);
            holdingId = nh.Id;
        }
        Store.Data.Buys.Add(new BuyLot { HoldingId = holdingId, Date = _bDate, Quantity = _bQty, UnitPrice = _bPrice });
        Save();
        _addOpen = false;
        // 追加した銘柄の現在価格をすぐ反映（自動）。
        await UpdatePrices(manual: false);
    }

    // 投信マスタから選択 → 銘柄名・協会コード（＋ISIN）を自動入力。「その他」「未選択」はクリア。
    private void OnFundPicked()
    {
        if (FundMaster.Items.FirstOrDefault(x => x.Code == _nFundPick) is { } f)
        {
            _nName = f.Name;
            _nAssocFundCd = f.Code;
            _nIsin = f.Isin ?? "";
        }
        else   // "" または "__other"
        {
            _nName = "";
            _nAssocFundCd = "";
            _nIsin = "";
        }
    }

    // ── 削除 ──
    private bool ShowConfirm;
    private string? _deleteId;
    private string ConfirmMessage = "";

    private void AskDelete(Holding h)
    {
        ConfirmMessage = $"「{DisplayName(h)}」を削除します。\n買付・売却・配当の記録も一緒に削除されます。\nこの操作は取り消せません。";
        _deleteId = h.Id;
        ShowConfirm = true;
    }

    private void ExecuteDelete()
    {
        if (_deleteId != null)
        {
            Store.Data.Holdings.RemoveAll(h => h.Id == _deleteId);
            Store.Data.Buys.RemoveAll(b => b.HoldingId == _deleteId);
            Store.Data.Sells.RemoveAll(s => s.HoldingId == _deleteId);
            Store.Data.Dividends.RemoveAll(d => d.HoldingId == _deleteId);
        }
        _deleteId = null;
        ShowConfirm = false;
        BuildComposition();
        BuildTimeSeries();
        Save();
    }

    // ── 取引・設定ダイアログ（編集はバッファに溜め「保存」で初めて Store へ反映）──
    private string? _txHoldingId;
    private Holding? _editH;                       // 編集中の銘柄（クローン）
    private List<BuyLot> _editBuys = new();
    private List<SellLot> _editSells = new();
    private List<Dividend> _editDivs = new();
    private decimal _editPrice;                    // 現在価格（この銘柄・編集中の値）
    private bool _txDirty;                          // 未保存の変更があるか
    private bool _txCloseWarn;                       // 破棄確認ダイアログの表示

    private static Holding CloneH(Holding h) => new()
    {
        Id = h.Id, Name = h.Name, Symbol = h.Symbol, Isin = h.Isin, AssocFundCd = h.AssocFundCd,
        Class = h.Class, Account = h.Account, CostCurrency = h.CostCurrency, SortOrder = h.SortOrder, IsDeleted = h.IsDeleted
    };
    private static BuyLot CloneB(BuyLot b) => new() { Id = b.Id, HoldingId = b.HoldingId, Date = b.Date, Quantity = b.Quantity, UnitPrice = b.UnitPrice, FxRate = b.FxRate, IsEspp = b.IsEspp, Amount = b.Amount };
    private static SellLot CloneS(SellLot s) => new() { Id = s.Id, HoldingId = s.HoldingId, Date = s.Date, Quantity = s.Quantity, UnitPrice = s.UnitPrice };
    private static Dividend CloneD(Dividend d) => new() { Id = d.Id, HoldingId = d.HoldingId, Date = d.Date, Amount = d.Amount, Currency = d.Currency, Quantity = d.Quantity };

    private void OpenTx(string id)
    {
        var src = HoldingById(id);
        if (src == null) return;
        _editH = CloneH(src);
        _editBuys = Store.Data.Buys.Where(b => b.HoldingId == id).Select(CloneB).ToList();
        _editSells = Store.Data.Sells.Where(s => s.HoldingId == id).Select(CloneS).ToList();
        _editDivs = Store.Data.Dividends.Where(d => d.HoldingId == id).Select(CloneD).ToList();
        _editPrice = Store.Data.CurrentPrices.GetValueOrDefault(id);
        _txDirty = false;
        _txCloseWarn = false;
        _txOpen.Clear();   // スマホ：取引カードは最初すべてたたむ
        _txHoldingId = id;
    }

    private void TxDirty() => _txDirty = true;

    // ダイアログ内プレビュー用の評価額（編集中の現在価格で計算・Store には触れない）
    private decimal? EditVal(decimal qty) => _editH == null ? null : PortfolioMath.Valuation(_editH, qty, _editPrice, Store.Data.UsdJpyRate);
    private decimal? EditUpnl(HoldingSummary s)
    {
        var v = EditVal(s.Quantity);
        return v.HasValue ? v.Value - s.CostBasis : (decimal?)null;
    }

    // ESPP 列の表示可否：本人が TSMC 社員（Owner は常に true）かつ TSM ティッカーの米国株のみ。非社員には一切出さない。
    private bool ShowEspp(Holding h) =>
        Store.IsTsmcEmployee && h.Class == AssetClass.UsStock
        && string.Equals(h.Symbol?.Trim(), "TSM", StringComparison.OrdinalIgnoreCase);

    // スマホ：取引カードの展開状態（id 集合）。既存はたたんで表示、＋追加は自動展開。
    private readonly HashSet<string> _txOpen = new();
    private void ToggleTx(string id) { if (!_txOpen.Remove(id)) _txOpen.Add(id); }

    private void AddBuy() { var b = new BuyLot { HoldingId = _editH!.Id, Date = DateTime.Today.ToString("yyyy-MM-dd"), IsEspp = ShowEspp(_editH!) }; _editBuys.Add(b); _txOpen.Add(b.Id); TxDirty(); }
    private void AddSell() { var sl = new SellLot { HoldingId = _editH!.Id, Date = DateTime.Today.ToString("yyyy-MM-dd") }; _editSells.Add(sl); _txOpen.Add(sl.Id); TxDirty(); }
    private void AddDiv() { var dv = new Dividend { HoldingId = _editH!.Id, Date = DateTime.Today.ToString("yyyy-MM-dd"), Currency = _editH!.CostCurrency }; _editDivs.Add(dv); _txOpen.Add(dv.Id); TxDirty(); }

    // 投信マスタ選択（編集バッファに反映）
    private void OnTxFundPicked(string? code)
    {
        if (_editH == null) return;
        if (FundMaster.Items.FirstOrDefault(x => x.Code == code) is { } f)
        {
            _editH.Name = f.Name; _editH.AssocFundCd = f.Code; _editH.Isin = f.Isin ?? "";
        }
        TxDirty();
    }

    // 資産クラス変更（編集バッファに反映）。米国株以外は円建て・投信はシンボル無し。
    private void OnClassChanged()
    {
        if (_editH == null) return;
        if (_editH.Class != AssetClass.UsStock) _editH.CostCurrency = Currency.Jpy;
        if (_editH.Class == AssetClass.Fund) _editH.Symbol = "";
        else { _editH.Isin = ""; _editH.AssocFundCd = ""; }
        TxDirty();
    }

    // 保存：編集バッファを Store へ書き戻し、価格自動更新（コード変更などを反映）。
    private async Task SaveTx()
    {
        if (_editH == null) return;
        var id = _editH.Id;
        var dest = HoldingById(id);
        if (dest != null)
        {
            dest.Name = _editH.Name; dest.Symbol = _editH.Symbol; dest.Isin = _editH.Isin; dest.AssocFundCd = _editH.AssocFundCd;
            dest.Class = _editH.Class; dest.Account = _editH.Account; dest.CostCurrency = _editH.CostCurrency;
        }
        Store.Data.Buys.RemoveAll(b => b.HoldingId == id); Store.Data.Buys.AddRange(_editBuys);
        Store.Data.Sells.RemoveAll(s => s.HoldingId == id); Store.Data.Sells.AddRange(_editSells);
        Store.Data.Dividends.RemoveAll(d => d.HoldingId == id); Store.Data.Dividends.AddRange(_editDivs);
        if (_editPrice > 0) Store.Data.CurrentPrices[id] = _editPrice;
        else Store.Data.CurrentPrices.Remove(id);

        _txHoldingId = null; _editH = null; _txDirty = false; _txCloseWarn = false;
        BuildComposition();
        BuildTimeSeries();
        Save();
        await UpdatePrices(manual: false);   // 価格を再取得して評価額・推移へ反映
    }

    // 閉じる：未保存の変更があれば破棄確認、無ければそのまま閉じる。
    private void RequestCloseTx() { if (_txDirty) _txCloseWarn = true; else DiscardTx(); }
    private void DiscardTx() { _txHoldingId = null; _editH = null; _txDirty = false; _txCloseWarn = false; }

    // 取引ダイアログからの削除：編集を破棄してダイアログを閉じ、削除確認を出す（重なり順の都合）。
    private void DeleteFromTx()
    {
        var id = _txHoldingId;
        DiscardTx();
        var h = id != null ? HoldingById(id) : null;
        if (h != null) AskDelete(h);
    }
}
