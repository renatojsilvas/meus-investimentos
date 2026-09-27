namespace Carteira.Core;

public record ParseResult(
    IReadOnlyList<TradeRow> Linhas,
    IReadOnlyList<ParseError> Erros);
