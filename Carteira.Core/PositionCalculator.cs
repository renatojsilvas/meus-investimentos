namespace Carteira.Core;

public static class PositionCalculator
{
    public static PortfolioSnapshot Calculate(
        IReadOnlyList<Asset> assets,
        IReadOnlyList<Trade> trades,
        IReadOnlyList<DailyPrice> prices,
        DateOnly asOf)
    {
        var assetsById = assets.ToDictionary(a => a.Id);

        foreach (var trade in trades)
        {
            if (!assetsById.ContainsKey(trade.AssetId))
                throw new ArgumentException($"Trade {trade.Id} referencia um ativo que não está em assets.", nameof(trades));
            if (trade.Quantidade <= 0)
                throw new ArgumentOutOfRangeException(nameof(trades), trade.Quantidade, "Quantidade deve ser maior que zero.");
            if (trade.PrecoUnitario <= 0)
                throw new ArgumentOutOfRangeException(nameof(trades), trade.PrecoUnitario, "PrecoUnitario deve ser maior que zero.");
        }

        // R3: cronológico; empate de data, aplicações antes de resgates.
        var ordered = trades
            .Where(t => t.Data <= asOf)
            .OrderBy(t => t.Data)
            .ThenBy(t => t.Tipo == TradeType.Aplicacao ? 0 : 1)
            .ToList();

        var states = new Dictionary<Guid, (decimal Quantidade, decimal Custo, decimal Realizado)>();

        foreach (var trade in ordered)
        {
            var asset = assetsById[trade.AssetId];
            if (trade.Data > asset.Vencimento)
                throw new TradeAfterMaturityException(
                    $"Operação de {trade.Data:yyyy-MM-dd} em {asset.Codigo} é posterior ao vencimento ({asset.Vencimento:yyyy-MM-dd}).");

            var (q, c, realizado) = states.GetValueOrDefault(trade.AssetId);

            if (trade.Tipo == TradeType.Aplicacao)
            {
                // R1
                q += trade.Quantidade;
                c += trade.Quantidade * trade.PrecoUnitario + trade.Taxas;
            }
            else
            {
                // R2
                if (trade.Quantidade > q)
                    throw new InsufficientPositionException(
                        $"Resgate de {trade.Quantidade} em {asset.Codigo} em {trade.Data:yyyy-MM-dd} excede a posição de {q}.");

                var custoBaixado = trade.Quantidade * (c / q);
                realizado += trade.Quantidade * trade.PrecoUnitario - trade.Taxas - custoBaixado;
                q -= trade.Quantidade;
                c = q == 0 ? 0m : c - custoBaixado;
            }

            states[trade.AssetId] = (q, c, realizado);
        }

        var posicoes = new List<PositionSnapshot>();
        foreach (var (assetId, (q, c, realizado)) in states)
        {
            if (q <= 0)
                continue;

            var asset = assetsById[assetId];

            // R4
            var preco = prices
                .Where(p => p.AssetId == assetId && p.Data <= asOf)
                .OrderByDescending(p => p.Data)
                .Select(p => (decimal?)p.PrecoUnitario)
                .FirstOrDefault();

            // R5
            var valor = preco * q;
            var rent = valor - c;
            var rentPercentual = rent / c;

            posicoes.Add(new PositionSnapshot(
                assetId, asset.Codigo, asset.Nome, q, c, c / q, realizado, preco, valor, rent, rentPercentual));
        }

        // R6
        var custoTotal = posicoes.Sum(p => p.Custo);
        var valorTotal = posicoes.Sum(p => p.ValorMercado ?? 0m);
        var rentTotal = posicoes.Sum(p => p.Rent ?? 0m);
        var custoComPreco = posicoes.Where(p => p.ValorMercado is not null).Sum(p => p.Custo);

        return new PortfolioSnapshot(
            posicoes.OrderBy(p => p.Codigo, StringComparer.Ordinal).ToList(),
            custoTotal,
            valorTotal,
            rentTotal,
            custoComPreco == 0m ? 0m : rentTotal / custoComPreco,
            states.Values.Sum(s => s.Realizado),
            posicoes.Any(p => p.ValorMercado is null));
    }
}
