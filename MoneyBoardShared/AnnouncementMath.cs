namespace MoneyBoardShared;

/// <summary>お知らせ本文の1変更項目。【種別】が無い行は TypeLabel が空文字になる。</summary>
public readonly record struct AnnouncementChangeItem(string TypeLabel, string Text);

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

    /// <summary>
    /// お知らせ本文（"- 【種別】本文" 形式の Markdown 箇条書き）を1行ずつ種別・本文へ分解する。
    /// 先頭に【種別】が無い行は TypeLabel を空文字として扱う（バッジなし表示の想定）。
    /// </summary>
    public static IReadOnlyList<AnnouncementChangeItem> ParseChangeItems(string body)
    {
        var result = new List<AnnouncementChangeItem>();
        foreach (var rawLine in (body ?? "").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("- ")) line = line[2..].Trim();
            else if (line.StartsWith('-')) line = line[1..].Trim();

            if (line.StartsWith('【'))
            {
                var close = line.IndexOf('】');
                if (close > 0)
                {
                    result.Add(new AnnouncementChangeItem(line[1..close], line[(close + 1)..].Trim()));
                    continue;
                }
            }

            result.Add(new AnnouncementChangeItem("", line));
        }
        return result;
    }
}
