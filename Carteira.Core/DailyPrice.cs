namespace Carteira.Core;

public record DailyPrice(
    Guid AssetId,
    DateOnly Data,
    decimal PrecoUnitario);
