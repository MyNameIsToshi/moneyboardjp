namespace MoneyBoardShared;

/// <summary>
/// 読み込んだ AppState を現行スキーマへ段階的に移行する。
/// スキーマを変更する際（例: #4 のドキュメント分割や Debit へのカテゴリ追加）は
/// CurrentVersion を上げ、Apply に移行ステップを追加する。
/// </summary>
public static class SchemaMigration
{
    // v2: Phase 2（カテゴリ／カード／カード明細）を追加。加算的なフィールド追加のみで移行不要。
    // v3: 月初残高を「作成時スナップショット」から「前月末からの自動連鎖」へ変更。
    //     非起点月の Confirmed は参照されなくなるだけ（フィールド削除）で、構造的な移行処理は不要。
    // v4: CategoryRules のキーを NormalizeStore（全角半角/空白正規化）済みに統一（#27）。
    //     OCR・CSV発行元差の表記ゆれで同一店名が別キーに分裂していた既存データを統合する。
    // v5: CategoryPrefixRules（前方一致カテゴリルール）を追加（#70）。加算的なフィールド追加のみで移行不要。
    public const int CurrentVersion = 5;

    /// <summary>最新スキーマへ移行する。実際に変更が発生した場合のみ true を返す（=保存が必要）。</summary>
    public static bool Apply(AppState state)
    {
        var from = state.SchemaVersion;

        if (state.SchemaVersion < 4) NormalizeCategoryRuleKeys(state);

        state.SchemaVersion = CurrentVersion;
        return from != CurrentVersion;
    }

    // 表記ゆれ（全角/半角・空白）で分裂した CategoryRules を正規化キーへ統合する。
    // 同一正規化キーに複数の店名が存在した場合は後勝ち（辞書の列挙順＝概ね挿入順）。
    private static void NormalizeCategoryRuleKeys(AppState state)
    {
        var merged = new Dictionary<string, string>();
        foreach (var (store, categoryId) in state.CategoryRules)
        {
            var key = LedgerEngine.NormalizeStore(store);
            if (key.Length == 0) continue;
            merged[key] = categoryId;
        }
        state.CategoryRules = merged;
    }
}
