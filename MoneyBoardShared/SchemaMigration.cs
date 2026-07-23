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
    // v6: FixedCost.IsVariable（変動費フラグ）・Debit.IsVariable/AmountOverridden を追加（#87）。
    //     加算的なフィールド追加のみで移行不要。
    // v7: 財布（現金）機能（#77）。Account.IsWallet・Ledger.WalletAtmDeposits・Debit.CategoryId を追加。
    //     いずれも安全な default（false / 空リスト / null）を持つ加算的なフィールド追加のみで移行不要。
    // v8: 収入の固定費（#95）。AppState.FixedIncomes・IncomeItem.IsFixed/IsVariable/AmountOverridden/
    //     FixedIncomeId を追加。いずれも安全な default（空リスト / false / null）を持つ加算的なフィールド
    //     追加のみで移行不要。
    // v9: チュートリアル既読管理（#111）。AppState.TutorialSeenVersion を追加。安全な default（0）を持つ
    //     加算的なフィールド追加のみで移行不要。
    // v10: ボーナス月設定（#134）。AppState.BonusMonths を追加。安全な default（{6,12}）を持つ
    //      加算的なフィールド追加のみで移行不要。
    // v11: 口座種別の enum 化（#147）。Account.IsWallet（bool）を廃止し Account.Type（AccountType）へ
    //      一本化。既存の IsWallet==true を Type=Wallet へ変換する（IsWallet は移行専用の後方互換受け口
    //      として型上は残るが、移行後はアプリロジックから参照されない）。
    // v12: 電子マネー口座（#148）。財布専用だった起点月 Account.WalletStartYm を Account.StartYm へ
    //      一般化し、電子マネーにも適用する。既存の WalletStartYm を StartYm へコピーする（加算的で
    //      安全だが、値そのものを引き継ぐ必要があるため通常の「加算のみ」より一段階移行処理が要る）。
    //      WalletStartYm は移行専用の後方互換受け口として型上は残るが、移行後はアプリロジックから
    //      参照されない（IsWallet と同じパターン）。
    // v13以降の運用変更（#155）: SaveData の版数フロア（保存済みdocより低い版数の保存を拒否）だけで
    //      未知フィールド欠落を防ぐため、v13以降はこれまでと異なり「加算的なフィールド追加でも必ず
    //      CurrentVersion を上げる」。旧クライアントは版数フロアで保存自体を拒否されるため、
    //      JSONレベルのフィールドマージを実装せずに済む（詳細はdocs/ARCHITECTURE.md「スキーマ移行」節）。
    public const int CurrentVersion = 12;

    /// <summary>
    /// Account.Type（口座種別）が導入された版数（#147）。これ未満のクライアントが書いたデータは
    /// Type を持たないため、旧 IsWallet から種別を復元してよい（<see cref="RestoreWalletTypeFromLegacyFlag"/>）。
    /// </summary>
    public const int AccountTypeVersion = 11;

    /// <summary>最新スキーマへ移行する。実際に変更が発生した場合のみ true を返す（=保存が必要）。</summary>
    public static bool Apply(AppState state)
    {
        var from = state.SchemaVersion;
        // 未来の版数（自分より新しいクライアントが書いたデータ）を旧クライアントが開いた場合、
        // 版数を巻き戻して保存すると新フィールドが欠落する（#154）。何もせず変更なしを返す。
        if (from > CurrentVersion) return false;

        if (from < 4) NormalizeCategoryRuleKeys(state);
        if (from < AccountTypeVersion) RestoreWalletTypeFromLegacyFlag(state.Accounts);
        if (from < 12) MigrateStartYm(state.Accounts);

        state.SchemaVersion = CurrentVersion;
        return from < CurrentVersion;
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

    /// <summary>
    /// 旧 <c>Account.IsWallet==true</c> を <c>Type=AccountType.Wallet</c> へ復元する（#147）。
    /// クライアント（移行）とサーバー（保存時の正規化・#154）の双方から使う共通規則。
    /// </summary>
    /// <remarks>
    /// <para>Type が既に設定済みの口座は上書きしない：旧フラグはクリアせず残すため、種別を Wallet 以外へ
    /// 変えた口座に再適用されると、その変更を巻き戻してしまう。</para>
    /// <para><b>呼び出し側の責務</b>：<c>Type == Normal</c> は「通常口座」と「Type 未送信（v11 未満）」を
    /// 区別できないため、<b>データ元が <see cref="AccountTypeVersion"/> 未満のときだけ呼ぶこと</b>。
    /// v11 以降のデータに適用すると、Wallet→通常口座へ変更した口座を Wallet へ巻き戻す（#154）。</para>
    /// </remarks>
    public static void RestoreWalletTypeFromLegacyFlag(List<Account> accounts)
    {
        foreach (var a in accounts)
        {
            if (a.IsWallet && a.Type == AccountType.Normal) a.Type = AccountType.Wallet;
        }
    }

    /// <summary>
    /// 財布専用だった起点月 <c>WalletStartYm</c> を汎用の <c>StartYm</c> へコピーする（#148）。
    /// 既に <c>StartYm</c> が設定済みの口座は上書きしない（null のときだけ埋める）。
    /// </summary>
    public static void MigrateStartYm(List<Account> accounts)
    {
        foreach (var a in accounts)
        {
            a.StartYm ??= a.WalletStartYm;
        }
    }
}
