using System.Globalization;
using System.Text;
using MoneyBoardShared;

namespace MoneyBoard.Services;

/// <summary>
/// JCB のご利用明細 CSV をパースして CardDetail のリストにする。
/// 列: [0]利用者 [1]区分 [2]利用日 [3]利用先 [4]金額 ...（摘要列に改行を含むため引用符対応が必要）。
/// ヘッダ/サマリ行は「利用日(列2)が日付として解釈できない」ことで自動的に除外される。
/// </summary>
public static class JcbCsvParser
{
    public static List<CardDetail> Parse(string text, string cardId)
    {
        var list = new List<CardDetail>();
        foreach (var row in ReadCsv(text))
        {
            if (row.Count < 5) continue;
            if (!TryParseDate(row[2].Trim(), out var date)) continue;
            var amountRaw = row[4].Replace(",", "").Trim();
            if (!decimal.TryParse(amountRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) continue;
            list.Add(new CardDetail
            {
                CardId = cardId,
                Date = date,
                Name = row[3].Trim(),
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
