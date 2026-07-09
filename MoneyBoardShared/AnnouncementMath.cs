namespace MoneyBoardShared;

/// <summary>
/// お知らせ（repo同梱 announcements.json）の未読判定。id は追記のたび単調増加する前提。
/// localStorage の「最後に見た id」との比較だけで未読件数・最新idを求める純粋ロジック。
/// </summary>
public static class AnnouncementMath
{
    /// <summary>id が未読か（lastSeenId より大きいか）。未読判定の唯一の比較ルール。</summary>
    public static bool IsUnread(int id, int lastSeenId) => id > lastSeenId;

    /// <summary>lastSeenId より大きい id の件数（未読件数）。</summary>
    public static int CountUnread(IReadOnlyCollection<int> ids, int lastSeenId) =>
        ids.Count(id => IsUnread(id, lastSeenId));

    /// <summary>最新（最大）の id。要素が無ければ null。</summary>
    public static int? LatestId(IReadOnlyCollection<int> ids) =>
        ids.Count == 0 ? null : ids.Max();
}
