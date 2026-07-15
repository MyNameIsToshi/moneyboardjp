using MoneyBoard.Services;

namespace MoneyBoard.Tests;

public class OverlayRegistryServiceTests
{
    [Fact]
    public void 初期状態は何も開いていない()
    {
        var svc = new OverlayRegistryService();
        Assert.False(svc.IsAnyOpen);
    }

    [Fact]
    public void SetOpen_trueで0件から1件になるとChangedが発火しIsAnyOpenがtrueになる()
    {
        var svc = new OverlayRegistryService();
        var fired = 0;
        svc.Changed += () => fired++;

        svc.SetOpen("A", true);

        Assert.True(svc.IsAnyOpen);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void 既に1件以上開いている状態で別キーをtrueにしてもChangedは発火しない()
    {
        var svc = new OverlayRegistryService();
        svc.SetOpen("A", true);
        var fired = 0;
        svc.Changed += () => fired++;

        svc.SetOpen("B", true);

        Assert.True(svc.IsAnyOpen);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void SetOpen_falseで最後の1件が閉じるとChangedが発火しIsAnyOpenがfalseになる()
    {
        var svc = new OverlayRegistryService();
        svc.SetOpen("A", true);
        var fired = 0;
        svc.Changed += () => fired++;

        svc.SetOpen("A", false);

        Assert.False(svc.IsAnyOpen);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void 複数開いている状態で1件だけ閉じてもまだ開いているならChangedは発火しない()
    {
        var svc = new OverlayRegistryService();
        svc.SetOpen("A", true);
        svc.SetOpen("B", true);
        var fired = 0;
        svc.Changed += () => fired++;

        svc.SetOpen("A", false);

        Assert.True(svc.IsAnyOpen);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void 同じキーに対する変化のないSetOpenはChangedを発火しない()
    {
        var svc = new OverlayRegistryService();
        var fired = 0;
        svc.Changed += () => fired++;

        svc.SetOpen("A", false);   // 元々閉じている→false は変化なし

        Assert.False(svc.IsAnyOpen);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void 同じキーを二重にtrueにしても1件のまま解除で正しく空になる()
    {
        var svc = new OverlayRegistryService();
        svc.SetOpen("A", true);
        svc.SetOpen("A", true);   // 重複登録（同一コンポーネントの再描画等を想定）

        svc.SetOpen("A", false);

        Assert.False(svc.IsAnyOpen);
    }
}
