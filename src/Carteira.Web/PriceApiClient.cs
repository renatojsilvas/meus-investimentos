using System.Net;
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

    public async Task<IReadOnlyList<PrecoApiDto>> GetHistoricoAsync(string codigo, DateOnly dataInicio, DateOnly dataFim, CancellationToken ct)
    {
        var url = $"titulos/{codigo}/precos?dataInicio={dataInicio:yyyy-MM-dd}&dataFim={dataFim:yyyy-MM-dd}";

        var response = await httpClient.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta
                ?? response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow
                ?? TimeSpan.FromSeconds(60);
            response.Dispose();

            if (retryAfter > TimeSpan.Zero)
                await Task.Delay(retryAfter, ct);

            response = await httpClient.GetAsync(url, ct);
        }

        using (response)
        {
            response.EnsureSuccessStatusCode();

            var precos = await response.Content.ReadFromJsonAsync<List<PrecoApiDto>>(JsonOptions, ct);
            return precos ?? [];
        }
    }
}
