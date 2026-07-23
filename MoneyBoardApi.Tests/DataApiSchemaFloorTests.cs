using MoneyBoardApi;
using Xunit;

namespace MoneyBoardApi.Tests;

// DataApi.ViolatesSchemaFloor（保存時の版数フロア判定・#155／stage2切替=#174）の純粋ロジックを検証する。
public class DataApiSchemaFloorTests
{
    [Fact]
    public void ClientSchemaVersionMissing_WithStoredDoc_IsRejected()
    {
        // ロールアウト第2段：保存済み doc がある状態で ClientSchemaVersion を送らない旧クライアントは拒否する。
        Assert.True(DataApi.ViolatesSchemaFloor(null, 12));
    }

    [Fact]
    public void ClientSchemaVersionMissing_NoStoredDoc_NewUser_IsAllowed()
    {
        // 新規ユーザー（保存済み doc 無し）は ClientSchemaVersion 欠落でもフロア対象外。
        Assert.False(DataApi.ViolatesSchemaFloor(null, null));
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
