using Carteira.Core;
using Microsoft.EntityFrameworkCore;

namespace Carteira.Web;

public class Snapshots(IServiceScopeFactory scopeFactory)
{
    private readonly SemaphoreSlim _preenchimentoLock = new(1, 1);

    public async Task ReconstruirSnapshotsAsync(DateOnly? desde, CancellationToken ct)
    {
        await _preenchimentoLock.WaitAsync(ct);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CarteiraDbContext>();

            IQueryable<SnapshotDiario> query = desde is null
                ? db.Snapshots
                : db.Snapshots.Where(s => s.Data >= desde.Value);
            await query.ExecuteDeleteAsync(ct);

            await PreencherSnapshotsInternalAsync(ct);
        }
        finally
        {
            _preenchimentoLock.Release();
        }
    }

    public async Task PreencherSnapshotsAsync(CancellationToken ct)
    {
        await _preenchimentoLock.WaitAsync(ct);
        try
        {
            await PreencherSnapshotsInternalAsync(ct);
        }
        finally
        {
            _preenchimentoLock.Release();
        }
    }

    private async Task PreencherSnapshotsInternalAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CarteiraDbContext>();

        var operacoes = await db.Operacoes.ToListAsync(ct);
        if (operacoes.Count == 0)
            return;

        var titulares = await db.Titulares.ToListAsync(ct);
        var ativos = await db.Ativos.ToListAsync(ct);
        var precos = await db.PrecosDiarios.ToListAsync(ct);
        var indices = await db.IndicesDiarios.ToListAsync(ct);

        var de = operacoes.Min(o => o.Data);
        var ate = Relogio.HojeSaoPaulo();

        var pontos = DailySeries.Build(titulares, ativos, operacoes, precos, de, ate);
        var pontosCdi = BenchmarkSeries.Build(titulares, operacoes, indices, de, ate);
        var cdiPorDiaETitular = pontosCdi.ToDictionary(p => (p.Data, p.TitularId), p => p.ValorCdi);

        var linhasSemCdi = await db.Snapshots.Where(s => s.ValorCdi == null).ToListAsync(ct);
        foreach (var linha in linhasSemCdi)
        {
            if (cdiPorDiaETitular.TryGetValue((linha.Data, linha.TitularId), out var valorCdi))
                db.Entry(linha).Property(s => s.ValorCdi).CurrentValue = valorCdi;
        }

        if (pontos.Count > 0)
        {
            var existentes = (await db.Snapshots
                    .Select(s => new { s.Data, s.TitularId })
                    .ToListAsync(ct))
                .Select(s => (s.Data, s.TitularId))
                .ToHashSet();

            foreach (var ponto in pontos)
            {
                if (existentes.Contains((ponto.Data, ponto.TitularId)))
                    continue;

                var valorCdi = cdiPorDiaETitular.TryGetValue((ponto.Data, ponto.TitularId), out var v) ? v : (decimal?)null;

                db.Snapshots.Add(new SnapshotDiario(
                    ponto.Data,
                    ponto.TitularId,
                    ponto.Custo,
                    ponto.CustoComPreco,
                    ponto.Valor,
                    ponto.Rentabilidade,
                    ponto.ResultadoRealizado,
                    ponto.TemPosicaoSemPreco,
                    valorCdi));
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
