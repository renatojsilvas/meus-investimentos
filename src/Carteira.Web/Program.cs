using Carteira.Core;
using Carteira.Web;
using Carteira.Web.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CarteiraDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<IPriceApiClient, PriceApiClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["PriceApi:BaseUrl"] ?? throw new InvalidOperationException("PriceApi:BaseUrl não configurado");
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("X-Api-Key", config["PriceApi:ApiKey"]);
});

builder.Services.AddSingleton<PriceSyncJob>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PriceSyncJob>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<CarteiraDbContext>().Database.Migrate();
}

app.UseAntiforgery();

app.MapGet("/health", () => "ok");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/api/import", async (HttpRequest request, CarteiraDbContext db, PriceSyncJob job, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { erro = "requisição precisa ser multipart/form-data" });

    var form = await request.ReadFormAsync();
    var arquivo = form.Files.FirstOrDefault();
    if (arquivo is null)
        return Results.BadRequest(new { erro = "arquivo obrigatório" });

    var hoje = Relogio.HojeSaoPaulo();

    await using var stream = arquivo.OpenReadStream();
    var parseResult = CsvTradeParser.Parse(stream, hoje);

    if (parseResult.Erros.Count > 0)
        return Results.BadRequest(new { erros = parseResult.Erros });

    var titularesExistentes = await db.Titulares.ToListAsync();
    var ativosExistentes = await db.Ativos.ToListAsync();
    var operacoesExistentes = await db.Operacoes.ToListAsync();

    var titularesPorSlug = titularesExistentes.ToDictionary(t => t.Slug);
    var ativosPorCodigo = ativosExistentes.ToDictionary(a => a.Codigo);
    var chavesConhecidas = operacoesExistentes.Select(o => o.ChaveImportacao).ToHashSet();

    var titularesNovos = new List<Titular>();
    var ativosNovos = new List<Ativo>();
    var operacoesNovas = new List<Operacao>();
    var jaExistentes = 0;

    foreach (var linha in parseResult.Linhas)
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
            return Results.BadRequest(new { erro = ex.Message });
        }
    }

    await using var transaction = await db.Database.BeginTransactionAsync();
    db.Titulares.AddRange(titularesNovos);
    db.Ativos.AddRange(ativosNovos);
    db.Operacoes.AddRange(operacoesNovas);
    await db.SaveChangesAsync();
    await transaction.CommitAsync();

    if (operacoesNovas.Count > 0)
    {
        var menorDataImportada = operacoesNovas.Min(o => o.Data);
        await db.Snapshots.Where(s => s.Data >= menorDataImportada).ExecuteDeleteAsync(ct);
        await job.PreencherSnapshotsAsync(ct);
    }

    return Results.Ok(new
    {
        importadas = operacoesNovas.Count,
        jaExistentes,
        ativosCriados = ativosNovos.Count,
        titularesCriados = titularesNovos.Count
    });
});

app.MapPost("/api/prices/sync", async (PriceSyncJob job, CancellationToken ct) =>
{
    await job.SincronizarAgoraAsync(ct);
    return Results.Ok(new { status = "sincronizado" });
});

app.MapPost("/api/prices/backfill", async (PriceSyncJob job, CancellationToken ct) =>
{
    var resultado = await job.BackfillAsync(ct);
    return Results.Ok(resultado);
});

app.MapPost("/api/snapshots/rebuild", async (CarteiraDbContext db, PriceSyncJob job, CancellationToken ct) =>
{
    await db.Snapshots.ExecuteDeleteAsync(ct);
    await job.PreencherSnapshotsAsync(ct);
    return Results.Ok(new { status = "reconstruido" });
});

app.Run();
