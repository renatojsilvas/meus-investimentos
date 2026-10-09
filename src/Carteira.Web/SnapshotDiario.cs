namespace Carteira.Web;

public record SnapshotDiario(
    DateOnly Data,
    Guid TitularId,
    decimal Custo,
    decimal CustoComPreco,
    decimal Valor,
    decimal Rentabilidade,
    decimal ResultadoRealizado,
    bool TemPosicaoSemPreco,
    decimal? ValorCdi);
