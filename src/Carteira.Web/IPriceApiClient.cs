namespace Carteira.Web;

public interface IPriceApiClient
{
    Task<IReadOnlyList<PrecoApiDto>> GetPrecosAsync(DateOnly dataBase, CancellationToken ct);

    Task<IReadOnlyList<PrecoApiDto>> GetHistoricoAsync(string codigo, DateOnly dataInicio, DateOnly dataFim, CancellationToken ct);
}
