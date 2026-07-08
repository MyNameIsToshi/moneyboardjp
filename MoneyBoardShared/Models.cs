namespace MoneyBoardShared;

public static class Util
{
    public static string NewId() => Guid.NewGuid().ToString("N")[..7];
}

public class AppState
{
    // 既存スキーマのバージョン。JSON に無い旧データは「最古=1」として読み込まれる
    // （初期値は CurrentVersion ではなく最古の 1 にすること。current にすると
    //  フィールド未保持の旧データが誤って最新扱いされ移行をスキップしてしまう）。
    public int SchemaVersion { get; set; } = 1;

    public List<Account> Accounts { get; set; } = new();
    public List<FixedCost> FixedCosts { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public List<Card> Cards { get; set; } = new();
    // 利用先(店名) → カテゴリId。一括適用で記憶し、以降の取込で自動分類する（完全一致）。
    public Dictionary<string, string> CategoryRules { get; set; } = new();
    // 利用先の前方一致(プレフィックス) → カテゴリId。ETC通行料金など区間ごとに店名が
    // 変わる明細を共通の接頭辞でまとめて分類する（#70）。キーは NormalizeStore + ToLowerInvariant 済み。
    public Dictionary<string, string> CategoryPrefixRules { get; set; } = new();
    public Dictionary<string, MonthData> Months { get; set; } = new();
}

// ── カテゴリ ──────────────────────────────────────
public class Category
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";   // パレットの16進カラー
    public int SortOrder { get; set; }
}

// ── カード ──────────────────────────────────────
public class Card
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";
    public string AccountId { get; set; } = "";   // 引き落とし口座
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }            // ソフト削除（過去明細の名前引きのため残す）
}

// ── 口座 ──────────────────────────────────────────
public class Account
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsBonusAccount { get; set; }
    // 現金の手元残高・使い道を追跡する特殊口座（#77）。アクティブ（!IsDeleted）は同時に1個のみ。
    public bool IsWallet { get; set; }
    // 財布を作成した月（yyyyMM）。他の口座と異なり、財布は「作成した月」を恒久的な起点（開始残高の入力月）
    // とするため、この月より前へは月次展開（EnsureMonth）で遡って台帳を作らない（#77 フォローアップ）。
    public string? WalletStartYm { get; set; }
}

// ── 固定費マスタ ──────────────────────────────────
public class FixedCost
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";
    public string AccountId { get; set; } = "";
    public decimal Amount { get; set; }
    public string? StartYm { get; set; }   // null / "yyyy"（年のみ）/ "yyyyMM"
    public string? EndYm { get; set; }     // null / "yyyy"（年のみ）/ "yyyyMM"
    public List<BonusSetting> BonusSettings { get; set; } = new();
    public int SortOrder { get; set; }
    // 変動費（#87）：true の場合 Amount は既定値/初期値に過ぎず、月次管理タブで月ごとに編集した額が優先される。
    public bool IsVariable { get; set; }

    // 有効期間の下限・上限を Ym として返す。年のみ指定は開始=1月 / 終了=12月 とみなす。
    public Ym? StartBound() => ParseBound(StartYm, 1);
    public Ym? EndBound() => ParseBound(EndYm, 12);

    private static Ym? ParseBound(string? s, int monthIfYearOnly)
    {
        if (string.IsNullOrEmpty(s) || s.Length < 4) return null;
        var year = int.Parse(s[..4]);
        var month = s.Length >= 6 ? int.Parse(s[4..6]) : monthIfYearOnly;
        return new Ym(year, month);
    }
}

public class BonusSetting
{
    public string Id { get; set; } = Util.NewId();
    public int Month { get; set; }
    public BonusType Type { get; set; }
    public decimal Amount { get; set; }
}

public enum BonusType { Add, Separate }

// ── 月次データ ────────────────────────────────────
public class MonthData
{
    public Dictionary<string, Ledger> Ledgers { get; set; } = new();
    public List<Transfer> Transfers { get; set; } = new();
    public List<CardDetail> CardDetails { get; set; } = new();   // この月に計上するカード利用明細

    // カード×月の「実請求額」（口座引き落とし額）。リボ・分割で利用額と引き落とし額が
    // 異なる場合に設定する。未設定のカードは明細合計（＝一括払い）を引き落とし額とみなす。
    // 明細合計は消費＝統計用としてそのまま残し、口座末残高だけをこの額で補正する。
    public Dictionary<string, decimal> CardBilled { get; set; } = new();
}

// カード利用明細（合計が ExpandCards で月次 Debit に反映される）
public class CardDetail
{
    public string Id { get; set; } = Util.NewId();
    public string CardId { get; set; } = "";
    public string Date { get; set; } = "";        // 利用日 "yyyy-MM-dd"（表示用）
    public string Name { get; set; } = "";         // 利用先・摘要
    public decimal Amount { get; set; }
    public string? CategoryId { get; set; }        // 未設定=未分類
}

public class Ledger
{
    // 月初残高の起点（開始残高）。前月の同口座台帳が無い「起点月」でのみ使う。
    // それ以外の月は前月末から自動計算するため、この値は参照されない。
    public decimal Confirmed { get; set; }
    public decimal Salary { get; set; }
    public decimal Bonus { get; set; }
    public List<Debit> Debits { get; set; } = new();
    public List<IncomeItem> Incomes { get; set; } = new();   // 臨時収入（給料/ボーナスとは別。統計の収入総額には別系列で反映）
    public decimal AtmDeposit { get; set; }                  // ATM入金（口座増・資産移動のため統計には含めない）
    public decimal AtmWithdraw { get; set; }                 // ATM出金（口座減・資産移動のため統計には含めない）
    // 財布→口座のATM入金明細（財布台帳でのみ使用・#77）。LedgerEngine.ExpandWallet が
    // 全件合算して財布自身の AtmWithdraw と、対象口座ごとの AtmDeposit へ実体化（materialize）する。
    public List<WalletAtmDeposit> WalletAtmDeposits { get; set; } = new();
}

// 財布→口座のATM入金1件（対象口座と金額）。#77。
public class WalletAtmDeposit
{
    public string Id { get; set; } = Util.NewId();
    public string AccountId { get; set; } = "";
    public decimal Amount { get; set; }
}

// 臨時収入の明細（給料・ボーナス以外の収入。入力名ごとに統計へ内訳表示）
public class IncomeItem
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
}

public class Debit
{
    public string Id { get; set; } = Util.NewId();
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public bool IsFixed { get; set; }
    public bool IsVariable { get; set; }  // 変動費（#87）由来：固定費と異なり月次管理タブで金額編集可
    public bool AmountOverridden { get; set; }  // 変動費（#87）：ユーザーが金額を手動編集済みか。
                                                 // 当月のみ、true ならマスタ変更の再展開で上書きしない（翌月以降は編集有無に関わらず常にマスタへ追随）。
    public string? FixedCostId { get; set; }
    public string? CardId { get; set; }   // カード由来 Debit の目印（明細合計を反映・読み取り専用）
    // 財布の現金支出（#77）のみ手入力で設定。他の Debit（固定費・カード・通常口座の手入力支出）は
    // 未設定のまま＝統計のカテゴリ別集計（GraphPage.BuildCategorySpend）には混入しない。
    public string? CategoryId { get; set; }
}

public class Transfer
{
    public string Id { get; set; } = Util.NewId();
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public decimal Amount { get; set; }
}
