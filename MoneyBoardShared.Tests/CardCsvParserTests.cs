using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class CardCsvParserTests
{
    [Fact]
    public void Parse_Jcb_ExcludesNonDateRows_AndParsesAmounts()
    {
        // JCB: [2]利用日 [3]利用先 [4]金額。先頭にカード情報行・末尾に合計行が混ざる。
        var csv = string.Join("\n",
            "ご利用明細,,,,",                              // 情報行（日付列が空→除外）
            "X,Y,2026/01/15,スーパーABC,\"1,500\"",        // 明細（金額にカンマ・引用符）
            "X,Y,2026/1/5,コンビニ,860",                   // yyyy/M/d も可
            "合計,,,,99999");                              // 合計行（日付列が空→除外）

        var rows = CardCsvParser.Parse(CardCsvFormat.Jcb, csv, "card1");

        Assert.Equal(2, rows.Count);
        Assert.Equal("card1", rows[0].CardId);
        Assert.Equal("2026-01-15", rows[0].Date);
        Assert.Equal("スーパーABC", rows[0].Name);
        Assert.Equal(1500m, rows[0].Amount);
        Assert.Equal("2026-01-05", rows[1].Date);
        Assert.Equal(860m, rows[1].Amount);
    }

    [Fact]
    public void Parse_QuotedFieldWithCommaInName()
    {
        var csv = "X,Y,2026/02/01,\"Cafe, Tokyo\",1200";
        var rows = CardCsvParser.Parse(CardCsvFormat.Jcb, csv, "c");

        Assert.Single(rows);
        Assert.Equal("Cafe, Tokyo", rows[0].Name);
        Assert.Equal(1200m, rows[0].Amount);
    }

    [Fact]
    public void Parse_Rakuten_RealEnaviLayout()
    {
        // 楽天(enavi)の実ヘッダ：[0]利用日 [1]利用店舗・商品名 [4]利用金額。ヘッダ行・末尾の集計行は除外される。
        // ※ Parse は復号済みテキストを受けるため文字コード(UTF-8 BOM)はここでは関与しない（デコードは CardTab 側）。
        var csv = string.Join("\n",
            "利用日,利用店舗・商品名,利用者,支払方法,利用金額,手数料/利息,支払総額,6月支払金額,当月請求額,7月繰越残高,新規サイン",
            "2026/05/11,楽天キャッシュチャージ,本人,1回払い,10000,0,10000,10000,10000,0,*",
            "2026/05/02,ＥＮＥＯＳ－ＳＳ,本人,1回払い,\"2,480\",0,2480,2480,2480,0,*");

        var rows = CardCsvParser.Parse(CardCsvFormat.Rakuten, csv, "rk");

        Assert.Equal(2, rows.Count);
        Assert.Equal("2026-05-11", rows[0].Date);
        Assert.Equal("楽天キャッシュチャージ", rows[0].Name);
        Assert.Equal(10000m, rows[0].Amount);
        Assert.Equal("ＥＮＥＯＳ－ＳＳ", rows[1].Name);
        Assert.Equal(2480m, rows[1].Amount);          // カンマ区切り金額もパース
    }

    [Fact]
    public void Parse_AuPay_HeaderRowExcluded()
    {
        // au PAY: [2]ご利用日 [3]ご利用店名 [4]ご利用金額。
        var csv = string.Join("\n",
            "番号,会員,ご利用日,ご利用店名,ご利用金額",   // ヘッダ（日付列がテキスト→除外）
            "1,本人,2026/03/10,書店,1800");

        var rows = CardCsvParser.Parse(CardCsvFormat.AuPay, csv, "au");

        Assert.Single(rows);
        Assert.Equal("2026-03-10", rows[0].Date);
        Assert.Equal("書店", rows[0].Name);
        Assert.Equal(1800m, rows[0].Amount);
    }

    [Fact]
    public void Parse_AllFormatsHaveSpec()
    {
        foreach (CardCsvFormat fmt in Enum.GetValues<CardCsvFormat>())
            Assert.True(CardCsvParser.Specs.ContainsKey(fmt), $"{fmt} のフォーマット定義が無い");
    }

    [Fact]
    public void Parse_SkipsRowsWithTooFewColumns_AndUnparsableAmount()
    {
        // JCB は最低5列必要。列不足・金額が数値でない行は黙って除外される。
        var csv = string.Join("\n",
            "A,B,2026/04/01",                  // 列不足（金額列なし）→除外
            "A,B,2026/04/02,店,---",            // 金額が数値でない→除外
            "A,B,2026/04/03,店,500");           // 正常
        var rows = CardCsvParser.Parse(CardCsvFormat.Jcb, csv, "c");

        Assert.Equal(500m, Assert.Single(rows).Amount);
    }

    [Fact]
    public void Parse_UnescapesDoubledQuoteInsideQuotedField()
    {
        // RFC4180：引用フィールド内の "" は1個の " を表す。
        var csv = "A,B,2026/04/01,\"24\"\"モニター\",30000";
        var rows = CardCsvParser.Parse(CardCsvFormat.Jcb, csv, "c");

        Assert.Equal("24\"モニター", Assert.Single(rows).Name);
    }
}
