using Carteira.Core;
using Microsoft.EntityFrameworkCore;

namespace Carteira.Web;

public record ResultadoImportacao(int Importadas, int JaExistentes, int AtivosCriados, int TitularesCriados);

public class ImportacaoInvalidaException(string message) : Exception(message);

public class Importacao(CarteiraDbContext db, PriceSyncJob job)
{
    public async Task<ResultadoImportacao> ExecutarAsync(IReadOnlyList<LinhaOperacao> linhas, CancellationToken ct)
    {
        var titularesExistentes = await db.Titulares.ToListAsync(ct);
        var ativosExistentes = await db.Ativos.ToListAsync(ct);
        var operacoesExistentes = await db.Operacoes.ToListAsync(ct);

        var titularesPorSlug = titularesExistentes.ToDictionary(t => t.Slug);
        var ativosPorCodigo = ativosExistentes.ToDictionary(a => a.Codigo);
        var chavesConhecidas = operacoesExistentes.Select(o => o.ChaveImportacao).ToHashSet();

        var titularesNovos = new List<Titular>();
        var ativosNovos = new List<Ativo>();
        var operacoesNovas = new List<Operacao>();
        var jaExistentes = 0;

        foreach (var linha in linhas)
        {
            if (!titularesPorSlug.TryGetValue(linha.Titular, out var titular))
            {
                titular = new Titular(Guid.NewGuid(), linha.Titular, linha.Titular);
                titularesPorSlug[linha.Titular] = titular;
                titularesNovos.Add(titular);
            }

            if (!ativosPorCodigo.TryGetValue(linha.Codigo, out var ativo))
            {
                ativo = new Ativo(Guid.NewGuid(), ClasseAtivo.TesouroDireto, linha.Codigo, linha.Titulo, linha.Vencimento);
                ativosPorCodigo[linha.Codigo] = ativo;
                ativosNovos.Add(ativo);
            }

            if (chavesConhecidas.Contains(linha.ChaveImportacao))
            {
                jaExistentes++;
                continue;
            }

            chavesConhecidas.Add(linha.ChaveImportacao);
            operacoesNovas.Add(new Operacao(
                Guid.NewGuid(),
                titular.Id,
                ativo.Id,
                linha.Data,
                linha.Tipo,
                linha.Quantidade,
                linha.PrecoUnitario,
                linha.Taxas,
                "BRL",
                linha.ChaveImportacao));
        }

        var todosTitulares = titularesExistentes.Concat(titularesNovos).ToList();
        var todosAtivos = ativosExistentes.Concat(ativosNovos).ToList();
        var todasOperacoes = operacoesExistentes.Concat(operacoesNovas).ToList();

        if (todasOperacoes.Count > 0)
        {
            try
            {
                PositionCalculator.Calculate(todosTitulares, todosAtivos, todasOperacoes, [], todasOperacoes.Max(o => o.Data));
            }
            catch (Exception ex) when (ex is PosicaoInsuficienteException or OperacaoAposVencimentoException)
            {
                throw new ImportacaoInvalidaException(ex.Message);
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Titulares.AddRange(titularesNovos);
        db.Ativos.AddRange(ativosNovos);
        db.Operacoes.AddRange(operacoesNovas);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        if (operacoesNovas.Count > 0)
        {
            var menorDataImportada = operacoesNovas.Min(o => o.Data);
            await db.Snapshots.Where(s => s.Data >= menorDataImportada).ExecuteDeleteAsync(ct);
            await job.PreencherSnapshotsAsync(ct);
        }

        return new ResultadoImportacao(operacoesNovas.Count, jaExistentes, ativosNovos.Count, titularesNovos.Count);
    }
}
