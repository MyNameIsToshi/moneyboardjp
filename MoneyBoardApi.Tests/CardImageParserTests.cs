using System.Text.Json;
using MoneyBoardApi;
using Xunit;

namespace MoneyBoardApi.Tests;

// カード明細スクショの Claude 構造化出力 → CardDetail 変換（取得と分離した純粋ロジック）を検証する。
public class CardImageParserTests
{
    [Fact]
    public void ParseCardImageResponse_ExtractsRows_WithCardId()
    {
        var json = """
        {"items":[
          {"date":"2026-06-18","name":"スーパー","amount":1234},
          {"date":"2026/06/19","name":" コンビニ ","amount":"2,500"}
        ]}
        """;
        var rows = DataApi.ParseCardImageResponse(json, "card-1");
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("card-1", r.CardId));
        Assert.Equal("2026-06-18", rows[0].Date);
        Assert.Equal("スーパー", rows[0].Name);
        Assert.Equal(1234m, rows[0].Amount);
        // yyyy/MM/dd は ISO に正規化、名前は trim、"2,500" はカンマ除去
        Assert.Equal("2026-06-19", rows[1].Date);
        Assert.Equal("コンビニ", rows[1].Name);
        Assert.Equal(2500m, rows[1].Amount);
    }

    [Fact]
    public void ParseCardImageResponse_KeepsNegativeAmount_ForRefund()
    {
        var json = """{"items":[{"date":"2026-06-20","name":"返金","amount":-980}]}""";
        var rows = DataApi.ParseCardImageResponse(json, "c");
        Assert.Single(rows);
        Assert.Equal(-980m, rows[0].Amount);
    }

    [Fact]
    public void ParseCardImageResponse_SkipsInvalidRows_KeepsValidOnes()
    {
        var json = """
        {"items":[
          {"date":"bad-date","name":"X","amount":100},
          {"date":"2026-06-18","name":"","amount":100},
          {"date":"2026-06-18","name":"金額不正","amount":"---"},
          {"date":"2026-06-18","name":"OK","amount":300}
        ]}
        """;
        var rows = DataApi.ParseCardImageResponse(json, "c");
        Assert.Single(rows);
        Assert.Equal("OK", rows[0].Name);
        Assert.Equal(300m, rows[0].Amount);
    }

    [Fact]
    public void ParseCardImageResponse_MissingItems_ReturnsEmpty()
    {
        Assert.Empty(DataApi.ParseCardImageResponse("""{"foo":1}""", "c"));
    }

    [Fact]
    public void ParseCardImageResponse_InvalidJson_ReturnsEmpty()
    {
        Assert.Empty(DataApi.ParseCardImageResponse("not json", "c"));
    }

    [Fact]
    public void CardImageSchema_IsValidJson()
    {
        using var doc = JsonDocument.Parse(DataApi.CardImageSchema);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }
}
