using MoneyBoardShared;

namespace MoneyBoard.Services;

// 口座種別のアイコン/タグ表示を集約する（AccountsTab・MonthlyTab で重複していた出し分けを#148で統合）。
// Portfolio.Disp.cs の AccountLabel(AccountKind) と同型（enum を引数に取る関数）。
public static class AccountDisplay
{
    public static string Icon(this AccountType type) => type switch
    {
        AccountType.Wallet => "account_balance_wallet",
        AccountType.EMoney => "contactless",
        _ => "account_balance",
    };

    // 種別タグの表示文言。通常口座は種別タグを表示しないため null（ボーナス受取チェック等の別UIに切り替える）。
    public static string? Tag(this AccountType type) => type switch
    {
        AccountType.Wallet => "財布（現金）",
        AccountType.EMoney => "電子マネー",
        _ => null,
    };
}
