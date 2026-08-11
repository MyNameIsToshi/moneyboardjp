namespace MoneyBoardShared;

/// <summary>統計（グラフ）画面の純粋ロジック。ym 文字列や明細を引数で受け、UI・チャートには依存しない。</summary>
public static class StatsMath
{
    /// <summary>
    /// 期間選択に応じて対象 ym（昇順）を絞り込む。allYmsAsc は昇順前提。
    /// period: "all"=全期間 / "current"=当月（給料サイクル起点、currentYm 一致分のみ） /
    /// "custom"=[customStart, customEnd]（逆指定は自動入替え） / 数値文字列=直近Nヶ月（currentYm 指定時は当月まで・未来月除外）。
    /// 該当なし・範囲外は空リスト。N が件数を超える場合は全件（TakeLast がクランプ）。
    /// </summary>
    public static List<string> SelectPeriodYms(
        IReadOnlyList<string> allYmsAsc, string period, string customStart, string customEnd, string? currentYm = null)
    {
        if (period == "all") return allYmsAsc.ToList();
        // 「当月」は直近Nヶ月(TakeLast)に乗せない：未来月を先行作成済みだと TakeLast(1) が
        // 実際の給料サイクル(15日〜14日)と異なる月を拾ってしまうため、呼び出し元(LedgerService.CurrentCycleStartYm)
        // が渡す currentYm と一致する月だけをピンポイントで返す。
        if (period == "current")
            return currentYm != null && allYmsAsc.Contains(currentYm) ? new List<string> { currentYm } : new List<string>();
        if (period == "custom")
        {
            var (s, e) = (customStart, customEnd);
            if (string.CompareOrdinal(s, e) > 0) (s, e) = (e, s);   // 逆指定の保険
            return allYmsAsc.Where(ym => string.CompareOrdinal(ym, s) >= 0 && string.CompareOrdinal(ym, e) <= 0).ToList();
        }
        int count = int.Parse(period);
        // 直近Nヶ月も「当月まで」に限定する(#119)：固定費の先行展開等で未来月が
        // 既に存在していても、統計のデフォルト表示には含めない。currentYm 未指定時は従来どおり全件対象。
        var eligible = currentYm != null
            ? allYmsAsc.Where(ym => string.CompareOrdinal(ym, currentYm) <= 0).ToList()
            : allYmsAsc;
        return eligible.TakeLast(count).ToList();
    }

    /// <summary>
    /// カテゴリ別集計のグルーピングキーを正規化する（#117）。categoryId が空、または
    /// knownCategoryIds に存在しない（削除済み等で参照が切れている）場合は空文字列に統一し、
    /// 未設定・参照切れの両方を同じ「未分類」1グループに集約できるようにする。
    /// </summary>
    public static string NormalizeCategoryKey(string? categoryId, IReadOnlyCollection<string> knownCategoryIds) =>
        !string.IsNullOrEmpty(categoryId) && knownCategoryIds.Contains(categoryId) ? categoryId : "";

    /// <summary>
    /// CardDetail.Date（"yyyy-MM-dd"）から利用月キー（"yyyyMM"）を取り出す（#105）。
    /// 請求月（月次ドキュメント所属）ではなく実際にカードを使った月で集計する軸のために使う。
    /// 形式が不正・空なら null（呼び出し側で対象外として扱う）。
    /// </summary>
    public static string? UsageYmOf(string date) =>
        date.Length >= 10 && date[4] == '-' && date[7] == '-'
            ? date[..4] + date[5..7]
            : null;

    /// <summary>
    /// 現金・電子マネーの手入力支出（Debit）は実日付を持たず所属月 ym（"yyyyMM"）しか無いため、
    /// 明細ドリルダウン（DetailDialog）の表示・日付順ソート用に「その月の1日」を "yyyy-MM-01" 形式
    /// で合成する（#171）。カード明細（実日付あり）は対象外＝この関数を通さずそのまま渡す。
    /// ym が "yyyyMM" 形式（6桁数字）でなければ、合成できないためそのまま返す。
    /// </summary>
    public static string MonthStartDate(string ym) =>
        ym is { Length: 6 } && int.TryParse(ym[..4], out var y) && int.TryParse(ym[4..6], out var m) && m is >= 1 and <= 12
            ? $"{y:D4}-{m:D2}-01"
            : ym;

    /// <summary>
    /// 月別推移（1 ym バケット分）のキー別金額に、追加分の金額をキーごとに合算する（#171）。
    /// カテゴリ別月別推移で、カード明細の金額分布（base）に現金・電子マネー支出の金額分布（extra）を
    /// 加算するために使う（カード別推移は extra を渡さない呼び出し元のため対象外のまま）。
    /// base に無いキーは extra の値がそのまま新規キーとして追加される。
    /// </summary>
    public static Dictionary<string, decimal> MergeAmounts(
        IReadOnlyDictionary<string, decimal> baseAmounts, IReadOnlyDictionary<string, decimal> extra)
    {
        var result = new Dictionary<string, decimal>(baseAmounts);
        foreach (var (key, amount) in extra)
            result[key] = result.GetValueOrDefault(key, 0) + amount;
        return result;
    }

    /// <summary>
    /// 口座別の系列を口座の並び順のまま組み立てる（#157）。値は一意な Id で解決するため、
    /// 同名の口座が複数あっても破綻しない（口座名をキーにした Dictionary では重複キー例外に
    /// なっていた）。戻り値を Dictionary ではなく List にしているのは、系列の並び順が色パレットの
    /// 割当順（ActiveAccounts 順）と対応する必要があるため（Dictionary の列挙順は仕様上保証されない）。
    /// 表示名は結果の中で必ず一意になる：既出の名前に当たったら " (2)", " (3)" … と、未使用の名前に
    /// 行き着くまで連番を進める（実機確認で発覚：ApexCharts の凡例ホバーは系列を名前で解決するため、
    /// 同名のままだと常に1件目がハイライトされる）。連番の付いた名前が別口座の実名（"楽天 (2)" 等）と
    /// 衝突する場合も次の番号へ送るため、一意性は入力の口座名によらず保たれる。
    /// 名前が重複していない口座は影響を受けない。
    /// </summary>
    public static List<(string Name, TValue Data)> BuildAccountSeries<TValue>(
        IEnumerable<(string Id, string Name)> accounts, Func<string, TValue> valueOf)
    {
        var list = accounts.ToList();
        // 実在する口座名の集合。連番で作った名前がこれを奪わないようにする。
        var realNames = list.Select(a => a.Name).ToHashSet();
        var used = new HashSet<string>();
        var series = new List<(string Name, TValue Data)>(list.Count);

        foreach (var a in list)
        {
            var displayName = a.Name;
            // HashSet.Add は「未使用だった」ときだけ true。実名は常にそのまま採用される。
            if (!used.Add(displayName))
            {
                // 既出の名前。未使用かつ他の口座の実名でもない連番に行き着くまで番号を送る。
                var n = 2;
                do { displayName = $"{a.Name} ({n++})"; }
                while (used.Contains(displayName) || realNames.Contains(displayName));
                used.Add(displayName);
            }
            series.Add((displayName, valueOf(a.Id)));
        }
        return series;
    }
}
