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

        // 平均取得単価＝買付の金額合計 ÷ 取得数量合計（再投資株は$0なので分母だけ増える。Divisor は約分されるので単価そのもの）
        decimal avg = boughtQty > 0 ? hb.Sum(b => b.Quantity * b.UnitPrice) / boughtQty : 0m;
        decimal costBasis = qty * avg / div;
        // 実現損益＝Σ(売却単価 − 平均取得単価)×売却数量 ÷ Divisor（平均取得単価法の概算）
        decimal realized = hs.Sum(s => (s.UnitPrice - avg) * s.Quantity) / div;
        decimal divSum = hd.Sum(x => x.Amount);

        return new HoldingSummary(qty, avg, costBasis, realized, divSum);
    }

    /// <summary>
    /// 現在評価額（建て通貨ベース）。数量0は0、価格未取得(&lt;=0)や為替不足は null。
    /// 米国株の現在価格はドル建て前提（Yahoo Finance）。円建て米国株は USD/JPY で円換算して返す。
    /// </summary>
    public static decimal? Valuation(Holding h, decimal qty, decimal nativePrice, decimal usdJpyRate)
    {
        if (qty == 0) return 0m;
        if (nativePrice <= 0) return null;
        decimal raw = qty * nativePrice / Divisor(h.Class);
        if (h.Class == AssetClass.UsStock && h.CostCurrency == Currency.Jpy)
            return usdJpyRate > 0 ? raw * usdJpyRate : (decimal?)null;
        return raw;
    }

    /// <summary>現在評価額（円換算・総資産集計用）。数量0は0、価格未取得や為替不足は null。</summary>
    public static decimal? ValuationJpy(Holding h, decimal qty, decimal nativePrice, decimal usdJpyRate)
    {
        if (qty == 0) return 0m;
        if (nativePrice <= 0) return null;
        decimal raw = qty * nativePrice / Divisor(h.Class);
        if (h.Class == AssetClass.UsStock)
            return usdJpyRate > 0 ? raw * usdJpyRate : (decimal?)null;
        return raw;
    }
}
