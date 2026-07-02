using System.Text.Json;
using MoneyBoardApi;
using Xunit;

namespace MoneyBoardApi.Tests;

// カテゴリ自動推定（C案・issue #27）の Claude 構造化出力 → Dictionary<store, categoryId> 変換
// （取得と分離した純粋ロジック）を検証する。
public class CategoryClassifyParserTests
{
    private static readonly HashSet<string> ValidIds = new() { "cat-food", "cat-transport" };

    [Fact]
    public void ParseCategoryClassifyResponse_MapsStoresToValidCategoryIds()
    {
        var json = """
        {"items":[
          {"store":"スーパー","categoryId":"cat-food"},
          {"store":" 地下鉄 ","categoryId":"cat-transport"}
        ]}
        """;
        var stores = new[] { "スーパー", "地下鉄" };
        var result = DataApi.ParseCategoryClassifyResponse(json, stores, ValidIds);
        Assert.Equal(2, result.Count);
        Assert.Equal("cat-food", result["スーパー"]);
        Assert.Equal("cat-transport", result["地下鉄"]);   // AI側の前後空白は正規化で吸収し原文キーへ
    }

    [Fact]
    public void ParseCategoryClassifyResponse_MatchesBackToRequestedStore_DespiteWidthDrift()
    {
        // AI が全角英数・全角空白で返しても、要求した原文の店名キーへ突き合わせる
        var json = """{"items":[{"store":"ＡＥＯＮ　モール","categoryId":"cat-food"}]}""";
        var stores = new[] { "AEON モール" };
        var result = DataApi.ParseCategoryClassifyResponse(json, stores, ValidIds);
        Assert.Single(result);
        Assert.Equal("cat-food", result["AEON モール"]);   // キーは要求した原文
    }

    [Fact]
    public void ParseCategoryClassifyResponse_SkipsNullOrUncertain()
    {
        var json = """{"items":[{"store":"謎の店","categoryId":null}]}""";
        var result = DataApi.ParseCategoryClassifyResponse(json, new[] { "謎の店" }, ValidIds);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseCategoryClassifyResponse_SkipsHallucinatedCategoryId()
    {
        // 与えていない id を Claude が返してきた行は破棄する（存在しないカテゴリを適用しない）
        var json = """{"items":[{"store":"店A","categoryId":"cat-not-exist"}]}""";
        var result = DataApi.ParseCategoryClassifyResponse(json, new[] { "店A" }, ValidIds);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseCategoryClassifyResponse_SkipsHallucinatedStore()
    {
        // 要求していない店名を返してきた行は破棄する（AI のでっち上げを採用しない）
        var json = """{"items":[{"store":"知らない店","categoryId":"cat-food"}]}""";
        var result = DataApi.ParseCategoryClassifyResponse(json, new[] { "スーパー" }, ValidIds);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseCategoryClassifyResponse_SkipsEmptyStoreName()
    {
        var json = """{"items":[{"store":"","categoryId":"cat-food"}]}""";
        var result = DataApi.ParseCategoryClassifyResponse(json, new[] { "スーパー" }, ValidIds);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseCategoryClassifyResponse_MissingItems_ReturnsEmpty()
    {
        Assert.Empty(DataApi.ParseCategoryClassifyResponse("""{"foo":1}""", new[] { "スーパー" }, ValidIds));
    }

    [Fact]
    public void ParseCategoryClassifyResponse_InvalidJson_ReturnsEmpty()
    {
        Assert.Empty(DataApi.ParseCategoryClassifyResponse("not json", new[] { "スーパー" }, ValidIds));
    }

    [Fact]
    public void CategoryClassifySchema_IsValidJson()
    {
        using var doc = JsonDocument.Parse(DataApi.CategoryClassifySchema);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }
}
