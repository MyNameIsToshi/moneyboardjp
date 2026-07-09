using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class FixedCostPeriodTests
{
    // ── YearPart ──
    [Theory]
    [InlineData("202604", "2026")]   // 年月から年
    [InlineData("2026", "2026")]     // 年のみ
    [InlineData("20", "")]           // 4文字未満
    [InlineData("", "")]             // 空
    [InlineData(null, "")]           // null
    public void YearPart_TakesFirst4OrEmpty(string? ym, string expected) =>
        Assert.Equal(expected, FixedCostPeriod.YearPart(ym));

    // ── MonthPart ──
    [Theory]
    [InlineData("202604", "4")]      // 6文字＝月あり（先頭0を落とす）
    [InlineData("202612", "12")]
    [InlineData("2026", "")]         // 年のみ＝月なし
    [InlineData("", "")]
    [InlineData(null, "")]
    public void MonthPart_OnlyWhenSixChars(string? ym, string expected) =>
        Assert.Equal(expected, FixedCostPeriod.MonthPart(ym));

    // ── ComposeYm ──
    [Theory]
    [InlineData("2026", "4", "202604")]    // 年＋月→ゼロ埋め6文字
    [InlineData("2026", "12", "202612")]
    [InlineData("2026", "", "2026")]       // 月未選択→年のみ
    [InlineData("2026", null, "2026")]
    public void ComposeYm_BuildsYearMonthOrYearOnly(string? year, string? month, string expected) =>
        Assert.Equal(expected, FixedCostPeriod.ComposeYm(year, month));

    [Theory]
    [InlineData("", "4")]      // 年が空→null（月があっても）
    [InlineData(null, "4")]
    public void ComposeYm_NullWhenYearEmpty(string? year, string? month) =>
        Assert.Null(FixedCostPeriod.ComposeYm(year, month));

    // ── FmtBound ──
    [Theory]
    [InlineData("202604", "2026年4月")]
    [InlineData("2026", "2026年")]
    public void FmtBound_FormatsYearMonthOrYear(string ym, string expected) =>
        Assert.Equal(expected, FixedCostPeriod.FmtBound(ym));

    // ── IsExpired ──
    [Fact]
    public void IsExpired_EndBeforeAsOf_True()
    {
        var fc = new FixedCost { EndYm = "202603" };
        Assert.True(FixedCostPeriod.IsExpired(fc, new Ym(2026, 4)));
    }

    [Fact]
    public void IsExpired_EndEqualsAsOf_False()
    {
        var fc = new FixedCost { EndYm = "202604" };
        Assert.False(FixedCostPeriod.IsExpired(fc, new Ym(2026, 4)));
    }

    [Fact]
    public void IsExpired_EndAfterAsOf_False()
    {
        var fc = new FixedCost { EndYm = "202605" };
        Assert.False(FixedCostPeriod.IsExpired(fc, new Ym(2026, 4)));
    }

    [Fact]
    public void IsExpired_NoEnd_NeverExpires()
    {
        var fc = new FixedCost { EndYm = null };
        Assert.False(FixedCostPeriod.IsExpired(fc, new Ym(9999, 12)));
    }

    [Fact]
    public void IsExpired_YearOnlyEnd_TreatsAsDecember()
    {
        var fc = new FixedCost { EndYm = "2026" };
        Assert.False(FixedCostPeriod.IsExpired(fc, new Ym(2026, 12)));
        Assert.True(FixedCostPeriod.IsExpired(fc, new Ym(2027, 1)));
    }

    // ── Summary ──
    [Fact]
    public void Summary_OpenStart_AndUnlimitedEnd_NoBonus()
    {
        var fc = new FixedCost { StartYm = null, EndYm = null };
        Assert.Equal("開始なし〜無期限・ボーナスなし", FixedCostPeriod.Summary(fc));
    }

    [Fact]
    public void Summary_BoundedRange_WithBonusCount()
    {
        var fc = new FixedCost { StartYm = "202604", EndYm = "2027" };
        fc.BonusSettings.Add(new BonusSetting { Month = 6 });
        Assert.Equal("2026年4月〜2027年・ボーナス1件", FixedCostPeriod.Summary(fc));
    }

    // ── IsExpired(FixedIncome)・#95フォローアップ ──
    [Fact]
    public void IsExpired_FixedIncome_EndBeforeAsOf_True()
    {
        var fi = new FixedIncome { EndYm = "202603" };
        Assert.True(FixedCostPeriod.IsExpired(fi, new Ym(2026, 4)));
    }

    [Fact]
    public void IsExpired_FixedIncome_EndEqualsAsOf_False()
    {
        var fi = new FixedIncome { EndYm = "202604" };
        Assert.False(FixedCostPeriod.IsExpired(fi, new Ym(2026, 4)));
    }

    [Fact]
    public void IsExpired_FixedIncome_NoEnd_NeverExpires()
    {
        var fi = new FixedIncome { EndYm = null };
        Assert.False(FixedCostPeriod.IsExpired(fi, new Ym(9999, 12)));
    }

    // ── Summary(FixedIncome)・#95（ボーナス設定は対象外のため期間のみ） ──
    [Fact]
    public void Summary_FixedIncome_OpenStart_AndUnlimitedEnd()
    {
        var fi = new FixedIncome { StartYm = null, EndYm = null };
        Assert.Equal("開始なし〜無期限", FixedCostPeriod.Summary(fi));
    }

    [Fact]
    public void Summary_FixedIncome_BoundedRange()
    {
        var fi = new FixedIncome { StartYm = "202604", EndYm = "2027" };
        Assert.Equal("2026年4月〜2027年", FixedCostPeriod.Summary(fi));
    }
}
