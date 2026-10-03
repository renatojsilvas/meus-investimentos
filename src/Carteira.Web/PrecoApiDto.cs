namespace Carteira.Web;

public record PrecoApiDto(
    string Codigo,
    DateOnly DataBase,
    decimal? TaxaCompra,
    decimal? TaxaVenda,
    decimal? PuCompra,
    decimal? PuVenda,
    decimal? PuBase);
