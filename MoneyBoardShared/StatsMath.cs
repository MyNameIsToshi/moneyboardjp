namespace MoneyBoardShared;

/// <summary>統計（グラフ）画面の純粋ロジック。ym 文字列や明細を引数で受け、UI・チャートには依存しない。</summary>
public static class StatsMath
{
    /// <summary>
    /// 期間選択に応じて対象 ym（昇順）を絞り込む。allYmsAsc は昇順前提。
    /// period: "all"=全期間 / "current"=当月（給料サイクル起点、currentYm 一致分のみ） /
    /// "custom"=[customStart, customEnd]（逆指定は自動入替え） / 数値文字列=直近Nヶ月（currentYm 指定時は当月まで・未来月除外）。
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
        // 直近Nヶ月も「当月まで」に限定する(#119)：固定費の先行展開等で未来月が
        // 既に存在していても、統計のデフォルト表示には含めない。currentYm 未指定時は従来どおり全件対象。
        var eligible = currentYm != null
            ? allYmsAsc.Where(ym => string.CompareOrdinal(ym, currentYm) <= 0).ToList()
            : allYmsAsc;
        return eligible.TakeLast(count).ToList();
    }

    /// <summary>
    /// カテゴリ別集計のグルーピングキーを正規化する（#117）。categoryId が空、または
    /// knownCategoryIds に存在しない（削除済み等で参照が切れている）場合は空文字列に統一し、
    /// 未設定・参照切れの両方を同じ「未分類」1グループに集約できるようにする。
    /// </summary>
    public static string NormalizeCategoryKey(string? categoryId, IReadOnlyCollection<string> knownCategoryIds) =>
        !string.IsNullOrEmpty(categoryId) && knownCategoryIds.Contains(categoryId) ? categoryId : "";
}
