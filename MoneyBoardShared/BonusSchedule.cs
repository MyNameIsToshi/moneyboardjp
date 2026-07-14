namespace MoneyBoardShared;

/// <summary>
/// ボーナス月（賞与を受け取る月の集合）の判定・正規化。実額（Ledger.Bonus）は別概念で、
/// ここでは「月次管理タブのボーナス入力欄をどの月に出すか」の表示判定のみを扱う（#134）。
/// </summary>
public static class BonusSchedule
{
    /// <summary>1-12 の範囲外・重複を除いて昇順に正規化する。</summary>
    public static List<int> Normalize(IEnumerable<int> months) =>
        months.Where(m => m is >= 1 and <= 12).Distinct().OrderBy(m => m).ToList();

    public static bool IsBonusMonth(IReadOnlyCollection<int> bonusMonths, int month) =>
        bonusMonths.Contains(month);

    /// <summary>ボーナス入力欄の表示可否：
    /// ①現受取口座かつボーナス月かつ当月以降（設定変更が過去月の表示へ遡って反映されないように。過去は②③のみ）／
    /// ②口座を問わず実額(Bonus)が入っている（受取口座変更後の過去凍結分を含む）／
    /// ③「＋ボーナスを追加」で手動追加済み。</summary>
    public static bool ShouldShowBonusInput(bool isBonusAccount, bool isCurrentOrFutureMonth, IReadOnlyCollection<int> bonusMonths, int month, decimal bonus, bool manuallyAdded) =>
        (isBonusAccount && isCurrentOrFutureMonth && IsBonusMonth(bonusMonths, month)) || bonus != 0 || manuallyAdded;
}
