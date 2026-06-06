using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MoneyBoardShared;

namespace MoneyBoard.Services;

public enum SaveResult { Ok, Conflict, Error }

public class StorageService(HttpClient http)
{
    private const string ApiPath = "api/data";

    // サーバーから受け取った最新の ETag。保存時に If-Match で送り返し、
    // サーバー側が新しければ 412（競合）として検出する。
    private string? _etag;

    // 取得失敗（通信エラー・500 等）は例外として呼び出し元へ伝播させる。
    // ここで握りつぶして null を返すと、呼び出し元が「データなし(新規)」と誤認し
    // 実データを空で上書き保存してしまうため。null は本当に中身が無い場合のみ。
    public async Task<string?> GetAsync(string key)
    {
        using var resp = await http.GetAsync(ApiPath);
        resp.EnsureSuccessStatusCode();
        _etag = resp.Headers.ETag?.Tag;
        var state = await resp.Content.ReadFromJsonAsync<AppState>();
        return state == null ? null : JsonSerializer.Serialize(state);
    }

    public async Task<SaveResult> SetAsync(string key, string value)
    {
        try
        {
            var state = JsonSerializer.Deserialize<AppState>(value);
            using var req = new HttpRequestMessage(HttpMethod.Post, ApiPath)
            {
                Content = JsonContent.Create(state)
            };
            if (!string.IsNullOrEmpty(_etag))
                req.Headers.TryAddWithoutValidation("If-Match", _etag);

            using var resp = await http.SendAsync(req);
            if (resp.StatusCode == HttpStatusCode.PreconditionFailed)
                return SaveResult.Conflict;
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"SaveData failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                return SaveResult.Error;
            }
            _etag = resp.Headers.ETag?.Tag;
            return SaveResult.Ok;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SaveData failed: {ex.Message}");
            return SaveResult.Error;
        }
    }
}
