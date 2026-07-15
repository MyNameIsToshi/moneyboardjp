using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class BonusScheduleTests
{
    [Fact]
    public void Normalize_RemovesOutOfRangeAndDuplicates_AndSorts()
    {
        var result = BonusSchedule.Normalize(new[] { 12, 6, 6, 0, 13, -1, 1 });
        Assert.Equal(new List<int> { 1, 6, 12 }, result);
    }

    [Fact]
    public void Normalize_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(BonusSchedule.Normalize(Array.Empty<int>()));
    }

    [Fact]
    public void IsBonusMonth_TrueOnlyWhenMonthInSet()
    {
        var months = new List<int> { 6, 12 };
        Assert.True(BonusSchedule.IsBonusMonth(months, 6));
        Assert.False(BonusSchedule.IsBonusMonth(months, 7));
    }

    [Fact]
    public void ShouldShowBonusInput_TrueForBonusAccountOnBonusMonth_CurrentOrFuture()
    {
        var months = new List<int> { 6, 12 };
        Assert.True(BonusSchedule.ShouldShowBonusInput(isBonusAccount: true, isCurrentOrFutureMonth: true, months, month: 6, bonus: 0m, manuallyAdded: false));
    }

    [Fact]
    public void ShouldShowBonusInput_FalseForBonusAccountOffBonusMonthWithNoAmount()
    {
        var months = new List<int> { 6, 12 };
        Assert.False(BonusSchedule.ShouldShowBonusInput(isBonusAccount: true, isCurrentOrFutureMonth: true, months, month: 7, bonus: 0m, manuallyAdded: false));
    }

    [Fact]
    public void ShouldShowBonusInput_FalseForBonusAccountOnBonusMonth_PastMonthWithNoAmount()
    {
        // #134の動作確認で発覚：ボーナス月の設定変更を過去月へ遡って表示に反映してはいけない（過去凍結）。
        // 設定変更前から実額が入っている過去月は②で別途表示される。
        var months = new List<int> { 6, 12 };
        Assert.False(BonusSchedule.ShouldShowBonusInput(isBonusAccount: true, isCurrentOrFutureMonth: false, months, month: 6, bonus: 0m, manuallyAdded: false));
    }

    [Fact]
    public void ShouldShowBonusInput_TrueWhenAmountRecorded_EvenForNonBonusAccountOrPastMonth()
    {
        // 受取口座変更後の旧口座・過去月など、実額が記録済みなら常に表示（過去凍結の表示側）。
        var months = new List<int> { 6, 12 };
        Assert.True(BonusSchedule.ShouldShowBonusInput(isBonusAccount: false, isCurrentOrFutureMonth: false, months, month: 3, bonus: 100_000m, manuallyAdded: false));
    }

    [Fact]
    public void ShouldShowBonusInput_TrueWhenManuallyAdded_EvenForPastMonth()
    {
        var months = new List<int>();
        Assert.True(BonusSchedule.ShouldShowBonusInput(isBonusAccount: false, isCurrentOrFutureMonth: false, months, month: 3, bonus: 0m, manuallyAdded: true));
    }
}
