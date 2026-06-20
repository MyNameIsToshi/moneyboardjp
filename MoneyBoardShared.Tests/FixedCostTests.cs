using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class FixedCostTests
{
    [Fact]
    public void Bounds_Null_WhenUnset()
    {
        var fc = new FixedCost { StartYm = null, EndYm = null };
        Assert.Null(fc.StartBound());
        Assert.Null(fc.EndBound());
    }

    [Fact]
    public void Bounds_YearMonth_ParsedExactly()
    {
        var fc = new FixedCost { StartYm = "202604", EndYm = "202609" };
        Assert.Equal(new Ym(2026, 4), fc.StartBound());
        Assert.Equal(new Ym(2026, 9), fc.EndBound());
    }

    [Fact]
    public void Bounds_YearOnly_StartIsJanuary_EndIsDecember()
    {
        var fc = new FixedCost { StartYm = "2026", EndYm = "2026" };
        Assert.Equal(new Ym(2026, 1), fc.StartBound());    // 年のみ開始＝1月
        Assert.Equal(new Ym(2026, 12), fc.EndBound());     // 年のみ終了＝12月
    }

    [Theory]
    [InlineData("")]
    [InlineData("20")]    // 4桁未満
    public void Bounds_Null_WhenTooShort(string value)
    {
        var fc = new FixedCost { StartYm = value, EndYm = value };
        Assert.Null(fc.StartBound());
        Assert.Null(fc.EndBound());
    }
}
