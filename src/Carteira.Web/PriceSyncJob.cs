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
    private static readonly TimeZoneInfo Tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

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
        var hoje = HojeEmSaoPaulo();

        for (var i = 1; i <= 7; i++)
        {
            if (_authFailed || ct.IsCancellationRequested)
                return;

            var dia = hoje.AddDays(-i);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CarteiraDbContext>();

            if (await DiaEstaCompletoAsync(db, dia, ct))
                continue;

            await SincronizarDiaAsync(db, dia, ct);
        }
    }

    private static async Task<bool> DiaEstaCompletoAsync(CarteiraDbContext db, DateOnly dia, CancellationToken ct)
    {
        var titulares = await db.Titulares.ToListAsync(ct);
        var ativos = await db.Ativos.ToListAsync(ct);
        var operacoes = await db.Operacoes.ToListAsync(ct);

        if (operacoes.Count == 0)
            return true;

        var snapshot = PositionCalculator.Calculate(titulares, ativos, operacoes, [], dia);
        var ativosComPosicao = snapshot.Posicoes.Select(p => p.AtivoId).Distinct().ToList();

        if (ativosComPosicao.Count == 0)
            return true;

        foreach (var ativoId in ativosComPosicao)
        {
            var temPreco = await db.PrecosDiarios.AnyAsync(p => p.AtivoId == ativoId && p.Data == dia, ct);
            if (!temPreco)
                return false;
        }

        return true;
    }

    private async Task SincronizarDiaAsync(CarteiraDbContext db, DateOnly dia, CancellationToken ct)
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

        var titulares = await db.Titulares.ToListAsync(ct);
        var ativos = await db.Ativos.ToListAsync(ct);
        var operacoes = await db.Operacoes.ToListAsync(ct);

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

            var existente = await db.PrecosDiarios.FindAsync([posicao.AtivoId, dia], ct);
            if (existente is not null)
                db.PrecosDiarios.Remove(existente);

            db.PrecosDiarios.Add(new PrecoDiario(posicao.AtivoId, dia, preco.PuVenda.Value));
        }

        await db.SaveChangesAsync(ct);
    }

    private static DateOnly HojeEmSaoPaulo() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Tz).DateTime);

    private static TimeSpan TempoAteProximaExecucao(int horaLocal)
    {
        var agoraSp = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Tz);
        var proximaSp = new DateTimeOffset(agoraSp.Year, agoraSp.Month, agoraSp.Day, horaLocal, 0, 0, agoraSp.Offset);
        if (proximaSp <= agoraSp)
            proximaSp = proximaSp.AddDays(1);

        return proximaSp - agoraSp;
    }
}
