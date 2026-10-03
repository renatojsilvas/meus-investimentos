namespace Carteira.Core;

public record PrecoDiario(
    Guid AtivoId,
    DateOnly Data,
    decimal PrecoUnitario);
