namespace MoneyBoard.Components;

/// <summary>
/// 統計の「ドーナツ＋一覧」1行分のビューモデル（カテゴリ別・カード別で共用）。
/// </summary>
public class SpendSlice
{
    public string Key { get; set; } = "";    // CategoryId / CardId（未分類・不明は ""）
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
    public string Color { get; set; } = "";
    public int Count { get; set; }
}
