using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

// AccountType 述語（#164）の真理値表を固定する。3種別×3述語の全組み合わせ。
public class AccountTypePredicatesTests
{
    [Theory]
    [InlineData(AccountType.Normal, true)]
    [InlineData(AccountType.Wallet, false)]
    [InlineData(AccountType.EMoney, false)]
    public void CanReceiveSalary_NormalOnly(AccountType type, bool expected) =>
        Assert.Equal(expected, type.CanReceiveSalary());

    [Theory]
    [InlineData(AccountType.Normal, true)]
    [InlineData(AccountType.Wallet, false)]
    [InlineData(AccountType.EMoney, false)]
    public void ParticipatesInAtm_NormalOnly(AccountType type, bool expected) =>
        Assert.Equal(expected, type.ParticipatesInAtm());

    [Theory]
    [InlineData(AccountType.Normal, false)]
    [InlineData(AccountType.Wallet, true)]
    [InlineData(AccountType.EMoney, true)]
    public void HasCategorizedSpending_WalletAndEMoney(AccountType type, bool expected) =>
        Assert.Equal(expected, type.HasCategorizedSpending());

    [Fact]
    public void UndefinedValue_AllPredicatesReturnFalse_NoException()
    {
        // 細工したリクエスト等による未定義値（#147 からの申し送り）。switchを使わず比較で書いているため
        // 例外にならず false を返す。
        var undefined = (AccountType)999;

        Assert.False(undefined.CanReceiveSalary());
        Assert.False(undefined.ParticipatesInAtm());
        Assert.False(undefined.HasCategorizedSpending());
    }
}
