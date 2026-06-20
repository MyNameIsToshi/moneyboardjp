using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class PortfolioMathTests
{
    private static Holding H(AssetClass cls = AssetClass.JpStock, Currency ccy = Currency.Jpy) =>
        new() { Id = "h", Class = cls, CostCurrency = ccy };

    private static BuyLot Buy(decimal qty, decimal price, bool espp = false, decimal amount = 0) =>
        new() { HoldingId = "h", Quantity = qty, UnitPrice = price, IsEspp = espp, Amount = amount };

    [Theory]
    [InlineData(AssetClass.Fund, 10000)]
    [InlineData(AssetClass.JpStock, 1)]
    [InlineData(AssetClass.UsStock, 1)]
    public void Divisor_FundIs10000_StocksAre1(AssetClass cls, int expected) =>
        Assert.Equal(expected, PortfolioMath.Divisor(cls));

    [Fact]
    public void CostFactor_EsppApplies15PercentDiscount()
    {
        Assert.Equal(1m, PortfolioMath.CostFactor(Buy(1, 1)));
        Assert.Equal(0.85m, PortfolioMath.CostFactor(Buy(1, 1, espp: true)));
    }

    [Fact]
    public void Summarize_WeightedAverageAndCostBasis()
    {
        var h = H();
        var buys = new[] { Buy(10, 100), Buy(10, 200) };
        var s = PortfolioMath.Summarize(h, buys, Array.Empty<SellLot>(), Array.Empty<Dividend>());

        Assert.Equal(20m, s.Quantity);
        Assert.Equal(150m, s.AvgUnitPrice);
        Assert.Equal(3000m, s.CostBasis);
        Assert.Equal(0m, s.RealizedPnl);
    }

    [Fact]
    public void Summarize_RealizedPnlOnSell()
    {
        var h = H();
        var buys = new[] { Buy(10, 100), Buy(10, 200) };          // 平均取得単価 150
        var sells = new[] { new SellLot { HoldingId = "h", Quantity = 5, UnitPrice = 250 } };
        var s = PortfolioMath.Summarize(h, buys, sells, Array.Empty<Dividend>());

        Assert.Equal(15m, s.Quantity);
        Assert.Equal(2250m, s.CostBasis);          // 15 × 150
        Assert.Equal(500m, s.RealizedPnl);         // (250 - 150) × 5
    }

    [Fact]
    public void Summarize_ReinvestedDividendLowersAverage()
    {
        var h = H();
        var buys = new[] { Buy(10, 100) };
        var dividends = new[] { new Dividend { HoldingId = "h", Amount = 0, Quantity = 10 } };  // 取得コスト$0で10株
        var s = PortfolioMath.Summarize(h, buys, Array.Empty<SellLot>(), dividends);

        Assert.Equal(20m, s.Quantity);
        Assert.Equal(50m, s.AvgUnitPrice);   // 1000 / 20
    }

    [Fact]
    public void Summarize_EsppLotUsesDiscountedCost()
    {
        var h = H(AssetClass.UsStock, Currency.Usd);
        var buys = new[] { Buy(10, 100, espp: true) };
        var s = PortfolioMath.Summarize(h, buys, Array.Empty<SellLot>(), Array.Empty<Dividend>());

        Assert.Equal(85m, s.AvgUnitPrice);   // 100 × 0.85
        Assert.Equal(850m, s.CostBasis);
    }

    [Fact]
    public void Summarize_FundAmountOverridesCostBasis()
    {
        var h = H(AssetClass.Fund);
        // 受渡金額 11,500 を実取得原価に（口数丸めズレを解消）。平均取得単価は基準価額のまま。
        var buys = new[] { Buy(10000, 12000, amount: 11500) };
        var s = PortfolioMath.Summarize(h, buys, Array.Empty<SellLot>(), Array.Empty<Dividend>());

        Assert.Equal(12000m, s.AvgUnitPrice);
        Assert.Equal(11500m, s.CostBasis);
    }

    [Fact]
    public void Summarize_NoBuys_ReturnsZeros()
    {
        var s = PortfolioMath.Summarize(H(), Array.Empty<BuyLot>(), Array.Empty<SellLot>(), Array.Empty<Dividend>());
        Assert.Equal(0m, s.Quantity);
        Assert.Equal(0m, s.AvgUnitPrice);     // 買付なし＝加重平均0（ゼロ除算しない）
        Assert.Equal(0m, s.CostBasis);
    }

    [Fact]
    public void Valuation_ZeroQuantityIsZero_NoPriceIsNull()
    {
        var h = H();
        Assert.Equal(0m, PortfolioMath.Valuation(h, qty: 0, nativePrice: 100, usdJpyRate: 150));
        Assert.Null(PortfolioMath.Valuation(h, qty: 10, nativePrice: 0, usdJpyRate: 150));
    }

    [Fact]
    public void Valuation_JpStockIsNativeRaw()
    {
        var h = H();
        Assert.Equal(2000m, PortfolioMath.Valuation(h, qty: 10, nativePrice: 200, usdJpyRate: 150));
    }

    [Fact]
    public void Valuation_UsStockUsdKeepsUsd_JpyConverts()
    {
        var usd = H(AssetClass.UsStock, Currency.Usd);
        Assert.Equal(500m, PortfolioMath.Valuation(usd, qty: 10, nativePrice: 50, usdJpyRate: 150));

        var jpy = H(AssetClass.UsStock, Currency.Jpy);
        Assert.Equal(75_000m, PortfolioMath.Valuation(jpy, qty: 10, nativePrice: 50, usdJpyRate: 150));
        Assert.Null(PortfolioMath.Valuation(jpy, qty: 10, nativePrice: 50, usdJpyRate: 0));  // 為替不足
    }

    [Fact]
    public void ValuationJpy_UsStockAlwaysConverts()
    {
        var usd = H(AssetClass.UsStock, Currency.Usd);
        Assert.Equal(75_000m, PortfolioMath.ValuationJpy(usd, qty: 10, nativePrice: 50, usdJpyRate: 150));
        Assert.Null(PortfolioMath.ValuationJpy(usd, qty: 10, nativePrice: 50, usdJpyRate: 0));
    }

    [Fact]
    public void ValuationJpy_FundUsesDivisor()
    {
        var h = H(AssetClass.Fund);
        Assert.Equal(12_000m, PortfolioMath.ValuationJpy(h, qty: 10000, nativePrice: 12000, usdJpyRate: 150));
    }
}
