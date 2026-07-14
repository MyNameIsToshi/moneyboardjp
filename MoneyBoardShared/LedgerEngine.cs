using System.Text;

namespace MoneyBoardShared;

/// <summary>
/// 家計簿の純粋な計算ロジック（残高の自動連鎖・カード明細の月次反映・取込重複除外・固定費計算）。
/// AppState を引数で受け取り副作用を局所化することで、UI/永続化に依存せず単体テストできる。
/// 実行時は LedgerService がこのエンジンへ委譲する（ロジックの単一定義）。
/// </summary>
public static class LedgerEngine
{
    public static string PrevYm(string ym) => Ym.Parse(ym).Prev().ToString();

    // ── 残高計算 ─────────────────────────────────────
    // 月初残高 = 前月末から自動連鎖。前月の同口座台帳が無い「起点月」のみ開始残高(Confirmed)を使う。
    public static decimal OpeningOf(AppState state, string ym, string accountId)
    {
        if (!state.Months.TryGetValue(ym, out var mo)) return 0;
        if (!mo.Ledgers.TryGetValue(accountId, out var l)) return 0;
        var prev = PrevYm(ym);
        return state.Months.TryGetValue(prev, out var pm) && pm.Ledgers.ContainsKey(accountId)
            ? CloseOf(state, prev, accountId)
            : l.Confirmed;
    }

    // 起点月（前月の同口座台帳が無い）か。起点月だけ開始残高を手入力できる。
    public static bool IsOpeningAnchor(AppState state, string ym, string accountId)
    {
        var prev = PrevYm(ym);
        return !(state.Months.TryGetValue(prev, out var pm) && pm.Ledgers.ContainsKey(accountId));
    }

    public static decimal CloseOf(AppState state, string ym, string accountId)
    {
        if (!state.Months.TryGetValue(ym, out var mo)) return 0;
        if (!mo.Ledgers.ContainsKey(accountId)) return 0;
        return LedgerMath.Close(mo, accountId, OpeningOf(state, ym, accountId));
    }

    // ── カード明細 → 月次 Debit 反映 ──────────────────
    // 各カードの「その月の明細合計」を、紐づく口座の Debit(CardId付き) に反映する。
    public static void ExpandCards(AppState state, MonthData mo)
    {
        foreach (var card in state.Cards.Where(c => !c.IsDeleted).OrderBy(c => c.SortOrder))
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

    // ── 財布（現金）── ATM入出金の対称実体化（materialize・#77）──────
    // アクティブな財布（IsWallet && !IsDeleted）は同時に1個のみ。無ければ何もしない
    // （既存の口座ATM入出金フィールドは従来どおり手入力のまま）。
    public static Account? ActiveWallet(AppState state) => state.Accounts.FirstOrDefault(a => a.IsWallet && !a.IsDeleted);

    // 財布は作成月（WalletStartYm）より前の月へ台帳を遡って作らない（#77 フォローアップ）。他の口座と異なり
    // 起点月（開始残高の入力月）を作成月に固定し、過去月を開いても起点が移動しないようにするための判定。
    public static bool ShouldCreateLedgerFor(Account a, string ym) =>
        !a.IsWallet || a.WalletStartYm == null || string.CompareOrdinal(ym, a.WalletStartYm) >= 0;

    // 口座⇄財布のATM入出金を対称に実体化する。派生値は保存するため、残高計算（LedgerMath.Close）は
    // 無改修で乗り、財布削除後も過去月に凍結保存される。ym を跨がず「この月」のみを対象にする
    // （固定費/カードの「当月以降のみ」とは異なり、過去月編集でもその月を再計算＝EnsureMonth から毎回呼ぶ）。
    public static void ExpandWallet(AppState state, MonthData mo)
    {
        var wallet = ActiveWallet(state);
        if (wallet == null) return;
        if (!mo.Ledgers.TryGetValue(wallet.Id, out var walletLedger)) return;

        var otherAccountIds = state.Accounts
            .Where(a => !a.IsDeleted && a.Id != wallet.Id)   // 財布自身は自己ループ防止のため除外
            .Select(a => a.Id)
            .ToList();

        // 口座→財布（自動）：各非財布口座の既存 AtmWithdraw（手入力）を合算し、財布の AtmDeposit へ。
        walletLedger.AtmDeposit = otherAccountIds
            .Sum(id => mo.Ledgers.TryGetValue(id, out var l) ? l.AtmWithdraw : 0);

        // 財布→口座（要口座選択）：財布の明細を全件合算して財布の AtmWithdraw へ。
        walletLedger.AtmWithdraw = walletLedger.WalletAtmDeposits.Sum(e => e.Amount);

        // 対象口座ごとに合算して各口座の AtmDeposit へ（財布有効時は口座側の手入力欄を無効化し
        // materialize に一本化するため、対象が無い口座も 0 で確定する）。
        foreach (var id in otherAccountIds)
        {
            if (!mo.Ledgers.TryGetValue(id, out var l)) continue;
            l.AtmDeposit = walletLedger.WalletAtmDeposits.Where(e => e.AccountId == id).Sum(e => e.Amount);
        }
    }

    // ── 取込明細の重複除外（リボ/分割の再掲対策）──────────
    // 取込明細のうち、同一カードでより早い月に既出（利用日・請求先(正規化)・金額が一致）の行を除外する。
    // 照合は ym より前の月のみ（＝最初の出現を残す。月をまたぐ取込は時系列順が前提）。
    public static (List<CardDetail> kept, int excluded) DedupAgainstEarlierMonths(
        AppState state, string ym, string cardId, List<CardDetail> parsed)
    {
        var earlier = new HashSet<string>();
        foreach (var (m, mo) in state.Months)
        {
            if (string.CompareOrdinal(m, ym) >= 0) continue;   // ym 以降は対象外（初出を残すため過去のみ照合）
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

    internal static string DetailKey(CardDetail d) => $"{d.Date}|{NormalizeStore(d.Name)}|{d.Amount}";

    // ── カテゴリルール解決（完全一致 → 前方一致・#70）──────
    // 店名→カテゴリの解決：完全一致（exactRules）を最優先とし、該当しなければ前方一致
    // （prefixRules）を最長プレフィックス優先で判定する。ETC通行料金のように区間ごとに
    // 店名が変わる明細を、共通の接頭辞（例: "etc"）でまとめて分類するための仕組み。
    // 完全一致が優先されるため、広い prefix に対する個別上書き（例: prefix "kabu&"→公共料金・
    // 完全一致 "kabu&(プラス/プレミアム)"→サブスク）が可能。
    public static string? ResolveCategory(
        IReadOnlyDictionary<string, string> exactRules,
        IReadOnlyDictionary<string, string> prefixRules,
        string? name)
    {
        var norm = NormalizeStore(name);
        if (norm.Length == 0) return null;
        return exactRules.TryGetValue(norm, out var exact) ? exact : ResolveCategoryByPrefix(prefixRules, norm);
    }

    // 前方一致のみで解決する（完全一致は見ない）。最長プレフィックス優先・大文字小文字は区別しない。
    // prefixRules のキーは NormalizeStore + ToLowerInvariant 済み（登録側で正準化）を前提とする。
    public static string? ResolveCategoryByPrefix(IReadOnlyDictionary<string, string> prefixRules, string normalizedName)
    {
        string? bestPrefix = null, bestCategoryId = null;
        foreach (var (prefix, categoryId) in prefixRules)
        {
            if (prefix.Length == 0 || !normalizedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (bestPrefix == null || prefix.Length > bestPrefix.Length) { bestPrefix = prefix; bestCategoryId = categoryId; }
        }
        return bestCategoryId;
    }

    // 前方一致ルール登録時のクリーンアップ対象：その prefix に包含され、かつ「同一カテゴリ」を
    // 指す完全一致ルールのキー一覧を返す（削除候補）。別カテゴリを指すものは個別上書きとして
    // 残すため対象外（例: prefix "kabu&"→公共料金 とは別に "kabu&(プラス)"→サブスクを維持）。
    public static List<string> ExactRulesCoveredByPrefix(
        IReadOnlyDictionary<string, string> exactRules, string prefixKey, string categoryId)
        => exactRules
            .Where(kv => kv.Value == categoryId && kv.Key.StartsWith(prefixKey, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

    // 請求先の表記ゆれ吸収：全角ASCII・全角空白を半角化し、前後/連続空白を正規化する。
    // String.Normalize は WASM(browser) 非対応のため、globalization API を使わず手動変換する。
    // public：カテゴリ分類 API が AI 応答の店名を元の要求店名へ突き合わせる際にも再利用する（#27）。
    public static string NormalizeStore(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch >= '！' && ch <= '～') sb.Append((char)(ch - 0xFEE0));  // 全角ASCII→半角
            else if (ch == '　') sb.Append(' ');                                // 全角空白→半角
            else sb.Append(ch);
        }
        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

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

    // EnsureMonth が固定費展開を実行すべきかの判定（#87）。新規作成月（バックフィル）または
    // 当月以降のみ実行する＝既存の過去月を開き直しただけでは、あとから追加/変更した固定費を
    // 遡って混入させない（OnFixedCostChanged の IsCurrentOrFutureCycle ガードと揃える）。
    public static bool ShouldExpandFixedCosts(bool isNewMonth, bool isCurrentOrFutureCycle) =>
        isNewMonth || isCurrentOrFutureCycle;

    // 未展開の固定費のみ追加する（既存 Debit はそのまま＝月ごとの編集値を保持）。新規月の作成（EnsureMonth）用。
    public static void ExpandFixedCosts(AppState state, string ym, MonthData mo)
    {
        var month = Ym.Parse(ym).Month;
        foreach (var fc in state.FixedCosts.Where(f => IsFixedCostActive(f, ym)))
        {
            if (!mo.Ledgers.TryGetValue(fc.AccountId, out var ledger)) continue;
            if (ledger.Debits.Any(d => d.FixedCostId == fc.Id)) continue;
            ledger.Debits.Add(NewFixedCostDebit(fc, GetFixedCostAmount(fc, month)));
        }
    }

    // マスタ変更（追加/削除/改名/金額/口座/期間）を当月以降へ反映する再展開（#87）。
    // 翌月以降は編集有無に関わらず常にマスタへ一律追随する（非変動の固定費と同じ挙動）。
    // 当月（isCurrentCycle=true）に限り、変動費（IsVariable）でユーザーが手動編集済み
    // （AmountOverridden）の分だけ編集値を保持して上書きしない。
    public static void ReconcileFixedCosts(AppState state, string ym, MonthData mo, bool isCurrentCycle)
    {
        var preserved = isCurrentCycle
            ? mo.Ledgers.Values
                .SelectMany(l => l.Debits)
                .Where(d => d.IsFixed && d.IsVariable && d.AmountOverridden && d.FixedCostId != null)
                .ToDictionary(d => d.FixedCostId!, d => d.Amount)
            : new Dictionary<string, decimal>();

        foreach (var ledger in mo.Ledgers.Values)
            ledger.Debits.RemoveAll(d => d.IsFixed);

        var month = Ym.Parse(ym).Month;
        foreach (var fc in state.FixedCosts.Where(f => IsFixedCostActive(f, ym)))
        {
            if (!mo.Ledgers.TryGetValue(fc.AccountId, out var ledger)) continue;
            var kept = fc.IsVariable && preserved.TryGetValue(fc.Id, out var keptAmount) ? (decimal?)keptAmount : null;
            var overridden = kept.HasValue;
            var amount = kept ?? GetFixedCostAmount(fc, month);
            var debit = NewFixedCostDebit(fc, amount);
            debit.AmountOverridden = overridden;
            ledger.Debits.Add(debit);
        }
    }

    private static Debit NewFixedCostDebit(FixedCost fc, decimal amount) => new()
    {
        Name = fc.Name,
        Amount = amount,
        IsFixed = true,
        IsVariable = fc.IsVariable,
        FixedCostId = fc.Id
    };

    // ── 収入固定費計算（#95）───────────────────────
    // 支出の固定費計算（IsFixedCostActive/ExpandFixedCosts/ReconcileFixedCosts）と同じ形。
    // 「金額固定」は fi.Amount を毎月自動計上。「金額未固定」（IsVariable）は Amount を使わず
    // 常に0から展開し、月次管理タブで手入力した当月分の値だけ AmountOverridden で保護する。
    public static bool IsFixedIncomeActive(FixedIncome fi, string ym)
    {
        var target = Ym.Parse(ym);
        if (fi.StartBound() is { } start && target < start) return false;
        if (fi.EndBound() is { } end && target > end) return false;
        return true;
    }

    // 未展開の収入固定費のみ追加する（既存 IncomeItem はそのまま＝月ごとの編集値を保持）。新規月の作成（EnsureMonth）用。
    public static void ExpandFixedIncomes(AppState state, string ym, MonthData mo)
    {
        foreach (var fi in state.FixedIncomes.Where(f => IsFixedIncomeActive(f, ym)))
        {
            if (!mo.Ledgers.TryGetValue(fi.AccountId, out var ledger)) continue;
            if (ledger.Incomes.Any(i => i.FixedIncomeId == fi.Id)) continue;
            ledger.Incomes.Add(NewFixedIncomeItem(fi, fi.IsVariable ? 0 : fi.Amount));
        }
    }

    // マスタ変更（追加/削除/改名/金額/口座/期間）を当月以降へ反映する再展開。
    // 当月（isCurrentCycle=true）に限り、金額未固定でユーザーが手動編集済み（AmountOverridden）の
    // 分だけ編集値を保持して上書きしない。翌月以降は常に「金額固定=fi.Amount／金額未固定=0」へ揃え直す
    // （前月に手入力した値をそのまま引き継がず、毎月あらためて手入力を求める＝要望どおりの挙動）。
    public static void ReconcileFixedIncomes(AppState state, string ym, MonthData mo, bool isCurrentCycle)
    {
        var preserved = isCurrentCycle
            ? mo.Ledgers.Values
                .SelectMany(l => l.Incomes)
                .Where(i => i.IsFixed && i.IsVariable && i.AmountOverridden && i.FixedIncomeId != null)
                .ToDictionary(i => i.FixedIncomeId!, i => i.Amount)
            : new Dictionary<string, decimal>();

        foreach (var ledger in mo.Ledgers.Values)
            ledger.Incomes.RemoveAll(i => i.IsFixed);

        foreach (var fi in state.FixedIncomes.Where(f => IsFixedIncomeActive(f, ym)))
        {
            if (!mo.Ledgers.TryGetValue(fi.AccountId, out var ledger)) continue;
            var kept = fi.IsVariable && preserved.TryGetValue(fi.Id, out var keptAmount) ? (decimal?)keptAmount : null;
            var overridden = kept.HasValue;
            var amount = kept ?? (fi.IsVariable ? 0 : fi.Amount);
            var item = NewFixedIncomeItem(fi, amount);
            item.AmountOverridden = overridden;
            ledger.Incomes.Add(item);
        }
    }

    private static IncomeItem NewFixedIncomeItem(FixedIncome fi, decimal amount) => new()
    {
        Name = fi.Name,
        Amount = amount,
        IsFixed = true,
        IsVariable = fi.IsVariable,
        FixedIncomeId = fi.Id
    };
}
