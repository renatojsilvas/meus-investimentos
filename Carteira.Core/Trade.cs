namespace Carteira.Core;

public record Trade(
    Guid Id,
    Guid AssetId,
    DateOnly Data,
    TradeType Tipo,
    decimal Quantidade,
    decimal PrecoUnitario,
    decimal Taxas,
    string Moeda,
    string ChaveImportacao);
