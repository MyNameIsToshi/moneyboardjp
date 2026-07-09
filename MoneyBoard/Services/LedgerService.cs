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
    public bool IsOwner => store.IsOwner;       // オーナー（承認管理UIの出し分け用）

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
        => string.CompareOrdinal(ym, CurrentCycleStartYm()) >= 0;

    // ── 月次展開 ─────────────────────────────────────
    public MonthData EnsureMonth(string ym)
    {
        bool isNewMonth = !State.Months.TryGetValue(ym, out var existing);
        var mo = existing ?? new MonthData();
        if (isNewMonth) State.Months[ym] = mo;
        var prev = PrevYm(ym);
        bool hasPrev = State.Months.ContainsKey(prev);
        var activeAccounts = State.Accounts.Where(a => !a.IsDeleted).OrderBy(a => a.SortOrder).ToList();
        foreach (var a in activeAccounts)
        {
            // 財布は作成月（WalletStartYm）より前へ遡って台帳を作らない（#77 フォローアップ）。
            if (!mo.Ledgers.ContainsKey(a.Id) && LedgerEngine.ShouldCreateLedgerFor(a, ym))
                // 前月ありは前月末から自動連鎖（Confirmed は参照されない）。起点月は開始残高0で作成。
                mo.Ledgers[a.Id] = new Ledger { Confirmed = hasPrev ? CloseOf(prev, a.Id) : 0 };
        }
        // 固定費展開の可否判定は LedgerEngine.ShouldExpandFixedCosts（純粋ロジック・テスト対象）へ委譲する。
        if (LedgerEngine.ShouldExpandFixedCosts(isNewMonth, IsCurrentOrFutureCycle(ym)))
            ExpandFixedCosts(ym, mo);
        ExpandCards(ym, mo);
        ExpandWallet(mo);
        return mo;
    }

    // 未展開分のみ追加する。計算本体は LedgerEngine.ExpandFixedCosts（純粋ロジック・テスト対象）へ委譲する。
    private void ExpandFixedCosts(string ym, MonthData mo) => LedgerEngine.ExpandFixedCosts(State, ym, mo);

    // マスタ変更を当月以降へ反映する再展開。計算本体は LedgerEngine.ReconcileFixedCosts（純粋ロジック・
    // テスト対象）へ委譲する。変動費（#87）は当月に限り手動編集済みの金額を上書きしない
    // （翌月以降は非変動の固定費と同様、常にマスタへ一律追随する）。
    public void OnFixedCostChanged()
    {
        var currentCycleYm = CurrentCycleStartYm();
        foreach (var ym in State.Months.Keys.Where(IsCurrentOrFutureCycle).ToList())
            LedgerEngine.ReconcileFixedCosts(State, ym, State.Months[ym], isCurrentCycle: ym == currentCycleYm);
    }

    // ── カード明細 → 月次 Debit 反映 ──────────────────
    // 計算本体は LedgerEngine.ExpandCards（純粋ロジック・テスト対象）へ委譲する。
    private void ExpandCards(string ym, MonthData mo) => LedgerEngine.ExpandCards(State, mo);

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

    // ── 財布（現金）── ATM入出金の実体化（#77）────────────
    // 計算本体は LedgerEngine.ExpandWallet（純粋ロジック・テスト対象）へ委譲する。
    private void ExpandWallet(MonthData mo) => LedgerEngine.ExpandWallet(State, mo);

    // 口座のATM出金／財布のATM入金明細を編集した月を再計算する（当月に限らず、編集された月そのものを対象にする）。
    public void RecalcWallet(string ym)
    {
        if (State.Months.TryGetValue(ym, out var mo)) ExpandWallet(mo);
    }

    public Account? ActiveWallet => LedgerEngine.ActiveWallet(State);
    public bool HasActiveWallet => ActiveWallet != null;

    // 店名→カテゴリの記憶ルールを明細に適用する（取込時の自動分類）。
    // 完全一致（CategoryRules）を優先し、該当しなければ前方一致（CategoryPrefixRules・#70）を
    // 最長プレフィックス優先で判定する。判定本体は LedgerEngine.ResolveCategory（純粋ロジック）へ委譲する。
    public void ApplyCategoryRules(IEnumerable<CardDetail> details)
    {
        foreach (var d in details)
        {
            var catId = LedgerEngine.ResolveCategory(State.CategoryRules, State.CategoryPrefixRules, d.Name);
            if (catId != null) d.CategoryId = catId;
        }
    }

    // 取込明細のうち、同一カードでより早い月に既出（利用日・請求先(正規化)・金額が一致）の行を除外する。
    // リボ/分割は完済まで毎月CSVに同じ明細が再掲されるため、初出月だけ残して二重計上を防ぐ。
    // 照合は ym より前の月のみ（＝最初の出現を残す。月をまたぐ取込は時系列順が前提）。
    public (List<CardDetail> kept, int excluded) DedupAgainstEarlierMonths(string ym, string cardId, List<CardDetail> parsed)
        => LedgerEngine.DedupAgainstEarlierMonths(State, ym, cardId, parsed);

    // ── カテゴリ/カード参照 ──────────────────────────
    public List<Category> CategoriesOrdered => State.Categories.OrderBy(c => c.SortOrder).ToList();
    public Category? CategoryById(string? id) => string.IsNullOrEmpty(id) ? null : State.Categories.FirstOrDefault(c => c.Id == id);
    // 一覧用は有効なカードのみ。CardById は削除済みも引ける（過去明細の名前表示用）。
    public List<Card> CardsOrdered => State.Cards.Where(c => !c.IsDeleted).OrderBy(c => c.SortOrder).ToList();
    public Card? CardById(string id) => State.Cards.FirstOrDefault(c => c.Id == id);

    // ── 固定費計算 ───────────────────────────────────
    public static bool IsFixedCostActive(FixedCost fc, string ym) => LedgerEngine.IsFixedCostActive(fc, ym);

    public static decimal GetFixedCostAmount(FixedCost fc, int month) => LedgerEngine.GetFixedCostAmount(fc, month);

    // ── 残高計算 ─────────────────────────────────────
    // 月初/月末残高は前月末から自動連鎖。計算本体は LedgerEngine（純粋ロジック・テスト対象）へ委譲する。
    public decimal OpeningOf(string ym, string accountId) => LedgerEngine.OpeningOf(State, ym, accountId);

    // 起点月（前月の同口座台帳が無い）か。起点月だけ開始残高を手入力できる。
    public bool IsOpeningAnchor(string ym, string accountId) => LedgerEngine.IsOpeningAnchor(State, ym, accountId);

    public decimal CloseOf(string ym, string accountId) => LedgerEngine.CloseOf(State, ym, accountId);

    // ── 口座/固定費の参照・操作 ──────────────────────
    public string? AccountName(string id) => State.Accounts.FirstOrDefault(a => a.Id == id)?.Name;

    // 財布は他の口座と異なる特殊枠のため常に先頭に固定表示（並び替え対象外・#77 フォローアップ）。
    public List<Account> ActiveAccounts =>
        State.Accounts.Where(a => !a.IsDeleted)
            .OrderByDescending(a => a.IsWallet)
            .ThenBy(a => a.SortOrder)
            .ToList();

    public List<string> GetFixedCostsUsingAccount(string accountId) =>
        State.FixedCosts.Where(f => f.AccountId == accountId).Select(f => f.Name).ToList();

    public List<string> GetCardsUsingAccount(string accountId) =>
        State.Cards.Where(c => !c.IsDeleted && c.AccountId == accountId).Select(c => c.Name).ToList();

    // カテゴリ削除時の使用中チェック（#113）。口座/カードと異なりカテゴリはソフト削除ではなく
    // 即時削除のため、過去月を含む全期間を対象にする（過去月だけ「参照切れ」を許容する理由がない）。
    public (int count, List<string> months) GetCardDetailsUsingCategory(string categoryId)
    {
        var hits = State.Months.Where(kv => kv.Value.CardDetails.Any(d => d.CategoryId == categoryId)).ToList();
        return (
            hits.Sum(kv => kv.Value.CardDetails.Count(d => d.CategoryId == categoryId)),
            // "yyyyMM" キーの時点で時系列ソート（Label化後の文字列ソートは "10月" が "5月" より前に来て崩れるため）。
            hits.OrderBy(kv => kv.Key).Select(kv => Label(kv.Key)).ToList()
        );
    }

    // 現金支出（財布のDebit・#77）でのカテゴリ使用中チェック（#113）。
    public (int count, List<string> months) GetDebitsUsingCategory(string categoryId)
    {
        var hits = State.Months.Where(kv => kv.Value.Ledgers.Values.Any(l => l.Debits.Any(d => d.CategoryId == categoryId))).ToList();
        return (
            hits.Sum(kv => kv.Value.Ledgers.Values.Sum(l => l.Debits.Count(d => d.CategoryId == categoryId))),
            hits.OrderBy(kv => kv.Key).Select(kv => Label(kv.Key)).ToList()
        );
    }

    // 財布は「使用中だから消せない」対象外（#77）。現金支出（Debits）が記帳済みでも、財布OFFは
    // カード削除(#49)と同じ「当月以降を掃除・過去は凍結」で正常に完了する設計のため、このガードを適用しない。
    public List<string> GetFutureMonthsUsingAccount(string accountId)
    {
        if (State.Accounts.FirstOrDefault(a => a.Id == accountId)?.IsWallet == true) return new();
        var cycleStart = CurrentCycleStartYm();
        return State.Months
            .Where(kvp => string.CompareOrdinal(kvp.Key, cycleStart) >= 0)
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
        if (a == null) return;
        a.IsDeleted = true;
        if (a.IsWallet) CleanupWalletOff(accountId);
        else RecalcWalletCurrentAndFuture();   // 非財布口座の削除で当該口座のATM出金が財布へ実体化されなくなるため、当月以降の財布を再計算する（#77）。
    }

    // 財布が有効な間、当月以降のすべての月を ExpandWallet で再計算する（財布が無ければ何もしない）。
    private void RecalcWalletCurrentAndFuture()
    {
        foreach (var ym in State.Months.Keys.Where(IsCurrentOrFutureCycle).ToList())
            ExpandWallet(State.Months[ym]);
    }

    // 財布OFF（ソフト削除）：カード削除に倣い当月以降を掃除し過去は凍結する。materialize を即時停止し、
    // 財布自身の実体化済み値・ATM入金明細、および財布由来で口座に実体化された AtmDeposit をクリアして
    // 各口座側の「ATM入金（手入力）」欄を復活させる（#77）。
    private void CleanupWalletOff(string walletId)
    {
        foreach (var ym in State.Months.Keys.Where(IsCurrentOrFutureCycle).ToList())
        {
            var mo = State.Months[ym];
            if (mo.Ledgers.TryGetValue(walletId, out var walletLedger))
            {
                walletLedger.AtmDeposit = 0;
                walletLedger.AtmWithdraw = 0;
                walletLedger.WalletAtmDeposits.Clear();
            }
            foreach (var (aid, ledger) in mo.Ledgers)
                if (aid != walletId) ledger.AtmDeposit = 0;
        }
    }
}
