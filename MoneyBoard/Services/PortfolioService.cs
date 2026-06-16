using System.Net;
using System.Net.Http.Json;
using MoneyBoardShared;

namespace MoneyBoard.Services;

/// <summary>
/// 証券ポートフォリオの API 通信（/api/portfolio）。家計簿の StorageService とは別経路。
/// 取得失敗は例外として呼び出し元へ伝播させる（空での上書き防止）。
/// </summary>
public class PortfolioService(HttpClient http, AuthService auth)
{
    private const string ApiPath = "api/portfolio";
    private string? _etag;   // サーバーの最新 etag（保存時に If-Match で送り返す）

    /// <summary>直近の GET で得た「本人が TSMC 社員か」（Owner は常に true）。ESPP UI 表示判定用。</summary>
    public bool IsTsmcEmployee { get; private set; }

    public async Task<PortfolioData?> LoadAsync()
    {
        await auth.ApplyTokenAsync(http);
        using var resp = await http.GetAsync(ApiPath);
        if (resp.StatusCode == HttpStatusCode.Forbidden)
            throw new AccessPendingException();   // 未承認＝承認待ち
        resp.EnsureSuccessStatusCode();
        var env = await resp.Content.ReadFromJsonAsync<PortfolioEnvelope>();
        if (env == null) return null;
        _etag = env.Etag;
        IsTsmcEmployee = env.IsTsmcEmployee;
        return env.Data;
    }

    public async Task<SaveResult> SaveAsync(PortfolioData data)
    {
        try
        {
            await auth.ApplyTokenAsync(http);
            var env = new PortfolioEnvelope { Etag = _etag, Data = data };
            using var resp = await http.PostAsJsonAsync(ApiPath, env);
            if (resp.StatusCode == HttpStatusCode.PreconditionFailed)
                return SaveResult.Conflict;
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"SavePortfolio failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                return SaveResult.Error;
            }
            var result = await resp.Content.ReadFromJsonAsync<PortfolioSaveResponse>();
            if (!string.IsNullOrEmpty(result?.Etag)) _etag = result!.Etag;
            return SaveResult.Ok;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SavePortfolio failed: {ex.Message}");
            return SaveResult.Error;
        }
    }
}
