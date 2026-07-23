using MoneyBoardShared;
using Xunit;

namespace MoneyBoardShared.Tests;

// 永続契約（SettingsPart/MonthPart。参照先の型も含む）の公開プロパティ集合を
// SchemaMigration.CurrentVersion ごとに凍結し、無言のスキーマ変更を検知する。
//
// v13以降の運用（#155）は「加算的なフィールド追加でも必ず CurrentVersion を上げる」ことで
// 版数フロア（DataApi.ViolatesSchemaFloor）に未知フィールド欠落防止（受入条件③）を効かせる。
// この不変条件はコメント/ADRだけでは強制できない（フィールドを足して版上げを忘れても
// コンパイル・既存テストは通ってしまう）ため、シグネチャの差分でCIを落として強制する。
//
// フィールドを追加/変更したとき：
//   1. SchemaMigration.CurrentVersion をインクリメントする
//   2. Frozen に新バージョンのシグネチャを追記する（過去のエントリは出荷済みの記録として変更しない）
public class PersistedSchemaGuardTests
{
    private static readonly Dictionary<int, (string Settings, string Month)> Frozen = new()
    {
        [12] = (
            Settings: "{Accounts:List<{AccountNumber:String,Id:String,IsBonusAccount:Boolean,IsDeleted:Boolean,IsWallet:Boolean,Name:String,SortOrder:Int32,StartYm:String,Type:AccountType,WalletStartYm:String}>,BonusMonths:List<Int32>,Cards:List<{AccountId:String,Id:String,IsDeleted:Boolean,Name:String,SortOrder:Int32}>,Categories:List<{Color:String,Id:String,Name:String,SortOrder:Int32}>,CategoryPrefixRules:Dict<String,String>,CategoryRules:Dict<String,String>,Etag:String,FixedCosts:List<{AccountId:String,Amount:Decimal,BonusSettings:List<{Amount:Decimal,Id:String,Month:Int32,Type:BonusType}>,EndYm:String,Id:String,IsVariable:Boolean,Name:String,SortOrder:Int32,StartYm:String}>,FixedIncomes:List<{AccountId:String,Amount:Decimal,EndYm:String,Id:String,IsVariable:Boolean,Name:String,SortOrder:Int32,StartYm:String}>,SchemaVersion:Int32,TutorialSeenVersion:Int32}",
            Month: "{CardBilled:Dict<String,Decimal>,CardDetails:List<{Amount:Decimal,CardId:String,CategoryId:String,Date:String,Id:String,Name:String}>,Etag:String,Ledgers:Dict<String,{AtmDeposit:Decimal,AtmWithdraw:Decimal,Bonus:Decimal,Confirmed:Decimal,Debits:List<{Amount:Decimal,AmountOverridden:Boolean,CardId:String,CategoryId:String,FixedCostId:String,Id:String,IsFixed:Boolean,IsVariable:Boolean,Name:String}>,Incomes:List<{Amount:Decimal,AmountOverridden:Boolean,FixedIncomeId:String,Id:String,IsFixed:Boolean,IsVariable:Boolean,Name:String}>,Salary:Decimal,WalletAtmDeposits:List<{AccountId:String,Amount:Decimal,Id:String}>}>,Transfers:List<{Amount:Decimal,From:String,Id:String,To:String}>}"
        ),
    };

    [Fact]
    public void PersistedSettingsSchema_MatchesFrozenSignatureForCurrentVersion()
    {
        var version = SchemaMigration.CurrentVersion;
        Assert.True(Frozen.TryGetValue(version, out var frozen),
            $"SchemaMigration.CurrentVersion={version} の凍結シグネチャが未登録です。" +
            $"SettingsPart/MonthPart にフィールドを追加したなら、このテストのFrozenに現在の" +
            $"SchemaSignature.Of(...)の出力を追記してください（過去バージョンのエントリは変更しないこと）。");

        Assert.Equal(frozen.Settings, SchemaSignature.Of(typeof(SettingsPart)));
        Assert.Equal(frozen.Month, SchemaSignature.Of(typeof(MonthPart)));
    }
}
