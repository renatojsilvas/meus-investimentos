namespace Carteira.Core;

public record IndiceDiario(
    string Indice,
    DateOnly Data,
    decimal Valor);
