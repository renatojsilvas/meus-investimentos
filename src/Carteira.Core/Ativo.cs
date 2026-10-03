namespace Carteira.Core;

public record Ativo(
    Guid Id,
    ClasseAtivo Classe,
    string Codigo,
    string Nome,
    DateOnly Vencimento);
