using System.Net;
using Carteira.Core;
using Microsoft.EntityFrameworkCore;

namespace Carteira.Web;

public class PriceSyncJob(
    IPriceApiClient priceApiClient, IBcbClient bcbClient, IServiceScopeFactory scopeFactory,
    Snapshots snapshots, IConfiguration configuration, ILogger<PriceSyncJob> logger) : BackgroundService
{
    private bool _authFailed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ExecutarCicloAsync(stoppingToken);

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

            await ExecutarCicloAsync(stoppingToken);
        }
    }

    private async Task ExecutarCicloAsync(CancellationToken ct)
    {
        try
        {
            await SincronizarAgoraAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha na execução do job diário. Tentando na próxima execução.");
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
            await SincronizarCdiAsync(db, hoje, ct);

        if (!ct.IsCancellationRequested)
            await snapshots.PreencherSnapshotsAsync(ct);
    }

    public async Task<ResultadoBackfillPrecos> BackfillPrecosAsync(CancellationToken ct)
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

        await snapshots.ReconstruirSnapshotsAsync(null, ct);

        return new ResultadoBackfillPrecos(ativosComOperacao.Count, precosGravados, dias);
    }

    public async Task<ResultadoBackfillIndices> BackfillIndicesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CarteiraDbContext>();

        var operacoes = await db.Operacoes.ToListAsync(ct);
        var hoje = Relogio.HojeSaoPaulo();
        var de = operacoes.Count > 0 ? operacoes.Min(o => o.Data) : hoje;

        var registrosGravados = 0;
        var inicioFatia = de;

        while (inicioFatia <= hoje)
        {
            var fimFatia = inicioFatia.AddYears(5).AddDays(-1);
            if (fimFatia > hoje)
                fimFatia = hoje;

            var registros = await bcbClient.GetCdiAsync(inicioFatia, fimFatia, ct);
            foreach (var registro in registros)
                await GravarIndiceAsync(db, "CDI", registro.Data, registro.Valor, ct);

            registrosGravados += registros.Count;
            await db.SaveChangesAsync(ct);

            inicioFatia = fimFatia.AddDays(1);
        }

        await snapshots.ReconstruirSnapshotsAsync(null, ct);

        return new ResultadoBackfillIndices("CDI", registrosGravados, de, hoje);
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

    private async Task SincronizarCdiAsync(CarteiraDbContext db, DateOnly hoje, CancellationToken ct)
    {
        var inicio = hoje.AddDays(-7);
        var fim = hoje.AddDays(-1);

        var existentes = await db.IndicesDiarios
            .Where(i => i.Indice == "CDI" && i.Data >= inicio && i.Data <= fim)
            .Select(i => i.Data)
            .ToListAsync(ct);

        var faltam = Enumerable.Range(0, 7)
            .Select(d => inicio.AddDays(d))
            .Except(existentes)
            .Any();

        if (!faltam)
            return;

        try
        {
            var registros = await bcbClient.GetCdiAsync(inicio, fim, ct);
            foreach (var registro in registros)
                await GravarIndiceAsync(db, "CDI", registro.Data, registro.Valor, ct);

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao buscar o CDI do BCB para {Inicio}..{Fim}. Tentando na próxima execução.", inicio, fim);
        }
    }

    private static async Task GravarIndiceAsync(CarteiraDbContext db, string indice, DateOnly data, decimal valor, CancellationToken ct)
    {
        var existente = await db.IndicesDiarios.FindAsync([indice, data], ct);
        if (existente is not null)
            db.IndicesDiarios.Remove(existente);

        db.IndicesDiarios.Add(new IndiceDiario(indice, data, valor));
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
