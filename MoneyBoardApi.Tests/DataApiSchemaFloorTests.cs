using MoneyBoardApi;
using Xunit;

namespace MoneyBoardApi.Tests;

// DataApi.ViolatesSchemaFloor（保存時の版数フロア判定・#155）の純粋ロジックを検証する。
public class DataApiSchemaFloorTests
{
    [Fact]
    public void ClientSchemaVersionMissing_IsAllowed()
    {
        // ロールアウト第1段：ClientSchemaVersion を送らない旧クライアントは拒否しない。
        Assert.False(DataApi.ViolatesSchemaFloor(null, 12));
    }

    [Fact]
    public void ClientBelowStored_IsRejected()
    {
        Assert.True(DataApi.ViolatesSchemaFloor(11, 12));
    }

    [Fact]
    public void ClientEqualToStored_IsAllowed()
    {
        Assert.False(DataApi.ViolatesSchemaFloor(12, 12));
    }

    [Fact]
    public void ClientAboveStored_IsAllowed()
    {
        Assert.False(DataApi.ViolatesSchemaFloor(13, 12));
    }

    [Fact]
    public void NoStoredDoc_NewUser_IsAllowed()
    {
        Assert.False(DataApi.ViolatesSchemaFloor(5, null));
    }
}
