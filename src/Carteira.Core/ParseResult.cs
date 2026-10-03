namespace Carteira.Core;

public record ParseResult(
    IReadOnlyList<LinhaOperacao> Linhas,
    IReadOnlyList<ParseError> Erros);
