namespace MoneyBoardShared;

public static class Util
{
    public static string NewId() => Guid.NewGuid().ToString("N")[..7];
}

public class AppState
{
    // 既存スキーマのバージョン。JSON に無い旧データは「最古=1」として読み込まれる
    // （初期値は CurrentVersion ではなく最古の 1 にすること。current にすると
    //  フィールド未保持の旧データが誤って最新扱いされ移行をスキップしてしまう）。
    public int SchemaVersion { get; set; } = 1;

    public List<Account> Accounts { get; set; } = new();
    public List<FixedCost> FixedCosts { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public List<Card> Cards { get; set; } = new();
    public Dictionary<string, MonthData> Months { get; set; } = new();
}

// ── カテゴリ ──────────────────────────────────────
public class Category
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";   // パレットの16進カラー
    public int SortOrder { get; set; }
}

// ── カード ──────────────────────────────────────
public class Card
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";
    public string AccountId { get; set; } = "";   // 引き落とし口座
    public int SortOrder { get; set; }
}

// ── 口座 ──────────────────────────────────────────
public class Account
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsBonusAccount { get; set; }
}

// ── 固定費マスタ ──────────────────────────────────
public class FixedCost
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";
    public string AccountId { get; set; } = "";
    public decimal Amount { get; set; }
    public string? StartYm { get; set; }   // null / "yyyy"（年のみ）/ "yyyyMM"
    public string? EndYm { get; set; }     // null / "yyyy"（年のみ）/ "yyyyMM"
    public List<BonusSetting> BonusSettings { get; set; } = new();
    public int SortOrder { get; set; }

    // 有効期間の下限・上限を Ym として返す。年のみ指定は開始=1月 / 終了=12月 とみなす。
    public Ym? StartBound() => ParseBound(StartYm, 1);
    public Ym? EndBound() => ParseBound(EndYm, 12);

    private static Ym? ParseBound(string? s, int monthIfYearOnly)
    {
        if (string.IsNullOrEmpty(s) || s.Length < 4) return null;
        var year = int.Parse(s[..4]);
        var month = s.Length >= 6 ? int.Parse(s[4..6]) : monthIfYearOnly;
        return new Ym(year, month);
    }
}

public class BonusSetting
{
    public string Id { get; set; } = Util.NewId();
    public int Month { get; set; }
    public BonusType Type { get; set; }
    public decimal Amount { get; set; }
}

public enum BonusType { Add, Separate }

// ── 月次データ ────────────────────────────────────
public class MonthData
{
    public Dictionary<string, Ledger> Ledgers { get; set; } = new();
    public List<Transfer> Transfers { get; set; } = new();
}

public class Ledger
{
    public decimal Confirmed { get; set; }
    public decimal Salary { get; set; }
    public decimal Bonus { get; set; }
    public List<Debit> Debits { get; set; } = new();
}

public class Debit
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public bool IsFixed { get; set; }
    public string? FixedCostId { get; set; }
}

public class Transfer
{
    public string Id { get; set; } = Util.NewId();
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public decimal Amount { get; set; }
}
