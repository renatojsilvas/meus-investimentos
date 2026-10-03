namespace Carteira.Core;

public record CarteiraSnapshot(
    IReadOnlyList<PosicaoSnapshot> Posicoes,
    decimal CustoTotal,
    decimal ValorTotal,
    decimal RentabilidadeTotal,
    decimal RentabilidadePercentual,
    decimal ResultadoRealizadoTotal,
    bool TemPosicaoSemPreco);
