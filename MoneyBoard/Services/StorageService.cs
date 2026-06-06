using System.Net.Http.Json;
using MoneyBoardShared;

namespace MoneyBoard.Services;

public class StorageService(HttpClient http)
{
    private const string ApiPath = "api/data";

    // 取得失敗（通信エラー・500 等）は例外として呼び出し元へ伝播させる。
    // ここで握りつぶして null を返すと、呼び出し元が「データなし(新規)」と誤認し
    // 実データを空で上書き保存してしまうため。null は本当に中身が無い場合のみ。
    public async Task<string?> GetAsync(string key)
    {
        var state = await http.GetFromJsonAsync<AppState>(ApiPath);
        return state == null ? null : System.Text.Json.JsonSerializer.Serialize(state);
    }

    public async Task SetAsync(string key, string value)
    {
        try
        {
            var state = System.Text.Json.JsonSerializer.Deserialize<AppState>(value);
            await http.PostAsJsonAsync(ApiPath, state);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SaveData failed: {ex.Message}");
        }
    }
}
