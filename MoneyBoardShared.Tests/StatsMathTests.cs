using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class StatsMathTests
{
    // 昇順 6ヶ月分（zero-pad 済み＝文字列比較で時系列順）
    private static readonly string[] Yms =
        { "2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06" };

    [Fact]
    public void SelectPeriodYms_All_ReturnsEverything()
    {
        var r = StatsMath.SelectPeriodYms(Yms, "all", "", "");
        Assert.Equal(Yms, r);
    }

    [Fact]
    public void SelectPeriodYms_Numeric_ReturnsLastN()
    {
        var r = StatsMath.SelectPeriodYms(Yms, "3", "", "");
        Assert.Equal(new[] { "2026-04", "2026-05", "2026-06" }, r);
    }

    [Fact]
    public void SelectPeriodYms_NumericLargerThanCount_ClampsToAll()
    {
        // 直近12ヶ月を要求しても6件しか無ければ6件（TakeLast がクランプ）
        var r = StatsMath.SelectPeriodYms(Yms, "12", "", "");
        Assert.Equal(Yms, r);
    }

    [Fact]
    public void SelectPeriodYms_Custom_InclusiveRange()
    {
        var r = StatsMath.SelectPeriodYms(Yms, "custom", "2026-02", "2026-04");
        Assert.Equal(new[] { "2026-02", "2026-03", "2026-04" }, r);
    }

    [Fact]
    public void SelectPeriodYms_Custom_ReversedRangeIsSwapped()
    {
        // 開始＞終了でも入れ替えて同じ範囲を返す
        var r = StatsMath.SelectPeriodYms(Yms, "custom", "2026-04", "2026-02");
        Assert.Equal(new[] { "2026-02", "2026-03", "2026-04" }, r);
    }

    [Fact]
    public void SelectPeriodYms_Custom_SingleMonth()
    {
        var r = StatsMath.SelectPeriodYms(Yms, "custom", "2026-03", "2026-03");
        Assert.Equal(new[] { "2026-03" }, r);
    }

    [Fact]
    public void SelectPeriodYms_Custom_OutOfRange_ReturnsEmpty()
    {
        var r = StatsMath.SelectPeriodYms(Yms, "custom", "2027-01", "2027-12");
        Assert.Empty(r);
    }

    [Fact]
    public void SelectPeriodYms_EmptyInput_ReturnsEmpty()
    {
        var r = StatsMath.SelectPeriodYms(System.Array.Empty<string>(), "3", "", "");
        Assert.Empty(r);
    }
}
