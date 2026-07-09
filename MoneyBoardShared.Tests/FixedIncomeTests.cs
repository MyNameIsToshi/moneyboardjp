using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class FixedIncomeTests
{
    [Fact]
    public void Bounds_Null_WhenUnset()
    {
        var fi = new FixedIncome { StartYm = null, EndYm = null };
        Assert.Null(fi.StartBound());
        Assert.Null(fi.EndBound());
    }

    [Fact]
    public void Bounds_YearMonth_ParsedExactly()
    {
        var fi = new FixedIncome { StartYm = "202604", EndYm = "202609" };
        Assert.Equal(new Ym(2026, 4), fi.StartBound());
        Assert.Equal(new Ym(2026, 9), fi.EndBound());
    }

    [Fact]
    public void Bounds_YearOnly_StartIsJanuary_EndIsDecember()
    {
        var fi = new FixedIncome { StartYm = "2026", EndYm = "2026" };
        Assert.Equal(new Ym(2026, 1), fi.StartBound());    // 年のみ開始＝1月
        Assert.Equal(new Ym(2026, 12), fi.EndBound());     // 年のみ終了＝12月
    }

    [Theory]
    [InlineData("")]
    [InlineData("20")]    // 4桁未満
    public void Bounds_Null_WhenTooShort(string value)
    {
        var fi = new FixedIncome { StartYm = value, EndYm = value };
        Assert.Null(fi.StartBound());
        Assert.Null(fi.EndBound());
    }
}
