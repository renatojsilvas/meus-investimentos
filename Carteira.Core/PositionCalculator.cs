namespace Carteira.Core;

public static class PositionCalculator
{
    public static PortfolioSnapshot Calculate(
        IReadOnlyList<Asset> assets,
        IReadOnlyList<Trade> trades,
        IReadOnlyList<DailyPrice> prices,
        DateOnly asOf)
    {
        throw new NotImplementedException();
    }
}
