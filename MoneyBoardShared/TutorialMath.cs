namespace MoneyBoardShared;

/// <summary>
/// チュートリアル既読判定の純粋ロジック（#111）。既読版は AppState.TutorialSeenVersion（サーバー保存・
/// 端末をまたいで有効）で管理する。お知らせ（AnnouncementMath）と異なり localStorage ではなくユーザー
/// 単位で保持するため、判定は「現行版」定数との単純比較になる。
/// </summary>
public static class TutorialMath
{
    // v1: PWA追加方法チュートリアル（コーチマーク基盤＋モーダル図解基盤の初回PoC・#111）。
    public const int CurrentVersion = 1;

    /// <summary>既読版が現行版未満なら強制表示すべきか。</summary>
    public static bool ShouldForceShow(int seenVersion) => seenVersion < CurrentVersion;
}
