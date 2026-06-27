namespace MoneyBoard.Pages;

using MoneyBoardShared;

// 表示ヘルパ・計算補助（Store の読み取りのみ・副作用なし）。更新・副作用系は Portfolio.razor.cs に集約。
public partial class Portfolio
{
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
    private static string PnlPct(decimal? upnl, decimal costBasis) => PortfolioMath.PnlPct(upnl, costBasis);
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
    private decimal? DayChangePct(Holding h) => PortfolioMath.DayChangePct(CurPrice(h.Id), PrevPrice(h.Id));
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
        get { var (total, missing) = TotalAssets(); return IsMasked ? "¥****" : "¥" + total.ToString("#,0") + (missing ? "（一部未取得）" : ""); }
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
    private decimal? GroupValueJpy(AssetClass c) => PortfolioMath.GroupValuationJpy(Store.Data, c);
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

    // 指定日時点の取得原価合計（円換算）。計算本体は PortfolioMath（純粋ロジック・テスト対象）へ委譲する。
    private decimal CostBasisJpyAsOf(string date, decimal snapRate) =>
        PortfolioMath.CostBasisJpyAsOf(Store.Data, date, snapRate);

    // ── マスク対応ラッパー ──
    private string MaskedMoney(Holding h, decimal v) => IsMasked ? CurSym(h) + "****" : Money(h, v);
    private string MaskedMoneySigned(Holding h, decimal v) => IsMasked ? CurSym(h) + "****" : MoneySigned(h, v);
    private string MaskedMoneyDisp(Holding h, decimal v) => IsMasked ? CcySym(DispCcy(h)) + "****" : MoneyDisp(h, v);
    private string MaskedMoneyDispSigned(Holding h, decimal v) => IsMasked ? CcySym(DispCcy(h)) + "****" : MoneyDispSigned(h, v);
    private string MaskedMoneyDispOpt(Holding h, decimal? v) => IsMasked ? CcySym(DispCcy(h)) + "****" : MoneyDispOpt(h, v);
    private string MaskedMoneyDispSignedOpt(Holding h, decimal? v) => IsMasked ? CcySym(DispCcy(h)) + "****" : MoneyDispSignedOpt(h, v);
    private string MaskedGroupValueDisp(AssetClass c) => IsMasked ? (c == AssetClass.UsStock && _usCcy == Currency.Usd ? "$****" : "¥****") : GroupValueDisp(c);
    private string MaskedGroupPnlDisp(AssetClass c) => IsMasked ? (c == AssetClass.UsStock && _usCcy == Currency.Usd ? "$****" : "¥****") : GroupPnlDisp(c);
    private string MaskedMetricsLineMobile(Holding h, HoldingSummary s) => IsMasked ? $"元本 {CurSym(h)}**** ・ {Qty(s.Quantity)}{UnitSuffix(h)}" : MetricsLineMobile(h, s);
}
