namespace Carteira.Web;

public interface ICotacoesB3Client
{
    Task<IReadOnlyList<CotacaoB3Dto>> GetFechamentosAsync(
        IReadOnlyCollection<string> tickers, DateOnly de, DateOnly ate, CancellationToken ct);
}
