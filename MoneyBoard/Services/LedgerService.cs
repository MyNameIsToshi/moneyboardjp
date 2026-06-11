using MoneyBoardShared;

namespace MoneyBoard.Services;

/// <summary>
/// 家計簿のドメインロジック（年月・給料日サイクル・月次展開・残高計算・
/// 口座/固定費の参照と操作）を担う。状態の保持と永続化は AppStateStore に委譲する。
/// </summary>
public class LedgerService(AppStateStore store)
{
    // ── 永続化（AppStateStore への委譲）──────────────
    public AppState State => store.State;
    public bool IsLoaded => store.IsLoaded;

    public event Action? StateReloadedExternally
    {
        add => store.StateReloadedExternally += value;
        remove => store.StateReloadedExternally -= value;
    }

    public Task<bool> LoadAsync() => store.LoadAsync();
    public Task SaveAsync() => store.SaveAsync();
    public void RequestSave(int delayMs = 600) => store.RequestSave(delayMs);

    // 表示中の月（ビュー状態）。月次管理タブとカードタブは独立して月を持つ。
    public string CurrentMonth { get; set; } = CurrentCycleStartYm();
    public string CardMonth { get; set; } = CurrentCycleStartYm();

    // ── 年月・給料日サイクル ─────────────────────────
    public static string PrevYm(string ym) => Ym.Parse(ym).Prev().ToString();
    public static string NextYm(string ym) => Ym.Parse(ym).Next().ToString();
    public static string Label(string ym) => Ym.Parse(ym).Label;
    public static string NowYm() => Ym.Today.ToString();

    public static string CurrentCycleStartYm()
    {
        var today = DateTime.Today;
        // 給料日サイクル: 15日以降は当月、14日以前は前月を起点とする
        var start = today.Day >= 15 ? Ym.FromDate(today) : Ym.FromDate(today.AddMonths(-1));
        return start.ToString();
    }

    public static bool IsCurrentOrFutureCycle(string ym)
        => string.Compare(ym, CurrentCycleStartYm()) >= 0;

    // ── 月次展開 ─────────────────────────────────────
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
        ExpandCards(ym, mo);
        return mo;
    }

    private void ExpandFixedCosts(string ym, MonthData mo)
    {
        var month = Ym.Parse(ym).Month;
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

    // ── カード明細 → 月次 Debit 反映 ──────────────────
    // 各カードの「その月の明細合計」を、紐づく口座の Debit(CardId付き) に反映する。
    private void ExpandCards(string ym, MonthData mo)
    {
        foreach (var card in State.Cards.Where(c => !c.IsDeleted).OrderBy(c => c.SortOrder))
        {
            if (!mo.Ledgers.TryGetValue(card.AccountId, out var ledger)) continue;
            var sum = mo.CardDetails.Where(d => d.CardId == card.Id).Sum(d => d.Amount);
            var debit = ledger.Debits.FirstOrDefault(d => d.CardId == card.Id);
            if (debit == null)
                ledger.Debits.Add(new Debit { Name = card.Name, Amount = sum, CardId = card.Id });
            else { debit.Name = card.Name; debit.Amount = sum; }
        }
    }

    // カード削除：ソフト削除（IsDeleted）し、当月以降の月から該当カードの明細を除去して
    // カード Debit を作り直す。過去月の明細・Debit は履歴として凍結し、レコードは残すため
    // 統計の名前引き（CardById）は機能し続ける。
    public void DeleteCard(string id)
    {
        var card = State.Cards.FirstOrDefault(c => c.Id == id);
        if (card != null) card.IsDeleted = true;
        foreach (var ym in State.Months.Keys.Where(IsCurrentOrFutureCycle).ToList())
            State.Months[ym].CardDetails.RemoveAll(d => d.CardId == id);
        OnCardsChanged();
    }

    // カード設定（追加/削除/口座変更/改名）の反映：当月以降のカード Debit を作り直す。
    public void OnCardsChanged()
    {
        foreach (var ym in State.Months.Keys.Where(IsCurrentOrFutureCycle).ToList())
        {
            var mo = State.Months[ym];
            foreach (var ledger in mo.Ledgers.Values)
                ledger.Debits.RemoveAll(d => d.CardId != null);
            ExpandCards(ym, mo);
        }
    }

    // 明細を編集した月のカード Debit 金額を再計算する。
    public void RecalcCards(string ym)
    {
        if (State.Months.TryGetValue(ym, out var mo)) ExpandCards(ym, mo);
    }

    // 店名→カテゴリの記憶ルールを明細に適用する（取込時の自動分類）。
    public void ApplyCategoryRules(IEnumerable<CardDetail> details)
    {
        foreach (var d in details)
            if (State.CategoryRules.TryGetValue(d.Name, out var catId))
                d.CategoryId = catId;
    }

    // ── カテゴリ/カード参照 ──────────────────────────
    public List<Category> CategoriesOrdered => State.Categories.OrderBy(c => c.SortOrder).ToList();
    public Category? CategoryById(string? id) => string.IsNullOrEmpty(id) ? null : State.Categories.FirstOrDefault(c => c.Id == id);
    // 一覧用は有効なカードのみ。CardById は削除済みも引ける（過去明細の名前表示用）。
    public List<Card> CardsOrdered => State.Cards.Where(c => !c.IsDeleted).OrderBy(c => c.SortOrder).ToList();
    public Card? CardById(string id) => State.Cards.FirstOrDefault(c => c.Id == id);

    // ── 固定費計算 ───────────────────────────────────
    public static bool IsFixedCostActive(FixedCost fc, string ym)
    {
        var target = Ym.Parse(ym);
        if (fc.StartBound() is { } start && target < start) return false;
        if (fc.EndBound() is { } end && target > end) return false;
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

    // ── 残高計算 ─────────────────────────────────────
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

    // ── 口座/固定費の参照・操作 ──────────────────────
    public string? AccountName(string id) => State.Accounts.FirstOrDefault(a => a.Id == id)?.Name;

    public List<Account> ActiveAccounts =>
        State.Accounts.Where(a => !a.IsDeleted).OrderBy(a => a.SortOrder).ToList();

    public List<string> GetFixedCostsUsingAccount(string accountId) =>
        State.FixedCosts.Where(f => f.AccountId == accountId).Select(f => f.Name).ToList();

    public List<string> GetCardsUsingAccount(string accountId) =>
        State.Cards.Where(c => !c.IsDeleted && c.AccountId == accountId).Select(c => c.Name).ToList();

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
}
