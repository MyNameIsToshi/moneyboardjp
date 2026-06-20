using System.Globalization;
using System.Text.Json;
using MoneyBoardShared;

namespace MoneyBoardApi;

// カード明細スクリーンショットを Claude(Vision)＋構造化出力で読み取り、CardDetail に変換する。
// 取得（Anthropic API への HTTP 呼び出し）と解析（このファイルの ParseCardImageResponse）を分離し、
// 解析だけを純粋ロジックとして MoneyBoardApi.Tests から検証する（価格パーサ ParseYahooQuote と同流儀）。
// 出力型は CardCsvParser.Parse と同じ List<CardDetail> に揃え、既存の取込レビュー/重複除外/自動分類を再利用する。
public partial class DataApi
{
    // Claude へ渡す抽出指示。構造化出力スキーマ（items[] = {date, name, amount}）と対で使う。
    internal const string CardImagePrompt =
        "これはクレジットカードの利用明細のスクリーンショットです。" +
        "表示されている各利用明細を1行ずつ、利用日・利用先・金額として正確に抽出してください。" +
        "日付は ISO 形式 yyyy-MM-dd。年が画面に表示されていない場合は文脈から最も妥当な年を補ってください。" +
        "金額は数値のみ（カンマ・通貨記号なし、返金や割引は負数）。" +
        "合計・繰越残高・ポイント残高など、個別の利用明細でない行は含めないでください。";

    // 構造化出力（output_config.format）に渡す JSON スキーマ。
    // ルートをオブジェクト（items 配列）にすることで構造化出力の制約を満たす。
    internal const string CardImageSchema = """
    {"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"date":{"type":"string","description":"利用日 yyyy-MM-dd"},"name":{"type":"string","description":"利用先・摘要"},"amount":{"type":"number","description":"金額（返金・割引は負）"}},"required":["date","name","amount"],"additionalProperties":false}}},"required":["items"],"additionalProperties":false}
    """;

    // Claude の構造化出力 JSON（{"items":[{date,name,amount}]}）を CardDetail のリストに変換する純粋ロジック。
    // internal=テストから検証。不正JSON・items 欠落は空リスト。日付不正/名前空/金額不正の行は個別に除外する。
    internal static List<CardDetail> ParseCardImageResponse(string json, string cardId)
    {
        var list = new List<CardDetail>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array) return list;

            foreach (var it in items.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Object) continue;

                if (!it.TryGetProperty("date", out var d) || d.ValueKind != JsonValueKind.String) continue;
                if (!TryNormalizeDate(d.GetString()!, out var iso)) continue;   // 日付不正行を除外

                if (!it.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String) continue;
                var name = n.GetString()!.Trim();
                if (name.Length == 0) continue;                                 // 名前空行を除外

                if (!it.TryGetProperty("amount", out var a) || !TryGetAmount(a, out var amount)) continue;

                list.Add(new CardDetail { CardId = cardId, Date = iso, Name = name, Amount = amount });
            }
        }
        catch (JsonException)
        {
            return list;   // 不正JSONはそれまでに取れた分（=空）を返す
        }
        return list;
    }

    // 金額: JSON 数値、または "1,234" のような文字列も許容（カンマ除去）。例外を投げずに判定する。
    private static bool TryGetAmount(JsonElement e, out decimal amount)
    {
        amount = 0;
        if (e.ValueKind == JsonValueKind.Number) return e.TryGetDecimal(out amount);
        if (e.ValueKind == JsonValueKind.String)
            return decimal.TryParse(e.GetString()!.Replace(",", "").Trim(),
                NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
        return false;
    }

    // 日付を ISO "yyyy-MM-dd" に正規化。yyyy-MM-dd / yyyy/MM/dd / yyyy/M/d を許容。
    private static bool TryNormalizeDate(string s, out string iso)
    {
        iso = "";
        var formats = new[] { "yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "yyyy/M/d" };
        if (DateTime.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            iso = dt.ToString("yyyy-MM-dd");
            return true;
        }
        return false;
    }
}
