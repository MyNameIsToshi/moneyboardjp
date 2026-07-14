namespace MoneyBoardShared;

/// <summary>
/// 口座の月末残高の計算式。実行時（LedgerService.CloseOf）とスキーマ移行
/// （SchemaMigration の月初残高ピン判定）で同じ式を使い、ロジックの二重化＝ドリフトを防ぐ。
/// </summary>
public static class LedgerMath
{
    /// <summary>月末残高 = 月初残高 + 給料 + ボーナス + 臨時収入 + ATM入金
    /// - 支出合計 - ATM出金 ± 振替。
    /// ボーナスは記録済みの Ledger.Bonus をそのまま計上する（受取口座フラグには依らない＝過去凍結。#134）。
    /// 受取口座を変更しても、旧口座に記録済みの過去ボーナスは旧口座の過去残高に計上され続ける。</summary>
    public static decimal Close(MonthData mo, string accountId, decimal opening)
    {
        if (!mo.Ledgers.TryGetValue(accountId, out var l)) return 0;
        decimal v = opening + l.Salary + l.Bonus + l.Incomes.Sum(i => i.Amount) + l.AtmDeposit
                    - l.Debits.Sum(d => d.Amount) - l.AtmWithdraw;
        foreach (var t in mo.Transfers)
        {
            if (t.To == accountId) v += t.Amount;
            if (t.From == accountId) v -= t.Amount;
        }
        return v;
    }
}
