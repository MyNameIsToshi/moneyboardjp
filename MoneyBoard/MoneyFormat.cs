namespace MoneyBoard;

/// <summary>金額の表示整形（¥ 記号つき）。各コンポーネントに同一定義で散在していたヘルパーを集約する。</summary>
public static class MoneyFormat
{
    /// <summary>¥ つき整数表示（例: ¥1,234）。</summary>
    public static string Yen(decimal v) => "¥" + v.ToString("#,0");

    /// <summary>符号つき ¥ 表示（増＝+¥ / 減＝−¥。負号は U+2212）。要約ヒーロー等で増減を明示する用途。</summary>
    public static string SignedYen(decimal v) => (v >= 0 ? "+¥" : "−¥") + Math.Abs(v).ToString("#,0");
}
