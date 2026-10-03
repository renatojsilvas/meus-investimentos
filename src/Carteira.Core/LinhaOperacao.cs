namespace Carteira.Core;

public record LinhaOperacao(
    DateOnly Data,
    string Titular,
    string Codigo,
    string Titulo,
    DateOnly Vencimento,
    TipoOperacao Tipo,
    decimal Quantidade,
    decimal PrecoUnitario,
    decimal Taxas,
    string ChaveImportacao);
