using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class SchemaMigrationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Apply_OldVersion_UpgradesToCurrent_AndReportsChange(int from)
    {
        var state = new AppState { SchemaVersion = from };
        var changed = SchemaMigration.Apply(state);

        Assert.True(changed);                                   // 変更あり＝保存が必要
        Assert.Equal(SchemaMigration.CurrentVersion, state.SchemaVersion);
    }

    [Fact]
    public void Apply_CurrentVersion_NoChange()
    {
        var state = new AppState { SchemaVersion = SchemaMigration.CurrentVersion };
        var changed = SchemaMigration.Apply(state);

        Assert.False(changed);
        Assert.Equal(SchemaMigration.CurrentVersion, state.SchemaVersion);
    }
}
