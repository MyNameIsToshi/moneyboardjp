using System.Reflection;

namespace MoneyBoardShared;

/// <summary>
/// 永続契約（<see cref="SettingsPart"/>/<see cref="MonthPart"/> と、それらが参照する型）の
/// 公開プロパティ集合を決定的な文字列へ直列化する。PersistedSchemaGuardTests が
/// バージョンごとに凍結したシグネチャとの差分検出に使う。
/// 「加算的なフィールド追加でも必ず SchemaVersion を上げる」運用（#155 v13以降）を、
/// コメント頼みではなくテストで強制するための土台。
/// </summary>
public static class SchemaSignature
{
    public static string Of(Type type) => Build(type, new HashSet<Type>());

    private static string Build(Type type, HashSet<Type> visiting)
    {
        // MoneyBoardShared のドメイン型は互いに循環参照しないが、将来の変更に対する保険。
        if (!visiting.Add(type)) return "...";
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(p => p.Name, StringComparer.Ordinal);
        var parts = props.Select(p => $"{p.Name}:{Describe(p.PropertyType, visiting)}");
        var signature = $"{{{string.Join(",", parts)}}}";
        visiting.Remove(type);
        return signature;
    }

    private static string Describe(Type t, HashSet<Type> visiting)
    {
        if (Nullable.GetUnderlyingType(t) is { } inner)
            return $"{Describe(inner, visiting)}?";
        if (t == typeof(string) || t.IsPrimitive || t == typeof(decimal) || t.IsEnum)
            return t.Name;
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            return $"List<{Describe(t.GetGenericArguments()[0], visiting)}>";
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var args = t.GetGenericArguments();
            return $"Dict<{Describe(args[0], visiting)},{Describe(args[1], visiting)}>";
        }
        if (t.Namespace == nameof(MoneyBoardShared))
            return Build(t, visiting);
        return t.Name; // フォールバック（想定外の型はそのまま名前だけ）
    }
}
