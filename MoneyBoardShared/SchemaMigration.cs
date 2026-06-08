namespace MoneyBoardShared;

/// <summary>
/// 読み込んだ AppState を現行スキーマへ段階的に移行する。
/// スキーマを変更する際（例: #4 のドキュメント分割や Debit へのカテゴリ追加）は
/// CurrentVersion を上げ、Apply に移行ステップを追加する。
/// </summary>
public static class SchemaMigration
{
    public const int CurrentVersion = 1;

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
