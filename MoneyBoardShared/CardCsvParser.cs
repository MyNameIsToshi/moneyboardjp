using System.Globalization;
using System.Text;

namespace MoneyBoardShared;

/// <summary>取り込めるカード明細 CSV の種別。</summary>
public enum CardCsvFormat { Jcb, Amazon, PayPay, AuPay, Rakuten }

/// <summary>
/// カード明細 CSV を種別ごとの列マッピングでパースして CardDetail のリストにする。
/// どの種別も「日付列・利用先列・金額列」を指定するだけの同じ骨格で、
/// 日付列が日付として解釈できない行（ヘッダ・合計・カード情報行）は自動的に除外される。
/// </summary>
public static class CardCsvParser
{
    /// <param name="Label">UI 表示名</param>
    /// <param name="IsUtf8">true=UTF-8(BOM可) / false=Shift-JIS（JS でデコード）</param>
    public record FormatSpec(string Label, bool IsUtf8, int DateCol, int NameCol, int AmountCol);

    public static readonly IReadOnlyDictionary<CardCsvFormat, FormatSpec> Specs =
        new Dictionary<CardCsvFormat, FormatSpec>
        {
            // JCB:    [2]利用日 [3]利用先 [4]金額（先頭にカード情報・末尾に合計）
            [CardCsvFormat.Jcb]     = new("JCB",           IsUtf8: false, DateCol: 2, NameCol: 3, AmountCol: 4),
            // Amazon Mastercard: [0]利用日 [1]利用先 [2]金額（1行目=カード情報・末尾=合計行）
            [CardCsvFormat.Amazon]  = new("Amazon Master", IsUtf8: false, DateCol: 0, NameCol: 1, AmountCol: 2),
            // PayPay(UTF-8): [0]利用日 [1]利用店名 [5]利用金額（ヘッダ行あり）
            [CardCsvFormat.PayPay]  = new("PayPay",        IsUtf8: true,  DateCol: 0, NameCol: 1, AmountCol: 5),
            // au PAY: [2]ご利用日 [3]ご利用店名 [4]ご利用金額（ヘッダ行あり）
            [CardCsvFormat.AuPay]   = new("au PAY",        IsUtf8: false, DateCol: 2, NameCol: 3, AmountCol: 4),
            // 楽天カード(enavi): [0]利用日 [1]利用店名・商品名 [4]利用金額（ヘッダ行あり・末尾に明細外の集計行）
            // ※ 標準的な enavi 明細CSV の列構成。実CSV未検証（楽天カード入手時に列ズレを確認）。
            [CardCsvFormat.Rakuten] = new("楽天カード",    IsUtf8: false, DateCol: 0, NameCol: 1, AmountCol: 4),
        };

    public static List<CardDetail> Parse(CardCsvFormat format, string text, string cardId)
    {
        var spec = Specs[format];
        int need = Math.Max(spec.DateCol, Math.Max(spec.NameCol, spec.AmountCol)) + 1;

        var list = new List<CardDetail>();
        foreach (var row in ReadCsv(text))
        {
            if (row.Count < need) continue;
            if (!TryParseDate(row[spec.DateCol].Trim(), out var date)) continue;   // ヘッダ/合計/情報行を自動除外
            var amountRaw = row[spec.AmountCol].Replace(",", "").Trim();
            if (!decimal.TryParse(amountRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) continue;
            list.Add(new CardDetail
            {
                CardId = cardId,
                Date = date,
                Name = row[spec.NameCol].Trim(),
                Amount = amount
            });
        }
        return list;
    }

    private static bool TryParseDate(string s, out string iso)
    {
        iso = "";
        var formats = new[] { "yyyy/MM/dd", "yyyy/M/d" };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            iso = dt.ToString("yyyy-MM-dd");
            return true;
        }
        return false;
    }

    // 引用符・埋め込みカンマ・埋め込み改行に対応した最小 CSV パーサ
    private static List<List<string>> ReadCsv(string text)
    {
        var rows = new List<List<string>>();
        var field = new StringBuilder();
        var row = new List<string>();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else
            {
                switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': row.Add(field.ToString()); field.Clear(); break;
                    case '\r': break;
                    case '\n':
                        row.Add(field.ToString()); field.Clear();
                        rows.Add(row); row = new List<string>();
                        break;
                    default: field.Append(c); break;
                }
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}
