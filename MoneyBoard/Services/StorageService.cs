using System.Net.Http.Json;
using MoneyBoardShared;

namespace MoneyBoard.Services;

public class StorageService(HttpClient http)
{
    private const string ApiPath = "api/data";

    public async Task<string?> GetAsync(string key)
    {
        try
        {
            var state = await http.GetFromJsonAsync<AppState>(ApiPath);
            return state == null ? null : System.Text.Json.JsonSerializer.Serialize(state);
        }
        catch
        {
            return null;
        }
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
