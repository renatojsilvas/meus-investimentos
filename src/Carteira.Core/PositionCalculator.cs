namespace Carteira.Core;

public static class PositionCalculator
{
    public static CarteiraSnapshot Calculate(
        IReadOnlyList<Titular> titulares,
        IReadOnlyList<Ativo> ativos,
        IReadOnlyList<Operacao> operacoes,
        IReadOnlyList<PrecoDiario> precos,
        DateOnly dataReferencia)
    {
        var ativosPorId = ativos.ToDictionary(a => a.Id);
        var titularesPorId = titulares.ToDictionary(t => t.Id);

        foreach (var operacao in operacoes)
        {
            if (!ativosPorId.ContainsKey(operacao.AtivoId))
                throw new ArgumentException($"Operação {operacao.Id} referencia um ativo que não está em ativos.", nameof(operacoes));
            if (!titularesPorId.ContainsKey(operacao.TitularId))
                throw new ArgumentException($"Operação {operacao.Id} referencia um titular que não está em titulares.", nameof(operacoes));
            if (operacao.Quantidade <= 0)
                throw new ArgumentOutOfRangeException(nameof(operacoes), operacao.Quantidade, "Quantidade deve ser maior que zero.");
            if (operacao.PrecoUnitario <= 0)
                throw new ArgumentOutOfRangeException(nameof(operacoes), operacao.PrecoUnitario, "PrecoUnitario deve ser maior que zero.");
        }

        var ordenadas = operacoes
            .Where(o => o.Data <= dataReferencia)
            .OrderBy(o => o.Data)
            .ThenBy(o => o.Tipo == TipoOperacao.Aplicacao ? 0 : 1)
            .ToList();

        var estados = new Dictionary<(Guid AtivoId, Guid TitularId), (decimal Quantidade, decimal Custo, decimal Realizado)>();

        foreach (var operacao in ordenadas)
        {
            var ativo = ativosPorId[operacao.AtivoId];
            if (operacao.Data > ativo.Vencimento)
                throw new OperacaoAposVencimentoException(
                    $"Operação de {operacao.Data:yyyy-MM-dd} em {ativo.Codigo} é posterior ao vencimento ({ativo.Vencimento:yyyy-MM-dd}).");

            var chave = (operacao.AtivoId, operacao.TitularId);
            var (q, c, realizado) = estados.GetValueOrDefault(chave);

            if (operacao.Tipo == TipoOperacao.Aplicacao)
            {
                q += operacao.Quantidade;
                c += operacao.Quantidade * operacao.PrecoUnitario + operacao.Taxas;
            }
            else
            {
                if (operacao.Quantidade > q)
                    throw new PosicaoInsuficienteException(
                        $"Resgate de {operacao.Quantidade} em {ativo.Codigo} em {operacao.Data:yyyy-MM-dd} excede a posição de {q}.");

                var custoBaixado = operacao.Quantidade * (c / q);
                realizado += operacao.Quantidade * operacao.PrecoUnitario - operacao.Taxas - custoBaixado;
                q -= operacao.Quantidade;
                c = q == 0 ? 0m : c - custoBaixado;
            }

            estados[chave] = (q, c, realizado);
        }

        var posicoes = new List<PosicaoSnapshot>();
        foreach (var ((ativoId, titularId), (q, c, realizado)) in estados)
        {
            if (q <= 0)
                continue;

            var ativo = ativosPorId[ativoId];
            var titular = titularesPorId[titularId];

            var preco = precos
                .Where(p => p.AtivoId == ativoId && p.Data <= dataReferencia)
                .OrderByDescending(p => p.Data)
                .Select(p => (decimal?)p.PrecoUnitario)
                .FirstOrDefault();

            var valor = preco * q;
            var rentabilidade = valor - c;
            var rentabilidadePercentual = rentabilidade / c;

            posicoes.Add(new PosicaoSnapshot(
                ativoId, titularId, ativo.Codigo, ativo.Nome, titular.Nome,
                q, c, c / q, realizado, preco, valor, rentabilidade, rentabilidadePercentual));
        }

        var custoTotal = posicoes.Sum(p => p.Custo);
        var valorTotal = posicoes.Sum(p => p.ValorMercado ?? 0m);
        var rentabilidadeTotal = posicoes.Sum(p => p.Rentabilidade ?? 0m);
        var custoComPreco = posicoes.Where(p => p.ValorMercado is not null).Sum(p => p.Custo);

        return new CarteiraSnapshot(
            posicoes
                .OrderBy(p => p.Codigo, StringComparer.Ordinal)
                .ThenBy(p => p.NomeTitular, StringComparer.Ordinal)
                .ToList(),
            custoTotal,
            valorTotal,
            rentabilidadeTotal,
            custoComPreco == 0m ? 0m : rentabilidadeTotal / custoComPreco,
            estados.Values.Sum(s => s.Realizado),
            posicoes.Any(p => p.ValorMercado is null));
    }
}
