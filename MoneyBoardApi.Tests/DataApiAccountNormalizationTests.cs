using System.Collections.Generic;
using MoneyBoardApi;
using MoneyBoardShared;
using Xunit;

namespace MoneyBoardApi.Tests;

// DataApi.NormalizeAccountTypes（保存時の口座種別正規化・#154）の純粋ロジックを検証する。
public class DataApiAccountNormalizationTests
{
    private const int Legacy = SchemaMigration.AccountTypeVersion - 1;   // Type を知らない旧クライアント
    private const int Current = SchemaMigration.CurrentVersion;

    [Fact]
    public void LegacyClient_WalletFlagOnly_IsRestoredToWalletType()
    {
        // v11 未満のクライアントは type を送らない（=既定の Normal）。旧フラグから種別を復元する。
        var accounts = new List<Account> { new Account { IsWallet = true, Type = AccountType.Normal } };

        DataApi.NormalizeAccountTypes(accounts, Legacy);

        Assert.Equal(AccountType.Wallet, accounts[0].Type);
        Assert.True(accounts[0].IsWallet);
    }

    [Fact]
    public void CurrentClient_TypeNormal_IsNotRevertedToWallet()
    {
        // v11 以降のクライアントでは Type が正本。財布→通常口座へ変更した口座に旧フラグが
        // 残っていても、保存のたびに Wallet へ巻き戻してはならない（#154・#148 で顕在化）。
        var accounts = new List<Account> { new Account { IsWallet = true, Type = AccountType.Normal } };

        DataApi.NormalizeAccountTypes(accounts, Current);

        Assert.Equal(AccountType.Normal, accounts[0].Type);
        Assert.False(accounts[0].IsWallet);   // 後方互換フラグは Type に合わせて落とす
    }

    [Fact]
    public void TypeSet_ButIsWalletFlagStale_IsWalletIsRewrittenToMatchType()
    {
        // 種別を Wallet 以外へ変えた後も旧フラグが true のまま残っている自己矛盾データ。
        var accounts = new List<Account> { new Account { IsWallet = true, Type = AccountType.EMoney } };

        DataApi.NormalizeAccountTypes(accounts, Current);

        Assert.Equal(AccountType.EMoney, accounts[0].Type);
        Assert.False(accounts[0].IsWallet);
    }

    [Fact]
    public void LegacyClient_DoesNotOverwriteAlreadySetType()
    {
        // 旧版数でも Type が Normal 以外なら正本として尊重する（復元は Normal のときのみ）。
        var accounts = new List<Account> { new Account { IsWallet = true, Type = AccountType.EMoney } };

        DataApi.NormalizeAccountTypes(accounts, Legacy);

        Assert.Equal(AccountType.EMoney, accounts[0].Type);
        Assert.False(accounts[0].IsWallet);
    }

    [Fact]
    public void NormalAccount_IsUnaffected()
    {
        var accounts = new List<Account> { new Account { IsWallet = false, Type = AccountType.Normal } };

        DataApi.NormalizeAccountTypes(accounts, Current);

        Assert.Equal(AccountType.Normal, accounts[0].Type);
        Assert.False(accounts[0].IsWallet);
    }

    [Fact]
    public void WalletType_KeepsLegacyFlagSet_ForOldClients()
    {
        // 旧クライアントが同じデータを開いても財布判定を落とさないよう、Wallet では IsWallet を立て続ける。
        var accounts = new List<Account> { new Account { IsWallet = false, Type = AccountType.Wallet } };

        DataApi.NormalizeAccountTypes(accounts, Current);

        Assert.Equal(AccountType.Wallet, accounts[0].Type);
        Assert.True(accounts[0].IsWallet);
    }
}
