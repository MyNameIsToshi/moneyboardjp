using MoneyBoardApi;
using MoneyBoardShared;
using Xunit;

namespace MoneyBoardApi.Tests;

// DataApi.IsStructurallyValid（保存前のデータ健全性ガード）の純粋ロジックを検証する。
public class DataApiValidationTests
{
    [Fact]
    public void Valid_SmallEnvelope_ReturnsTrue()
    {
        var env = new DataEnvelope
        {
            Settings = new SettingsPart { Accounts = { new Account() } },
        };
        env.Months["202606"] = new MonthPart();

        Assert.True(DataApi.IsStructurallyValid(env, out var reason));
        Assert.Equal("", reason);
    }

    [Fact]
    public void NullSettings_IsAllowed()
    {
        // POST は設定変更なしのとき Settings=null（月次のみ送る）。これを拒否しない。
        var env = new DataEnvelope { Settings = null };
        env.Months["202606"] = new MonthPart();

        Assert.True(DataApi.IsStructurallyValid(env, out _));
    }

    [Fact]
    public void TooManyAccounts_ReturnsFalse_WithReason()
    {
        var env = new DataEnvelope { Settings = new SettingsPart() };
        for (int i = 0; i < 101; i++) env.Settings.Accounts.Add(new Account());   // 上限100

        Assert.False(DataApi.IsStructurallyValid(env, out var reason));
        Assert.Contains("accounts", reason);
    }

    [Fact]
    public void TooManyMonths_ReturnsFalse()
    {
        var env = new DataEnvelope { Settings = new SettingsPart() };
        for (int i = 0; i < 601; i++) env.Months[$"m{i}"] = new MonthPart();   // 上限600

        Assert.False(DataApi.IsStructurallyValid(env, out var reason));
        Assert.Contains("months", reason);
    }

    [Fact]
    public void TooManyCardDetailsInMonth_ReturnsFalse()
    {
        var env = new DataEnvelope { Settings = new SettingsPart() };
        var mo = new MonthPart();
        for (int i = 0; i < 5001; i++) mo.CardDetails.Add(new CardDetail());   // 上限5000
        env.Months["202606"] = mo;

        Assert.False(DataApi.IsStructurallyValid(env, out var reason));
        Assert.Contains("cardDetails", reason);
    }

    [Fact]
    public void TooManyDebitsInLedger_ReturnsFalse()
    {
        var env = new DataEnvelope { Settings = new SettingsPart() };
        var mo = new MonthPart();
        var ledger = new Ledger();
        for (int i = 0; i < 1001; i++) ledger.Debits.Add(new Debit());   // 上限1000
        mo.Ledgers["a"] = ledger;
        env.Months["202606"] = mo;

        Assert.False(DataApi.IsStructurallyValid(env, out var reason));
        Assert.Contains("debits", reason);
    }
}
