namespace Carteira.Core;

public record PortfolioSnapshot(
    IReadOnlyList<PositionSnapshot> Posicoes,
    decimal CustoTotal,
    decimal ValorTotal,
    decimal RentTotal,
    decimal RentPercentual,
    decimal ResultadoRealizadoTotal,
    bool TemPosicaoSemPreco);
