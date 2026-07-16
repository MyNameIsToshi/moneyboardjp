namespace MoneyBoardShared;

/// <summary>
/// 収入（給与）記録ページ（#107）の年収サマリー計算（純粋ロジック）。
/// 総支給（額面）は記録専用で残高計算・統計には一切影響しない。想定年収は
/// 「総支給を入力済みの月の平均×12＋賞与想定」で算出し、入力が増えるほど精度が上がる。
/// </summary>
public static class IncomeMath
{
    public record MonthIncome(int Month, decimal GrossSalary, decimal NetSalary, bool IsBonusMonth, decimal GrossBonus, decimal NetBonus);

    public record YearSummary(
        decimal ProjectedGross,       // 想定年収（額面・見込み）＝ SalaryEstimate + BonusEstimate
        decimal ActualGrossYtd,       // 年計（実績）＝ 入力済み月の総支給（給料+賞与）合計
        decimal MonthlyAvgGross,      // 月平均（給料の総支給のみ）
        int FilledSalaryMonths,       // 総支給（給料）入力済み月数
        decimal SalaryEstimate,       // 給料（想定・月平均×12）
        decimal BonusEstimate,        // 賞与（想定・実績から）
        decimal ProjectedNet,         // 手取りベース想定
        decimal DeductionGap,         // 控除見込み差（額面－手取り）
        int DeductionPct);            // 控除見込み差の割合（%・四捨五入）

    /// <summary>月ごとの入力（1年分）から年収サマリーを算出する。bonusMonthsPerYear は賞与の年内回数（設定のボーナス月数）。</summary>
    public static YearSummary Summarize(IReadOnlyList<MonthIncome> months, int bonusMonthsPerYear)
    {
        var filledSalary = months.Where(m => m.GrossSalary > 0).ToList();
        var filledBonus = months.Where(m => m.IsBonusMonth && m.GrossBonus > 0).ToList();

        decimal avgGrossSalary = filledSalary.Count > 0 ? filledSalary.Sum(m => m.GrossSalary) / filledSalary.Count : 0;
        decimal avgNetSalary = filledSalary.Count > 0 ? filledSalary.Sum(m => m.NetSalary) / filledSalary.Count : 0;
        decimal avgGrossBonus = filledBonus.Count > 0 ? filledBonus.Sum(m => m.GrossBonus) / filledBonus.Count : 0;
        decimal avgNetBonus = filledBonus.Count > 0 ? filledBonus.Sum(m => m.NetBonus) / filledBonus.Count : 0;

        decimal salaryEstimate = Math.Round(avgGrossSalary * 12);
        decimal bonusEstimate = Math.Round(avgGrossBonus * bonusMonthsPerYear);
        decimal projectedGross = salaryEstimate + bonusEstimate;

        decimal netSalaryEstimate = Math.Round(avgNetSalary * 12);
        decimal netBonusEstimate = Math.Round(avgNetBonus * bonusMonthsPerYear);
        decimal projectedNet = netSalaryEstimate + netBonusEstimate;

        decimal actualGrossYtd = months.Sum(m => m.GrossSalary + (m.IsBonusMonth ? m.GrossBonus : 0));
        decimal deductionGap = projectedGross - projectedNet;
        int deductionPct = projectedGross == 0 ? 0 : (int)Math.Round(deductionGap / projectedGross * 100);

        return new YearSummary(
            projectedGross,
            actualGrossYtd,
            Math.Round(avgGrossSalary),
            filledSalary.Count,
            salaryEstimate,
            bonusEstimate,
            projectedNet,
            deductionGap,
            deductionPct);
    }

    // 「この月の確認」パネル（額面ベースの前月比・前回賞与比。null=比較対象なし=非表示）。
    public record MonthCheck(
        decimal TotalGross, decimal TotalNet, decimal Deduction, int DeductionPct,
        decimal? SalaryMomDiff, decimal? SalaryMomPct,
        decimal? BonusVsPrevDiff, decimal? BonusVsPrevPct);

    /// <summary>
    /// 選択月の集計＋前月比／前回賞与比を算出する。年をまたいだ比較のため、対象年の12ヶ月分だけでなく
    /// 全期間の総支給記録（ym→IncomeMonthRecord）を渡す。手取り(netSalary/netBonus)は呼び出し側で
    /// 解決済みの値（Ledger優先・無ければ収入記録の独立値）を渡す。
    /// </summary>
    public static MonthCheck BuildMonthCheck(
        IReadOnlyDictionary<string, IncomeMonthRecord> records, string selectedYm,
        bool isBonusMonth, decimal netSalary, decimal netBonus)
    {
        var sel = records.GetValueOrDefault(selectedYm);
        decimal grossSalary = sel?.SalaryGross ?? 0;
        decimal grossBonus = isBonusMonth ? (sel?.BonusGross ?? 0) : 0;
        decimal totalGross = grossSalary + grossBonus;
        decimal totalNet = netSalary + (isBonusMonth ? netBonus : 0);
        decimal deduction = totalGross - totalNet;
        int deductionPct = totalGross == 0 ? 0 : (int)Math.Round(deduction / totalGross * 100);

        decimal? momDiff = null, momPct = null;
        var prevYm = Ym.Parse(selectedYm).Prev().ToString();
        var prevSalary = records.GetValueOrDefault(prevYm)?.SalaryGross ?? 0;
        if (prevSalary > 0)
        {
            momDiff = grossSalary - prevSalary;
            momPct = Math.Round(momDiff.Value / prevSalary * 100, 1);
        }

        decimal? bonusDiff = null, bonusPct = null;
        if (isBonusMonth)
        {
            var refGross = records
                .Where(kv => string.CompareOrdinal(kv.Key, selectedYm) < 0 && kv.Value.BonusGross > 0)
                .OrderByDescending(kv => kv.Key)
                .Select(kv => (decimal?)kv.Value.BonusGross)
                .FirstOrDefault();
            if (refGross is > 0)
            {
                bonusDiff = grossBonus - refGross.Value;
                bonusPct = Math.Round(bonusDiff.Value / refGross.Value * 100, 1);
            }
        }

        return new MonthCheck(totalGross, totalNet, deduction, deductionPct, momDiff, momPct, bonusDiff, bonusPct);
    }
}
