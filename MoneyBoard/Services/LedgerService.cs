using System.Text.Json;
using MoneyBoardShared;

namespace MoneyBoard.Services;

public class LedgerService(StorageService storage)
{
    private const string Key = "moneyboard:data";

    public AppState State { get; private set; } = new();
    public string CurrentMonth { get; set; } = CurrentCycleStartYm();

    public bool IsLoaded { get; private set; }

    /// <summary>
    /// サーバーから状態を読み込む。成功時 true。
    /// 取得・解析に失敗した場合は State を変更せず、保存も行わずに false を返す
    /// （失敗を「データなし」と誤認して実データを空で上書きするのを防ぐため）。
    /// </summary>
    public async Task<bool> LoadAsync()
    {
        try
        {
            var json = await storage.GetAsync(Key);
            // API は新規ユーザーに対し 200 + 空の AppState を返すため、
            // json が空になるのは本当に中身が無い場合のみ。
            State = string.IsNullOrEmpty(json)
                ? new AppState()
                : JsonSerializer.Deserialize<AppState>(json) ?? new AppState();
            IsLoaded = true;
            return true;
        }
        catch
        {
            // 通信エラー・JSON 破損など。State は触らず保存もしない。
            return false;
        }
    }

    // 保存はすべて _saveLock で直列化し、フル状態アップロードが
    // 同時に走って互いを古い内容で上書きする（更新ロスト）のを防ぐ。
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private CancellationTokenSource? _debounceCts;

    /// <summary>
    /// 連続入力をまとめて1回だけ保存する（デバウンス）。
    /// 金額入力など高頻度の編集で毎キーストローク POST するのを防ぐ。
    /// </summary>
    public void RequestSave(int delayMs = 600)
    {
        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = DebouncedSaveAsync(delayMs, cts.Token);
    }

    private async Task DebouncedSaveAsync(int delayMs, CancellationToken token)
    {
        try { await Task.Delay(delayMs, token); }
        catch (TaskCanceledException) { return; }
        if (token.IsCancellationRequested) return;
        await SaveAsync();
    }

    /// <summary>保存が競合し、最新状態を読み込み直したときに発火（UI 再描画用）。</summary>
    public event Action? StateReloadedExternally;

    /// <summary>即時保存。保留中のデバウンスはキャンセルする（直後に最新状態を保存するため）。</summary>
    public async Task SaveAsync()
    {
        _debounceCts?.Cancel();
        await _saveLock.WaitAsync();
        try
        {
            var result = await storage.SetAsync(Key, JsonSerializer.Serialize(State));
            if (result == SaveResult.Conflict)
            {
                // 別タブ/別端末が先に更新済み。ローカルの変更で上書きせず最新を読み込む。
                try
                {
                    var json = await storage.GetAsync(Key);
                    State = string.IsNullOrEmpty(json)
                        ? new AppState()
                        : JsonSerializer.Deserialize<AppState>(json) ?? State;
                }
                catch { /* 再読込失敗時は既存 State を維持 */ }
                StateReloadedExternally?.Invoke();
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

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

    public static string CurrentCycleStartYm()
    {
        var today = DateTime.Today;
        if (today.Day >= 15)
            return today.ToString("yyyyMM");
        var prev = today.AddMonths(-1);
        return prev.ToString("yyyyMM");
    }

    public static bool IsCurrentOrFutureCycle(string ym)
        => string.Compare(ym, CurrentCycleStartYm()) >= 0;

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

    public void OnFixedCostChanged()
    {
        var targets = State.Months.Keys.Where(IsCurrentOrFutureCycle).ToList();
        foreach (var ym in targets)
        {
            var mo = State.Months[ym];
            foreach (var ledger in mo.Ledgers.Values)
                ledger.Debits.RemoveAll(d => d.IsFixed);
            ExpandFixedCosts(ym, mo);
        }
    }

    public static bool IsFixedCostActive(FixedCost fc, string ym)
    {
        if (fc.StartYm != null)
        {
            var startFull = fc.StartYm.Length == 6 ? fc.StartYm : fc.StartYm + "01";
            if (string.Compare(ym, startFull) < 0) return false;
        }
        if (fc.EndYm != null)
        {
            var endFull = fc.EndYm.Length == 6 ? fc.EndYm : fc.EndYm + "12";
            if (string.Compare(ym, endFull) > 0) return false;
        }
        return true;
    }

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
