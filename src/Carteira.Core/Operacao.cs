namespace Carteira.Core;

public record Operacao(
    Guid Id,
    Guid TitularId,
    Guid AtivoId,
    DateOnly Data,
    TipoOperacao Tipo,
    decimal Quantidade,
    decimal PrecoUnitario,
    decimal Taxas,
    string Moeda,
    string ChaveImportacao);
