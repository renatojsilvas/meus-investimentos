namespace Carteira.Web;

public interface IPriceApiClient
{
    Task<IReadOnlyList<PrecoApiDto>> GetPrecosAsync(DateOnly dataBase, CancellationToken ct);
}
