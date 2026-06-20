using MoneyBoardApi;
using Xunit;

namespace MoneyBoardApi.Tests;

// DataApi の価格パーサ（取得と分離した純粋ロジック）を検証する。
public class QuoteParserTests
{
    // ── Yahoo chart JSON ─────────────────────────────
    [Fact]
    public void ParseYahooQuote_ExtractsPriceAndPrevClose()
    {
        var json = """
        {"chart":{"result":[{"meta":{"regularMarketPrice":123.45,"chartPreviousClose":120.0}}]}}
        """;
        var (price, prev) = DataApi.ParseYahooQuote(json);
        Assert.Equal(123.45m, price);
        Assert.Equal(120.0m, prev);
    }

    [Fact]
    public void ParseYahooQuote_FallsBackToPreviousClose()
    {
        // chartPreviousClose が無ければ previousClose を使う。
        var json = """
        {"chart":{"result":[{"meta":{"regularMarketPrice":50,"previousClose":48}}]}}
        """;
        var (price, prev) = DataApi.ParseYahooQuote(json);
        Assert.Equal(50m, price);
        Assert.Equal(48m, prev);
    }

    [Fact]
    public void ParseYahooQuote_MissingMeta_ReturnsNulls()
    {
        var json = """{"chart":{"result":[{}]}}""";
        Assert.Equal((null, null), DataApi.ParseYahooQuote(json));
    }

    [Fact]
    public void ParseYahooQuote_EmptyResult_ReturnsNulls()
    {
        var json = """{"chart":{"result":[]}}""";
        Assert.Equal((null, null), DataApi.ParseYahooQuote(json));
    }

    [Fact]
    public void ParseYahooQuote_InvalidJson_ReturnsNulls()
    {
        Assert.Equal((null, null), DataApi.ParseYahooQuote("not json"));
    }

    // ── 投信協会 CSV ─────────────────────────────────
    [Fact]
    public void ParseFundCsv_LastRowIsLatest_PrevIsOneBefore()
    {
        var csv = string.Join("\n",
            "年月日,基準価額,純資産",        // ヘッダ（スキップ）
            "2026/06/18,12000,100",
            "2026/06/19,12100,101");
        var (latest, prev) = DataApi.ParseFundCsv(csv);
        Assert.Equal(12100m, latest);
        Assert.Equal(12000m, prev);
    }

    [Fact]
    public void ParseFundCsv_IgnoresBlankAndShortAndNonNumericRows()
    {
        var csv = string.Join("\n",
            "年月日,基準価額",
            "",                       // 空行
            "2026/06/18",             // 列不足
            "2026/06/19,---",         // 非数値
            "2026/06/20,0",           // 0は無効(>0要件)
            "2026/06/21,12345");      // 唯一の有効行
        var (latest, prev) = DataApi.ParseFundCsv(csv);
        Assert.Equal(12345m, latest);
        Assert.Null(prev);
    }

    [Fact]
    public void ParseFundCsv_HeaderOnly_ReturnsNulls()
    {
        Assert.Equal((null, null), DataApi.ParseFundCsv("年月日,基準価額"));
    }
}
