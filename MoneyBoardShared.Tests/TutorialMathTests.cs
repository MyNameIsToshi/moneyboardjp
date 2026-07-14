using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class TutorialMathTests
{
    [Fact]
    public void ShouldForceShow_UnseenVersion_True()
    {
        Assert.True(TutorialMath.ShouldForceShow(0));
    }

    [Fact]
    public void ShouldForceShow_CurrentVersion_False()
    {
        Assert.False(TutorialMath.ShouldForceShow(TutorialMath.CurrentVersion));
    }

    [Fact]
    public void ShouldForceShow_FutureVersion_False()
    {
        // 将来チュートリアルが刷新され CurrentVersion が上がった端末を戻す運用は想定しないが、
        // 既読版が現行版以上なら強制表示しないという比較規則自体は崩れない。
        Assert.False(TutorialMath.ShouldForceShow(TutorialMath.CurrentVersion + 1));
    }
}
