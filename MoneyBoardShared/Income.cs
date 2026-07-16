namespace MoneyBoardShared;

/// <summary>
/// 収入（給与）記録の永続データ（#107）。家計簿（settings / month:yyyyMM）とは独立した
/// Cosmos ドキュメント `income`（portfolio と同格）として保存する。理由：月ごとに積み上がる
/// 実績データであり、設定（口座・カテゴリ等）の doc に混ぜると設定が肥大化して「設定」の
/// 定義が崩れるため。
///
/// **総支給（額面）** は常にここで記録する。残高計算・統計に一切影響しない記録専用の値で、
/// 過去・現在・未来のどの月でも自由に記録できる。
///
/// **手取り**は月次（Ledger.Salary/Bonus）が既にある場合はそれを唯一の真実として扱い、
/// ここには複製しない（現在月・未来月のみ収入記録ページから月次へ同期・月次が既に存在する
/// 過去月は月次の凍結値を表示するだけで編集不可）。
/// **ただし月次データが1度も作られていない過去月**は、月次を新規作成する副作用
/// （起点月/開始残高の自動連鎖が崩れる）を避けるため、ここに独立して記録する
/// （`SalaryNet`/`BonusNet`）。月次とは一切連携しない＝後から月次側にその月のデータができても同期しない。
/// </summary>
public class IncomeData
{
    public int SchemaVersion { get; set; } = 1;

    // ym("yyyyMM") → その月の収入記録。
    public Dictionary<string, IncomeMonthRecord> Records { get; set; } = new();
}

/// <summary>ある月の収入記録。総支給は常にここが真実。手取り(Net)は「月次データが無い過去月」でのみ使う。</summary>
public class IncomeMonthRecord
{
    public decimal SalaryGross { get; set; }
    public decimal BonusGross { get; set; }
    public decimal SalaryNet { get; set; }
    public decimal BonusNet { get; set; }
}

/// <summary>GET /api/income のレスポンス兼 POST /api/income のリクエスト。</summary>
public class IncomeEnvelope
{
    public string? Etag { get; set; }
    public IncomeData Data { get; set; } = new();
}

/// <summary>POST /api/income の成功レスポンス（保存後の新しい etag）。</summary>
public class IncomeSaveResponse
{
    public string? Etag { get; set; }
}
