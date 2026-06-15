using System.Net.Http.Json;
using MoneyBoardShared;

namespace MoneyBoard.Services;

/// <summary>
/// 価格取得（/api/quote）。Yahoo Finance の株価と USD/JPY をサーバー経由で取得する。
/// 失敗時は null を返す（呼び出し元で「取得失敗」を表示）。
/// </summary>
public class QuoteService(HttpClient http, AuthService auth)
{
    public async Task<QuoteResponse?> FetchAsync(IEnumerable<string> symbols, IEnumerable<FundRef> funds)
    {
        try
        {
            await auth.ApplyTokenAsync(http);
            var req = new QuoteRequest { Symbols = symbols.ToList(), Funds = funds.ToList() };
            using var resp = await http.PostAsJsonAsync("api/quote", req);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"FetchQuote failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<QuoteResponse>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FetchQuote failed: {ex.Message}");
            return null;
        }
    }
}
