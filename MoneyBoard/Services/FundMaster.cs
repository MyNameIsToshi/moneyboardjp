namespace MoneyBoard.Services;

/// <summary>投信マスタの1件（人気ファンドの名前→協会コード。ISINは任意＝あれば併送）。</summary>
public record FundMasterItem(string Name, string Code, string? Isin = null);

/// <summary>
/// 投資信託の人気銘柄マスタ（名前サジェスト＋協会コード自動入力用）。
/// 基準価額は投信協会ライブラリで「協会コード」だけで一意に決まる（ISINは有効値であれば何でも可）ため、
/// ここでは協会コードを主キーとして持つ。各コードは投信協会CSVで実値が返ることを検証済み（2026-06-15）。
/// 足りない銘柄はユーザーが各銘柄の「取引」で協会コードを直接入力すれば自動取得できる。
/// </summary>
public static class FundMaster
{
    public static readonly IReadOnlyList<FundMasterItem> Items = new List<FundMasterItem>
    {
        new("eMAXIS Slim 全世界株式（オール・カントリー）", "0331418A", "JP90C000H1T1"),
        new("eMAXIS Slim 米国株式（S&P500）",            "03311187", "JP90C000GKC6"),
        new("eMAXIS Slim 全世界株式（除く日本）",          "03316183"),
        new("eMAXIS Slim 先進国株式インデックス（除く日本）", "03319172", "JP90C000ENC5"),
        new("iFreeNEXT NASDAQ100インデックス",            "04317188", "JP90C000GUN2"),
        new("楽天・プラス・オールカントリー株式インデックス・ファンド", "9I31123A", "JP90C000Q2W2"),
        new("楽天・プラス・S&P500インデックス・ファンド",   "9I31223A"),
        new("楽天・全米株式インデックス・ファンド（楽天・VTI）", "9I312179", "JP90C000FHD2"),
        new("楽天・全世界株式（除く米国）インデックス・ファンド", "9I31122C", "JP90C000P228"),
        new("SBI・V・S&P500インデックス・ファンド",        "89311199", "JP90C000J569"),
        new("SBI・V・全米株式インデックス・ファンド",       "89311216"),
        new("SBI・V・全世界株式インデックス・ファンド",     "89311221"),
        new("野村世界業種別投資シリーズ（世界半導体株投資）", "01313098", "JP90C0006G52"),
    };

    public static FundMasterItem? ByName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
        : Items.FirstOrDefault(f => string.Equals(f.Name, name.Trim(), StringComparison.Ordinal));
}
