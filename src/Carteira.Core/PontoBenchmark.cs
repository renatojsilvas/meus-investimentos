namespace Carteira.Core;

public record PontoBenchmark(
    DateOnly Data,
    Guid TitularId,
    string Slug,
    decimal ValorCdi);
