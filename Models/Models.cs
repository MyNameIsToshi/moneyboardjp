namespace Seikei.Models;

public static class Util
{
    public static string NewId() => Guid.NewGuid().ToString("N")[..7];
}

public class AppState
{
    public List<Account> Accounts { get; set; } = new();
    public Dictionary<string, MonthData> Months { get; set; } = new();
}

public class Account
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";
}

public class MonthData
{
    public Dictionary<string, Ledger> Ledgers { get; set; } = new();
    public List<Transfer> Transfers { get; set; } = new();
}

public class Ledger
{
    public decimal Confirmed { get; set; }
    public decimal Salary { get; set; }
    public List<Debit> Debits { get; set; } = new();
}

public class Debit
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
}

public class Transfer
{
    public string Id { get; set; } = Util.NewId();
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public decimal Amount { get; set; }
}
