namespace Carteira.Core;

public record PosicaoSnapshot(
    Guid AtivoId,
    Guid TitularId,
    string Codigo,
    string Nome,
    string NomeTitular,
    decimal Quantidade,
    decimal Custo,
    decimal CustoMedio,
    decimal ResultadoRealizado,
    decimal? Preco,
    decimal? ValorMercado,
    decimal? Rentabilidade,
    decimal? RentabilidadePercentual);
