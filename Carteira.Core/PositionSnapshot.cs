namespace Carteira.Core;

public record PositionSnapshot(
    Guid AssetId,
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
    decimal? Rent,
    decimal? RentPercentual);
