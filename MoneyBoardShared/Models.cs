namespace MoneyBoardShared;

public static class Util
{
    public static string NewId() => Guid.NewGuid().ToString("N")[..7];
}

public class AppState
{
    public List<Account> Accounts { get; set; } = new();
    public List<FixedCost> FixedCosts { get; set; } = new();
    public Dictionary<string, MonthData> Months { get; set; } = new();
}

// ── 口座 ──────────────────────────────────────────
public class Account
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";
    public string BankCode { get; set; } = "";
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
    public string? StartYm { get; set; }
    public string? EndYm { get; set; }
    public List<BonusSetting> BonusSettings { get; set; } = new();
}

public class BonusSetting
{
    public string Id { get; set; } = Util.NewId();
    public int Month { get; set; }
    public BonusType Type { get; set; }
    public decimal Amount { get; set; }
}

public enum BonusType { Add, Separate }

// ── 月次チE�Eタ ────────────────────────────────────
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

