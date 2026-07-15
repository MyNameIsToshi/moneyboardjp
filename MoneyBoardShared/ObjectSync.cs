using System.Reflection;

namespace MoneyBoardShared;

/// <summary>
/// 名前が一致する public プロパティを source → target へコピーする。
/// AppState ⇄ SettingsPart（通信DTO）⇄ SettingsDoc（Cosmos保存用）のように、同じ論理フィールドを
/// 複数の型で表現している契約層は、フィールド追加のたびに手書きの object initializer を
/// 複数箇所で更新する必要があり、更新漏れ（#134で2度発生：フィールド追加時に一部の型だけ更新し忘れる。#136）
/// の温床になっていた。この共通コピーを使えば、対応する型すべてに同名プロパティを追加するだけで
/// マッピングコード自体は変更不要になる。
/// 月次データ（MonthData ⇄ MonthPart ⇄ MonthDoc/MonthReadDoc）も同型構造のため #137 で横展開した。
/// 型が一致しないプロパティはあえて握りつぶさず例外にする（早期に気付けるように。加えて SettingsSyncTests /
/// MonthSyncTests が名前だけでなく型の不一致もビルド時に検知する）。source 側が null のプロパティは target を上書きしない
/// （target の既定値＝new() 等を温存し、旧 StorageService.LoadAsync の `?? new()` 相当の null 防御を一般化して保持する）。
/// </summary>
public static class ObjectSync
{
    public static void CopyMatchingProperties(object source, object target)
    {
        var sourceProps = source.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .ToDictionary(p => p.Name);

        foreach (var targetProp in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!targetProp.CanWrite) continue;
            if (!sourceProps.TryGetValue(targetProp.Name, out var sourceProp)) continue;

            var value = sourceProp.GetValue(source);
            // source 側が null のプロパティは target を上書きしない（target の既定値を温存）。
            // 旧 LoadAsync の `env.Settings?.Xxx ?? new()` 相当の null 防御を ObjectSync 側で一般化して保持する。
            if (value != null) targetProp.SetValue(target, value);
        }
    }
}
