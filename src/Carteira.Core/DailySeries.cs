namespace Carteira.Core;

public static class DailySeries
{
    public static IReadOnlyList<PontoSerieDiaria> Build(
        IReadOnlyList<Titular> titulares,
        IReadOnlyList<Ativo> ativos,
        IReadOnlyList<Operacao> operacoes,
        IReadOnlyList<PrecoDiario> precos,
        DateOnly de,
        DateOnly ate)
    {
        if (de > ate)
            return [];

        var operacoesPorTitular = titulares
            .Select(t => (Titular: t, Operacoes: operacoes.Where(o => o.TitularId == t.Id).ToList()))
            .Where(x => x.Operacoes.Count > 0)
            .Select(x => (x.Titular, x.Operacoes, Primeira: x.Operacoes.Min(o => o.Data)))
            .ToList();

        var diasComPreco = precos
            .Select(p => p.Data)
            .Where(d => d >= de && d <= ate)
            .Distinct()
            .OrderBy(d => d);

        var pontos = new List<PontoSerieDiaria>();

        foreach (var dia in diasComPreco)
        {
            var snapshots = operacoesPorTitular
                .Select(x => (x.Titular, x.Operacoes, x.Primeira,
                    Snapshot: PositionCalculator.Calculate(titulares, ativos, x.Operacoes, precos, dia)))
                .ToList();

            var ativosComPosicaoOuResgate = snapshots
                .SelectMany(x => x.Snapshot.Posicoes.Select(p => p.AtivoId)
                    .Concat(x.Operacoes.Where(o => o.Data == dia).Select(o => o.AtivoId)))
                .ToHashSet();

            var diaCandidato = precos.Any(p => p.Data == dia && ativosComPosicaoOuResgate.Contains(p.AtivoId));
            if (!diaCandidato)
                continue;

            foreach (var (titular, operacoesTitular, primeira, snapshot) in snapshots)
            {
                if (dia < primeira)
                    continue;

                var temPosicao = snapshot.Posicoes.Count > 0;
                var diaDoResgateTotal = !temPosicao && operacoesTitular.Any(o => o.Data == dia);
                if (!temPosicao && !diaDoResgateTotal)
                    continue;

                var custoComPreco = snapshot.Posicoes
                    .Where(p => p.ValorMercado is not null)
                    .Sum(p => p.Custo);

                pontos.Add(new PontoSerieDiaria(
                    dia,
                    titular.Id,
                    titular.Slug,
                    titular.Nome,
                    snapshot.CustoTotal,
                    custoComPreco,
                    snapshot.ValorTotal,
                    snapshot.RentabilidadeTotal,
                    snapshot.ResultadoRealizadoTotal,
                    snapshot.TemPosicaoSemPreco));
            }
        }

        return pontos
            .OrderBy(p => p.Data)
            .ThenBy(p => p.Slug, StringComparer.Ordinal)
            .ToList();
    }
}
