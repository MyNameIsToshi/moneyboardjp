namespace MoneyBoardShared;

/// <summary>1銘柄の集計（建て通貨ベース）。評価額・評価損益は現在価格が要るためここには含めない（後のスライス）。</summary>
public record HoldingSummary(
    decimal Quantity,      // 現在数量（Σ買付 − Σ売却）
    decimal AvgUnitPrice,  // 平均取得単価（買付の数量加重平均。投信は基準価額）
    decimal CostBasis,     // 取得原価（現在保有分・建て通貨）
    decimal RealizedPnl,   // 実現損益（建て通貨・平均取得単価法の概算）
    decimal Dividends);    // 配当合計（建て通貨）

/// <summary>ポートフォリオの集計計算（実行時と将来の集計で共有）。</summary>
public static class PortfolioMath
{
    /// <summary>投信の基準価額は「1万口あたり」。金額＝数量×単価÷Divisor（投信=10,000 / 株=1）。</summary>
    public static int Divisor(AssetClass c) => c == AssetClass.Fund ? 10000 : 1;

    /// <summary>ESPP（従業員株式購入制度）の会社補助＝15%。ESPP 買付の取得原価は市場価格×(1−これ)＝実拠出。</summary>
    public const decimal EsppDiscount = 0.15m;
    /// <summary>買付ロットの実取得単価係数（ESPP は割引後＝実際に払った価格）。</summary>
    public static decimal CostFactor(BuyLot b) => b.IsEspp ? 1m - EsppDiscount : 1m;

    /// <summary>1買付ロットの実取得原価（建て通貨）。Amount(受渡金額)があればそのまま（口数丸め・割引等は実額に内包済み）、
    /// 無ければ数量×単価÷Divisor×ESPP係数。Summarize と CostBasisJpy 系で式を共有しドリフトを防ぐ。</summary>
    private static decimal LotNativeCost(BuyLot b, int divisor)
        => b.Amount > 0 ? b.Amount : b.Quantity * b.UnitPrice * CostFactor(b) / divisor;

    public static HoldingSummary Summarize(
        Holding h,
        IEnumerable<BuyLot> buys,
        IEnumerable<SellLot> sells,
        IEnumerable<Dividend> dividends)
    {
        int div = Divisor(h.Class);
        var hb = buys.Where(b => b.HoldingId == h.Id).ToList();
        var hs = sells.Where(s => s.HoldingId == h.Id).ToList();
        var hd = dividends.Where(x => x.HoldingId == h.Id).ToList();

        // 配当再投資株（取得コスト$0）＝取得株数には含めるが、買付金額には足さない（→平均取得単価が下がる）
        decimal reinvestQty = hd.Sum(d => d.Quantity);
        decimal boughtQty = hb.Sum(b => b.Quantity) + reinvestQty;
        decimal soldQty = hs.Sum(s => s.Quantity);
        decimal qty = boughtQty - soldQty;

        // 取得総額（建て通貨）＝買付ロットの実取得原価の合計。
        decimal totalCost = hb.Sum(b => LotNativeCost(b, div));
        // 平均取得単価（基準価額/単価・表示用）＝単価の数量加重平均。米国株は単価＝ドルなのでドルの加重平均、
        // 日本株は円/株、投信は基準価額。ESPP は割引後の実価格・再投資株は$0で薄まる。
        // （元本＝取得金額を入れた円拠出でも、単価・平均取得単価はドル建て＝価格通貨で表示する）
        decimal avg = boughtQty > 0 ? hb.Sum(b => b.Quantity * b.UnitPrice * CostFactor(b)) / boughtQty : 0m;
        // 1口/1株あたり実取得原価（建て通貨）。取得原価・実現損益はこれで按分（平均取得単価法）。
        decimal costPerUnit = boughtQty > 0 ? totalCost / boughtQty : 0m;
        decimal costBasis = qty * costPerUnit;
        // 実現損益＝Σ(売却単価÷Divisor − 1口あたり取得原価)×売却数量
        decimal realized = hs.Sum(s => (s.UnitPrice / div - costPerUnit) * s.Quantity);
        decimal divSum = hd.Sum(x => x.Amount);

        return new HoldingSummary(qty, avg, costBasis, realized, divSum);
    }

    /// <summary>評価額の共通計算。数量0は0、価格未取得(&lt;=0)は null。convertToJpy なら USD/JPY で円換算
    /// （為替不足は null）、それ以外は raw（建て通貨）をそのまま返す。Valuation/ValuationJpy の差は円換算条件のみ。</summary>
    private static decimal? RawValuation(Holding h, decimal qty, decimal nativePrice, decimal usdJpyRate, bool convertToJpy)
    {
        if (qty == 0) return 0m;
        if (nativePrice <= 0) return null;
        decimal raw = qty * nativePrice / Divisor(h.Class);
        if (convertToJpy)
            return usdJpyRate > 0 ? raw * usdJpyRate : (decimal?)null;
        return raw;
    }

    /// <summary>
    /// 現在評価額（建て通貨ベース）。数量0は0、価格未取得(&lt;=0)や為替不足は null。
    /// 米国株の現在価格はドル建て前提（Yahoo Finance）。円建て米国株は USD/JPY で円換算して返す。
    /// </summary>
    public static decimal? Valuation(Holding h, decimal qty, decimal nativePrice, decimal usdJpyRate)
        => RawValuation(h, qty, nativePrice, usdJpyRate, h.Class == AssetClass.UsStock && h.CostCurrency == Currency.Jpy);

    /// <summary>現在評価額（円換算・総資産集計用）。数量0は0、価格未取得や為替不足は null。</summary>
    public static decimal? ValuationJpy(Holding h, decimal qty, decimal nativePrice, decimal usdJpyRate)
        => RawValuation(h, qty, nativePrice, usdJpyRate, h.Class == AssetClass.UsStock);

    /// <summary>
    /// 指定日（"yyyy-MM-dd"）時点の取得原価合計（円換算）。買付/売却を日付で絞り平均取得単価法で算出。
    /// ドル建ては買付ロットごとの「約定為替レート」で円換算（実際の拠出円に一致）。約定レート未設定ロットは
    /// fallback（snapRate&gt;0 ならそれ、無ければ data.UsdJpyRate）で代用。
    /// </summary>
    public static decimal CostBasisJpyAsOf(PortfolioData data, string date, decimal snapRate)
    {
        decimal total = 0m;
        foreach (var h in data.Holdings.Where(h => !h.IsDeleted))
            total += HoldingCostBasisJpyAsOf(data, h, date, snapRate);
        return total;
    }

    /// <summary>
    /// 単一銘柄の指定日時点の取得原価（円換算）。<see cref="CostBasisJpyAsOf"/> の銘柄単位版で、
    /// 資産クラスごとのグループ小計（評価損益）に使う。アルゴリズムは全体版と同一（全体版＝本メソッドの総和）。
    /// </summary>
    public static decimal HoldingCostBasisJpyAsOf(PortfolioData data, Holding h, string date, decimal snapRate)
    {
        decimal fallback = snapRate > 0 ? snapRate : data.UsdJpyRate;   // 約定レート未設定ロットの代用
        int divsor = Divisor(h.Class);
        var buys = data.Buys.Where(b => b.HoldingId == h.Id && string.CompareOrdinal(b.Date, date) <= 0).ToList();
        // 配当再投資株（取得コスト$0）も as-of で取得数量に含める（Summarize と整合）
        decimal reinvest = data.Dividends.Where(d => d.HoldingId == h.Id && string.CompareOrdinal(d.Date, date) <= 0).Sum(d => d.Quantity);
        if (buys.Count == 0 && reinvest == 0) return 0m;
        decimal bq = buys.Sum(b => b.Quantity) + reinvest;
        decimal sq = data.Sells.Where(s => s.HoldingId == h.Id && string.CompareOrdinal(s.Date, date) <= 0).Sum(s => s.Quantity);
        decimal qty = bq - sq;
        if (qty <= 0 || bq <= 0) return 0m;

        // 取得総額（円）。ドル建ては各ロットを約定レート(無ければ fallback)で円換算。
        decimal boughtJpy = h.CostCurrency == Currency.Usd
            ? buys.Sum(b => LotNativeCost(b, divsor) * (b.FxRate > 0 ? b.FxRate : fallback))
            : buys.Sum(b => LotNativeCost(b, divsor));
        // 現在保有分の元本＝取得総額 ×(現在数量 / 取得数量)。再投資株は$0で取得総額に寄与しないので平均が薄まる。
        return boughtJpy * (qty / bq);
    }

    /// <summary>Yahoo Finance 用シンボル。日本株は証券コードに .T を付与（既に "." 付きはそのまま）、米国株はティッカーそのまま。</summary>
    public static string YahooSymbol(Holding h)
    {
        var s = h.Symbol.Trim();
        if (h.Class == AssetClass.JpStock && s.Length > 0 && !s.Contains('.')) return s + ".T";
        return s;
    }
}
