namespace Carteira.Core;

public record Asset(
    Guid Id,
    AssetClass Classe,
    string Codigo,
    string Nome,
    DateOnly Vencimento);
