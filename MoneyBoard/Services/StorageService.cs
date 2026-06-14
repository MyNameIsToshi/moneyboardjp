using System.Net;
using System.Net.Http.Json;
using MoneyBoardShared;

namespace MoneyBoard.Services;

public enum SaveResult { Ok, Conflict, Error }

/// <summary>サインイン済みだが未承認（オーナーの承認待ち）。サーバーが 403 を返したときに送出。</summary>
public class AccessPendingException : Exception { }

public class StorageService(HttpClient http, AuthService auth)
{
    private const string ApiPath = "api/data";

    // サーバーから受け取った最新の etag（設定＋月ごと）。保存時に If-Match で送り返す。
    private string? _settingsEtag;
    private readonly Dictionary<string, string> _monthEtags = new();

    // 直近の GET で判明した、現在のユーザーがオーナーか（承認管理UIの出し分け用）。
    public bool IsOwner { get; private set; }

    // 取得失敗（通信エラー・500 等）は例外として呼び出し元へ伝播させる
    // （失敗を「データなし」と誤認して実データを空で上書きするのを防ぐため）。
    public async Task<AppState?> LoadAsync()
    {
        await auth.ApplyTokenAsync(http);
        using var resp = await http.GetAsync(ApiPath);
        if (resp.StatusCode == HttpStatusCode.Forbidden)
            throw new AccessPendingException();   // 未承認＝承認待ち
        resp.EnsureSuccessStatusCode();
        var env = await resp.Content.ReadFromJsonAsync<DataEnvelope>();
        if (env == null) return null;

        IsOwner = env.IsOwner;
        _settingsEtag = env.Settings?.Etag;
        _monthEtags.Clear();

        var state = new AppState
        {
            SchemaVersion = env.Settings?.SchemaVersion ?? 1,
            Accounts = env.Settings?.Accounts ?? new(),
            FixedCosts = env.Settings?.FixedCosts ?? new(),
            Categories = env.Settings?.Categories ?? new(),
            Cards = env.Settings?.Cards ?? new(),
            CategoryRules = env.Settings?.CategoryRules ?? new()
        };
        foreach (var (ym, m) in env.Months)
        {
            if (!string.IsNullOrEmpty(m.Etag)) _monthEtags[ym] = m.Etag;
            state.Months[ym] = new MonthData { Ledgers = m.Ledgers, Transfers = m.Transfers, CardDetails = m.CardDetails, CardBilled = m.CardBilled };
        }
        return state;
    }

    /// <summary>変更分のみ（changes）を送信する。etag は保持中の値を付与し、成功時に更新する。</summary>
    public async Task<SaveResult> SaveAsync(DataEnvelope changes)
    {
        try
        {
            await auth.ApplyTokenAsync(http);
            if (changes.Settings != null)
                changes.Settings.Etag = _settingsEtag;
            foreach (var (ym, m) in changes.Months)
                m.Etag = _monthEtags.GetValueOrDefault(ym);

            using var resp = await http.PostAsJsonAsync(ApiPath, changes);
            if (resp.StatusCode == HttpStatusCode.PreconditionFailed)
                return SaveResult.Conflict;
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"SaveData failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                return SaveResult.Error;
            }

            var result = await resp.Content.ReadFromJsonAsync<SaveResponse>();
            if (result != null)
            {
                if (!string.IsNullOrEmpty(result.SettingsEtag)) _settingsEtag = result.SettingsEtag;
                foreach (var (ym, e) in result.MonthEtags) _monthEtags[ym] = e;
            }
            return SaveResult.Ok;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SaveData failed: {ex.Message}");
            return SaveResult.Error;
        }
    }
}
