using MoneyBoardShared;
using Xunit;

namespace MoneyBoardApi.Tests;

// MonthData（フロント状態）⇄ MonthPart（通信DTO）⇄ MonthDoc/MonthReadDoc（Cosmos保存用）は、
// フィールド追加のたびに対応する型へ同名プロパティを足す必要がある構造。SettingsSyncTests（#136）と
// 同型の対応漏れ事故（#134）を防ぐため、プロパティ名と型の集合が一致することをビルド時に検知するガードとして追加する（#137）。
// マッピング処理自体は ObjectSync.CopyMatchingProperties に一本化済み（更新箇所は型定義だけで済む）。
public class MonthSyncTests
{
    [Fact]
    public void MonthData_Fields_MatchMonthPart()
    {
        var monthDataProps = SyncTestHelpers.PropSignatures(typeof(MonthData));
        // Etag は MonthPart 固有（Cosmos の楽観的並行制御用）のため対象外。
        var monthPartProps = SyncTestHelpers.PropSignatures(typeof(MonthPart), nameof(MonthPart.Etag));

        Assert.Equal(monthDataProps, monthPartProps);
    }

    [Fact]
    public void MonthPart_Fields_MatchMonthDoc()
    {
        var monthPartProps = SyncTestHelpers.PropSignatures(typeof(MonthPart), nameof(MonthPart.Etag));
        // Id/UserId/Type/Ym は MonthDoc 固有（Cosmos ドキュメントのキー・種別）のため対象外。
        var monthDocProps = SyncTestHelpers.PropSignatures(typeof(MonthDoc),
            nameof(MonthDoc.Id), nameof(MonthDoc.UserId), nameof(MonthDoc.Type), nameof(MonthDoc.Ym));

        Assert.Equal(monthPartProps, monthDocProps);
    }

    [Fact]
    public void MonthPart_Fields_MatchMonthReadDoc()
    {
        // MonthReadDoc は Etag を持つ（クエリ結果の _etag をここへ格納する）ため MonthPart 側も含めて比較する。
        var monthPartProps = SyncTestHelpers.PropSignatures(typeof(MonthPart));
        // Ym は MonthReadDoc 固有（クエリ結果のキー）のため対象外。
        var monthReadDocProps = SyncTestHelpers.PropSignatures(typeof(MonthReadDoc), nameof(MonthReadDoc.Ym));

        Assert.Equal(monthPartProps, monthReadDocProps);
    }
}
