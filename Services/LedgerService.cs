using System.Text.Json;
using Seikei.Models;

namespace Seikei.Services;

public class LedgerService(StorageService storage)
{
    private const string Key = "seikei:data";

    public AppState State { get; private set; } = new();
    public string CurrentMonth { get; set; } = "202605";

    public async Task LoadAsync()
    {
        var json = await storage.GetAsync(Key);
        if (string.IsNullOrEmpty(json))
        {
            State = DefaultState();
            await SaveAsync();
        }
        else
        {
            try { State = JsonSerializer.Deserialize<AppState>(json) ?? DefaultState(); }
            catch { State = DefaultState(); }
        }
    }

    public Task SaveAsync() => storage.SetAsync(Key, JsonSerializer.Serialize(State));

    public static string PrevYm(string ym)
    {
        int y = int.Parse(ym[..4]), m = int.Parse(ym[4..]) - 1;
        if (m < 1) { m = 12; y--; }
        return $"{y}{m:D2}";
    }

    public static string NextYm(string ym)
    {
        int y = int.Parse(ym[..4]), m = int.Parse(ym[4..]) + 1;
        if (m > 12) { m = 1; y++; }
        return $"{y}{m:D2}";
    }

    public static string Label(string ym) => $"{ym[..4]}年{int.Parse(ym[4..])}月";

    // 月を開いた時点で台帳を用意。新規ならその月の確認時点に前月末残高を初期値として入れる（以後は手動）。
    public MonthData EnsureMonth(string ym)
    {
        if (!State.Months.TryGetValue(ym, out var mo))
        {
            mo = new MonthData();
            State.Months[ym] = mo;
        }
        var prev = PrevYm(ym);
        bool hasPrev = State.Months.ContainsKey(prev);
        foreach (var a in State.Accounts)
        {
            if (!mo.Ledgers.ContainsKey(a.Id))
                mo.Ledgers[a.Id] = new Ledger { Confirmed = hasPrev ? CloseOf(prev, a.Id) : 0 };
        }
        return mo;
    }

    // 月末残高 = 確認時点 + 給料 + 受取振込 - 引き落とし - 送金振込
    public decimal CloseOf(string ym, string accountId)
    {
        if (!State.Months.TryGetValue(ym, out var mo)) return 0;
        if (!mo.Ledgers.TryGetValue(accountId, out var l)) return 0;
        decimal v = l.Confirmed + l.Salary - l.Debits.Sum(d => d.Amount);
        foreach (var t in mo.Transfers)
        {
            if (t.To == accountId) v += t.Amount;
            if (t.From == accountId) v -= t.Amount;
        }
        return v;
    }

    public string? AccountName(string id) => State.Accounts.FirstOrDefault(a => a.Id == id)?.Name;

    private static AppState DefaultState() => new AppState();
}
