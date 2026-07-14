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
    public void SelectPeriodYms_Current_ReturnsCurrentCycleMonthOnly()
    {
        // 「当月」（#88）＝ currentYm と一致する月のみ。
        var r = StatsMath.SelectPeriodYms(Yms, "current", "", "", "2026-05");
        Assert.Equal(new[] { "2026-05" }, r);
    }

    [Fact]
    public void SelectPeriodYms_Current_IgnoresFutureMonthAlreadyCreated()
    {
        // 未来月（2026-06）を先行作成済みでも、給料サイクル起点(currentYm=2026-05)を
        // ピンポイントで返す（TakeLast(1) だと最新作成月を誤って拾ってしまうため）。
        var r = StatsMath.SelectPeriodYms(Yms, "current", "", "", "2026-05");
        Assert.Equal(new[] { "2026-05" }, r);
        Assert.DoesNotContain("2026-06", r);
    }

    [Fact]
    public void SelectPeriodYms_Current_NotYetCreated_ReturnsEmpty()
    {
        // 当月サイクルのレコードがまだ無ければ空（該当なし）。
        var r = StatsMath.SelectPeriodYms(Yms, "current", "", "", "2026-07");
        Assert.Empty(r);
    }

    [Fact]
    public void SelectPeriodYms_NumericLargerThanCount_ClampsToAll()
    {
        // 直近12ヶ月を要求しても6件しか無ければ6件（TakeLast がクランプ）
        var r = StatsMath.SelectPeriodYms(Yms, "12", "", "");
        Assert.Equal(Yms, r);
    }

    [Fact]
    public void SelectPeriodYms_Numeric_ExcludesFutureMonthsBeyondCurrentYm()
    {
        // 固定費先行展開等で未来月(2026-06)が既に存在していても、
        // 直近3ヶ月は当月サイクル(currentYm=2026-05)までに限定する(#119)。
        var r = StatsMath.SelectPeriodYms(Yms, "3", "", "", "2026-05");
        Assert.Equal(new[] { "2026-03", "2026-04", "2026-05" }, r);
        Assert.DoesNotContain("2026-06", r);
    }

    [Fact]
    public void SelectPeriodYms_Numeric_CurrentYmNull_KeepsIncludingLatestMonth()
    {
        // currentYm 未指定時は従来どおり全件を対象に TakeLast する
        var r = StatsMath.SelectPeriodYms(Yms, "3", "", "", null);
        Assert.Equal(new[] { "2026-04", "2026-05", "2026-06" }, r);
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

    // NormalizeCategoryKey（#117）：参照切れ・未設定の CategoryId を "" に統一し、
    // 「未分類」が複数行に分裂しないようにする。
    private static readonly string[] KnownCategoryIds = { "cat-1", "cat-2" };

    [Fact]
    public void NormalizeCategoryKey_KnownId_ReturnsSameId()
    {
        var r = StatsMath.NormalizeCategoryKey("cat-1", KnownCategoryIds);
        Assert.Equal("cat-1", r);
    }

    [Fact]
    public void NormalizeCategoryKey_EmptyId_ReturnsEmptyKey()
    {
        var r = StatsMath.NormalizeCategoryKey("", KnownCategoryIds);
        Assert.Equal("", r);
    }

    [Fact]
    public void NormalizeCategoryKey_NullId_ReturnsEmptyKey()
    {
        var r = StatsMath.NormalizeCategoryKey(null, KnownCategoryIds);
        Assert.Equal("", r);
    }

    [Fact]
    public void NormalizeCategoryKey_DanglingReference_ReturnsEmptyKey()
    {
        // 削除済み等でカテゴリが存在しない CategoryId（参照切れ）も "" に正規化される
        var r = StatsMath.NormalizeCategoryKey("deleted-cat-id", KnownCategoryIds);
        Assert.Equal("", r);
    }

    [Fact]
    public void NormalizeCategoryKey_EmptyAndDanglingReference_CollapseToSameKey()
    {
        // 未設定（空）と参照切れの両方が同じキーに集約されることを確認
        var empty = StatsMath.NormalizeCategoryKey("", KnownCategoryIds);
        var dangling = StatsMath.NormalizeCategoryKey("deleted-cat-id", KnownCategoryIds);
        Assert.Equal(empty, dangling);
    }
}
