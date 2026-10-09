namespace Carteira.Web;

public interface IBcbClient
{
    Task<IReadOnlyList<IndiceApiDto>> GetCdiAsync(DateOnly dataInicial, DateOnly dataFinal, CancellationToken ct);
}
