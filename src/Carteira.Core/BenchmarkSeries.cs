namespace Carteira.Core;

public static class BenchmarkSeries
{
    public static IReadOnlyList<PontoBenchmark> Build(
        IReadOnlyList<Titular> titulares,
        IReadOnlyList<Operacao> operacoes,
        IReadOnlyList<IndiceDiario> indices,
        DateOnly de,
        DateOnly ate)
    {
        if (de > ate)
            return [];

        var indicesPorDia = indices
            .Where(i => i.Indice == "CDI")
            .ToDictionary(i => i.Data, i => i.Valor);

        var pontos = new List<PontoBenchmark>();

        foreach (var titular in titulares)
        {
            var operacoesTitular = operacoes.Where(o => o.TitularId == titular.Id).ToList();
            if (operacoesTitular.Count == 0)
                continue;

            var primeira = operacoesTitular.Min(o => o.Data);
            if (primeira > ate)
                continue;

            var fluxosPorDia = operacoesTitular
                .GroupBy(o => o.Data)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(o => o.Tipo == TipoOperacao.Aplicacao
                        ? o.Quantidade * o.PrecoUnitario + o.Taxas
                        : -(o.Quantidade * o.PrecoUnitario - o.Taxas)));

            var saldo = 0m;
            for (var dia = de; dia <= ate; dia = dia.AddDays(1))
            {
                var fator = indicesPorDia.TryGetValue(dia, out var valorIndice)
                    ? 1 + valorIndice / 100
                    : 1m;
                var fluxo = fluxosPorDia.TryGetValue(dia, out var f) ? f : 0m;

                saldo = saldo * fator + fluxo;

                if (dia < primeira)
                    continue;

                var temIndice = indicesPorDia.ContainsKey(dia);
                var temOperacao = fluxosPorDia.ContainsKey(dia);
                if (temIndice || temOperacao)
                    pontos.Add(new PontoBenchmark(dia, titular.Id, titular.Slug, saldo));
            }
        }

        return pontos
            .OrderBy(p => p.Data)
            .ThenBy(p => p.Slug, StringComparer.Ordinal)
            .ToList();
    }
}
