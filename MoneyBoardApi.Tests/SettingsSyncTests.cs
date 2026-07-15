using System.Reflection;
using MoneyBoardShared;
using Xunit;

namespace MoneyBoardApi.Tests;

// AppState（設定部分）⇄ SettingsPart（通信DTO）⇄ SettingsDoc（Cosmos保存用ドキュメント）は、
// フィールド追加のたびに対応する型へ同名プロパティを足す必要がある構造。この対応漏れが
// #134 で2度発生したため、プロパティ名と型の集合が一致することをビルド時に検知するガードとして追加する（#136）。
// マッピング処理自体は ObjectSync.CopyMatchingProperties に一本化済み（更新箇所は型定義だけで済む）。
public class SettingsSyncTests
{
    // 名前だけでなく型(PropertyType)も含めて比較する。名前は一致するが型が食い違うドリフト
    // （例：片方だけ List<int>→HashSet<int>）は ObjectSync.CopyMatchingProperties の SetValue が
    // 実行時例外になり本番の読み書きが落ちるため、ビルド時（テスト）で止める。
    private static (string Name, Type Type)[] PropSignatures(Type t, params string[] exclude) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !exclude.Contains(p.Name))
            .Select(p => (p.Name, p.PropertyType))
            .OrderBy(x => x.Name)
            .ToArray();

    [Fact]
    public void AppState_SettingsScopeFields_MatchSettingsPart()
    {
        // Months は別ドキュメント（月次）のため対象外。
        var appStateProps = PropSignatures(typeof(AppState), nameof(AppState.Months));
        // Etag は SettingsPart 固有（Cosmos の楽観的並行制御用）のため対象外。
        var settingsPartProps = PropSignatures(typeof(SettingsPart), nameof(SettingsPart.Etag));

        Assert.Equal(appStateProps, settingsPartProps);
    }

    [Fact]
    public void SettingsPart_Fields_MatchSettingsDoc()
    {
        var settingsPartProps = PropSignatures(typeof(SettingsPart), nameof(SettingsPart.Etag));
        // Id/UserId/Type は SettingsDoc 固有（Cosmos ドキュメントのキー・種別）のため対象外。
        var settingsDocProps = PropSignatures(typeof(SettingsDoc),
            nameof(SettingsDoc.Id), nameof(SettingsDoc.UserId), nameof(SettingsDoc.Type));

        Assert.Equal(settingsPartProps, settingsDocProps);
    }
}
