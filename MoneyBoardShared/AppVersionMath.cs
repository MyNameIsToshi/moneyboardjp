namespace MoneyBoardShared;

/// <summary>
/// アプリのバージョン表示・更新検知（#86）に関する純粋ロジック。
/// SWのライフサイクルイベント（installed/waiting/controllerchange）はiOSのスタンドアロンPWAで
/// 実質発火しないことが多いため、それらに依存せず「起動時にlocalStorageへ保存した前回バージョンと
/// 現在のAppVersionを比較する」方式（事後報告）を採用した。
/// </summary>
public static class AppVersionMath
{
    /// <summary>
    /// csproj の &lt;Version&gt;（AssemblyInformationalVersionAttribute）から "vX.Y.Z" 表示用文字列を作る。
    /// SourceLink 併用時に付与されるビルドメタ（'+'以降）は落とす。未設定なら空文字。
    /// </summary>
    public static string FormatDisplayVersion(string? informationalVersion) =>
        string.IsNullOrEmpty(informationalVersion) ? "" : "v" + informationalVersion.Split('+')[0];

    /// <summary>
    /// 前回記録したバージョン（lastSeenVersion）と現在のバージョンが異なるか＝更新通知を出すべきか。
    /// 初回起動（lastSeenVersion が空＝未記録）は「更新された」わけではないため通知しない。
    /// </summary>
    public static bool ShouldNotifyUpdate(string? lastSeenVersion, string currentVersion) =>
        !string.IsNullOrEmpty(lastSeenVersion) && lastSeenVersion != currentVersion;

    /// <summary>
    /// pwa.js の「更新する」クリック時刻（setAt）が、事後通知を抑止してよい時間枠（window）内か。
    /// クリック直後の自タブreloadでの二重通知だけを抑止する目的のため、reloadが完了しなかった
    /// （タブを閉じた・iOSでcontrollerchangeが発火しない等）場合にフラグが残り続けて無関係な
    /// 将来の更新まで誤って抑止してしまわないよう、時間枠を超えたら false（抑止しない）とする。
    /// </summary>
    public static bool IsWithinManualReloadWindow(DateTimeOffset setAt, DateTimeOffset now, TimeSpan window)
    {
        var elapsed = now - setAt;
        return elapsed >= TimeSpan.Zero && elapsed < window;
    }
}
