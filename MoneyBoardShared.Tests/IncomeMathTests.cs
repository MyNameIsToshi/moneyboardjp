using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class IncomeMathTests
{
    // デザインモック（income-spec.md）の数値を再現する回帰テスト：
    // 1〜5月 給料総支給 430,000/430,000/432,000/430,000/436,000・6月（ボーナス月）給料430,000+賞与780,000。
    // 7〜12月は未入力（0）。ボーナス月は設定どおり年2回（6月・12月）。
    private static List<IncomeMath.MonthIncome> MockYear() => new()
    {
        new(1, 430_000, 341_000, false, 0, 0),
        new(2, 430_000, 341_000, false, 0, 0),
        new(3, 432_000, 341_000, false, 0, 0),
        new(4, 430_000, 341_000, false, 0, 0),
        new(5, 436_000, 342_000, false, 0, 0),
        new(6, 430_000, 341_000, true, 780_000, 600_000),
        new(7, 0, 0, false, 0, 0),
        new(8, 0, 0, false, 0, 0),
        new(9, 0, 0, false, 0, 0),
        new(10, 0, 0, false, 0, 0),
        new(11, 0, 0, false, 0, 0),
        new(12, 0, 0, true, 0, 0),
    };

    [Fact]
    public void Summarize_MockYear_MatchesDesignFigures()
    {
        var result = IncomeMath.Summarize(MockYear(), bonusMonthsPerYear: 2);

        Assert.Equal(6, result.FilledSalaryMonths);
        Assert.Equal(431_333m, result.MonthlyAvgGross);
        Assert.Equal(5_176_000m, result.SalaryEstimate);
        Assert.Equal(1_560_000m, result.BonusEstimate);
        Assert.Equal(6_736_000m, result.ProjectedGross);
        Assert.Equal(3_368_000m, result.ActualGrossYtd);
        Assert.Equal(1_442_000m, result.DeductionGap);
        Assert.Equal(21, result.DeductionPct);
    }

    [Fact]
    public void Summarize_NoInput_AllZero()
    {
        var months = Enumerable.Range(1, 12).Select(m => new IncomeMath.MonthIncome(m, 0, 0, m is 6 or 12, 0, 0)).ToList();

        var result = IncomeMath.Summarize(months, bonusMonthsPerYear: 2);

        Assert.Equal(0, result.FilledSalaryMonths);
        Assert.Equal(0m, result.ProjectedGross);
        Assert.Equal(0m, result.ActualGrossYtd);
        Assert.Equal(0, result.DeductionPct);
    }

    [Fact]
    public void Summarize_NoBonusMonthsFilled_BonusEstimateZero()
    {
        var months = new List<IncomeMath.MonthIncome> { new(1, 300_000, 250_000, true, 0, 0) };

        var result = IncomeMath.Summarize(months, bonusMonthsPerYear: 2);

        Assert.Equal(0m, result.BonusEstimate);
        Assert.Equal(3_600_000m, result.SalaryEstimate);
        Assert.Equal(3_600_000m, result.ProjectedGross);
    }

    [Fact]
    public void Summarize_SingleMonth_ActualYtdOnlyThatMonth()
    {
        var months = new List<IncomeMath.MonthIncome> { new(4, 500_000, 400_000, false, 0, 0) };

        var result = IncomeMath.Summarize(months, bonusMonthsPerYear: 2);

        Assert.Equal(500_000m, result.ActualGrossYtd);
        Assert.Equal(1, result.FilledSalaryMonths);
        Assert.Equal(500_000m, result.MonthlyAvgGross);
    }

    [Fact]
    public void BuildMonthCheck_TypicalMonth_ComputesTotalsAndMomDiff()
    {
        var records = new Dictionary<string, IncomeMonthRecord>
        {
            ["202605"] = new() { SalaryGross = 430_000 },
            ["202606"] = new() { SalaryGross = 450_000, BonusGross = 780_000 },
        };

        var result = IncomeMath.BuildMonthCheck(records, "202606", isBonusMonth: true, netSalary: 350_000, netBonus: 600_000);

        Assert.Equal(1_230_000m, result.TotalGross);
        Assert.Equal(950_000m, result.TotalNet);
        Assert.Equal(280_000m, result.Deduction);
        Assert.Equal(23, result.DeductionPct);
        Assert.Equal(20_000m, result.SalaryMomDiff);
        Assert.Equal(4.7m, result.SalaryMomPct);
    }

    [Fact]
    public void BuildMonthCheck_NoPrevMonthData_MomHiddenNull()
    {
        var records = new Dictionary<string, IncomeMonthRecord> { ["202606"] = new() { SalaryGross = 430_000 } };

        var result = IncomeMath.BuildMonthCheck(records, "202606", isBonusMonth: false, netSalary: 340_000, netBonus: 0);

        Assert.Null(result.SalaryMomDiff);
        Assert.Null(result.SalaryMomPct);
    }

    [Fact]
    public void BuildMonthCheck_CrossesYearBoundary_ComparesToDecemberOfPreviousYear()
    {
        var records = new Dictionary<string, IncomeMonthRecord>
        {
            ["202512"] = new() { SalaryGross = 400_000 },
            ["202601"] = new() { SalaryGross = 420_000 },
        };

        var result = IncomeMath.BuildMonthCheck(records, "202601", isBonusMonth: false, netSalary: 330_000, netBonus: 0);

        Assert.Equal(20_000m, result.SalaryMomDiff);
        Assert.Equal(5.0m, result.SalaryMomPct);
    }

    [Fact]
    public void BuildMonthCheck_BonusMonth_FindsMostRecentPriorBonusAcrossGap()
    {
        // 前回賞与（6月・78万）から次のボーナス月（12月）まで比較。間の月は賞与月ではない。
        var records = new Dictionary<string, IncomeMonthRecord>
        {
            ["202606"] = new() { BonusGross = 780_000 },
            ["202612"] = new() { BonusGross = 800_000 },
        };

        var result = IncomeMath.BuildMonthCheck(records, "202612", isBonusMonth: true, netSalary: 0, netBonus: 0);

        Assert.Equal(20_000m, result.BonusVsPrevDiff);
        Assert.Equal(2.6m, result.BonusVsPrevPct);
    }

    [Fact]
    public void BuildMonthCheck_FirstBonusEver_NoReference_BonusCompareHiddenNull()
    {
        var records = new Dictionary<string, IncomeMonthRecord> { ["202606"] = new() { BonusGross = 780_000 } };

        var result = IncomeMath.BuildMonthCheck(records, "202606", isBonusMonth: true, netSalary: 0, netBonus: 0);

        Assert.Null(result.BonusVsPrevDiff);
        Assert.Null(result.BonusVsPrevPct);
    }

    [Fact]
    public void BuildMonthCheck_NotBonusMonth_BonusExcludedFromTotalsAndCompare()
    {
        var records = new Dictionary<string, IncomeMonthRecord> { ["202607"] = new() { SalaryGross = 430_000, BonusGross = 999_999 } };

        var result = IncomeMath.BuildMonthCheck(records, "202607", isBonusMonth: false, netSalary: 340_000, netBonus: 0);

        Assert.Equal(430_000m, result.TotalGross);
        Assert.Null(result.BonusVsPrevDiff);
    }
}
