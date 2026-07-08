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

    // ── カテゴリルール解決（ResolveCategory・前方一致・#70）─────
    [Fact]
    public void ResolveCategory_ExactMatch_TakesPriorityOverPrefix()
    {
        // 完全一致キーは NormalizeStore 済み（大小文字はそのまま）で保存されるため、
        // 一致させるには照合側も同じ表記で渡す（前方一致のみが大小無視・#70）。
        var exact = new Dictionary<string, string> { ["ETC 一宮IC"] = "cat-specific" };
        var prefix = new Dictionary<string, string> { ["etc"] = "cat-traffic" };

        Assert.Equal("cat-specific", LedgerEngine.ResolveCategory(exact, prefix, "ETC 一宮IC"));
    }

    [Fact]
    public void ResolveCategory_FallsBackToPrefix_WhenNoExactMatch()
    {
        var exact = new Dictionary<string, string>();
        var prefix = new Dictionary<string, string> { ["etc"] = "cat-traffic" };

        Assert.Equal("cat-traffic", LedgerEngine.ResolveCategory(exact, prefix, "ETC 音羽蒲郡 -東海合併 普通車"));
    }

    [Fact]
    public void ResolveCategory_PrefixMatch_IsCaseInsensitive()
    {
        var prefix = new Dictionary<string, string> { ["etc"] = "cat-traffic" };

        Assert.Equal("cat-traffic", LedgerEngine.ResolveCategory(new Dictionary<string, string>(), prefix, "etC特割 一宮IC"));
    }

    [Fact]
    public void ResolveCategory_LongestPrefixWins()
    {
        var prefix = new Dictionary<string, string>
        {
            ["kabu"] = "cat-public",
            ["kabu&プレミアム"] = "cat-subscription",
        };

        Assert.Equal("cat-subscription", LedgerEngine.ResolveCategory(new Dictionary<string, string>(), prefix, "kabu&プレミアム 月額"));
        Assert.Equal("cat-public", LedgerEngine.ResolveCategory(new Dictionary<string, string>(), prefix, "kabu&その他"));
    }

    [Fact]
    public void ResolveCategory_NoMatch_ReturnsNull()
    {
        var prefix = new Dictionary<string, string> { ["etc"] = "cat-traffic" };
        Assert.Null(LedgerEngine.ResolveCategory(new Dictionary<string, string>(), prefix, "スーパー"));
    }

    [Fact]
    public void ExactRulesCoveredByPrefix_ReturnsOnlySameCategory_KeepsDifferentCategoryOverrides()
    {
        var exact = new Dictionary<string, string>
        {
            ["etc 一宮ic入-鳥見町出口 普通車"] = "cat-traffic",   // 同カテゴリ→クリーンアップ対象
            ["etc特割サービス"] = "cat-subscription",              // 別カテゴリ→個別上書きとして残す
        };

        var covered = LedgerEngine.ExactRulesCoveredByPrefix(exact, "etc", "cat-traffic");

        Assert.Equal(new[] { "etc 一宮ic入-鳥見町出口 普通車" }, covered);
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

    // ── EnsureMonth の固定費展開ガード（ShouldExpandFixedCosts・#87）─────
    // 既存の過去月（isNewMonth=false かつ当月より前）を開き直しただけでは固定費を展開しない
    // （あとから追加/変更した固定費が過去の確定済み月へ遡って混入するバグの回帰防止）。
    [Theory]
    [InlineData(false, false, false)]  // 既存の過去月 → 展開しない
    [InlineData(false, true, true)]    // 既存の当月/未来月 → 展開する
    [InlineData(true, false, true)]    // 新規作成月（バックフィル含む） → 展開する
    [InlineData(true, true, true)]     // 新規作成月かつ当月/未来月 → 展開する
    public void ShouldExpandFixedCosts_GuardsExistingPastMonthOnly(bool isNewMonth, bool isCurrentOrFutureCycle, bool expected)
    {
        Assert.Equal(expected, LedgerEngine.ShouldExpandFixedCosts(isNewMonth, isCurrentOrFutureCycle));
    }

    // ── 固定費の展開・再展開（ExpandFixedCosts / ReconcileFixedCosts・#87）─────
    [Fact]
    public void ExpandFixedCosts_DoesNotOverwriteExistingDebitAmount()
    {
        var state = FixedCostState(out var mo, isVariable: true);
        LedgerEngine.ExpandFixedCosts(state, "202606", mo);
        mo.Ledgers["a"].Debits.Single().Amount = 3_500m;   // 月次管理タブでの編集を模擬

        LedgerEngine.ExpandFixedCosts(state, "202606", mo);   // 同月を再度展開（EnsureMonth の再呼び出し相当）

        Assert.Equal(3_500m, mo.Ledgers["a"].Debits.Single().Amount);   // 上書きされない
    }

    [Fact]
    public void ExpandFixedCosts_NewDebit_CarriesIsVariableFlag()
    {
        var state = FixedCostState(out var mo, isVariable: true);
        LedgerEngine.ExpandFixedCosts(state, "202606", mo);

        var debit = mo.Ledgers["a"].Debits.Single();
        Assert.True(debit.IsFixed);
        Assert.True(debit.IsVariable);
        Assert.Equal(1_000m, debit.Amount);   // 初回はマスタの既定額
    }

    [Fact]
    public void ReconcileFixedCosts_OnCurrentCycle_PreservesOverriddenAmount_ForVariableFixedCost()
    {
        var state = FixedCostState(out var mo, isVariable: true);
        LedgerEngine.ExpandFixedCosts(state, "202606", mo);
        var debit = mo.Ledgers["a"].Debits.Single();
        debit.Amount = 3_500m;             // 月次管理タブでの編集を模擬
        debit.AmountOverridden = true;

        state.FixedCosts[0].Amount = 1_200m;               // マスタの既定額を変更
        LedgerEngine.ReconcileFixedCosts(state, "202606", mo, isCurrentCycle: true);

        Assert.Equal(3_500m, mo.Ledgers["a"].Debits.Single().Amount);   // 当月は編集値を保持（マスタ変更で上書きされない）
    }

    [Fact]
    public void ReconcileFixedCosts_OnCurrentCycle_UsesMasterAmount_WhenNotOverridden()
    {
        var state = FixedCostState(out var mo, isVariable: true);
        LedgerEngine.ExpandFixedCosts(state, "202606", mo);
        // 編集していない（AmountOverridden=false）のまま

        state.FixedCosts[0].Amount = 1_200m;
        LedgerEngine.ReconcileFixedCosts(state, "202606", mo, isCurrentCycle: true);

        Assert.Equal(1_200m, mo.Ledgers["a"].Debits.Single().Amount);   // 未編集ならマスタへ追随
    }

    [Fact]
    public void ReconcileFixedCosts_NotCurrentCycle_AlwaysUsesMasterAmount_EvenIfOverridden()
    {
        // 翌月以降は編集有無に関わらず一律マスタへ追随する（非変動の固定費と同じ挙動）。
        var state = FixedCostState(out var mo, isVariable: true);
        LedgerEngine.ExpandFixedCosts(state, "202607", mo);
        var debit = mo.Ledgers["a"].Debits.Single();
        debit.Amount = 3_500m;
        debit.AmountOverridden = true;

        state.FixedCosts[0].Amount = 1_200m;
        LedgerEngine.ReconcileFixedCosts(state, "202607", mo, isCurrentCycle: false);

        Assert.Equal(1_200m, mo.Ledgers["a"].Debits.Single().Amount);
        Assert.False(mo.Ledgers["a"].Debits.Single().AmountOverridden);
    }

    [Fact]
    public void ReconcileFixedCosts_UsesMasterAmount_ForNonVariableFixedCost()
    {
        var state = FixedCostState(out var mo, isVariable: false);
        LedgerEngine.ExpandFixedCosts(state, "202606", mo);

        state.FixedCosts[0].Amount = 1_200m;               // マスタの金額変更
        LedgerEngine.ReconcileFixedCosts(state, "202606", mo, isCurrentCycle: true);

        Assert.Equal(1_200m, mo.Ledgers["a"].Debits.Single().Amount);   // 固定費は常にマスタへ追随
    }

    [Fact]
    public void ReconcileFixedCosts_RemovesDebit_WhenFixedCostDeactivated()
    {
        var state = FixedCostState(out var mo, isVariable: true);
        LedgerEngine.ExpandFixedCosts(state, "202606", mo);

        state.FixedCosts[0].EndYm = "202605";   // 当月より前に終了＝非アクティブ化
        LedgerEngine.ReconcileFixedCosts(state, "202606", mo, isCurrentCycle: true);

        Assert.Empty(mo.Ledgers["a"].Debits);
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

    // 口座a＋固定費fc1（Amount=1,000・口座a紐付け）の最小 state と、口座台帳を持つ当月 MonthData を返す。
    private static AppState FixedCostState(out MonthData mo, bool isVariable)
    {
        var state = new AppState
        {
            Accounts = { new Account { Id = "a" } },
            FixedCosts = { new FixedCost { Id = "fc1", Name = "水道代", AccountId = "a", Amount = 1_000m, IsVariable = isVariable } },
        };
        mo = MonthWith("a", new Ledger());
        state.Months["202606"] = mo;
        return state;
    }
}
