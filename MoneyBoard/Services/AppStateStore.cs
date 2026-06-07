using MoneyBoardShared;

namespace MoneyBoard.Services;

/// <summary>
/// アプリ状態(AppState)の保持と永続化を担う。
/// 読み込み・保存・デバウンス・保存の直列化・競合(412)時の再読込を集約する。
/// （#4 でドキュメント分割する際は主にこのクラスを書き換える）
/// </summary>
public class AppStateStore(StorageService storage)
{
    public AppState State { get; private set; } = new();
    public bool IsLoaded { get; private set; }

    /// <summary>保存が競合し、最新状態を読み込み直したときに発火（UI 再描画用）。</summary>
    public event Action? StateReloadedExternally;

    // 保存はすべて _saveLock で直列化し、フル状態アップロードが
    // 同時に走って互いを古い内容で上書きする（更新ロスト）のを防ぐ。
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private CancellationTokenSource? _debounceCts;

    /// <summary>
    /// サーバーから状態を読み込む。成功時 true。
    /// 取得・解析に失敗した場合は State を変更せず、保存も行わずに false を返す
    /// （失敗を「データなし」と誤認して実データを空で上書きするのを防ぐため）。
    /// </summary>
    public async Task<bool> LoadAsync()
    {
        try
        {
            // API は新規ユーザーに対し 200 + 空の AppState を返すため null にはならない。
            State = await storage.LoadAsync() ?? new AppState();

            // 旧スキーマなら最新へ移行し、移行が発生したときだけ永続化する。
            if (SchemaMigration.Apply(State))
                await SaveAsync();

            IsLoaded = true;
            return true;
        }
        catch
        {
            // 通信エラー・JSON 破損など。State は触らず保存もしない。
            return false;
        }
    }

    /// <summary>
    /// 連続入力をまとめて1回だけ保存する（デバウンス）。
    /// 金額入力など高頻度の編集で毎キーストローク POST するのを防ぐ。
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

    /// <summary>即時保存。保留中のデバウンスはキャンセルする（直後に最新状態を保存するため）。</summary>
    public async Task SaveAsync()
    {
        _debounceCts?.Cancel();
        await _saveLock.WaitAsync();
        try
        {
            State.UpdatedAt = DateTimeOffset.UtcNow;
            var result = await storage.SaveAsync(State);
            if (result == SaveResult.Conflict)
            {
                // 別タブ/別端末が先に更新済み。ローカルの変更で上書きせず最新を読み込む。
                try { State = await storage.LoadAsync() ?? State; }
                catch { /* 再読込失敗時は既存 State を維持 */ }
                StateReloadedExternally?.Invoke();
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
