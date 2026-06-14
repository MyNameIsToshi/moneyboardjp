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
    public const int CurrentVersion = 3;

    /// <summary>最新スキーマへ移行する。実際に変更が発生した場合のみ true を返す（=保存が必要）。</summary>
    public static bool Apply(AppState state)
    {
        var from = state.SchemaVersion;

        // 将来のバージョンアップはここに昇順で追加していく:
        // if (state.SchemaVersion < 4) { MigrateV3ToV4(state); state.SchemaVersion = 4; }

        state.SchemaVersion = CurrentVersion;
        return from != CurrentVersion;
    }
}
