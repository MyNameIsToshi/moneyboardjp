using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace MoneyBoardShared;

/// <summary>保存競合（412）でサーバーの最新内容を読み込み直す際に破棄される、ローカルの未保存編集の
/// 要約（#158）。「対象の月／設定」と「件数」だけを最低限持つ（内容そのものの復元・マージは対象外）。</summary>
public class DiscardedChanges
{
    /// <summary>設定（口座・固定費・カテゴリ等）の変更項目数。設定に変更が無ければ null。</summary>
    public int? SettingsItemCount { get; set; }
    public List<DiscardedMonth> Months { get; set; } = new();

    public bool IsEmpty => (SettingsItemCount is null or 0) && Months.Count == 0;
}

public record DiscardedMonth(string Ym, int ItemCount);

/// <summary>保存直前のローカル編集（<see cref="SettingsPart"/>/<see cref="MonthPart"/>）と、直前まで
/// サーバーと一致していたベースラインを比較し、破棄される項目数を数える。
/// <see cref="ObjectSync"/> と同様、List/Dictionary の公開プロパティを反射で列挙するため、
/// 両パートにフィールドが追加されても本クラス側の更新は不要。</summary>
public static class ConflictDiff
{
    public static DiscardedChanges Extract(
        SettingsPart? baselineSettings, SettingsPart? discardedSettings,
        IReadOnlyDictionary<string, MonthPart> baselineMonths, IReadOnlyDictionary<string, MonthPart> discardedMonths)
    {
        var result = new DiscardedChanges();

        if (discardedSettings != null)
        {
            var count = CountItemDiff(baselineSettings, discardedSettings);
            if (count > 0) result.SettingsItemCount = count;
        }

        foreach (var (ym, after) in discardedMonths)
        {
            baselineMonths.TryGetValue(ym, out var before);
            var count = CountItemDiff(before, after);
            if (count > 0) result.Months.Add(new DiscardedMonth(ym, count));
        }
        result.Months.Sort((a, b) => string.CompareOrdinal(a.Ym, b.Ym));

        return result;
    }

    /// <summary>単一ドキュメント（<see cref="PortfolioData"/> 等、設定/月次のような分割が無い型）向け。
    /// T の公開プロパティのうち List<>/Dictionary<,> をすべて比較し、増減・変更した件数を合算する。
    /// Etag/SchemaVersion のようなスカラー値は対象外（コレクションではないため CountCollectionDiff が 0 を返す）。
    /// <paramref name="ignoreProps"/> にプロパティ名を渡すとそのコレクションは件数から除外する（例: 価格更新で
    /// 自動再取得される派生データはユーザー編集ではないため #158 の破棄件数に含めない）。</summary>
    public static int CountItemDiff<T>(T? before, T after, IReadOnlySet<string>? ignoreProps = null) where T : class
    {
        var total = 0;
        foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (ignoreProps != null && ignoreProps.Contains(prop.Name)) continue;
            var beforeValue = before == null ? null : prop.GetValue(before);
            var afterValue = prop.GetValue(after);
            total += CountCollectionDiff(beforeValue, afterValue);
        }
        return total;
    }

    private static int CountCollectionDiff(object? before, object? after)
    {
        if (after is IDictionary afterDict) return CountDictDiff(before as IDictionary, afterDict);
        if (after is IList afterList) return CountListDiff(before as IList, afterList);
        return 0;
    }

    /// <summary>辞書の値が <see cref="Ledger"/> のように List/Dictionary を内包する複合型かどうか。
    /// 複合型ならキー単位ではなく明細単位（<see cref="CountCompoundValueDiff"/>）で数える。
    /// <see cref="MonthData.CardBilled"/> のような値がスカラー（decimal 等）の辞書は対象外
    /// （従来どおりキー単位のまま。過剰な再帰をしない）。</summary>
    private static bool IsCompoundType(Type t) =>
        t.IsClass && t != typeof(string) &&
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => typeof(IDictionary).IsAssignableFrom(p.PropertyType) || typeof(IList).IsAssignableFrom(p.PropertyType));

    private static int CountDictDiff(IDictionary? before, IDictionary after)
    {
        var valueType = after.GetType().IsGenericType ? after.GetType().GetGenericArguments()[1] : null;
        var isCompound = valueType != null && IsCompoundType(valueType);

        var beforeByKey = new Dictionary<string, object?>();
        if (before != null)
            foreach (DictionaryEntry entry in before)
                beforeByKey[entry.Key.ToString()!] = entry.Value;

        var count = 0;
        var afterKeys = new HashSet<string>();
        foreach (DictionaryEntry entry in after)
        {
            var key = entry.Key.ToString()!;
            afterKeys.Add(key);
            var hasBefore = beforeByKey.TryGetValue(key, out var beforeValue);

            if (isCompound)
            {
                count += CountCompoundValueDiff(hasBefore ? beforeValue : null, entry.Value!, valueType!);
            }
            else
            {
                var afterJson = JsonSerializer.Serialize(entry.Value);
                if (!hasBefore || JsonSerializer.Serialize(beforeValue) != afterJson) count++;
            }
        }

        var removedKeys = beforeByKey.Keys.Where(k => !afterKeys.Contains(k)).ToList();
        if (isCompound)
            foreach (var key in removedKeys)
                count += CountCompoundValueDiff(beforeByKey[key], Activator.CreateInstance(valueType!)!, valueType!);
        else
            count += removedKeys.Count;   // 削除されたキー

        return count;
    }

    /// <summary><see cref="Ledger"/> のような複合型の辞書値を、内包する List/Dictionary は明細単位で、
    /// それ以外のスカラープロパティ（<see cref="Ledger.Salary"/> 等）はまとめて「台帳あたり1件」で数える
    /// （方針は docs/ARCHITECTURE.md「保存の信頼性」参照）。</summary>
    private static int CountCompoundValueDiff(object? before, object after, Type type)
    {
        // 新規キー追加（before==null）は削除（after=既定インスタンス）と対称にするため、
        // 既定インスタンスで比較する。これをしないとスカラーが全て既定値の新規台帳でも
        // null≠既定値でスカラー変更と誤判定し、削除より1件多く数えてしまう。
        before ??= Activator.CreateInstance(type);

        var total = 0;
        var scalarChanged = false;
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var beforeValue = prop.GetValue(before);
            var afterValue = prop.GetValue(after);
            if (afterValue is IDictionary or IList)
            {
                total += CountCollectionDiff(beforeValue, afterValue);
            }
            else if (!scalarChanged && JsonSerializer.Serialize(beforeValue) != JsonSerializer.Serialize(afterValue))
            {
                scalarChanged = true;
            }
        }
        return total + (scalarChanged ? 1 : 0);
    }

    private static int CountListDiff(IList? before, IList after)
    {
        var elementType = after.GetType().IsGenericType ? after.GetType().GetGenericArguments()[0] : null;
        var idProp = elementType?.GetProperty("Id");

        // 要素に Id があれば Id 単位（追加/削除/内容変更）で数える。無ければ（例: List<int>）
        // 順不同の多重集合として対称差を数える簡易フォールバック。
        return idProp != null ? CountListDiffById(before, after, idProp) : CountListDiffAsMultiset(before, after);
    }

    private static int CountListDiffById(IList? before, IList after, PropertyInfo idProp)
    {
        var beforeJsonById = new Dictionary<string, string>();
        if (before != null)
            foreach (var item in before)
            {
                var id = idProp.GetValue(item)?.ToString();
                if (id != null) beforeJsonById[id] = JsonSerializer.Serialize(item);
            }

        var count = 0;
        var afterIds = new HashSet<string>();
        foreach (var item in after)
        {
            var id = idProp.GetValue(item)?.ToString();
            if (id == null) continue;
            afterIds.Add(id);
            var afterJson = JsonSerializer.Serialize(item);
            if (!beforeJsonById.TryGetValue(id, out var beforeJson) || beforeJson != afterJson) count++;
        }
        count += beforeJsonById.Keys.Count(id => !afterIds.Contains(id));   // 削除された要素
        return count;
    }

    private static int CountListDiffAsMultiset(IList? before, IList after)
    {
        var beforeCounts = new Dictionary<string, int>();
        if (before != null)
            foreach (var item in before)
            {
                var json = JsonSerializer.Serialize(item);
                beforeCounts[json] = beforeCounts.GetValueOrDefault(json) + 1;
            }
        var afterCounts = new Dictionary<string, int>();
        foreach (var item in after)
        {
            var json = JsonSerializer.Serialize(item);
            afterCounts[json] = afterCounts.GetValueOrDefault(json) + 1;
        }

        var diff = 0;
        foreach (var key in beforeCounts.Keys.Union(afterCounts.Keys))
            diff += Math.Abs(afterCounts.GetValueOrDefault(key) - beforeCounts.GetValueOrDefault(key));
        return diff;
    }
}
