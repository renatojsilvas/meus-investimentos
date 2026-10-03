namespace Carteira.Core;

public record PontoSerieDiaria(
    DateOnly Data,
    Guid TitularId,
    string Slug,
    string NomeTitular,
    decimal Custo,
    decimal CustoComPreco,
    decimal Valor,
    decimal Rentabilidade,
    decimal ResultadoRealizado,
    bool TemPosicaoSemPreco);
