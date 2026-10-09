using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Carteira.Web;

public class BcbClient(HttpClient httpClient) : IBcbClient
{
    private const int SerieCdi = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<IndiceApiDto>> GetCdiAsync(DateOnly dataInicial, DateOnly dataFinal, CancellationToken ct)
    {
        var url = $"dados/serie/bcdata.sgs.{SerieCdi}/dados?formato=json&dataInicial={dataInicial:dd/MM/yyyy}&dataFinal={dataFinal:dd/MM/yyyy}";

        using var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType != "application/json")
            throw new InvalidOperationException(
                $"Resposta do BCB (série {SerieCdi}, {dataInicial:dd/MM/yyyy} a {dataFinal:dd/MM/yyyy}) veio com content-type '{contentType}' em vez de 'application/json'.");

        var registros = await response.Content.ReadFromJsonAsync<List<BcbRegistroDto>>(JsonOptions, ct) ?? [];

        return registros
            .Select(r => new IndiceApiDto(
                DateOnly.ParseExact(r.Data, "dd/MM/yyyy", CultureInfo.InvariantCulture),
                decimal.Parse(r.Valor, CultureInfo.InvariantCulture)))
            .ToList();
    }

    private record BcbRegistroDto(string Data, string Valor);
}
