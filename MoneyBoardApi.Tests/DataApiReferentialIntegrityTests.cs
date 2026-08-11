using MoneyBoardApi;
using MoneyBoardShared;
using Xunit;

namespace MoneyBoardApi.Tests;

// DataApi.HasValidReferences（保存時の AccountId/CardId 参照整合性検証・#159）の純粋ロジックを検証する。
public class DataApiReferentialIntegrityTests
{
    private static Account NewAccount(string id) => new() { Id = id };
    private static Card NewCard(string id, string accountId) => new() { Id = id, AccountId = accountId };

    [Fact]
    public void Empty_IsValid()
    {
        var env = new DataEnvelope();
        Assert.True(DataApi.HasValidReferences(env, new(), new(), out var reason));
        Assert.Equal("", reason);
    }

    [Fact]
    public void FixedCost_ValidAccountId_IsValid()
    {
        var env = new DataEnvelope
        {
            Settings = new SettingsPart { FixedCosts = { new FixedCost { AccountId = "a1" } } },
        };
        Assert.True(DataApi.HasValidReferences(env, new() { NewAccount("a1") }, new(), out _));
    }

    [Fact]
    public void FixedCost_DanglingAccountId_IsRejected()
    {
        var env = new DataEnvelope
        {
            Settings = new SettingsPart { FixedCosts = { new FixedCost { Id = "fc1", AccountId = "ghost" } } },
        };
        Assert.False(DataApi.HasValidReferences(env, new() { NewAccount("a1") }, new(), out var reason));
        Assert.Contains("fixedCost", reason);
        Assert.Contains("ghost", reason);
    }

    [Fact]
    public void FixedIncome_DanglingAccountId_IsRejected()
    {
        var env = new DataEnvelope
        {
            Settings = new SettingsPart { FixedIncomes = { new FixedIncome { AccountId = "ghost" } } },
        };
        Assert.False(DataApi.HasValidReferences(env, new() { NewAccount("a1") }, new(), out var reason));
        Assert.Contains("fixedIncome", reason);
    }

    [Fact]
    public void Card_DanglingAccountId_IsRejected()
    {
        var env = new DataEnvelope
        {
            Settings = new SettingsPart { Cards = { NewCard("c1", "ghost") } },
        };
        Assert.False(DataApi.HasValidReferences(env, new() { NewAccount("a1") }, new(), out var reason));
        Assert.Contains("card", reason);
    }

    [Fact]
    public void DeletedAccount_IsStillAValidReference()
    {
        // ソフト削除済み口座（IsDeleted=true）でも「実在」とみなす（loose existence）。
        // 過去の固定費・カードが削除済み口座を指し続けることは通常操作でも起こりうるため。
        var env = new DataEnvelope
        {
            Settings = new SettingsPart { FixedCosts = { new FixedCost { AccountId = "a1" } } },
        };
        Assert.True(DataApi.HasValidReferences(env, new() { new Account { Id = "a1", IsDeleted = true } }, new(), out _));
    }

    [Fact]
    public void SettingsNull_FixedCostsNotValidated()
    {
        // env.Settings == null（月次のみの保存）のときは Settings 側の参照は検証対象外
        // （そもそも今回の保存に FixedCosts は含まれない）。
        var env = new DataEnvelope { Settings = null };
        Assert.True(DataApi.HasValidReferences(env, new(), new(), out _));
    }

    [Fact]
    public void LedgerKey_ValidAccountId_IsValid()
    {
        var env = new DataEnvelope();
        env.Months["202606"] = new MonthPart { Ledgers = { ["a1"] = new Ledger() } };
        Assert.True(DataApi.HasValidReferences(env, new() { NewAccount("a1") }, new(), out _));
    }

    [Fact]
    public void LedgerKey_DanglingAccountId_IsRejected()
    {
        var env = new DataEnvelope();
        env.Months["202606"] = new MonthPart { Ledgers = { ["ghost"] = new Ledger() } };
        Assert.False(DataApi.HasValidReferences(env, new(), new(), out var reason));
        Assert.Contains("ledger", reason);
        Assert.Contains("202606", reason);
    }

    [Fact]
    public void Transfer_EmptyFromOrTo_IsAllowed()
    {
        // #168（クレカ発チャージの片側 Transfer 案）と衝突しうるため、空文字は検証対象外に据え置く。
        var env = new DataEnvelope();
        env.Months["202606"] = new MonthPart { Transfers = { new Transfer { From = "", To = "" } } };
        Assert.True(DataApi.HasValidReferences(env, new(), new(), out _));
    }

    [Fact]
    public void Transfer_DanglingFrom_IsRejected()
    {
        var env = new DataEnvelope();
        env.Months["202606"] = new MonthPart { Transfers = { new Transfer { Id = "t1", From = "ghost", To = "" } } };
        Assert.False(DataApi.HasValidReferences(env, new(), new(), out var reason));
        Assert.Contains("transfer", reason);
    }

    [Fact]
    public void Transfer_DanglingTo_IsRejected()
    {
        var env = new DataEnvelope();
        env.Months["202606"] = new MonthPart { Transfers = { new Transfer { Id = "t1", From = "", To = "ghost" } } };
        Assert.False(DataApi.HasValidReferences(env, new(), new(), out var reason));
        Assert.Contains("transfer", reason);
    }

    [Fact]
    public void Debit_NullCardId_IsAllowed()
    {
        var env = new DataEnvelope();
        var ledger = new Ledger { Debits = { new Debit { CardId = null } } };
        env.Months["202606"] = new MonthPart { Ledgers = { ["a1"] = ledger } };
        Assert.True(DataApi.HasValidReferences(env, new() { NewAccount("a1") }, new(), out _));
    }

    [Fact]
    public void Debit_DanglingCardId_IsRejected()
    {
        var env = new DataEnvelope();
        var ledger = new Ledger { Debits = { new Debit { Id = "d1", CardId = "ghost" } } };
        env.Months["202606"] = new MonthPart { Ledgers = { ["a1"] = ledger } };
        Assert.False(DataApi.HasValidReferences(env, new() { NewAccount("a1") }, new(), out var reason));
        Assert.Contains("debit", reason);
    }

    [Fact]
    public void Debit_CardIdOnDeletedCard_IsStillValid()
    {
        // カードは削除後も IsDeleted のまま残り続ける（過去明細の名前引きのため）。
        // 削除済みカードを指す既存 Debit は正常な履歴データであり拒否してはならない。
        var env = new DataEnvelope();
        var ledger = new Ledger { Debits = { new Debit { CardId = "c1" } } };
        env.Months["202606"] = new MonthPart { Ledgers = { ["a1"] = ledger } };
        Assert.True(DataApi.HasValidReferences(env, new() { NewAccount("a1") }, new() { new Card { Id = "c1", AccountId = "a1", IsDeleted = true } }, out _));
    }

    [Fact]
    public void CardDetail_DanglingCardId_IsRejected()
    {
        var env = new DataEnvelope();
        env.Months["202606"] = new MonthPart { CardDetails = { new CardDetail { Id = "cd1", CardId = "ghost" } } };
        Assert.False(DataApi.HasValidReferences(env, new(), new(), out var reason));
        Assert.Contains("cardDetail", reason);
    }

    [Fact]
    public void CardBilled_ValidCardId_IsValid()
    {
        var env = new DataEnvelope();
        env.Months["202606"] = new MonthPart { CardBilled = { ["c1"] = 12_000m } };
        Assert.True(DataApi.HasValidReferences(env, new(), new() { NewCard("c1", "a1") }, out _));
    }

    [Fact]
    public void CardBilled_DanglingCardId_IsRejected()
    {
        // CardBilled のキーは cardId。実害は小さい（孤児キーは ExpandCards から引かれない）が、
        // CardDetail.CardId / Debit.CardId と検証範囲を対称に保つ。
        var env = new DataEnvelope();
        env.Months["202606"] = new MonthPart { CardBilled = { ["ghost"] = 12_000m } };
        Assert.False(DataApi.HasValidReferences(env, new(), new(), out var reason));
        Assert.Contains("cardBilled", reason);
        Assert.Contains("ghost", reason);
    }

    [Fact]
    public void WalletAtmDeposit_DanglingAccountId_IsRejected()
    {
        var env = new DataEnvelope();
        var ledger = new Ledger { WalletAtmDeposits = { new WalletAtmDeposit { Id = "w1", AccountId = "ghost" } } };
        env.Months["202606"] = new MonthPart { Ledgers = { ["a1"] = ledger } };
        Assert.False(DataApi.HasValidReferences(env, new() { NewAccount("a1") }, new(), out var reason));
        Assert.Contains("walletAtmDeposit", reason);
    }
}
