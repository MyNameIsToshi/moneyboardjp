namespace MoneyBoardShared;

/// <summary>
/// 読み込んだ AppState を現行スキーマへ段階的に移行する。
/// スキーマを変更する際（例: #4 のドキュメント分割や Debit へのカテゴリ追加）は
/// CurrentVersion を上げ、Apply に移行ステップを追加する。
/// </summary>
public static class SchemaMigration
{
    // v2: Phase 2（カテゴリ／カード／カード明細）を追加。いずれも加算的な
    // フィールド追加のため、旧データは欠損フィールドが空で読み込まれるだけで移行処理は不要。
    public const int CurrentVersion = 2;

    /// <summary>最新スキーマへ移行する。実際に変更が発生した場合のみ true を返す（=保存が必要）。</summary>
    public static bool Apply(AppState state)
    {
        var from = state.SchemaVersion;

        // 将来のバージョンアップはここに昇順で追加していく:
        // if (state.SchemaVersion < 2) { MigrateV1ToV2(state); state.SchemaVersion = 2; }

        state.SchemaVersion = CurrentVersion;
        return from != CurrentVersion;
    }
}
