namespace MoneyBoardShared;

/// <summary>
/// 固定費の有効期間 StartYm/EndYm（null=指定なし / "yyyy"=年のみ / "yyyyMM"=年月）の
/// 解析・組み立て・表示整形。設定UIの年/月ドロップダウンとサマリー表示で共有する純粋ロジック。
/// ドメインの期間判定（Ym 境界）は FixedCost.StartBound()/EndBound() 側が担う。
/// </summary>
public static class FixedCostPeriod
{
    /// <summary>年部分 "yyyy"。4文字以上あれば先頭4文字、無ければ ""。</summary>
    public static string YearPart(string? ym) => ym is { Length: >= 4 } ? ym[..4] : "";

    /// <summary>月部分。"yyyyMM"（6文字）のときだけ月を数値文字列（先頭0なし）で返す。年のみ/null は ""。</summary>
    public static string MonthPart(string? ym) => ym is { Length: 6 } ? int.Parse(ym[4..6]).ToString() : "";

    /// <summary>年・月の選択値から StartYm/EndYm を組み立てる。年が空なら null、月が空なら年のみ、両方あれば "yyyyMM"。</summary>
    public static string? ComposeYm(string? year, string? month)
    {
        if (string.IsNullOrEmpty(year)) return null;
        return string.IsNullOrEmpty(month) ? year : $"{year}{int.Parse(month):D2}";
    }

    /// <summary>境界の表示。月ありは "yyyy年m月"、年のみは "yyyy年"。</summary>
    public static string FmtBound(string ym)
    {
        var month = MonthPart(ym);
        return month != "" ? $"{YearPart(ym)}年{month}月" : $"{YearPart(ym)}年";
    }

    /// <summary>有効期間＋ボーナス件数のサマリー文（例: 2026年4月〜無期限・ボーナス1件）。</summary>
    public static string Summary(FixedCost fc)
    {
        var start = fc.StartYm == null ? "開始なし〜" : FmtBound(fc.StartYm) + "〜";
        var end   = fc.EndYm   == null ? "無期限"     : FmtBound(fc.EndYm);
        var bonus = fc.BonusSettings.Count > 0 ? $"ボーナス{fc.BonusSettings.Count}件" : "ボーナスなし";
        return $"{start}{end}・{bonus}";
    }
}
