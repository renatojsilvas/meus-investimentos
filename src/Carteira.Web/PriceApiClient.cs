using System.Net.Http.Json;
using System.Text.Json;

namespace Carteira.Web;

public class PriceApiClient(HttpClient httpClient) : IPriceApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<PrecoApiDto>> GetPrecosAsync(DateOnly dataBase, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync($"precos?dataBase={dataBase:yyyy-MM-dd}", ct);
        response.EnsureSuccessStatusCode();

        var precos = await response.Content.ReadFromJsonAsync<List<PrecoApiDto>>(JsonOptions, ct);
        return precos ?? [];
    }
}
