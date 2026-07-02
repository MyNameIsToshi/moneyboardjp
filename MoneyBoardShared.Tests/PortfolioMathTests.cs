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

    // ── YahooSymbol ──
    [Theory]
    [InlineData(AssetClass.JpStock, "7203", "7203.T")]   // 日本株は .T 付与
    [InlineData(AssetClass.JpStock, "7203.T", "7203.T")] // 既に "." 付きはそのまま
    [InlineData(AssetClass.JpStock, " 7203 ", "7203.T")] // 前後空白は除去してから付与
    [InlineData(AssetClass.JpStock, "", "")]             // 空は空（誤って ".T" を付けない）
    [InlineData(AssetClass.UsStock, "AAPL", "AAPL")]     // 米国株はそのまま
    public void YahooSymbol_AppendsDotTOnlyForJpStock(AssetClass cls, string symbol, string expected)
    {
        var h = new Holding { Class = cls, Symbol = symbol };
        Assert.Equal(expected, PortfolioMath.YahooSymbol(h));
    }

    // ── CostBasisJpyAsOf ──
    private static BuyLot BuyOn(string date, decimal qty, decimal price, decimal fx = 0, decimal amount = 0, bool espp = false) =>
        new() { HoldingId = "h", Date = date, Quantity = qty, UnitPrice = price, FxRate = fx, Amount = amount, IsEspp = espp };

    [Fact]
    public void CostBasisJpyAsOf_JpStock_SumsBuysUpToDate()
    {
        var data = new PortfolioData
        {
            Holdings = { new Holding { Id = "h", Class = AssetClass.JpStock, CostCurrency = Currency.Jpy } },
            Buys =
            {
                BuyOn("2026-01-10", 10, 100),   // 1,000
                BuyOn("2026-02-10", 10, 200),   // 2,000
                BuyOn("2026-03-10", 10, 300),   // 期間外（asOf より後）→除外
            },
        };
        // 2/15 時点：1月・2月の買付のみ＝3,000
        Assert.Equal(3000m, PortfolioMath.CostBasisJpyAsOf(data, "2026-02-15", 0m));
    }

    [Fact]
    public void CostBasisJpyAsOf_SellReducesHeldCostProportionally()
    {
        var data = new PortfolioData
        {
            Holdings = { new Holding { Id = "h", Class = AssetClass.JpStock, CostCurrency = Currency.Jpy } },
            Buys = { BuyOn("2026-01-10", 20, 100) },                                  // 取得 20株・原価 2,000
            Sells = { new SellLot { HoldingId = "h", Date = "2026-02-01", Quantity = 5 } },  // 5株売却→残15株
        };
        // 残元本＝2,000 ×(15/20)＝1,500
        Assert.Equal(1500m, PortfolioMath.CostBasisJpyAsOf(data, "2026-02-15", 0m));
    }

    [Fact]
    public void CostBasisJpyAsOf_UsdLot_UsesPerLotFxRate_FallbackWhenUnset()
    {
        var data = new PortfolioData
        {
            UsdJpyRate = 150m,   // fallback（約定レート未設定ロット用）
            Holdings = { new Holding { Id = "h", Class = AssetClass.UsStock, CostCurrency = Currency.Usd } },
            Buys =
            {
                BuyOn("2026-01-10", 10, 50, fx: 140m),   // $500 ×140 = 70,000
                BuyOn("2026-02-10", 10, 50),             // $500 ×fallback150 = 75,000
            },
        };
        Assert.Equal(145_000m, PortfolioMath.CostBasisJpyAsOf(data, "2026-02-15", 0m));
    }

    [Fact]
    public void CostBasisJpyAsOf_SnapRateOverridesDataRateAsFallback()
    {
        var data = new PortfolioData
        {
            UsdJpyRate = 150m,
            Holdings = { new Holding { Id = "h", Class = AssetClass.UsStock, CostCurrency = Currency.Usd } },
            Buys = { BuyOn("2026-01-10", 10, 50) },   // 約定レート未設定→ snapRate を優先
        };
        // snapRate=130 を fallback に使用：$500 ×130 = 65,000
        Assert.Equal(65_000m, PortfolioMath.CostBasisJpyAsOf(data, "2026-02-15", 130m));
    }

    [Fact]
    public void CostBasisJpyAsOf_ReinvestedDividendAddsQtyButNoCost()
    {
        var data = new PortfolioData
        {
            Holdings = { new Holding { Id = "h", Class = AssetClass.JpStock, CostCurrency = Currency.Jpy } },
            Buys = { BuyOn("2026-01-10", 10, 100) },                                       // 原価 1,000・10株
            Dividends = { new Dividend { HoldingId = "h", Date = "2026-02-01", Quantity = 10 } },  // 再投資10株（$0）
        };
        // 取得数量20・原価1,000のまま、現在保有20株 → 1,000 ×(20/20)＝1,000
        Assert.Equal(1000m, PortfolioMath.CostBasisJpyAsOf(data, "2026-02-15", 0m));
    }

    [Fact]
    public void CostBasisJpyAsOf_ExcludesDeletedHoldings()
    {
        var data = new PortfolioData
        {
            Holdings = { new Holding { Id = "h", Class = AssetClass.JpStock, CostCurrency = Currency.Jpy, IsDeleted = true } },
            Buys = { BuyOn("2026-01-10", 10, 100) },
        };
        Assert.Equal(0m, PortfolioMath.CostBasisJpyAsOf(data, "2026-02-15", 0m));
    }

    // ── HoldingCostBasisJpyAsOf（銘柄単位版・グループ小計用。全体版＝各銘柄の総和）──
    [Fact]
    public void HoldingCostBasisJpyAsOf_PerHolding_SumsToAggregate()
    {
        var jp = new Holding { Id = "h", Class = AssetClass.JpStock, CostCurrency = Currency.Jpy };
        var us = new Holding { Id = "u", Class = AssetClass.UsStock, CostCurrency = Currency.Usd };
        var data = new PortfolioData
        {
            UsdJpyRate = 150m,
            Holdings = { jp, us },
            Buys =
            {
                new BuyLot { HoldingId = "h", Date = "2026-01-10", Quantity = 10, UnitPrice = 100 },          // 1,000 円
                new BuyLot { HoldingId = "u", Date = "2026-01-10", Quantity = 10, UnitPrice = 50, FxRate = 140 }, // $500 ×140 = 70,000 円
            },
        };
        // 銘柄単位＝各々の取得原価（円）
        Assert.Equal(1_000m, PortfolioMath.HoldingCostBasisJpyAsOf(data, jp, "2026-02-15", 0m));
        Assert.Equal(70_000m, PortfolioMath.HoldingCostBasisJpyAsOf(data, us, "2026-02-15", 0m));
        // 全体版は銘柄単位の総和に一致する
        Assert.Equal(
            PortfolioMath.HoldingCostBasisJpyAsOf(data, jp, "2026-02-15", 0m) + PortfolioMath.HoldingCostBasisJpyAsOf(data, us, "2026-02-15", 0m),
            PortfolioMath.CostBasisJpyAsOf(data, "2026-02-15", 0m));
    }

    // ── PnlPct ──
    [Theory]
    [InlineData(100, 1000, " (+10.0%)")]
    [InlineData(-100, 1000, " (-10.0%)")]
    [InlineData(0, 1000, " (+0.0%)")]
    [InlineData(1, 3, " (+33.3%)")]
    public void PnlPct_FormatsSignedPercent(decimal upnl, decimal cost, string expected) =>
        Assert.Equal(expected, PortfolioMath.PnlPct(upnl, cost));

    [Fact]
    public void PnlPct_NullUpnl_ReturnsEmpty() => Assert.Equal("", PortfolioMath.PnlPct(null, 1000));

    [Fact]
    public void PnlPct_ZeroCost_ReturnsEmpty() => Assert.Equal("", PortfolioMath.PnlPct(100, 0));

    // ── DayChangePct ──
    [Theory]
    [InlineData(110, 100, 10.0)]
    [InlineData(90, 100, -10.0)]
    [InlineData(100, 100, 0.0)]
    public void DayChangePct_ReturnsCorrectPercent(decimal cur, decimal prev, double expected) =>
        Assert.Equal((decimal)expected, PortfolioMath.DayChangePct(cur, prev)!.Value, precision: 1);

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(0, 0)]
    public void DayChangePct_ZeroOrNegativePrice_ReturnsNull(decimal cur, decimal prev) =>
        Assert.Null(PortfolioMath.DayChangePct(cur, prev));

    // ── GroupValuationJpy ──
    [Fact]
    public void GroupValuationJpy_SumsOnlyMatchingClass()
    {
        var jp = new Holding { Id = "j", Class = AssetClass.JpStock, CostCurrency = Currency.Jpy };
        var us = new Holding { Id = "u", Class = AssetClass.UsStock, CostCurrency = Currency.Usd };
        var data = new PortfolioData
        {
            UsdJpyRate = 150m,
            Holdings = { jp, us },
            Buys =
            {
                new BuyLot { HoldingId = "j", Quantity = 10 },   // qty=10
                new BuyLot { HoldingId = "u", Quantity = 5 },    // qty=5
            },
            CurrentPrices = { ["j"] = 200m, ["u"] = 100m },
        };
        // 日本株：10株 × ¥200 = ¥2,000
        Assert.Equal(2_000m, PortfolioMath.GroupValuationJpy(data, AssetClass.JpStock));
        // 米国株：5株 × $100 × 150 = ¥75,000
        Assert.Equal(75_000m, PortfolioMath.GroupValuationJpy(data, AssetClass.UsStock));
        // 投信は保有なし → null
        Assert.Null(PortfolioMath.GroupValuationJpy(data, AssetClass.Fund));
    }

    [Fact]
    public void GroupValuationJpy_ExcludesDeletedHoldings()
    {
        var h = new Holding { Id = "h", Class = AssetClass.JpStock, CostCurrency = Currency.Jpy, IsDeleted = true };
        var data = new PortfolioData
        {
            Holdings = { h },
            Buys = { new BuyLot { HoldingId = "h", Quantity = 10 } },
            CurrentPrices = { ["h"] = 200m },
        };
        Assert.Null(PortfolioMath.GroupValuationJpy(data, AssetClass.JpStock));
    }

    [Fact]
    public void GroupValuationJpy_NoPriceIsNull()
    {
        var h = new Holding { Id = "h", Class = AssetClass.JpStock, CostCurrency = Currency.Jpy };
        var data = new PortfolioData
        {
            Holdings = { h },
            Buys = { new BuyLot { HoldingId = "h", Quantity = 10 } },
            // CurrentPrices に登録なし → 価格未取得 → null
        };
        Assert.Null(PortfolioMath.GroupValuationJpy(data, AssetClass.JpStock));
    }

    // ── BuildSnapshot ──
    [Fact]
    public void BuildSnapshot_NoHoldings_ReturnsNull()
    {
        var data = new PortfolioData();
        Assert.Null(PortfolioMath.BuildSnapshot(data, "2026-01-01 10:00"));
    }

    [Fact]
    public void BuildSnapshot_NoPrices_ReturnsNull()
    {
        var data = new PortfolioData
        {
            Holdings = { new Holding { Id = "h", Class = AssetClass.JpStock, CostCurrency = Currency.Jpy } },
            Buys = { new BuyLot { HoldingId = "h", Quantity = 10, UnitPrice = 100 } },
            // CurrentPrices 未設定 → ValuationJpy が null → スナップショット不成立
        };
        Assert.Null(PortfolioMath.BuildSnapshot(data, "2026-01-01 10:00"));
    }

    [Fact]
    public void BuildSnapshot_ExcludesDeletedHoldings()
    {
        var data = new PortfolioData
        {
            Holdings = { new Holding { Id = "h", Class = AssetClass.JpStock, CostCurrency = Currency.Jpy, IsDeleted = true } },
            Buys = { new BuyLot { HoldingId = "h", Quantity = 10, UnitPrice = 100 } },
            CurrentPrices = { ["h"] = 200m },
        };
        Assert.Null(PortfolioMath.BuildSnapshot(data, "2026-01-01 10:00"));
    }

    [Fact]
    public void BuildSnapshot_ExcludesZeroQtyHoldings()
    {
        var data = new PortfolioData
        {
            Holdings = { new Holding { Id = "h", Class = AssetClass.JpStock, CostCurrency = Currency.Jpy } },
            // 買付なし → qty=0 → 除外
            CurrentPrices = { ["h"] = 200m },
        };
        Assert.Null(PortfolioMath.BuildSnapshot(data, "2026-01-01 10:00"));
    }

    [Fact]
    public void BuildSnapshot_BuildsValues_JpStock()
    {
        var data = new PortfolioData
        {
            Holdings = { new Holding { Id = "h", Class = AssetClass.JpStock, CostCurrency = Currency.Jpy } },
            Buys = { new BuyLot { HoldingId = "h", Quantity = 10, UnitPrice = 100 } },
            CurrentPrices = { ["h"] = 200m },
        };
        var snap = PortfolioMath.BuildSnapshot(data, "2026-01-15 09:30");

        Assert.NotNull(snap);
        Assert.Equal("2026-01-15 09:30", snap!.At);
        Assert.Single(snap.Values);
        Assert.Equal("h", snap.Values[0].HoldingId);
        Assert.Equal(200m, snap.Values[0].PriceNative);
        Assert.Equal(2_000m, snap.Values[0].ValuationJpy);  // 10株 × ¥200
    }

    [Fact]
    public void BuildSnapshot_BuildsValues_UsStock_ConvertsToJpy()
    {
        var data = new PortfolioData
        {
            UsdJpyRate = 150m,
            Holdings = { new Holding { Id = "u", Class = AssetClass.UsStock, CostCurrency = Currency.Usd } },
            Buys = { new BuyLot { HoldingId = "u", Quantity = 5, UnitPrice = 100 } },
            CurrentPrices = { ["u"] = 100m },
        };
        var snap = PortfolioMath.BuildSnapshot(data, "2026-01-15 09:30");

        Assert.NotNull(snap);
        Assert.Equal(150m, snap!.UsdJpyRate);
        Assert.Single(snap.Values);
        Assert.Equal(100m, snap.Values[0].PriceNative);
        Assert.Equal(75_000m, snap.Values[0].ValuationJpy);  // 5株 × $100 × 150
    }

    [Fact]
    public void BuildSnapshot_MultipleHoldings_AllPresent()
    {
        var jp = new Holding { Id = "j", Class = AssetClass.JpStock, CostCurrency = Currency.Jpy };
        var us = new Holding { Id = "u", Class = AssetClass.UsStock, CostCurrency = Currency.Usd };
        var data = new PortfolioData
        {
            UsdJpyRate = 150m,
            Holdings = { jp, us },
            Buys =
            {
                new BuyLot { HoldingId = "j", Quantity = 10, UnitPrice = 100 },
                new BuyLot { HoldingId = "u", Quantity = 5, UnitPrice = 100 },
            },
            CurrentPrices = { ["j"] = 200m, ["u"] = 100m },
        };
        var snap = PortfolioMath.BuildSnapshot(data, "2026-01-15 09:30");

        Assert.NotNull(snap);
        Assert.Equal(2, snap!.Values.Count);
        Assert.Equal(2_000m, snap.Values.Single(v => v.HoldingId == "j").ValuationJpy);
        Assert.Equal(75_000m, snap.Values.Single(v => v.HoldingId == "u").ValuationJpy);
    }

    // ── UpsertSnapshot ──
    private static PriceSnapshot Snap(string at) => new() { At = at };

    [Fact]
    public void UpsertSnapshot_NewDay_Appends()
    {
        var data = new PortfolioData { Snapshots = { Snap("2026-01-14 10:00") } };
        PortfolioMath.UpsertSnapshot(data, Snap("2026-01-15 09:30"));

        Assert.Equal(2, data.Snapshots.Count);
        Assert.Equal("2026-01-15 09:30", data.Snapshots[1].At);
    }

    [Fact]
    public void UpsertSnapshot_SameDay_Overwrites()
    {
        var data = new PortfolioData { Snapshots = { Snap("2026-01-14 10:00"), Snap("2026-01-15 09:30") } };
        var latest = Snap("2026-01-15 15:00");
        PortfolioMath.UpsertSnapshot(data, latest);

        Assert.Equal(2, data.Snapshots.Count);   // 同日は1点に保たれる
        Assert.Same(latest, data.Snapshots[1]);
    }

    [Fact]
    public void UpsertSnapshot_KeepsAscendingOrder_WhenBackfillingEarlierDay()
    {
        var data = new PortfolioData { Snapshots = { Snap("2026-01-16 10:00") } };
        PortfolioMath.UpsertSnapshot(data, Snap("2026-01-15 09:30"));

        Assert.Equal(new[] { "2026-01-15 09:30", "2026-01-16 10:00" },
            data.Snapshots.Select(s => s.At).ToArray());
    }
}
