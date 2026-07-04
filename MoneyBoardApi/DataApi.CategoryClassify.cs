using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Anthropic;
using Anthropic.Models.Messages;
using MoneyBoardShared;

namespace MoneyBoardApi;

// カード明細の利用先（店名）を Claude(Haiku) で一括分類する（C案・issue #27）。
// カード明細スクショ読み取り（DataApi.CardImage.cs）と同じ土台（サーバー側キーの Anthropic クライアントを再利用・
// 取得と解析を分離・解析部は internal static でテスト可能）を踏襲する。
// カテゴリ一覧はユーザーごとに異なりサーバー側で保持していないため、リクエストボディで受け取る。
// 結果は呼び出し側（フロント）が一括カテゴリのレビュー画面へ反映し、ユーザーが確認・適用した分だけ
// CategoryRules にキャッシュされる（このエンドポイント自体は何も永続化しない）。
public partial class DataApi
{
    private const int MaxClassifyStores = 200;

    internal const string CategoryClassifyPrompt =
        "これはクレジットカード明細の利用先（店名）の一覧です。それぞれの利用先を、与えられたカテゴリ一覧から" +
        "最も適切と思われるものに分類してください。店名から一般的に想定される用途をもとに判断し、" +
        "確信が持てない場合は categoryId に null を返してください。categoryId は必ず与えられたカテゴリ一覧の" +
        "id のいずれか、または null にしてください（存在しない id を作らないこと）。";

    internal const string CategoryClassifySchema = """
    {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"store":{"type":"string"},"categoryId":{"type":["string","null"]}},"required":["store","categoryId"],"additionalProperties":false}}},"required":["items"],"additionalProperties":false}
    """;

    // POST /api/classify-categories → 利用先の一覧をカテゴリへ一括分類する。承認ユーザーのみ。
    [Function("ClassifyCategories")]
    public async Task<IActionResult> ClassifyCategories(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "classify-categories")] HttpRequest req)
    {
        try
        {
            var container = GetContainer();
            var (_, _, authError) = await AuthorizeAsync(container, req);
            if (authError is not null) return authError;

            if (Anthropic is null) return new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);

            var (body, bodyError) = await ReadCappedBodyAsync(req);
            if (bodyError is not null) return bodyError;
            var reqData = JsonSerializer.Deserialize<ClassifyCategoriesRequest>(body!, JsonOptions)
                          ?? new ClassifyCategoriesRequest();

            var stores = (reqData.Stores ?? new()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            var categories = reqData.Categories ?? new();
            if (stores.Count == 0 || categories.Count == 0) return new OkObjectResult(new Dictionary<string, string>());
            if (stores.Count > MaxClassifyStores) return new BadRequestResult();

            var result = await ClassifyCategoriesAsync(stores, categories);
            return new OkObjectResult(result);
        }
        catch (Anthropic.Exceptions.AnthropicApiException ex)
        {
            var upstream = (int)ex.StatusCode;
            logger.LogError(ex, "ClassifyCategories upstream error {Status}: {Body}", upstream, ex.ResponseBody);
            return new ObjectResult(new ClassifyCategoriesError(upstream, SummarizeAnthropicError(ex.ResponseBody)))
            {
                StatusCode = StatusCodes.Status502BadGateway
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ClassifyCategories failed");
            return new ObjectResult(new ClassifyCategoriesError(null, "サーバー内部エラーが発生しました。"))
            {
                StatusCode = StatusCodes.Status502BadGateway
            };
        }
    }

    // /api/classify-categories の失敗時にフロントへ返すエラー本文（extract-card と同形・ExtractCardError流用）。
    private sealed record ClassifyCategoriesError(int? UpstreamStatus, string Message)
    {
        public string Error => "classify_categories_error";
    }

    private sealed class ClassifyCategoriesRequest
    {
        public List<string>? Stores { get; set; }
        public List<CategoryOption>? Categories { get; set; }
    }

    private sealed class CategoryOption
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    // 取得：店名一覧＋カテゴリ一覧を Claude へ投げ、構造化出力(JSON)を ParseCategoryClassifyResponse で
    // Dictionary<store, categoryId> に変換する薄いラッパ。
    private static async Task<Dictionary<string, string>> ClassifyCategoriesAsync(
        List<string> stores, List<CategoryOption> categories)
    {
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(CategoryClassifySchema)!;
        var catList = string.Join("\n", categories.Select(c => $"- id:{c.Id} name:{c.Name}"));
        var storeList = string.Join("\n", stores.Select(s => $"- {s}"));
        var prompt = $"{CategoryClassifyPrompt}\n\n【カテゴリ一覧】\n{catList}\n\n【利用先一覧】\n{storeList}";

        var resp = await Anthropic!.Messages.Create(new MessageCreateParams
        {
            Model = Model.ClaudeHaiku4_5,
            // 最大 MaxClassifyStores(200) 件×1件あたり数十トークンでも上限に収まるよう余裕を持たせる
            // （Haiku 4.5 の出力上限 64K 内。実際に生成した分しか課金されないので大きめで安全）。
            // 小さすぎると構造化出力が途中で切れ、JSON 不正→空辞書になり全件分類失敗する。
            MaxTokens = 32000,
            OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } },
            Messages =
            [
                new()
                {
                    Role = Role.User,
                    Content = new List<ContentBlockParam> { new TextBlockParam { Text = prompt } },
                },
            ],
        });

        var json = resp.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
        var validIds = categories.Select(c => c.Id).ToHashSet();
        return ParseCategoryClassifyResponse(json, stores, validIds);
    }

    // Claude の構造化出力 JSON（{"items":[{store,categoryId}]}）を Dictionary<store, categoryId> に変換する
    // 純粋ロジック。internal=テストから検証。不正JSON・items 欠落は空辞書。
    // store 空文字・categoryId が validCategoryIds に無い（null/空含む）行は結果に含めない
    // （＝フロント側は「変更しない」のまま＝未分類or手動判断に委ねる）。
    // AI が返す店名は表記ゆれ（trim/全角半角）しうるため、要求した店名(requestedStores)へ
    // NormalizeStore 経由で突き合わせ、キーは必ず要求した原文の店名に揃える（フロントの完全一致採用のため）。
    // requestedStores に無い（＝AI がでっち上げた）店名は捨てる。
    internal static Dictionary<string, string> ParseCategoryClassifyResponse(
        string json, IEnumerable<string> requestedStores, HashSet<string> validCategoryIds)
    {
        // 正規化キー → 要求した原文の店名。重複時は先勝ち（要求側は Distinct 済みの想定）。
        var byNormalized = new Dictionary<string, string>();
        foreach (var s in requestedStores)
        {
            var norm = LedgerEngine.NormalizeStore(s);
            if (norm.Length > 0) byNormalized.TryAdd(norm, s);
        }

        var result = new Dictionary<string, string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array) return result;

            foreach (var it in items.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Object) continue;

                if (!it.TryGetProperty("store", out var s) || s.ValueKind != JsonValueKind.String) continue;
                if (!byNormalized.TryGetValue(LedgerEngine.NormalizeStore(s.GetString()), out var store))
                    continue;   // 要求していない店名（表記ゆれで一致しない/でっち上げ）は捨てる

                if (!it.TryGetProperty("categoryId", out var c) || c.ValueKind != JsonValueKind.String) continue;
                var categoryId = c.GetString()!.Trim();
                if (categoryId.Length == 0 || !validCategoryIds.Contains(categoryId)) continue;

                result[store] = categoryId;   // キーは要求した原文の店名。重複は最後を採用
            }
        }
        catch (JsonException)
        {
            return result;
        }
        return result;
    }
}
