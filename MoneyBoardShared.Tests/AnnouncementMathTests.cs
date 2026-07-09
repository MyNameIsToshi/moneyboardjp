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
}
