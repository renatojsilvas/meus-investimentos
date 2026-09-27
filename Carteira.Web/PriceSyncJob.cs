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

    // Disparado no boot, na hora agendada e pelo endpoint manual POST /api/prices/sync.
    // Verifica os últimos 7 dias corridos; para cada um sem nenhum DailyPrice gravado,
    // busca /precos e grava os preços dos ativos com posição > 0 naquela data.
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

            var jaTemPreco = await db.DailyPrices.AnyAsync(p => p.Data == dia, ct);
            if (jaTemPreco)
                continue;

            await SincronizarDiaAsync(db, dia, ct);
        }
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
            return; // dia sem pregão, não é erro

        var titulares = await db.Titulares.ToListAsync(ct);
        var assets = await db.Assets.ToListAsync(ct);
        var trades = await db.Trades.ToListAsync(ct);

        if (trades.Count == 0)
            return;

        var snapshot = PositionCalculator.Calculate(titulares, assets, trades, [], dia);

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

            var existente = await db.DailyPrices.FindAsync([posicao.AssetId, dia], ct);
            if (existente is not null)
                db.DailyPrices.Remove(existente);

            db.DailyPrices.Add(new DailyPrice(posicao.AssetId, dia, preco.PuVenda.Value));
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
