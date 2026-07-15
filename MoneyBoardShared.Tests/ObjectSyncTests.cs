using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class ObjectSyncTests
{
    private class Source
    {
        public int Shared { get; set; }
        public string? OnlyOnSource { get; set; }
    }

    private class Target
    {
        public int Shared { get; set; }
        public string OnlyOnTarget { get; set; } = "untouched";
    }

    [Fact]
    public void CopyMatchingProperties_CopiesOnlySameNamedProperties()
    {
        var source = new Source { Shared = 42, OnlyOnSource = "x" };
        var target = new Target();

        ObjectSync.CopyMatchingProperties(source, target);

        Assert.Equal(42, target.Shared);
        Assert.Equal("untouched", target.OnlyOnTarget);   // ソース側に無い名前は変更されない
    }

    private class NullableSource { public string? Shared { get; set; } }
    private class DefaultedTarget { public string Shared { get; set; } = "default"; }

    [Fact]
    public void CopyMatchingProperties_DoesNotOverwriteTargetWhenSourceValueIsNull()
    {
        var target = new DefaultedTarget();

        ObjectSync.CopyMatchingProperties(new NullableSource { Shared = null }, target);

        // source 側が null のプロパティは target の既定値を温存する
        // （LoadAsync で設定リストが null でも AppState の new() 既定へフォールバックする挙動の担保・#136）。
        Assert.Equal("default", target.Shared);
    }
}
