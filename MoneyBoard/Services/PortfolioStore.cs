using MoneyBoardShared;

namespace MoneyBoard.Services;

/// <summary>
/// ポートフォリオ状態の保持と永続化。家計簿の AppStateStore と同流儀（デバウンス＋直列化、
/// 競合時は最新を読み直して通知）だが、ドキュメントが1つなので差分送信はせず全体を保存する。
/// </summary>
public class PortfolioStore(PortfolioService svc)
{
    public PortfolioData Data { get; private set; } = new();
    public bool IsLoaded { get; private set; }
    public bool IsPending { get; private set; }   // サインイン済みだが未承認

    /// <summary>保存競合で最新を読み込み直したときに発火（UI 再描画用）。</summary>
    public event Action? StateReloadedExternally;

    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private CancellationTokenSource? _debounceCts;

    public async Task<bool> LoadAsync()
    {
        IsPending = false;
        try
        {
            Data = await svc.LoadAsync() ?? new PortfolioData();
            IsLoaded = true;
            return true;
        }
        catch (AccessPendingException) { IsPending = true; return false; }
        catch { return false; }
    }

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

    public async Task SaveAsync()
    {
        _debounceCts?.Cancel();
        await _saveLock.WaitAsync();
        try
        {
            var result = await svc.SaveAsync(Data);
            if (result == SaveResult.Conflict)
            {
                // 別タブ/別端末が先に更新済み。ローカルで上書きせず最新を読み込む。
                try { Data = await svc.LoadAsync() ?? Data; } catch { /* 再読込失敗時は維持 */ }
                StateReloadedExternally?.Invoke();
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
