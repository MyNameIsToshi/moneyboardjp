using System.Text.Json;
using MoneyBoardShared;

namespace MoneyBoard.Services;

/// <summary>
/// アプリ状態(AppState)の保持と永続化を担う。
/// メモリ上は AppState 全体を保持しつつ、保存は「設定」と「月ごと」の
/// ドキュメント単位で行い、前回保存分と異なるパートだけを送る(snapshot-diff)。
/// </summary>
public class AppStateStore(StorageService storage)
{
    public AppState State { get; private set; } = new();
    public bool IsLoaded { get; private set; }
    public bool IsPending { get; private set; }   // サインイン済みだが未承認（承認待ち）
    public bool IsOwner => storage.IsOwner;        // 現在のユーザーがオーナーか

    /// <summary>未保存のインライン編集があるか（#141：アプリ更新ダイアログの操作中判定に使用）。
    /// RequestSave で true、デバウンス保存（成功/競合/失敗いずれも）が完了したら false になる。</summary>
    public bool HasPendingChanges { get; private set; }

    /// <summary>保存が競合し、最新状態を読み込み直したときに発火（UI 再描画用）。</summary>
    public event Action? StateReloadedExternally;

    /// <summary>サーバーが版数フロア違反で保存を拒否したときに発火（#155）。
    /// AppUpdateService が購読し、上書きせずアプリ更新ダイアログを出す。</summary>
    public event Action? SchemaOutdated;

    /// <summary>HasPendingChanges が変化したときに発火（#141）。</summary>
    public event Action? Changed;

    // 保存はすべて _saveLock で直列化し、同時実行による更新ロストを防ぐ。
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private CancellationTokenSource? _debounceCts;

    // 差分検出用の「最後に保存した内容」スナップショット（JSON 文字列）
    private string _settingsBaseline = "";
    private readonly Dictionary<string, string> _monthBaseline = new();

    /// <summary>
    /// サーバーから状態を読み込む。成功時 true。
    /// 取得・解析に失敗した場合は State を変更せず false を返す（空での上書き防止）。
    /// </summary>
    public async Task<bool> LoadAsync()
    {
        IsPending = false;
        try
        {
            State = await storage.LoadAsync() ?? new AppState();
            SeedBaselines();   // 移行前の内容を基準点にする（移行差分を後続の保存で検出するため）
            var migrated = SchemaMigration.Apply(State);
            IsLoaded = true;
            if (migrated) RequestSave();   // 移行結果をストレージへ書き戻す
            return true;
        }
        catch (AccessPendingException)
        {
            IsPending = true;   // 承認待ち（実データではない・UIで承認待ち画面を出す）
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 連続入力をまとめて1回だけ保存する（デバウンス）。
    /// </summary>
    public void RequestSave(int delayMs = 600)
    {
        SetPendingChanges(true);
        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = DebouncedSaveAsync(delayMs, cts.Token);
    }

    private void SetPendingChanges(bool value)
    {
        if (HasPendingChanges == value) return;
        HasPendingChanges = value;
        Changed?.Invoke();
    }

    private async Task DebouncedSaveAsync(int delayMs, CancellationToken token)
    {
        try { await Task.Delay(delayMs, token); }
        catch (TaskCanceledException) { return; }
        if (token.IsCancellationRequested) return;
        await SaveAsync();
    }

    /// <summary>即時保存。変更されたドキュメント（設定/各月）だけを送信する。</summary>
    public async Task SaveAsync()
    {
        // データ未読込のうちは保存しない。空の初期 State（accounts 等が空）で
        // サーバーの実データを上書きしてしまう事故を防ぐ（例: 家計簿未読込のページから
        // 保存フラッシュが呼ばれるケース）。読込成功で IsLoaded=true になって初めて保存する。
        if (!IsLoaded) { SetPendingChanges(false); return; }
        _debounceCts?.Cancel();
        await _saveLock.WaitAsync();
        try
        {
            var changes = new DataEnvelope();

            var settingsJson = SerializeSettings();
            bool settingsChanged = settingsJson != _settingsBaseline;
            if (settingsChanged) changes.Settings = BuildSettingsPart();

            var changedMonthJson = new Dictionary<string, string>();
            foreach (var (ym, mo) in State.Months)
            {
                var json = SerializeMonth(mo);
                if (!_monthBaseline.TryGetValue(ym, out var prev) || prev != json)
                {
                    changes.Months[ym] = BuildMonthPart(mo);
                    changedMonthJson[ym] = json;
                }
            }

            if (changes.Settings == null && changes.Months.Count == 0)
                return; // 変更なし

            var result = await storage.SaveAsync(changes);
            if (result == SaveResult.Conflict)
            {
                // 別タブ/別端末が先に更新済み。ローカルの変更で上書きせず最新を読み込む。
                // 読み込んだ内容は LoadAsync と同じくスキーマ移行を通す（#147）。旧スキーマの端末が
                // 先に保存していた場合、ここで移行を挟まないとそのセッションは未移行の State で
                // 動き続ける（例: 財布が Type=Normal のまま＝財布として扱われなくなる）。
                try
                {
                    State = await storage.LoadAsync() ?? State;
                    SeedBaselines();
                    if (SchemaMigration.Apply(State)) RequestSave();
                }
                catch { /* 再読込失敗時は既存 State を維持 */ }
                StateReloadedExternally?.Invoke();
            }
            else if (result == SaveResult.Ok)
            {
                if (settingsChanged) _settingsBaseline = settingsJson;
                foreach (var (ym, json) in changedMonthJson) _monthBaseline[ym] = json;
            }
            else if (result == SaveResult.SchemaOutdated)
            {
                // サーバーの保存済みデータより版数が低い＝このクライアントは古い。ローカルの変更で
                // サーバーを上書きせず、アプリ更新を促す。強制更新→リロードで最新データを読み直すため、
                // この未保存編集は再送されず破棄される（#155「マージせず拒否」設計どおりの割り切り）。
                SchemaOutdated?.Invoke();
            }
            // Error: ベースラインは据え置き → 次回保存で再送される
        }
        finally
        {
            // 直列化を待つ間に新しい RequestSave（デバウンス）が入っていた場合、その保存はまだ走っていない
            // ＝未保存の編集が残っているため、ここで false にしない（誤って「操作中でない」と判定し更新
            // ダイアログが編集中に割り込むのを防ぐ）。その保存が走るとき自身の開始時の Cancel を経て false になる。
            var pending = _debounceCts;
            if (pending is null || pending.IsCancellationRequested) SetPendingChanges(false);
            _saveLock.Release();
        }
    }

    private void SeedBaselines()
    {
        _settingsBaseline = SerializeSettings();
        _monthBaseline.Clear();
        foreach (var (ym, mo) in State.Months)
            _monthBaseline[ym] = SerializeMonth(mo);
    }

    // 同名プロパティを機械的にコピー（ObjectSync）。AppState にフィールドを追加したときに
    // ここを更新し忘れる事故（#134で2度発生・#136）を防ぐ。
    private SettingsPart BuildSettingsPart() => ObjectSync.CopyMatchingProperties(State, new SettingsPart());

    // 同名プロパティを機械的にコピー（ObjectSync）。MonthData にフィールドを追加したときに
    // ここを更新し忘れる事故（#134/#136 と同型）を防ぐ（#137）。
    private static MonthPart BuildMonthPart(MonthData mo) => ObjectSync.CopyMatchingProperties(mo, new MonthPart());

    private string SerializeSettings() => JsonSerializer.Serialize(BuildSettingsPart());
    private static string SerializeMonth(MonthData mo) => JsonSerializer.Serialize(BuildMonthPart(mo));
}
