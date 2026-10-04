using System.Net;
using Carteira.Core;
using Microsoft.EntityFrameworkCore;

namespace Carteira.Web;

public class PriceSyncJob(
    IPriceApiClient priceApiClient,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PriceSyncJob> logger) : BackgroundService
{
    private bool _authFailed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SincronizarAgoraAsync(stoppingToken);

        var horaLocal = configuration.GetValue("PriceSync:HourLocal", 7);

        while (!stoppingToken.IsCancellationRequested && !_authFailed)
        {
            var delay = TempoAteProximaExecucao(horaLocal);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested || _authFailed)
                break;

            await SincronizarAgoraAsync(stoppingToken);
        }
    }

    public async Task SincronizarAgoraAsync(CancellationToken ct)
    {
        var hoje = Relogio.HojeSaoPaulo();

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CarteiraDbContext>();

        var titulares = await db.Titulares.ToListAsync(ct);
        var ativos = await db.Ativos.ToListAsync(ct);
        var operacoes = await db.Operacoes.ToListAsync(ct);

        var primeiroDia = hoje.AddDays(-7);
        var precos = await db.PrecosDiarios
            .Where(p => p.Data >= primeiroDia && p.Data <= hoje)
            .ToListAsync(ct);

        for (var i = 1; i <= 7; i++)
        {
            if (_authFailed || ct.IsCancellationRequested)
                return;

            var dia = hoje.AddDays(-i);

            if (DiaEstaCompleto(titulares, ativos, operacoes, precos, dia))
                continue;

            await SincronizarDiaAsync(db, titulares, ativos, operacoes, dia, ct);
        }

        if (!ct.IsCancellationRequested)
            await PreencherSnapshotsAsync(ct);
    }

    public async Task<object> BackfillAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CarteiraDbContext>();

        var ativos = await db.Ativos.ToListAsync(ct);
        var operacoes = await db.Operacoes.ToListAsync(ct);

        var ativosComOperacao = operacoes
            .GroupBy(o => o.AtivoId)
            .Select(g => new { AtivoId = g.Key, PrimeiraData = g.Min(o => o.Data) })
            .ToList();

        var hoje = Relogio.HojeSaoPaulo();
        var precosGravados = 0;
        var dias = 0;

        foreach (var item in ativosComOperacao)
        {
            var ativo = ativos.First(a => a.Id == item.AtivoId);
            var historico = await priceApiClient.GetHistoricoAsync(ativo.Codigo, item.PrimeiraData, hoje, ct);
            dias += historico.Count;

            foreach (var registro in historico)
            {
                if (registro.PuVenda is null)
                    continue;

                await GravarPrecoAsync(db, ativo.Id, registro.DataBase, registro.PuVenda.Value, ct);
                precosGravados++;
            }

            await db.SaveChangesAsync(ct);
        }

        await db.Snapshots.ExecuteDeleteAsync(ct);
        await PreencherSnapshotsAsync(ct);

        return new { ativos = ativosComOperacao.Count, precosGravados, dias };
    }

    public async Task PreencherSnapshotsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CarteiraDbContext>();

        var operacoes = await db.Operacoes.ToListAsync(ct);
        if (operacoes.Count == 0)
            return;

        var titulares = await db.Titulares.ToListAsync(ct);
        var ativos = await db.Ativos.ToListAsync(ct);
        var precos = await db.PrecosDiarios.ToListAsync(ct);

        var de = operacoes.Min(o => o.Data);
        var ate = Relogio.HojeSaoPaulo();

        var pontos = DailySeries.Build(titulares, ativos, operacoes, precos, de, ate);
        if (pontos.Count == 0)
            return;

        var existentes = (await db.Snapshots
                .Select(s => new { s.Data, s.TitularId })
                .ToListAsync(ct))
            .Select(s => (s.Data, s.TitularId))
            .ToHashSet();

        foreach (var ponto in pontos)
        {
            if (existentes.Contains((ponto.Data, ponto.TitularId)))
                continue;

            db.Snapshots.Add(new SnapshotDiario(
                ponto.Data,
                ponto.TitularId,
                ponto.Custo,
                ponto.CustoComPreco,
                ponto.Valor,
                ponto.Rentabilidade,
                ponto.ResultadoRealizado,
                ponto.TemPosicaoSemPreco));
        }

        await db.SaveChangesAsync(ct);
    }

    private static bool DiaEstaCompleto(
        IReadOnlyList<Titular> titulares,
        IReadOnlyList<Ativo> ativos,
        IReadOnlyList<Operacao> operacoes,
        IReadOnlyList<PrecoDiario> precos,
        DateOnly dia)
    {
        if (operacoes.Count == 0)
            return true;

        var snapshot = PositionCalculator.Calculate(titulares, ativos, operacoes, [], dia);
        var ativosComPosicao = snapshot.Posicoes.Select(p => p.AtivoId).Distinct().ToList();

        if (ativosComPosicao.Count == 0)
            return true;

        foreach (var ativoId in ativosComPosicao)
        {
            var temPreco = precos.Any(p => p.AtivoId == ativoId && p.Data == dia);
            if (!temPreco)
                return false;
        }

        return true;
    }

    private async Task SincronizarDiaAsync(
        CarteiraDbContext db,
        IReadOnlyList<Titular> titulares,
        IReadOnlyList<Ativo> ativos,
        IReadOnlyList<Operacao> operacoes,
        DateOnly dia,
        CancellationToken ct)
    {
        IReadOnlyList<PrecoApiDto> precos;
        try
        {
            precos = await priceApiClient.GetPrecosAsync(dia, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogError(ex, "API de preços recusou a chave (401/403) ao buscar {Dia}. Parando até o próximo boot.", dia);
            _authFailed = true;
            return;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Falha ao chamar a API de preços para {Dia}. Tentando na próxima execução.", dia);
            return;
        }

        if (precos.Count == 0)
            return;

        if (operacoes.Count == 0)
            return;

        var snapshot = PositionCalculator.Calculate(titulares, ativos, operacoes, [], dia);

        var precosPorCodigo = precos.ToDictionary(p => p.Codigo);

        foreach (var posicao in snapshot.Posicoes)
        {
            if (!precosPorCodigo.TryGetValue(posicao.Codigo, out var preco))
            {
                logger.LogWarning("Código {Codigo} ausente na resposta da API de preços para {Dia}.", posicao.Codigo, dia);
                continue;
            }

            if (preco.PuVenda is null)
            {
                logger.LogWarning("puVenda nulo para {Codigo} em {Dia}.", posicao.Codigo, dia);
                continue;
            }

            await GravarPrecoAsync(db, posicao.AtivoId, dia, preco.PuVenda.Value, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task GravarPrecoAsync(CarteiraDbContext db, Guid ativoId, DateOnly dia, decimal puVenda, CancellationToken ct)
    {
        var existente = await db.PrecosDiarios.FindAsync([ativoId, dia], ct);
        if (existente is not null)
            db.PrecosDiarios.Remove(existente);

        db.PrecosDiarios.Add(new PrecoDiario(ativoId, dia, puVenda));
    }

    private static TimeSpan TempoAteProximaExecucao(int horaLocal)
    {
        var agoraSp = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Relogio.SaoPaulo);
        var proximaSp = new DateTimeOffset(agoraSp.Year, agoraSp.Month, agoraSp.Day, horaLocal, 0, 0, agoraSp.Offset);
        if (proximaSp <= agoraSp)
            proximaSp = proximaSp.AddDays(1);

        return proximaSp - agoraSp;
    }
}
