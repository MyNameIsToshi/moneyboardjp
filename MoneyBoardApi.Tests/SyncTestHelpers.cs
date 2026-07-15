using System.Reflection;

namespace MoneyBoardApi.Tests;

// 契約層の同期漏れガード（SettingsSyncTests / MonthSyncTests）共通のプロパティ集合ヘルパ。
internal static class SyncTestHelpers
{
    // 名前だけでなく型(PropertyType)も含めて比較する。名前は一致するが型が食い違うドリフト
    // （例：片方だけ List<int>→HashSet<int>）は ObjectSync.CopyMatchingProperties の SetValue が
    // 実行時例外になり本番の読み書きが落ちるため、ビルド時（テスト）で止める。
    // ObjectSync は「読み書き可能な public プロパティ」だけをコピー対象にするため、比較集合も
    // CanRead && CanWrite に限定する（永続化されない get-only の計算プロパティを足しても擬似失敗しない）。
    public static (string Name, Type Type)[] PropSignatures(Type t, params string[] exclude) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && !exclude.Contains(p.Name))
            .Select(p => (p.Name, p.PropertyType))
            .OrderBy(x => x.Name)
            .ToArray();
}
