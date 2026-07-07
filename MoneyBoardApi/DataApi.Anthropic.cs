using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Anthropic;
using Anthropic.Models.Messages;

namespace MoneyBoardApi;

// extract-card / classify-categories が共有する Anthropic 呼び出し基盤（クライアント生成・エラー整形・
// 構造化出力の定型呼び出し）。エンドポイント固有のプロンプト・スキーマ・解析ロジックは各ファイルに残す。
public partial class DataApi
{
    // Anthropic クライアントはキー設定時のみ生成（未設定環境では null → 503）。Functions Isolated で使い回す。
    private static readonly AnthropicClient? Anthropic = CreateAnthropic();
    private static AnthropicClient? CreateAnthropic()
    {
        var key = Environment.GetEnvironmentVariable("Anthropic__ApiKey");
        if (string.IsNullOrWhiteSpace(key)) return null;

        // 本番(Azure SWA)の egress IP は Anthropic のエッジに 403(forbidden/"Request not allowed") で
        // ブロックされるため、Anthropic__BaseUrl（例: Cloudflare AI Gateway）が指定されていれば
        // そこへ中継する。未指定なら既定の api.anthropic.com（ローカル開発はこれで通る）。
        var baseUrl = Environment.GetEnvironmentVariable("Anthropic__BaseUrl");
        return string.IsNullOrWhiteSpace(baseUrl)
            ? new AnthropicClient { ApiKey = key }
            : new AnthropicClient { ApiKey = key, BaseUrl = baseUrl };
    }

    // extract-card / classify-categories 共通の失敗時レスポンス本文（Error は呼び出し側が渡す判別用文字列）。
    private sealed record AnthropicError(string Error, int? UpstreamStatus, string Message);

    // Anthropic のエラー本文 JSON（{"error":{"type","message"}}）から人が読めるメッセージを作る。
    // パースできなければ本文の先頭を切り出して返す（空なら定型文）。
    private static string SummarizeAnthropicError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "Anthropic API でエラーが発生しました。";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                var type = err.TryGetProperty("type", out var t) ? t.GetString() : null;
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                if (!string.IsNullOrWhiteSpace(msg))
                    return string.IsNullOrWhiteSpace(type) ? msg! : $"{msg}（{type}）";
            }
        }
        catch (JsonException) { /* JSON でなければ素の本文を使う */ }
        return body.Length > 300 ? body[..300] : body;
    }

    // Anthropic からの上流エラー（401/403/429/5xx 等）。生の 401/403 を素通しすると
    // フロントが「アクセス承認待ち(403)」と誤認するため、ステータスは 502 に統一し、
    // 本文に上流の status とメッセージを載せて可視化する（ログにも全文を残す）。
    private ObjectResult HandleAnthropicUpstreamError(Anthropic.Exceptions.AnthropicApiException ex, string source, string errorCode)
    {
        var upstream = (int)ex.StatusCode;
        logger.LogError(ex, "{Source} upstream error {Status}: {Body}", source, upstream, ex.ResponseBody);
        return new ObjectResult(new AnthropicError(errorCode, upstream, SummarizeAnthropicError(ex.ResponseBody)))
        {
            StatusCode = StatusCodes.Status502BadGateway
        };
    }

    private ObjectResult HandleAnthropicGenericError(Exception ex, string source, string errorCode)
    {
        logger.LogError(ex, "{Source} failed", source);
        return new ObjectResult(new AnthropicError(errorCode, null, "サーバー内部エラーが発生しました。"))
        {
            StatusCode = StatusCodes.Status502BadGateway
        };
    }

    // 「スキーマ deserialize → Messages.Create(構造化出力) → 先頭テキスト抽出」の定型処理をまとめた薄いラッパ。
    // 呼び出し側はモデル固有のプロンプト内容(content)とスキーマ・トークン上限だけを渡す。
    private static async Task<string> CreateStructuredMessageAsync(
        string schemaJson, int maxTokens, List<ContentBlockParam> content)
    {
        var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schemaJson)!;
        var resp = await Anthropic!.Messages.Create(new MessageCreateParams
        {
            Model = Model.ClaudeHaiku4_5,
            MaxTokens = maxTokens,
            OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } },
            Messages =
            [
                new() { Role = Role.User, Content = content },
            ],
        });

        // 構造化出力により先頭の text ブロックが結果 JSON になる。
        return resp.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text ?? "";
    }
}
