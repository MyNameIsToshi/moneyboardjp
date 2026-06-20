using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class YmTests
{
    [Fact]
    public void Parse_And_ToString_RoundTrip()
    {
        var ym = Ym.Parse("202606");
        Assert.Equal(2026, ym.Year);
        Assert.Equal(6, ym.Month);
        Assert.Equal("202606", ym.ToString());          // 月は2桁ゼロ埋め
        Assert.Equal("202612", new Ym(2026, 12).ToString());
    }

    [Theory]
    [InlineData("202606", true, 2026, 6)]
    [InlineData("202601", true, 2026, 1)]
    [InlineData("202612", true, 2026, 12)]
    [InlineData("202600", false, 0, 0)]   // 月0は不正
    [InlineData("202613", false, 0, 0)]   // 月13は不正
    [InlineData("20260", false, 0, 0)]    // 桁不足
    [InlineData("2026/06", false, 0, 0)]  // 区切りあり（月側が数値にならない）
    [InlineData("abcd06", false, 0, 0)]   // 年が数値でない
    [InlineData(null, false, 0, 0)]
    public void TryParse_ValidatesLengthAndMonthRange(string? s, bool ok, int year, int month)
    {
        var result = Ym.TryParse(s, out var ym);
        Assert.Equal(ok, result);
        if (ok)
        {
            Assert.Equal(year, ym.Year);
            Assert.Equal(month, ym.Month);
        }
    }

    [Fact]
    public void Prev_And_Next_CrossYearBoundary()
    {
        Assert.Equal(new Ym(2025, 12), new Ym(2026, 1).Prev());
        Assert.Equal(new Ym(2027, 1), new Ym(2026, 12).Next());
        Assert.Equal(new Ym(2026, 5), new Ym(2026, 6).Prev());
        Assert.Equal(new Ym(2026, 7), new Ym(2026, 6).Next());
    }

    [Fact]
    public void Comparison_OrdersByYearThenMonth()
    {
        Assert.True(new Ym(2026, 1) < new Ym(2026, 2));
        Assert.True(new Ym(2025, 12) < new Ym(2026, 1));   // 年が優先
        Assert.True(new Ym(2026, 6) >= new Ym(2026, 6));
        Assert.True(new Ym(2026, 6) <= new Ym(2026, 6));
        Assert.Equal(0, new Ym(2026, 6).CompareTo(new Ym(2026, 6)));
    }

    [Fact]
    public void Label_And_FromDate()
    {
        Assert.Equal("2026年6月", new Ym(2026, 6).Label);
        Assert.Equal(new Ym(2026, 6), Ym.FromDate(new System.DateTime(2026, 6, 20)));
    }
}
