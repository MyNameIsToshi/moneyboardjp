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
    public void Apply_FutureVersion_DoesNotDowngrade_AndReportsNoChange()
    {
        // 新クライアントが書いた未来版数のデータを旧クライアントが開いても、版数を巻き戻さない（#154）。
        var state = new AppState { SchemaVersion = SchemaMigration.CurrentVersion + 1 };
        var changed = SchemaMigration.Apply(state);

        Assert.False(changed);
        Assert.Equal(SchemaMigration.CurrentVersion + 1, state.SchemaVersion);
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

    [Fact]
    public void Apply_V10ToV11_ConvertsIsWalletTrue_ToTypeWallet()
    {
        // v11 は口座種別の enum 化（#147）。旧 IsWallet==true は Type=Wallet へ変換される。
        var state = new AppState
        {
            SchemaVersion = 10,
            Accounts =
            {
                new Account { Id = "a", IsWallet = false },
                new Account { Id = "w", IsWallet = true },
            },
        };

        var changed = SchemaMigration.Apply(state);

        Assert.True(changed);
        Assert.Equal(SchemaMigration.CurrentVersion, state.SchemaVersion);
        Assert.Equal(AccountType.Normal, state.Accounts[0].Type);
        Assert.Equal(AccountType.Wallet, state.Accounts[1].Type);
    }

    [Fact]
    public void Apply_V10ToV11_DoesNotOverwriteAlreadySetType()
    {
        // 旧フラグはクリアせず残すため、種別を Wallet 以外へ変えた口座に移行が再適用されても
        // その変更を巻き戻さないこと（Type が設定済みなら旧フラグより優先）。
        var state = new AppState
        {
            SchemaVersion = 10,
            Accounts = { new Account { Id = "e", IsWallet = true, Type = AccountType.EMoney } },
        };

        SchemaMigration.Apply(state);

        Assert.Equal(AccountType.EMoney, state.Accounts[0].Type);
    }

    [Fact]
    public void Apply_IsIdempotent_ForWalletCreatedOnV11()
    {
        // v11 以降に作成された財布（Type/IsWallet の両方が立つ）は、再度 Apply しても Wallet のまま。
        var state = new AppState
        {
            SchemaVersion = SchemaMigration.CurrentVersion,
            Accounts = { new Account { Id = "w", IsWallet = true, Type = AccountType.Wallet } },
        };

        var changed = SchemaMigration.Apply(state);

        Assert.False(changed);
        Assert.Equal(AccountType.Wallet, state.Accounts[0].Type);
    }
}
