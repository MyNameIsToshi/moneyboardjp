using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class AnnouncementMathTests
{
    // ── IsUnread ──
    [Theory]
    [InlineData(2, 1, true)]
    [InlineData(1, 1, false)]
    [InlineData(1, 2, false)]
    public void IsUnread_GreaterThanLastSeen(int id, int lastSeenId, bool expected) =>
        Assert.Equal(expected, AnnouncementMath.IsUnread(id, lastSeenId));

    // ── CountUnread ──
    [Fact]
    public void CountUnread_LastSeenZero_AllUnread()
    {
        Assert.Equal(3, AnnouncementMath.CountUnread(new[] { 1, 2, 3 }, 0));
    }

    [Fact]
    public void CountUnread_LastSeenLatest_None()
    {
        Assert.Equal(0, AnnouncementMath.CountUnread(new[] { 1, 2, 3 }, 3));
    }

    [Fact]
    public void CountUnread_Partial()
    {
        Assert.Equal(1, AnnouncementMath.CountUnread(new[] { 1, 2, 3 }, 2));
    }

    [Fact]
    public void CountUnread_Empty_Zero()
    {
        Assert.Equal(0, AnnouncementMath.CountUnread(Array.Empty<int>(), 0));
    }

    [Fact]
    public void CountUnread_LastSeenBeyondLatest_Zero()
    {
        // 端末をまたいで既読idの方が大きい（他端末で先に見た等）ケースでも負にならない
        Assert.Equal(0, AnnouncementMath.CountUnread(new[] { 1, 2 }, 99));
    }

    // ── LatestId ──
    [Fact]
    public void LatestId_ReturnsMax()
    {
        Assert.Equal(5, AnnouncementMath.LatestId(new[] { 1, 5, 3 }));
    }

    [Fact]
    public void LatestId_Empty_Null()
    {
        Assert.Null(AnnouncementMath.LatestId(Array.Empty<int>()));
    }

    // ── ParseChangeItems ──
    [Fact]
    public void ParseChangeItems_ExtractsLabelAndText()
    {
        var items = AnnouncementMath.ParseChangeItems("- 【新機能】機能Aを追加しました\n- 【修正】不具合Bを修正しました");

        Assert.Equal(2, items.Count);
        Assert.Equal(("新機能", "機能Aを追加しました"), (items[0].TypeLabel, items[0].Text));
        Assert.Equal(("修正", "不具合Bを修正しました"), (items[1].TypeLabel, items[1].Text));
    }

    [Fact]
    public void ParseChangeItems_LineWithoutLabel_EmptyTypeLabel()
    {
        var items = AnnouncementMath.ParseChangeItems("- ラベル無しの行です");

        Assert.Single(items);
        Assert.Equal("", items[0].TypeLabel);
        Assert.Equal("ラベル無しの行です", items[0].Text);
    }

    [Fact]
    public void ParseChangeItems_SkipsBlankLines()
    {
        var items = AnnouncementMath.ParseChangeItems("- 【改善】改善しました\n\n- 【改善】もう1件");

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void ParseChangeItems_EmptyBody_Empty()
    {
        Assert.Empty(AnnouncementMath.ParseChangeItems(""));
    }

    [Fact]
    public void ParseChangeItems_UnclosedBracket_TreatedAsPlainText()
    {
        var items = AnnouncementMath.ParseChangeItems("- 【閉じていない行です");

        Assert.Single(items);
        Assert.Equal("", items[0].TypeLabel);
        Assert.Equal("【閉じていない行です", items[0].Text);
    }
}
