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

    /// <summary>保存が競合し、最新状態を読み込み直したときに発火（UI 再描画用）。</summary>
    public event Action? StateReloadedExternally;

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
            SchemaMigration.Apply(State);   // 将来のスキーマ移行用（現状 no-op）
            SeedBaselines();
            IsLoaded = true;
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
        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = DebouncedSaveAsync(delayMs, cts.Token);
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
                try
                {
                    State = await storage.LoadAsync() ?? State;
                    SeedBaselines();
                }
                catch { /* 再読込失敗時は既存 State を維持 */ }
                StateReloadedExternally?.Invoke();
            }
            else if (result == SaveResult.Ok)
            {
                if (settingsChanged) _settingsBaseline = settingsJson;
                foreach (var (ym, json) in changedMonthJson) _monthBaseline[ym] = json;
            }
            // Error: ベースラインは据え置き → 次回保存で再送される
        }
        finally
        {
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

    private SettingsPart BuildSettingsPart() => new()
    {
        SchemaVersion = State.SchemaVersion,
        Accounts = State.Accounts,
        FixedCosts = State.FixedCosts,
        Categories = State.Categories,
        Cards = State.Cards,
        CategoryRules = State.CategoryRules
    };

    private static MonthPart BuildMonthPart(MonthData mo) => new()
    {
        Ledgers = mo.Ledgers,
        Transfers = mo.Transfers,
        CardDetails = mo.CardDetails,
        CardBilled = mo.CardBilled
    };

    private string SerializeSettings() => JsonSerializer.Serialize(BuildSettingsPart());
    private static string SerializeMonth(MonthData mo) => JsonSerializer.Serialize(BuildMonthPart(mo));
}
