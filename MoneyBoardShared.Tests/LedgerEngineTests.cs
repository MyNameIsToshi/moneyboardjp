using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class LedgerEngineTests
{
    // ── 残高の自動連鎖（OpeningOf / CloseOf）──────────────
    [Fact]
    public void Balance_ChainsAcrossMonths_AndIgnoresConfirmedOnNonAnchor()
    {
        var state = new AppState { Accounts = { new Account { Id = "a" } } };
        // 起点月：Confirmed=10,000 + 給料5,000
        state.Months["202601"] = MonthWith("a", new Ledger { Confirmed = 10_000m, Salary = 5_000m });
        // 翌月：Confirmed=999 は無視（前月末から連鎖）。給料2,000 − 支出1,000
        var feb = MonthWith("a", new Ledger { Confirmed = 999m, Salary = 2_000m });
        feb.Ledgers["a"].Debits.Add(new Debit { Amount = 1_000m });
        state.Months["202602"] = feb;
        // 翌々月：収入なし → 前月末がそのまま月初・月末
        state.Months["202603"] = MonthWith("a", new Ledger { Salary = 0m });

        Assert.Equal(10_000m, LedgerEngine.OpeningOf(state, "202601", "a"));   // 起点＝Confirmed
        Assert.Equal(15_000m, LedgerEngine.CloseOf(state, "202601", "a"));
        Assert.Equal(15_000m, LedgerEngine.OpeningOf(state, "202602", "a"));   // 前月末から連鎖
        Assert.Equal(16_000m, LedgerEngine.CloseOf(state, "202602", "a"));
        Assert.Equal(16_000m, LedgerEngine.OpeningOf(state, "202603", "a"));   // 2か月遡って連鎖
        Assert.Equal(16_000m, LedgerEngine.CloseOf(state, "202603", "a"));
    }

    [Fact]
    public void OpeningAnchor_TrueWhenNoPrevLedger()
    {
        var state = new AppState { Accounts = { new Account { Id = "a" } } };
        state.Months["202601"] = MonthWith("a", new Ledger());
        state.Months["202602"] = MonthWith("a", new Ledger());

        Assert.True(LedgerEngine.IsOpeningAnchor(state, "202601", "a"));   // 前月台帳なし＝起点
        Assert.False(LedgerEngine.IsOpeningAnchor(state, "202602", "a"));  // 前月台帳あり
    }

    [Fact]
    public void Balance_ZeroWhenMonthOrAccountMissing()
    {
        var state = new AppState { Accounts = { new Account { Id = "a" } } };
        Assert.Equal(0m, LedgerEngine.OpeningOf(state, "202601", "a"));   // 月なし
        state.Months["202601"] = MonthWith("a", new Ledger { Confirmed = 5m });
        Assert.Equal(0m, LedgerEngine.OpeningOf(state, "202601", "zzz")); // 口座なし
    }

    [Fact]
    public void Close_CountsBonusOnlyForBonusAccount()
    {
        var state = new AppState { Accounts = { new Account { Id = "a", IsBonusAccount = true } } };
        state.Months["202601"] = MonthWith("a", new Ledger { Bonus = 500_000m });
        Assert.Equal(500_000m, LedgerEngine.CloseOf(state, "202601", "a"));
    }

    // ── カード明細 → 月次 Debit 反映（ExpandCards）──────────
    [Fact]
    public void ExpandCards_AddsCardDebitFromDetailSum()
    {
        var state = CardState(out var mo);
        mo.CardDetails.Add(new CardDetail { CardId = "c1", Amount = 1_200m });
        mo.CardDetails.Add(new CardDetail { CardId = "c1", Amount = 1_800m });

        LedgerEngine.ExpandCards(state, mo);

        var debit = Assert.Single(mo.Ledgers["a"].Debits);
        Assert.Equal("c1", debit.CardId);
        Assert.Equal(3_000m, debit.Amount);          // 明細合計＝一括払い
        Assert.Equal("カードC1", debit.Name);
    }

    [Fact]
    public void ExpandCards_UsesCardBilledWhenSet_AndDoesNotDuplicateOnRerun()
    {
        var state = CardState(out var mo);
        mo.CardDetails.Add(new CardDetail { CardId = "c1", Amount = 3_000m });
        mo.CardBilled["c1"] = 1_000m;   // リボ：請求額＝引落

        LedgerEngine.ExpandCards(state, mo);
        LedgerEngine.ExpandCards(state, mo);   // 2回流しても二重計上しない

        var debit = Assert.Single(mo.Ledgers["a"].Debits);
        Assert.Equal(1_000m, debit.Amount);          // 利用額3,000ではなく請求額1,000
    }

    [Fact]
    public void ExpandCards_SkipsDeletedCard()
    {
        var state = CardState(out var mo);
        state.Cards[0].IsDeleted = true;
        mo.CardDetails.Add(new CardDetail { CardId = "c1", Amount = 3_000m });

        LedgerEngine.ExpandCards(state, mo);

        Assert.Empty(mo.Ledgers["a"].Debits);
    }

    // ── 取込重複除外（DedupAgainstEarlierMonths）────────────
    [Fact]
    public void Dedup_ExcludesEarlierMonthDuplicate_NormalizingStoreName()
    {
        var state = new AppState();
        // 過去月(202601)に全角表記の既出明細
        var jan = new MonthData();
        jan.CardDetails.Add(new CardDetail { CardId = "c1", Date = "2026-01-10", Name = "ＡＢＣ商店", Amount = 1_000m });
        state.Months["202601"] = jan;

        // 202602 取込：半角表記の同一明細＋新規明細
        var parsed = new List<CardDetail>
        {
            new() { CardId = "c1", Date = "2026-01-10", Name = "ABC商店", Amount = 1_000m },  // 正規化で一致→除外
            new() { CardId = "c1", Date = "2026-02-01", Name = "Cafe",   Amount = 500m },     // 新規→残す
        };

        var (kept, excluded) = LedgerEngine.DedupAgainstEarlierMonths(state, "202602", "c1", parsed);

        Assert.Equal(1, excluded);
        Assert.Equal("Cafe", Assert.Single(kept).Name);
    }

    [Fact]
    public void Dedup_OnlyComparesEarlierMonths_KeepsFirstOccurrence()
    {
        var state = new AppState();
        // 同月/未来月に同じ明細があっても除外しない（初出を残す）
        var feb = new MonthData();
        feb.CardDetails.Add(new CardDetail { CardId = "c1", Date = "2026-02-05", Name = "Shop", Amount = 800m });
        state.Months["202602"] = feb;

        var parsed = new List<CardDetail>
        {
            new() { CardId = "c1", Date = "2026-02-05", Name = "Shop", Amount = 800m },
        };

        var (kept, excluded) = LedgerEngine.DedupAgainstEarlierMonths(state, "202602", "c1", parsed);

        Assert.Equal(0, excluded);
        Assert.Single(kept);
    }

    // ── 固定費計算 ───────────────────────────────────
    [Theory]
    [InlineData("202603", false)]   // 開始前
    [InlineData("202604", true)]    // 開始月
    [InlineData("202609", true)]    // 終了月
    [InlineData("202610", false)]   // 終了後
    public void IsFixedCostActive_RespectsBounds(string ym, bool active)
    {
        var fc = new FixedCost { StartYm = "202604", EndYm = "202609" };
        Assert.Equal(active, LedgerEngine.IsFixedCostActive(fc, ym));
    }

    [Fact]
    public void IsFixedCostActive_NoBounds_AlwaysActive()
    {
        var fc = new FixedCost { StartYm = null, EndYm = null };
        Assert.True(LedgerEngine.IsFixedCostActive(fc, "209912"));
    }

    [Fact]
    public void GetFixedCostAmount_AppliesBonusOverride()
    {
        var fc = new FixedCost { Amount = 1_000m };
        fc.BonusSettings.Add(new BonusSetting { Month = 6, Type = BonusType.Add, Amount = 500m });
        fc.BonusSettings.Add(new BonusSetting { Month = 12, Type = BonusType.Separate, Amount = 2_000m });

        Assert.Equal(1_000m, LedgerEngine.GetFixedCostAmount(fc, 3));    // ボーナス無しの月＝基本額
        Assert.Equal(1_500m, LedgerEngine.GetFixedCostAmount(fc, 6));    // Add＝基本＋加算
        Assert.Equal(2_000m, LedgerEngine.GetFixedCostAmount(fc, 12));   // Separate＝置換
    }

    // ── ヘルパ ───────────────────────────────────────
    private static MonthData MonthWith(string accountId, Ledger ledger)
    {
        var mo = new MonthData();
        mo.Ledgers[accountId] = ledger;
        return mo;
    }

    // 口座a＋カードc1（口座a紐付け）の最小 state と、口座台帳を持つ当月 MonthData を返す。
    private static AppState CardState(out MonthData mo)
    {
        var state = new AppState
        {
            Accounts = { new Account { Id = "a" } },
            Cards = { new Card { Id = "c1", Name = "カードC1", AccountId = "a" } },
        };
        mo = MonthWith("a", new Ledger());
        state.Months["202606"] = mo;
        return state;
    }
}
