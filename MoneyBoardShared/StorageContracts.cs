namespace MoneyBoardShared;

/// <summary>
/// GET /api/data のレスポンス兼 POST /api/data のリクエスト。
/// POST 時は変更があった部分のみ設定する（Settings=null は設定変更なし、Months は変更月のみ）。
/// </summary>
public class DataEnvelope
{
    public SettingsPart? Settings { get; set; }
    public Dictionary<string, MonthPart> Months { get; set; } = new();
    public bool IsOwner { get; set; }   // GET レスポンスのみ：現在のユーザーがオーナーか（承認管理UIの出し分け用）
}

/// <summary>設定ドキュメント（口座・固定費）に対応するパート。</summary>
public class SettingsPart
{
    public string? Etag { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public List<Account> Accounts { get; set; } = new();
    public List<FixedCost> FixedCosts { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public List<Card> Cards { get; set; } = new();
    public Dictionary<string, string> CategoryRules { get; set; } = new();
}

/// <summary>月次ドキュメントに対応するパート。</summary>
public class MonthPart
{
    public string? Etag { get; set; }
    public Dictionary<string, Ledger> Ledgers { get; set; } = new();
    public List<Transfer> Transfers { get; set; } = new();
    public List<CardDetail> CardDetails { get; set; } = new();
    public Dictionary<string, decimal> CardBilled { get; set; } = new();
}

/// <summary>POST /api/data の成功レスポンス。保存後の新しい etag を返す。</summary>
public class SaveResponse
{
    public string? SettingsEtag { get; set; }
    public Dictionary<string, string> MonthEtags { get; set; } = new();
}
