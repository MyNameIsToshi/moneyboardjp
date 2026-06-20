using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class LedgerMathTests
{
    private static MonthData OneLedger(string acct, Ledger l)
    {
        var mo = new MonthData();
        mo.Ledgers[acct] = l;
        return mo;
    }

    [Fact]
    public void Close_NoLedgerForAccount_ReturnsZero()
    {
        var mo = new MonthData();
        Assert.Equal(0m, LedgerMath.Close(mo, "acct", opening: 1000m, isBonusAccount: false));
    }

    [Fact]
    public void Close_OpeningPlusSalaryMinusDebits()
    {
        var l = new Ledger { Salary = 300_000m };
        l.Debits.Add(new Debit { Amount = 50_000m });
        l.Debits.Add(new Debit { Amount = 20_000m });
        var mo = OneLedger("a", l);

        // 100,000 + 300,000 - 70,000
        Assert.Equal(330_000m, LedgerMath.Close(mo, "a", opening: 100_000m, isBonusAccount: false));
    }

    [Fact]
    public void Close_BonusCountedOnlyForBonusAccount()
    {
        var l = new Ledger { Bonus = 500_000m };
        var mo = OneLedger("a", l);

        Assert.Equal(0m, LedgerMath.Close(mo, "a", opening: 0m, isBonusAccount: false));
        Assert.Equal(500_000m, LedgerMath.Close(mo, "a", opening: 0m, isBonusAccount: true));
    }

    [Fact]
    public void Close_IncludesIncomesAndAtm()
    {
        var l = new Ledger { AtmDeposit = 10_000m, AtmWithdraw = 4_000m };
        l.Incomes.Add(new IncomeItem { Amount = 2_000m });
        l.Incomes.Add(new IncomeItem { Amount = 3_000m });
        var mo = OneLedger("a", l);

        // 0 + 5,000(incomes) + 10,000 - 4,000
        Assert.Equal(11_000m, LedgerMath.Close(mo, "a", opening: 0m, isBonusAccount: false));
    }

    [Fact]
    public void Close_TransfersAddToTargetSubtractFromSource()
    {
        var mo = OneLedger("a", new Ledger());
        mo.Transfers.Add(new Transfer { From = "b", To = "a", Amount = 7_000m });   // a に入る
        mo.Transfers.Add(new Transfer { From = "a", To = "c", Amount = 2_000m });   // a から出る

        Assert.Equal(5_000m, LedgerMath.Close(mo, "a", opening: 0m, isBonusAccount: false));
    }
}
