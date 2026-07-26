using System.Text.Json;
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
    public bool IsEsppEligible => svc.IsEsppEligible;   // ESPP UI 表示可否（Owner は常に true）

    /// <summary>保存競合で最新を読み込み直したときに発火（UI 再描画用）。
    /// 破棄されたローカル編集の項目数（#158。0件/検出不能なら null）を引数で渡す。</summary>
    public event Action<int?>? StateReloadedExternally;

    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private CancellationTokenSource? _debounceCts;

    // 差分検出用の「最後にサーバーと一致していた内容」スナップショット（JSON 文字列）。
    // 差分送信はしない（設計どおり全体保存）ため保存判断には使わず、競合時に破棄件数を出すためだけに使う。
    private string _baseline = "";

    // 価格更新ボタンで自動再取得・派生する価格系フィールドはユーザー編集ではないため、競合時の破棄件数から
    // 除外する（#158）。含めると、価格更新1回の競合で保有 N 銘柄ぶん（Current/Prev/スナップショット）が
    // 「未保存の変更 N 件が失われた」と誤表示される。再読込後は次回の価格更新で再取得され実損もない。
    private static readonly HashSet<string> AutoManagedPriceFields = new()
    {
        nameof(PortfolioData.CurrentPrices),
        nameof(PortfolioData.PrevPrices),
        nameof(PortfolioData.Snapshots),
    };

    public async Task<bool> LoadAsync()
    {
        IsPending = false;
        try
        {
            Data = await svc.LoadAsync() ?? new PortfolioData();
            _baseline = JsonSerializer.Serialize(Data);
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
                // 上書きで消える側（ローカルの未保存編集）の項目数を、ベースラインを更新する前に数えておく（#158）。
                var baselineData = string.IsNullOrEmpty(_baseline) ? null : JsonSerializer.Deserialize<PortfolioData>(_baseline);
                var itemCount = baselineData != null ? ConflictDiff.CountItemDiff(baselineData, Data, AutoManagedPriceFields) : 0;

                // 別タブ/別端末が先に更新済み。ローカルで上書きせず最新を読み込む。
                // 再読込に失敗したときは Data も _baseline も据え置く（AppStateStore と同じく、次回保存で
                // 再送・再カウントできるように）。ここで _baseline を現 Data に更新してしまうと、失敗後の
                // 次の競合で「破棄件数 0」と過少表示される。
                var reloaded = false;
                try
                {
                    Data = await svc.LoadAsync() ?? Data;
                    _baseline = JsonSerializer.Serialize(Data);
                    reloaded = true;
                }
                catch { /* 再読込失敗時は維持 */ }

                // 実際に再読込できたときだけ「再読み込みした／N 件失われた」と通知する。失敗時は編集が
                // 保持され次回再送されるため、失われたと断定しない。
                if (reloaded) StateReloadedExternally?.Invoke(itemCount > 0 ? itemCount : null);
            }
            else if (result == SaveResult.Ok)
            {
                _baseline = JsonSerializer.Serialize(Data);
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
