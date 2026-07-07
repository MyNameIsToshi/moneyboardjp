namespace MoneyBoardShared;

/// <summary>統計（グラフ）画面の純粋ロジック。ym 文字列や明細を引数で受け、UI・チャートには依存しない。</summary>
public static class StatsMath
{
    /// <summary>
    /// 期間選択に応じて対象 ym（昇順）を絞り込む。allYmsAsc は昇順前提。
    /// period: "all"=全期間 / "current"=当月（給料サイクル起点、currentYm 一致分のみ） /
    /// "custom"=[customStart, customEnd]（逆指定は自動入替え） / 数値文字列=直近Nヶ月。
    /// 該当なし・範囲外は空リスト。N が件数を超える場合は全件（TakeLast がクランプ）。
    /// </summary>
    public static List<string> SelectPeriodYms(
        IReadOnlyList<string> allYmsAsc, string period, string customStart, string customEnd, string? currentYm = null)
    {
        if (period == "all") return allYmsAsc.ToList();
        // 「当月」は直近Nヶ月(TakeLast)に乗せない：未来月を先行作成済みだと TakeLast(1) が
        // 実際の給料サイクル(15日〜14日)と異なる月を拾ってしまうため、呼び出し元(LedgerService.CurrentCycleStartYm)
        // が渡す currentYm と一致する月だけをピンポイントで返す。
        if (period == "current")
            return currentYm != null && allYmsAsc.Contains(currentYm) ? new List<string> { currentYm } : new List<string>();
        if (period == "custom")
        {
            var (s, e) = (customStart, customEnd);
            if (string.CompareOrdinal(s, e) > 0) (s, e) = (e, s);   // 逆指定の保険
            return allYmsAsc.Where(ym => string.CompareOrdinal(ym, s) >= 0 && string.CompareOrdinal(ym, e) <= 0).ToList();
        }
        int count = int.Parse(period);
        return allYmsAsc.TakeLast(count).ToList();
    }
}
