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
    // 利用先(店名) → カテゴリId。一括適用で記憶し、以降の取込で自動分類する。
    public Dictionary<string, string> CategoryRules { get; set; } = new();
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
    public string? FixedCostId { get; set; }
    public string? CardId { get; set; }   // カード由来 Debit の目印（明細合計を反映・読み取り専用）
}

public class Transfer
{
    public string Id { get; set; } = Util.NewId();
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public decimal Amount { get; set; }
}
