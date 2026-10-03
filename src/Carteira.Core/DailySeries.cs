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

        var pontos = new List<PontoSerieDiaria>();

        foreach (var titular in titulares)
        {
            var operacoesTitular = operacoes.Where(o => o.TitularId == titular.Id).ToList();
            if (operacoesTitular.Count == 0)
                continue;

            var primeira = operacoesTitular.Min(o => o.Data);
            var inicio = primeira > de ? primeira : de;
            if (inicio > ate)
                continue;

            // R7: um dia só pode qualificar se existe PrecoDiario nele ou operação do titular nele,
            // então os únicos candidatos possíveis são as datas de precos e as das operações do titular.
            var diasCandidatos = precos
                .Select(p => p.Data)
                .Concat(operacoesTitular.Select(o => o.Data))
                .Where(d => d >= inicio && d <= ate)
                .Distinct()
                .OrderBy(d => d);

            foreach (var dia in diasCandidatos)
            {
                var snapshot = PositionCalculator.Calculate(titulares, ativos, operacoesTitular, precos, dia);

                var temPrecoDeAtivoComPosicao = precos.Any(p =>
                    p.Data == dia && snapshot.Posicoes.Any(pos => pos.AtivoId == p.AtivoId));
                var temOperacaoNoDia = operacoesTitular.Any(o => o.Data == dia);
                if (!temPrecoDeAtivoComPosicao && !temOperacaoNoDia)
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
