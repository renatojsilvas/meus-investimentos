namespace Carteira.Core;

public record TradeRow(
    DateOnly Data,
    string Codigo,
    string Titulo,
    DateOnly Vencimento,
    TradeType Tipo,
    decimal Quantidade,
    decimal PrecoUnitario,
    decimal Taxas,
    string ChaveImportacao);
