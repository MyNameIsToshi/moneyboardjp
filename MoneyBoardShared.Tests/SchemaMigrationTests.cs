using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class SchemaMigrationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
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

    [Fact]
    public void Apply_V3ToV4_MergesCategoryRuleKeys_ByNormalizedStoreName()
    {
        // 全角/半角の表記ゆれで分裂した同一店名のルールが統合される（#27）
        var state = new AppState
        {
            SchemaVersion = 3,
            CategoryRules = new Dictionary<string, string>
            {
                ["Ａｍａｚｏｎ　Ｄｏｗｎｌｏａｄｓ"] = "cat-shop",
                ["Amazon Downloads"] = "cat-shop",
                ["スーパー"] = "cat-food",
            },
        };

        SchemaMigration.Apply(state);

        Assert.Equal(2, state.CategoryRules.Count);   // 2件へ統合される
        Assert.Equal("cat-shop", state.CategoryRules["Amazon Downloads"]);
        Assert.Equal("cat-food", state.CategoryRules["スーパー"]);
    }

    [Fact]
    public void Apply_V4ToV5_IsAdditiveOnly_PreservesExistingRules()
    {
        // v5 は CategoryPrefixRules（#70）の追加のみ。既存の完全一致ルールは変化しない。
        var state = new AppState
        {
            SchemaVersion = 4,
            CategoryRules = new Dictionary<string, string> { ["スーパー"] = "cat-food" },
        };

        var changed = SchemaMigration.Apply(state);

        Assert.True(changed);
        Assert.Equal(SchemaMigration.CurrentVersion, state.SchemaVersion);
        Assert.Equal("cat-food", state.CategoryRules["スーパー"]);
        Assert.Empty(state.CategoryPrefixRules);
    }
}
