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
    public bool IsPending => store.IsPending;   // 承認待ち（未承認サインイン）

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

    // 月次→カードタブ遷移時にスクロール＆展開したい対象カードId（カードタブ側が消費）。
    public string? ScrollToCardId { get; set; }

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
                // 前月ありは前月末から自動連鎖（Confirmed は参照されない）。起点月は開始残高0で作成。
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
            // 請求額が設定されていれば口座引き落としはその額（リボ・分割）。未設定なら明細合計＝一括。
            var amount = mo.CardBilled.TryGetValue(card.Id, out var billed) ? billed : sum;
            var debit = ledger.Debits.FirstOrDefault(d => d.CardId == card.Id);
            if (debit == null)
                ledger.Debits.Add(new Debit { Name = card.Name, Amount = amount, CardId = card.Id });
            else { debit.Name = card.Name; debit.Amount = amount; }
        }
    }

    // カードの「今月の請求額」を取得（null=未設定＝一括払い）。
    public decimal? CardBilledOf(string ym, string cardId) =>
        State.Months.TryGetValue(ym, out var mo) && mo.CardBilled.TryGetValue(cardId, out var b) ? b : null;

    // カードの「今月の請求額」を設定（null で解除＝明細合計に戻す）。当月のカード Debit を再計算する。
    public void SetCardBilled(string ym, string cardId, decimal? billed)
    {
        if (!State.Months.TryGetValue(ym, out var mo)) return;
        if (billed is null) mo.CardBilled.Remove(cardId);
        else mo.CardBilled[cardId] = billed.Value;
        ExpandCards(ym, mo);
    }

    // カード削除で消える対象（当月以降の明細）の件数と合計。削除確認の明示用。
    public (int count, decimal total) CardDeletionImpact(string cardId)
    {
        var details = State.Months
            .Where(kv => IsCurrentOrFutureCycle(kv.Key))
            .SelectMany(kv => kv.Value.CardDetails.Where(d => d.CardId == cardId))
            .ToList();
        return (details.Count, details.Sum(d => d.Amount));
    }

    // カード削除：ソフト削除（IsDeleted）し、当月以降の月から該当カードの明細を除去して
    // カード Debit を作り直す。過去月の明細・Debit は履歴として凍結し、レコードは残すため
    // 統計の名前引き（CardById）は機能し続ける。
    public void DeleteCard(string id)
    {
        var card = State.Cards.FirstOrDefault(c => c.Id == id);
        if (card != null) card.IsDeleted = true;
        foreach (var ym in State.Months.Keys.Where(IsCurrentOrFutureCycle).ToList())
        {
            State.Months[ym].CardDetails.RemoveAll(d => d.CardId == id);
            State.Months[ym].CardBilled.Remove(id);
        }
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

    // 取込明細のうち、同一カードでより早い月に既出（利用日・請求先(正規化)・金額が一致）の行を除外する。
    // リボ/分割は完済まで毎月CSVに同じ明細が再掲されるため、初出月だけ残して二重計上を防ぐ。
    // 照合は ym より前の月のみ（＝最初の出現を残す。月をまたぐ取込は時系列順が前提）。
    public (List<CardDetail> kept, int excluded) DedupAgainstEarlierMonths(string ym, string cardId, List<CardDetail> parsed)
    {
        var earlier = new HashSet<string>();
        foreach (var (m, mo) in State.Months)
        {
            if (string.Compare(m, ym) >= 0) continue;   // ym 以降は対象外（初出を残すため過去のみ照合）
            foreach (var d in mo.CardDetails.Where(d => d.CardId == cardId))
                earlier.Add(DetailKey(d));
        }

        var kept = new List<CardDetail>();
        int excluded = 0;
        foreach (var d in parsed)
        {
            if (earlier.Contains(DetailKey(d))) { excluded++; continue; }
            kept.Add(d);
        }
        return (kept, excluded);
    }

    private static string DetailKey(CardDetail d) => $"{d.Date}|{NormalizeStore(d.Name)}|{d.Amount}";

    // 請求先の表記ゆれ吸収：全角ASCII・全角空白を半角化し、前後/連続空白を正規化する。
    // String.Normalize は WASM(browser) 非対応のため、globalization API を使わず手動変換する。
    private static string NormalizeStore(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch >= '！' && ch <= '～') sb.Append((char)(ch - 0xFEE0));  // 全角ASCII→半角
            else if (ch == '　') sb.Append(' ');                                // 全角空白→半角
            else sb.Append(ch);
        }
        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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
    // 月初残高 = 前月末から自動連鎖。前月の同口座台帳が無い「起点月」のみ開始残高(Confirmed)を使う。
    public decimal OpeningOf(string ym, string accountId)
    {
        if (!State.Months.TryGetValue(ym, out var mo)) return 0;
        if (!mo.Ledgers.TryGetValue(accountId, out var l)) return 0;
        var prev = PrevYm(ym);
        return State.Months.TryGetValue(prev, out var pm) && pm.Ledgers.ContainsKey(accountId)
            ? CloseOf(prev, accountId)
            : l.Confirmed;
    }

    // 起点月（前月の同口座台帳が無い）か。起点月だけ開始残高を手入力できる。
    public bool IsOpeningAnchor(string ym, string accountId)
    {
        var prev = PrevYm(ym);
        return !(State.Months.TryGetValue(prev, out var pm) && pm.Ledgers.ContainsKey(accountId));
    }

    public decimal CloseOf(string ym, string accountId)
    {
        if (!State.Months.TryGetValue(ym, out var mo)) return 0;
        if (!mo.Ledgers.ContainsKey(accountId)) return 0;
        var account = State.Accounts.FirstOrDefault(a => a.Id == accountId);
        return LedgerMath.Close(mo, accountId, OpeningOf(ym, accountId), account?.IsBonusAccount == true);
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
