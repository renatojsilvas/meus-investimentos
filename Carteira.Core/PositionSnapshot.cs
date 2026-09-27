namespace Carteira.Core;

public record PositionSnapshot(
    Guid AssetId,
    string Codigo,
    string Nome,
    decimal Quantidade,
    decimal Custo,
    decimal CustoMedio,
    decimal ResultadoRealizado,
    decimal? Preco,
    decimal? ValorMercado,
    decimal? Rent,
    decimal? RentPercentual);
