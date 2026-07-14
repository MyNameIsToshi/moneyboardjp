using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class AppVersionMathTests
{
    // ── FormatDisplayVersion ──
    [Fact]
    public void FormatDisplayVersion_PrependsV()
    {
        Assert.Equal("v2.15.0", AppVersionMath.FormatDisplayVersion("2.15.0"));
    }

    [Fact]
    public void FormatDisplayVersion_StripsSourceLinkBuildMeta()
    {
        Assert.Equal("v2.15.0", AppVersionMath.FormatDisplayVersion("2.15.0+abcdef123456"));
    }

    [Fact]
    public void FormatDisplayVersion_NullOrEmpty_EmptyString()
    {
        Assert.Equal("", AppVersionMath.FormatDisplayVersion(null));
        Assert.Equal("", AppVersionMath.FormatDisplayVersion(""));
    }

    // ── ShouldNotifyUpdate ──
    [Fact]
    public void ShouldNotifyUpdate_FirstLaunch_NoLastSeen_False()
    {
        Assert.False(AppVersionMath.ShouldNotifyUpdate(null, "v2.15.0"));
        Assert.False(AppVersionMath.ShouldNotifyUpdate("", "v2.15.0"));
    }

    [Fact]
    public void ShouldNotifyUpdate_SameVersion_False()
    {
        Assert.False(AppVersionMath.ShouldNotifyUpdate("v2.15.0", "v2.15.0"));
    }

    [Fact]
    public void ShouldNotifyUpdate_DifferentVersion_True()
    {
        Assert.True(AppVersionMath.ShouldNotifyUpdate("v2.14.0", "v2.15.0"));
    }

    // ── IsWithinManualReloadWindow ──
    [Fact]
    public void IsWithinManualReloadWindow_JustClicked_True()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(AppVersionMath.IsWithinManualReloadWindow(now.AddSeconds(-1), now, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void IsWithinManualReloadWindow_ExactlyAtWindowBoundary_False()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(AppVersionMath.IsWithinManualReloadWindow(now.AddSeconds(-30), now, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void IsWithinManualReloadWindow_StaleFlag_False()
    {
        // reloadが完了しないままタブを閉じた等でフラグが残り続けたケース。
        // 無関係な将来の更新まで誤って抑止しないことを担保する。
        var now = DateTimeOffset.UtcNow;
        Assert.False(AppVersionMath.IsWithinManualReloadWindow(now.AddDays(-1), now, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void IsWithinManualReloadWindow_FutureSetAt_False()
    {
        // 端末の時計ズレ等でsetAtが未来になっているケースでも誤って抑止しない。
        var now = DateTimeOffset.UtcNow;
        Assert.False(AppVersionMath.IsWithinManualReloadWindow(now.AddSeconds(5), now, TimeSpan.FromSeconds(30)));
    }
}
