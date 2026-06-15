namespace MoneyBoardShared;

// ── 証券ポートフォリオ（家計簿本体とは完全独立。別ドキュメント portfolio / 別API /api/portfolio）──

/// <summary>資産クラス。Fund=投資信託（基準価額は1万口あたり＝評価額は数量×単価÷10,000）/ 株は÷1。</summary>
public enum AssetClass { Fund, JpStock, UsStock }

/// <summary>口座区分（税区分）。Nisa は旧・未分類（成長/つみたて分割前の既存データ用に残置）。
/// 整数でシリアライズされるため末尾追加で既存値は不変（Nisa=0 / Tokutei=1 / General=2 / NisaGrowth=3 / NisaTsumitate=4）。</summary>
public enum AccountKind { Nisa, Tokutei, General, NisaGrowth, NisaTsumitate }

/// <summary>建て通貨＝取得原価の通貨。円建て購入=Jpy / ドル建て購入=Usd。日本株・投信は常に Jpy。</summary>
public enum Currency { Jpy, Usd }

/// <summary>保有銘柄マスタ。</summary>
public class Holding
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";          // 表示名
    public string Symbol { get; set; } = "";          // 株のみ。日本株=証券コード4桁 "7203"（取得時 .T 付与）/ 米国株=ティッカー "AAPL"。投信は空
    public string Isin { get; set; } = "";            // 投信のみ・任意。ISINコード 例 "JP90C000H1T1"（マスタ自動入力時に併せて保持）
    public string AssocFundCd { get; set; } = "";     // 投信のみ。協会コード 例 "0331418A"（基準価額取得の主キー）
    public AssetClass Class { get; set; } = AssetClass.Fund;
    public AccountKind Account { get; set; } = AccountKind.Nisa;
    public Currency CostCurrency { get; set; } = Currency.Jpy;
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>買付（履歴を積んで平均取得単価を導出）。UnitPrice は CostCurrency 建て。投信は基準価額（÷10,000は評価時に適用）。</summary>
public class BuyLot
{
    public string Id { get; set; } = Util.NewId();
    public string HoldingId { get; set; } = "";
    public string Date { get; set; } = "";   // "yyyy-MM-dd"
    public decimal Quantity { get; set; }    // 株数 / 口数
    public decimal UnitPrice { get; set; }   // 取得単価（CostCurrency 建て・投信は基準価額）
}

/// <summary>売却（解約）。実現損益＝(売却単価−平均取得単価)×数量。</summary>
public class SellLot
{
    public string Id { get; set; } = Util.NewId();
    public string HoldingId { get; set; } = "";
    public string Date { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>配当・分配金（受取記録）。</summary>
public class Dividend
{
    public string Id { get; set; } = Util.NewId();
    public string HoldingId { get; set; } = "";
    public string Date { get; set; } = "";
    public decimal Amount { get; set; }
    public Currency Currency { get; set; } = Currency.Jpy;
}

/// <summary>価格更新ボタンで記録する時系列の1点（総資産・評価損益の推移＆構成ドリルダウン用）。</summary>
public class PriceSnapshot
{
    public string At { get; set; } = "";        // 記録日時 "yyyy-MM-dd HH:mm"
    public decimal UsdJpyRate { get; set; }
    public List<HoldingValue> Values { get; set; } = new();
}

/// <summary>スナップショット内の銘柄別評価（円換算）。</summary>
public class HoldingValue
{
    public string HoldingId { get; set; } = "";
    public decimal PriceNative { get; set; }   // その時点の単価（ネイティブ通貨・投信は基準価額）
    public decimal ValuationJpy { get; set; }  // 円換算評価額
}

/// <summary>ポートフォリオ全体（portfolio ドキュメントのペイロード）。</summary>
public class PortfolioData
{
    public int SchemaVersion { get; set; } = 1;
    public List<Holding> Holdings { get; set; } = new();
    public List<BuyLot> Buys { get; set; } = new();
    public List<SellLot> Sells { get; set; } = new();
    public List<Dividend> Dividends { get; set; } = new();
    public List<PriceSnapshot> Snapshots { get; set; } = new();

    // ── 現在価格（価格更新ボタン or 投信の手入力）。評価額・評価損益の算出に使う ──
    public Dictionary<string, decimal> CurrentPrices { get; set; } = new();  // holdingId → 現在単価（ネイティブ通貨・投信は基準価額）
    public decimal UsdJpyRate { get; set; }    // 直近の USD/JPY（ドル建て/円建て米国株の円換算に使用）
    public string PricedAt { get; set; } = ""; // 価格更新日時 "yyyy-MM-dd HH:mm"
}

/// <summary>POST /api/quote のリクエスト。株+為替は Yahoo シンボル、投信は ISIN＋協会コード。</summary>
public class QuoteRequest
{
    public List<string> Symbols { get; set; } = new();   // Yahoo形式（株 "7203.T"/"AAPL"、為替は内部で付与）
    public List<FundRef> Funds { get; set; } = new();     // 投信協会で基準価額を取得する投信
}

/// <summary>投信の基準価額取得に使う識別子。協会コードが主キー（価格を一意に決める）。ISINは任意（あれば併送）。</summary>
public class FundRef
{
    public string Isin { get; set; } = "";        // 任意（未指定ならサーバーが既知の有効ISINで代替）
    public string AssocFundCd { get; set; } = "";  // 必須
}

/// <summary>/api/quote のレスポンス。取得失敗分は欠落する。</summary>
public class QuoteResponse
{
    public decimal UsdJpyRate { get; set; }
    public Dictionary<string, decimal> Prices { get; set; } = new();      // 株シンボル(大文字)→終値（ネイティブ通貨）
    public Dictionary<string, decimal> FundPrices { get; set; } = new();  // 協会コード(大文字)→基準価額（円・1万口あたり）
    public string At { get; set; } = "";   // 取得時刻（UTC）
}

/// <summary>GET /api/portfolio のレスポンス兼 POST /api/portfolio のリクエスト。</summary>
public class PortfolioEnvelope
{
    public string? Etag { get; set; }
    public PortfolioData Data { get; set; } = new();
}

/// <summary>POST /api/portfolio の成功レスポンス（保存後の新しい etag）。</summary>
public class PortfolioSaveResponse
{
    public string? Etag { get; set; }
}
