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
        var account = state.Accounts.FirstOrDefault(a => a.Id == accountId);
        return LedgerMath.Close(mo, accountId, OpeningOf(state, ym, accountId), account?.IsBonusAccount == true);
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

    // 請求先の表記ゆれ吸収：全角ASCII・全角空白を半角化し、前後/連続空白を正規化する。
    // String.Normalize は WASM(browser) 非対応のため、globalization API を使わず手動変換する。
    internal static string NormalizeStore(string? s)
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
}
