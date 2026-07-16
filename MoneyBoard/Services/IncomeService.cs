using System.Net;
using System.Net.Http.Json;
using MoneyBoardShared;

namespace MoneyBoard.Services;

/// <summary>
/// 収入（給与）記録の API 通信（/api/income）。家計簿の StorageService・ポートフォリオの
/// PortfolioService とは別経路（#107）。取得失敗は例外として呼び出し元へ伝播させる（空での上書き防止）。
/// </summary>
public class IncomeService(HttpClient http, AuthService auth)
{
    private const string ApiPath = "api/income";
    private string? _etag;   // サーバーの最新 etag（保存時に If-Match で送り返す）

    public async Task<IncomeData?> LoadAsync()
    {
        await auth.ApplyTokenAsync(http);
        using var resp = await http.GetAsync(ApiPath);
        if (resp.StatusCode == HttpStatusCode.Forbidden)
            throw new AccessPendingException();   // 未承認＝承認待ち
        resp.EnsureSuccessStatusCode();
        var env = await resp.Content.ReadFromJsonAsync<IncomeEnvelope>();
        if (env == null) return null;
        _etag = env.Etag;
        return env.Data;
    }

    public async Task<SaveResult> SaveAsync(IncomeData data)
    {
        try
        {
            await auth.ApplyTokenAsync(http);
            var env = new IncomeEnvelope { Etag = _etag, Data = data };
            using var resp = await http.PostAsJsonAsync(ApiPath, env);
            if (resp.StatusCode == HttpStatusCode.PreconditionFailed)
                return SaveResult.Conflict;
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"SaveIncome failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                return SaveResult.Error;
            }
            var result = await resp.Content.ReadFromJsonAsync<IncomeSaveResponse>();
            if (!string.IsNullOrEmpty(result?.Etag)) _etag = result!.Etag;
            return SaveResult.Ok;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SaveIncome failed: {ex.Message}");
            return SaveResult.Error;
        }
    }
}
