namespace MoneyBoardShared;

/// <summary>
/// 市場指標バー（#26）の確定5本。フロント（/portfolio 上部のティッカー）と
/// API（/api/market-summary）で同じ定義を共有する（二重定義によるドリフト防止）。
/// ⚠️ TOPIX は対象外：Yahoo v8 が TOPIX 指数を配信していないため
/// （^TOPX/998405.T は空・^TPX は別物=米国OPRA指数/USD）。ETF(1306.T 等)は絶対値が
/// 指数値と桁違いになるため誤表示を避けて除外し、日本株は日経平均で代表させる。
/// </summary>
public static class MarketIndexCatalog
{
    /// <param name="Decimals">フロントの値表示の小数桁（API 側は未使用）。</param>
    public record Index(string Symbol, string Label, int Decimals);

    public static readonly IReadOnlyList<Index> Items = new Index[]
    {
        new("^DJI", "NYダウ", 0),
        new("^IXIC", "ナスダック", 0),
        new("^GSPC", "S&P500", 2),
        new("^N225", "日経平均", 0),
        new("^KS11", "KOSPI", 0),
    };
}
