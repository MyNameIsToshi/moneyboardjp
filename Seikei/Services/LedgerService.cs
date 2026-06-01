using System.Text.Json;
using SeikeiShared;

namespace Seikei.Services;

public class LedgerService(StorageService storage)
{
    private const string Key = "seikei:data";

    public AppState State { get; private set; } = new();
    public string CurrentMonth { get; set; } = CurrentCycleStartYm();

    public async Task LoadAsync()
    {
        var json = await storage.GetAsync(Key);
        if (string.IsNullOrEmpty(json))
        {
            State = new AppState();
            await SaveAsync();
        }
        else
        {
            try { State = JsonSerializer.Deserialize<AppState>(json) ?? new AppState(); }
            catch { State = new AppState(); }
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

    public static string NowYm() => DateTime.Today.ToString("yyyyMM");

    // 15日〆の「当月サイクル開始年月」を返す
    // 15日以降 → 当月、14日以前 → 先月
    public static string CurrentCycleStartYm()
    {
        var today = DateTime.Today;
        if (today.Day >= 15)
            return today.ToString("yyyyMM");
        var prev = today.AddMonths(-1);
        return prev.ToString("yyyyMM");
    }

    // 対象年月が「当月サイクル以降」かどうか
    public static bool IsCurrentOrFutureCycle(string ym)
        => string.Compare(ym, CurrentCycleStartYm()) >= 0;

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
        var activeAccounts = State.Accounts.Where(a => !a.IsDeleted).OrderBy(a => a.SortOrder).ToList();
        foreach (var a in activeAccounts)
        {
            if (!mo.Ledgers.ContainsKey(a.Id))
                mo.Ledgers[a.Id] = new Ledger { Confirmed = hasPrev ? CloseOf(prev, a.Id) : 0 };
        }
        ExpandFixedCosts(ym, mo);
        return mo;
    }

    // 固定費マスタを月次台帳に展開（未登録分のみ）
    private void ExpandFixedCosts(string ym, MonthData mo)
    {
        var month = int.Parse(ym[4..]);
        foreach (var fc in State.FixedCosts.Where(f => IsFixedCostActive(f, ym)))
        {
            if (!mo.Ledgers.TryGetValue(fc.AccountId, out var ledger)) continue;
            if (ledger.Debits.Any(d => d.FixedCostId == fc.Id)) continue;
            ledger.Debits.Add(new Debit
            {
                Name = fc.Name,
                Amount = GetFixedCostAmount(fc, month),
                IsFixed = true,
                FixedCostId = fc.Id
            });
        }
    }

    // 固定費マスタが変更されたとき、当月サイクル以降の作成済み月次を自動再展開する
    public void OnFixedCostChanged()
    {
        var targets = State.Months.Keys
            .Where(IsCurrentOrFutureCycle)
            .ToList();
        foreach (var ym in targets)
        {
            var mo = State.Months[ym];
            foreach (var ledger in mo.Ledgers.Values)
                ledger.Debits.RemoveAll(d => d.IsFixed);
            ExpandFixedCosts(ym, mo);
        }
    }

    // 固定費が対象年月に有効かどうか
    // StartYm/EndYm は年だけ保存（4文字）の場合もあるため、年月比較は先頭4文字（年）で行う
    public static bool IsFixedCostActive(FixedCost fc, string ym)
    {
        // 年月の先頭4文字（年）と5文字目以降（月、なければ空）で比較
        // ym は必ず6文字（yyyyMM）
        if (fc.StartYm != null)
        {
            // 年だけの場合は "yyyyMM" → "yyyy01" として比較（その年の1月以降が有効）
            var startFull = fc.StartYm.Length == 6 ? fc.StartYm : fc.StartYm + "01";
            if (string.Compare(ym, startFull) < 0) return false;
        }
        if (fc.EndYm != null)
        {
            // 年だけの場合は "yyyy12" として比較（その年の12月まで有効）
            var endFull = fc.EndYm.Length == 6 ? fc.EndYm : fc.EndYm + "12";
            if (string.Compare(ym, endFull) > 0) return false;
        }
        return true;
    }

    // 月に応じた金額を返す（ボーナス設定を考慮）
    public static decimal GetFixedCostAmount(FixedCost fc, int month)
    {
        var bonus = fc.BonusSettings.FirstOrDefault(b => b.Month == month);
        return bonus?.Type switch
        {
            BonusType.Add => fc.Amount + bonus.Amount,
            BonusType.Separate => bonus.Amount,
            _ => fc.Amount
        };
    }

    // 月末残高 = 確認時点 + 給料 + ボーナス（受取口座のみ）+ 受取振込 - 引き落とし - 送金振込
    public decimal CloseOf(string ym, string accountId)
    {
        if (!State.Months.TryGetValue(ym, out var mo)) return 0;
        if (!mo.Ledgers.TryGetValue(accountId, out var l)) return 0;
        var account = State.Accounts.FirstOrDefault(a => a.Id == accountId);
        decimal bonus = (account?.IsBonusAccount == true) ? l.Bonus : 0;
        decimal v = l.Confirmed + l.Salary + bonus - l.Debits.Sum(d => d.Amount);
        foreach (var t in mo.Transfers)
        {
            if (t.To == accountId) v += t.Amount;
            if (t.From == accountId) v -= t.Amount;
        }
        return v;
    }

    public string? AccountName(string id) => State.Accounts.FirstOrDefault(a => a.Id == id)?.Name;

    public List<Account> ActiveAccounts =>
        State.Accounts.Where(a => !a.IsDeleted).OrderBy(a => a.SortOrder).ToList();

    public List<string> GetFixedCostsUsingAccount(string accountId) =>
        State.FixedCosts.Where(f => f.AccountId == accountId).Select(f => f.Name).ToList();

    public List<string> GetFutureMonthsUsingAccount(string accountId)
    {
        var cycleStart = CurrentCycleStartYm();
        return State.Months
            .Where(kvp => string.Compare(kvp.Key, cycleStart) >= 0)
            .Where(kvp =>
            {
                var mo = kvp.Value;
                bool hasDebit = mo.Ledgers.TryGetValue(accountId, out var l) && l.Debits.Count > 0;
                bool hasTransfer = mo.Transfers.Any(t => t.From == accountId || t.To == accountId);
                return hasDebit || hasTransfer;
            })
            .Select(kvp => Label(kvp.Key))
            .OrderBy(s => s)
            .ToList();
    }

    public void DeleteAccount(string accountId)
    {
        var a = State.Accounts.FirstOrDefault(x => x.Id == accountId);
        if (a != null) a.IsDeleted = true;
    }

    public void ReorderAccounts(List<string> orderedIds)
    {
        for (int i = 0; i < orderedIds.Count; i++)
        {
            var a = State.Accounts.FirstOrDefault(x => x.Id == orderedIds[i]);
            if (a != null) a.SortOrder = i;
        }
    }
}
