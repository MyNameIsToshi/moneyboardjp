using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

public class StatsMathTests
{
    // 昇順 6ヶ月分（zero-pad 済み＝文字列比較で時系列順）
    private static readonly string[] Yms =
        { "2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06" };

    [Fact]
    public void SelectPeriodYms_All_ReturnsEverything()
    {
        var r = StatsMath.SelectPeriodYms(Yms, "all", "", "");
        Assert.Equal(Yms, r);
    }

    [Fact]
    public void SelectPeriodYms_Numeric_ReturnsLastN()
    {
        var r = StatsMath.SelectPeriodYms(Yms, "3", "", "");
        Assert.Equal(new[] { "2026-04", "2026-05", "2026-06" }, r);
    }

    [Fact]
    public void SelectPeriodYms_Current_ReturnsCurrentCycleMonthOnly()
    {
        // 「当月」（#88）＝ currentYm と一致する月のみ。
        var r = StatsMath.SelectPeriodYms(Yms, "current", "", "", "2026-05");
        Assert.Equal(new[] { "2026-05" }, r);
    }

    [Fact]
    public void SelectPeriodYms_Current_IgnoresFutureMonthAlreadyCreated()
    {
        // 未来月（2026-06）を先行作成済みでも、給料サイクル起点(currentYm=2026-05)を
        // ピンポイントで返す（TakeLast(1) だと最新作成月を誤って拾ってしまうため）。
        var r = StatsMath.SelectPeriodYms(Yms, "current", "", "", "2026-05");
        Assert.Equal(new[] { "2026-05" }, r);
        Assert.DoesNotContain("2026-06", r);
    }

    [Fact]
    public void SelectPeriodYms_Current_NotYetCreated_ReturnsEmpty()
    {
        // 当月サイクルのレコードがまだ無ければ空（該当なし）。
        var r = StatsMath.SelectPeriodYms(Yms, "current", "", "", "2026-07");
        Assert.Empty(r);
    }

    [Fact]
    public void SelectPeriodYms_NumericLargerThanCount_ClampsToAll()
    {
        // 直近12ヶ月を要求しても6件しか無ければ6件（TakeLast がクランプ）
        var r = StatsMath.SelectPeriodYms(Yms, "12", "", "");
        Assert.Equal(Yms, r);
    }

    [Fact]
    public void SelectPeriodYms_Numeric_ExcludesFutureMonthsBeyondCurrentYm()
    {
        // 固定費先行展開等で未来月(2026-06)が既に存在していても、
        // 直近3ヶ月は当月サイクル(currentYm=2026-05)までに限定する(#119)。
        var r = StatsMath.SelectPeriodYms(Yms, "3", "", "", "2026-05");
        Assert.Equal(new[] { "2026-03", "2026-04", "2026-05" }, r);
        Assert.DoesNotContain("2026-06", r);
    }

    [Fact]
    public void SelectPeriodYms_Numeric_CurrentYmNull_KeepsIncludingLatestMonth()
    {
        // currentYm 未指定時は従来どおり全件を対象に TakeLast する
        var r = StatsMath.SelectPeriodYms(Yms, "3", "", "", null);
        Assert.Equal(new[] { "2026-04", "2026-05", "2026-06" }, r);
    }

    [Fact]
    public void SelectPeriodYms_Custom_InclusiveRange()
    {
        var r = StatsMath.SelectPeriodYms(Yms, "custom", "2026-02", "2026-04");
        Assert.Equal(new[] { "2026-02", "2026-03", "2026-04" }, r);
    }

    [Fact]
    public void SelectPeriodYms_Custom_ReversedRangeIsSwapped()
    {
        // 開始＞終了でも入れ替えて同じ範囲を返す
        var r = StatsMath.SelectPeriodYms(Yms, "custom", "2026-04", "2026-02");
        Assert.Equal(new[] { "2026-02", "2026-03", "2026-04" }, r);
    }

    [Fact]
    public void SelectPeriodYms_Custom_SingleMonth()
    {
        var r = StatsMath.SelectPeriodYms(Yms, "custom", "2026-03", "2026-03");
        Assert.Equal(new[] { "2026-03" }, r);
    }

    [Fact]
    public void SelectPeriodYms_Custom_OutOfRange_ReturnsEmpty()
    {
        var r = StatsMath.SelectPeriodYms(Yms, "custom", "2027-01", "2027-12");
        Assert.Empty(r);
    }

    [Fact]
    public void SelectPeriodYms_EmptyInput_ReturnsEmpty()
    {
        var r = StatsMath.SelectPeriodYms(System.Array.Empty<string>(), "3", "", "");
        Assert.Empty(r);
    }

    // NormalizeCategoryKey（#117）：参照切れ・未設定の CategoryId を "" に統一し、
    // 「未分類」が複数行に分裂しないようにする。
    private static readonly string[] KnownCategoryIds = { "cat-1", "cat-2" };

    [Fact]
    public void NormalizeCategoryKey_KnownId_ReturnsSameId()
    {
        var r = StatsMath.NormalizeCategoryKey("cat-1", KnownCategoryIds);
        Assert.Equal("cat-1", r);
    }

    [Fact]
    public void NormalizeCategoryKey_EmptyId_ReturnsEmptyKey()
    {
        var r = StatsMath.NormalizeCategoryKey("", KnownCategoryIds);
        Assert.Equal("", r);
    }

    [Fact]
    public void NormalizeCategoryKey_NullId_ReturnsEmptyKey()
    {
        var r = StatsMath.NormalizeCategoryKey(null, KnownCategoryIds);
        Assert.Equal("", r);
    }

    [Fact]
    public void NormalizeCategoryKey_DanglingReference_ReturnsEmptyKey()
    {
        // 削除済み等でカテゴリが存在しない CategoryId（参照切れ）も "" に正規化される
        var r = StatsMath.NormalizeCategoryKey("deleted-cat-id", KnownCategoryIds);
        Assert.Equal("", r);
    }

    [Fact]
    public void NormalizeCategoryKey_EmptyAndDanglingReference_CollapseToSameKey()
    {
        // 未設定（空）と参照切れの両方が同じキーに集約されることを確認
        var empty = StatsMath.NormalizeCategoryKey("", KnownCategoryIds);
        var dangling = StatsMath.NormalizeCategoryKey("deleted-cat-id", KnownCategoryIds);
        Assert.Equal(empty, dangling);
    }

    // UsageYmOf（#105）：CardDetail.Date から利用月キー（"yyyyMM"）を取り出す
    [Fact]
    public void UsageYmOf_ValidDate_ReturnsYyyyMm()
    {
        var r = StatsMath.UsageYmOf("2026-07-15");
        Assert.Equal("202607", r);
    }

    [Fact]
    public void UsageYmOf_EmptyDate_ReturnsNull()
    {
        var r = StatsMath.UsageYmOf("");
        Assert.Null(r);
    }

    [Fact]
    public void UsageYmOf_MalformedDate_ReturnsNull()
    {
        // 区切り文字が "-" でない、または短すぎる場合は不正とみなす
        Assert.Null(StatsMath.UsageYmOf("2026/07/15"));
        Assert.Null(StatsMath.UsageYmOf("2026-07"));
    }

    // BuildAccountSeries（#157）：口座名で系列を引くと同名口座で破綻していた不具合の回帰テスト。
    [Fact]
    public void BuildAccountSeries_DuplicateAccountNames_KeepsBothSeriesSeparately()
    {
        // 「楽天」という同名の口座が2つ。旧実装（口座名キーの ToDictionary）はここで
        // ArgumentException を投げ、統計ページ全体が描画できなくなっていた。
        var accounts = new[] { ("acc-1", "楽天"), ("acc-2", "楽天"), ("acc-3", "三井住友") };

        var r = StatsMath.BuildAccountSeries(accounts, id => id + "-balance");

        // 同名でも2系列とも残り、値は Id で解決されるため取り違えが起きない
        Assert.Equal(3, r.Count);
        Assert.Equal(new[] { "楽天", "楽天 (2)", "三井住友" }, r.Select(x => x.Name));
        Assert.Equal(new[] { "acc-1-balance", "acc-2-balance", "acc-3-balance" }, r.Select(x => x.Data));
    }

    [Fact]
    public void BuildAccountSeries_ThreeDuplicateNames_NumbersSecondAndThirdOnly()
    {
        // 実機確認で発覚：ApexCharts の凡例ホバーは系列を「名前」で解決するため、
        // 同名のままだと常に1件目がハイライトされる。2件目以降に連番を付けて一意化する。
        var accounts = new[] { ("acc-1", "あいち銀行"), ("acc-2", "あいち銀行"), ("acc-3", "あいち銀行") };

        var r = StatsMath.BuildAccountSeries(accounts, id => id);

        Assert.Equal(new[] { "あいち銀行", "あいち銀行 (2)", "あいち銀行 (3)" }, r.Select(x => x.Name));
    }

    [Fact]
    public void BuildAccountSeries_GeneratedSuffixCollidesWithRealName_SkipsToNextNumber()
    {
        // 連番で作った名前が「別口座の実名」と衝突しうる。ここで先勝ちを許すと、
        // 一意化したはずの凡例ホバーが再び同名衝突を起こす。
        var accounts = new[] { ("acc-1", "楽天"), ("acc-2", "楽天"), ("acc-3", "楽天 (2)") };

        var r = StatsMath.BuildAccountSeries(accounts, id => id);

        // acc-2 は "楽天 (2)"（acc-3 の実名）を避けて "楽天 (3)" になる
        Assert.Equal(new[] { "楽天", "楽天 (3)", "楽天 (2)" }, r.Select(x => x.Name));
        Assert.Equal(r.Select(x => x.Name).Distinct().Count(), r.Count);
    }

    [Fact]
    public void BuildAccountSeries_UniqueNames_AreNotRenamed()
    {
        // 重複していない口座名はそのまま（連番なし）で影響を受けない
        var accounts = new[] { ("acc-1", "楽天"), ("acc-2", "三井住友"), ("acc-3", "みずほ") };

        var r = StatsMath.BuildAccountSeries(accounts, id => id);

        Assert.Equal(new[] { "楽天", "三井住友", "みずほ" }, r.Select(x => x.Name));
    }

    [Fact]
    public void BuildAccountSeries_PreservesAccountOrder()
    {
        // 系列の並び順は色パレット（BalancePalette）の割当順と 1:1 で対応するため、
        // 入力（ActiveAccounts）の順序がそのまま保たれる必要がある。
        var accounts = new[] { ("acc-3", "財布"), ("acc-1", "みずほ"), ("acc-2", "楽天") };

        var r = StatsMath.BuildAccountSeries(accounts, id => id);

        Assert.Equal(new[] { "財布", "みずほ", "楽天" }, r.Select(x => x.Name));
        Assert.Equal(new[] { "acc-3", "acc-1", "acc-2" }, r.Select(x => x.Data));
    }

    [Fact]
    public void BuildAccountSeries_NoAccounts_ReturnsEmpty()
    {
        var r = StatsMath.BuildAccountSeries(System.Array.Empty<(string, string)>(), id => id);

        Assert.Empty(r);
    }
}
