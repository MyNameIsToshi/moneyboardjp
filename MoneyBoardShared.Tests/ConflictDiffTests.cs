using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class ConflictDiffTests
{
    [Fact]
    public void Extract_NoChanges_ReturnsEmpty()
    {
        var result = ConflictDiff.Extract(null, null, new Dictionary<string, MonthPart>(), new Dictionary<string, MonthPart>());

        Assert.True(result.IsEmpty);
        Assert.Null(result.SettingsItemCount);
        Assert.Empty(result.Months);
    }

    [Fact]
    public void Extract_SettingsItemAdded_CountsOne()
    {
        var before = new SettingsPart { Accounts = new() { new Account { Id = "a1", Name = "旧" } } };
        var after = new SettingsPart
        {
            Accounts = new() { new Account { Id = "a1", Name = "旧" }, new Account { Id = "a2", Name = "新" } }
        };

        var result = ConflictDiff.Extract(before, after, new Dictionary<string, MonthPart>(), new Dictionary<string, MonthPart>());

        Assert.Equal(1, result.SettingsItemCount);
    }

    [Fact]
    public void Extract_SettingsItemModified_CountsOneByIdNotTwo()
    {
        // 同じ Id の要素が編集された場合、削除+追加ではなく「変更1件」として数える
        var before = new SettingsPart { FixedCosts = new() { new FixedCost { Id = "f1", Name = "家賃", Amount = 50000 } } };
        var after = new SettingsPart { FixedCosts = new() { new FixedCost { Id = "f1", Name = "家賃", Amount = 55000 } } };

        var result = ConflictDiff.Extract(before, after, new Dictionary<string, MonthPart>(), new Dictionary<string, MonthPart>());

        Assert.Equal(1, result.SettingsItemCount);
    }

    [Fact]
    public void Extract_SettingsItemRemoved_CountsOne()
    {
        var before = new SettingsPart { Categories = new() { new Category { Id = "c1" }, new Category { Id = "c2" } } };
        var after = new SettingsPart { Categories = new() { new Category { Id = "c1" } } };

        var result = ConflictDiff.Extract(before, after, new Dictionary<string, MonthPart>(), new Dictionary<string, MonthPart>());

        Assert.Equal(1, result.SettingsItemCount);
    }

    [Fact]
    public void Extract_SettingsUnchanged_SettingsItemCountIsNull()
    {
        var before = new SettingsPart { Accounts = new() { new Account { Id = "a1", Name = "同じ" } } };
        var after = new SettingsPart { Accounts = new() { new Account { Id = "a1", Name = "同じ" } } };

        var result = ConflictDiff.Extract(before, after, new Dictionary<string, MonthPart>(), new Dictionary<string, MonthPart>());

        Assert.Null(result.SettingsItemCount);
    }

    [Fact]
    public void Extract_MonthLedgerChanged_ReportsMonthWithCount()
    {
        var before = new MonthPart { Ledgers = new() { ["a1"] = new Ledger { Salary = 300000 } } };
        var after = new MonthPart { Ledgers = new() { ["a1"] = new Ledger { Salary = 320000 } } };
        var baselineMonths = new Dictionary<string, MonthPart> { ["2026-06"] = before };
        var discardedMonths = new Dictionary<string, MonthPart> { ["2026-06"] = after };

        var result = ConflictDiff.Extract(null, null, baselineMonths, discardedMonths);

        var month = Assert.Single(result.Months);
        Assert.Equal("2026-06", month.Ym);
        Assert.Equal(1, month.ItemCount);
    }

    [Fact]
    public void Extract_MonthLedgerDebitsAddedAndModified_CountsByLineItemNotByAccount()
    {
        // #177: 口座内の複数明細を編集しても「1件」に丸めず、明細単位で数える
        var before = new MonthPart
        {
            Ledgers = new()
            {
                ["a1"] = new Ledger
                {
                    Debits = new()
                    {
                        new Debit { Id = "d1", Name = "既存1", Amount = 1000 },
                        new Debit { Id = "d2", Name = "既存2", Amount = 2000 },
                    },
                },
            },
        };
        var after = new MonthPart
        {
            Ledgers = new()
            {
                ["a1"] = new Ledger
                {
                    Debits = new()
                    {
                        new Debit { Id = "d1", Name = "既存1", Amount = 1500 },   // 変更
                        // d2 は削除
                        new Debit { Id = "d3", Name = "新規1", Amount = 300 },    // 追加
                        new Debit { Id = "d4", Name = "新規2", Amount = 400 },    // 追加
                    },
                },
            },
        };
        var baselineMonths = new Dictionary<string, MonthPart> { ["2026-07"] = before };
        var discardedMonths = new Dictionary<string, MonthPart> { ["2026-07"] = after };

        var result = ConflictDiff.Extract(null, null, baselineMonths, discardedMonths);

        var month = Assert.Single(result.Months);
        Assert.Equal(4, month.ItemCount);   // d1変更 + d2削除 + d3追加 + d4追加
    }

    [Fact]
    public void Extract_MonthLedgerScalarAndDebitBothChanged_CountsLineItemsPlusOneForScalar()
    {
        var before = new MonthPart { Ledgers = new() { ["a1"] = new Ledger { Salary = 300000 } } };
        var after = new MonthPart
        {
            Ledgers = new()
            {
                ["a1"] = new Ledger
                {
                    Salary = 320000,   // スカラー変更
                    Debits = new() { new Debit { Id = "d1", Name = "支出", Amount = 500 } },   // 明細追加
                },
            },
        };
        var baselineMonths = new Dictionary<string, MonthPart> { ["2026-07"] = before };
        var discardedMonths = new Dictionary<string, MonthPart> { ["2026-07"] = after };

        var result = ConflictDiff.Extract(null, null, baselineMonths, discardedMonths);

        var month = Assert.Single(result.Months);
        Assert.Equal(2, month.ItemCount);   // Debits +1 と スカラー変更で1件（合算2件）
    }

    [Fact]
    public void Extract_MonthLedgerScalarOnlyChanged_CountsOnePerLedger()
    {
        // 受入条件: スカラーのみ（Salary/Bonus/Confirmed/AtmDeposit/AtmWithdraw）の変更は
        // 変更フィールド数ではなく「台帳あたり1件」で数える
        var before = new MonthPart { Ledgers = new() { ["a1"] = new Ledger { Salary = 300000, Bonus = 100000 } } };
        var after = new MonthPart { Ledgers = new() { ["a1"] = new Ledger { Salary = 320000, Bonus = 150000, AtmWithdraw = 5000 } } };
        var baselineMonths = new Dictionary<string, MonthPart> { ["2026-07"] = before };
        var discardedMonths = new Dictionary<string, MonthPart> { ["2026-07"] = after };

        var result = ConflictDiff.Extract(null, null, baselineMonths, discardedMonths);

        var month = Assert.Single(result.Months);
        Assert.Equal(1, month.ItemCount);   // 3フィールド変わっても台帳あたり1件
    }

    [Fact]
    public void Extract_NewLedgerAddedWithDefaultScalars_CountsOnlyLineItems()
    {
        // #177: 新規追加された口座台帳は、削除時と対称に明細のみ数える（スカラー既定値で
        // 誤って +1 しない）。before に無い口座キーを追加し、明細だけを入れたケース。
        var before = new MonthPart();
        var after = new MonthPart
        {
            Ledgers = new()
            {
                ["a1"] = new Ledger
                {
                    Debits = new() { new Debit { Id = "d1" }, new Debit { Id = "d2" } },
                },
            },
        };
        var baselineMonths = new Dictionary<string, MonthPart> { ["2026-07"] = before };
        var discardedMonths = new Dictionary<string, MonthPart> { ["2026-07"] = after };

        var result = ConflictDiff.Extract(null, null, baselineMonths, discardedMonths);

        var month = Assert.Single(result.Months);
        Assert.Equal(2, month.ItemCount);   // d1・d2 の追加のみ（新規台帳のスカラー既定値は数えない）
    }

    [Fact]
    public void Extract_MonthLedgerIncomesAndWalletDeposits_CountsByLineItem()
    {
        // 受入条件: Debits 以外の明細リスト（Incomes/WalletAtmDeposits）も明細単位で数える
        var before = new MonthPart
        {
            Ledgers = new()
            {
                ["a1"] = new Ledger
                {
                    Incomes = new() { new IncomeItem { Id = "i1", Amount = 1000 } },
                    WalletAtmDeposits = new() { new WalletAtmDeposit { Id = "w1", Amount = 2000 } },
                },
            },
        };
        var after = new MonthPart
        {
            Ledgers = new()
            {
                ["a1"] = new Ledger
                {
                    Incomes = new() { new IncomeItem { Id = "i1", Amount = 1500 } },   // 変更
                    WalletAtmDeposits = new()
                    {
                        new WalletAtmDeposit { Id = "w1", Amount = 2000 },              // 不変
                        new WalletAtmDeposit { Id = "w2", Amount = 3000 },              // 追加
                    },
                },
            },
        };
        var baselineMonths = new Dictionary<string, MonthPart> { ["2026-07"] = before };
        var discardedMonths = new Dictionary<string, MonthPart> { ["2026-07"] = after };

        var result = ConflictDiff.Extract(null, null, baselineMonths, discardedMonths);

        var month = Assert.Single(result.Months);
        Assert.Equal(2, month.ItemCount);   // i1変更 + w2追加
    }

    [Fact]
    public void Extract_MonthLedgerRemoved_CountsRemainingLineItems()
    {
        var before = new MonthPart
        {
            Ledgers = new()
            {
                ["a1"] = new Ledger
                {
                    Debits = new() { new Debit { Id = "d1" }, new Debit { Id = "d2" } },
                },
            },
        };
        var after = new MonthPart();   // 口座台帳ごと削除
        var baselineMonths = new Dictionary<string, MonthPart> { ["2026-07"] = before };
        var discardedMonths = new Dictionary<string, MonthPart> { ["2026-07"] = after };

        var result = ConflictDiff.Extract(null, null, baselineMonths, discardedMonths);

        var month = Assert.Single(result.Months);
        Assert.Equal(2, month.ItemCount);   // d1・d2 の削除をそれぞれ数える
    }

    [Fact]
    public void Extract_MonthCardBilledChanged_StaysAccountKeyGranularity()
    {
        // CardBilled は値がスカラー（decimal）の辞書なので、複合型の再帰対象にせずキー単位のまま数える
        var before = new MonthPart { CardBilled = new() { ["card1"] = 10000m } };
        var after = new MonthPart { CardBilled = new() { ["card1"] = 12000m, ["card2"] = 5000m } };
        var baselineMonths = new Dictionary<string, MonthPart> { ["2026-07"] = before };
        var discardedMonths = new Dictionary<string, MonthPart> { ["2026-07"] = after };

        var result = ConflictDiff.Extract(null, null, baselineMonths, discardedMonths);

        var month = Assert.Single(result.Months);
        Assert.Equal(2, month.ItemCount);   // card1変更 + card2追加（キー単位）
    }

    [Fact]
    public void Extract_NewMonthNotInBaseline_CountsAllItemsAsDiscarded()
    {
        var after = new MonthPart
        {
            Transfers = new() { new Transfer { Id = "t1" }, new Transfer { Id = "t2" } }
        };
        var discardedMonths = new Dictionary<string, MonthPart> { ["2026-07"] = after };

        var result = ConflictDiff.Extract(null, null, new Dictionary<string, MonthPart>(), discardedMonths);

        var month = Assert.Single(result.Months);
        Assert.Equal("2026-07", month.Ym);
        Assert.Equal(2, month.ItemCount);
    }

    [Fact]
    public void Extract_MultipleMonths_SortedByYm()
    {
        var discardedMonths = new Dictionary<string, MonthPart>
        {
            ["2026-07"] = new MonthPart { Transfers = new() { new Transfer { Id = "t1" } } },
            ["2026-06"] = new MonthPart { Transfers = new() { new Transfer { Id = "t2" } } },
        };

        var result = ConflictDiff.Extract(null, null, new Dictionary<string, MonthPart>(), discardedMonths);

        Assert.Equal(new[] { "2026-06", "2026-07" }, result.Months.Select(m => m.Ym));
    }

    [Fact]
    public void Extract_BonusMonthsListWithoutId_UsesMultisetFallback()
    {
        // List<int> のような Id を持たない要素は多重集合の対称差で数える
        var before = new SettingsPart { BonusMonths = new() { 6, 12 } };
        var after = new SettingsPart { BonusMonths = new() { 6 } };

        var result = ConflictDiff.Extract(before, after, new Dictionary<string, MonthPart>(), new Dictionary<string, MonthPart>());

        Assert.Equal(1, result.SettingsItemCount);
    }

    [Fact]
    public void CountItemDiff_IgnoredProperties_ExcludedFromCount()
    {
        // 資産の破棄件数は、価格更新で自動再取得される派生フィールド（CurrentPrices/PrevPrices/Snapshots）を
        // 除外し、実際のユーザー編集（Holdings 等）だけを数える（#158）。
        var ignore = new HashSet<string>
        {
            nameof(PortfolioData.CurrentPrices),
            nameof(PortfolioData.PrevPrices),
            nameof(PortfolioData.Snapshots),
        };
        var before = new PortfolioData
        {
            Holdings = new() { new Holding { Id = "h1" } },
            CurrentPrices = new() { ["h1"] = 100m },
        };
        var after = new PortfolioData
        {
            Holdings = new() { new Holding { Id = "h1" }, new Holding { Id = "h2" } },   // 実編集: 銘柄+1
            CurrentPrices = new() { ["h1"] = 200m, ["h2"] = 300m },                      // 価格更新のみ（除外対象）
            PrevPrices = new() { ["h1"] = 90m },                                         // 価格更新のみ（除外対象）
            Snapshots = new() { new PriceSnapshot { At = "2026-07-24 09:00" } },         // 価格更新のみ（除外対象）
        };

        Assert.Equal(1, ConflictDiff.CountItemDiff(before, after, ignore));   // Holdings +1 だけ
        Assert.True(ConflictDiff.CountItemDiff(before, after) > 1);           // 除外しないと価格系で水増しされる
    }
}
