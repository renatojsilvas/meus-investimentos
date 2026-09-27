using Carteira.Core;
using Carteira.Web;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CarteiraDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

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

app.MapGet("/health", () => "ok");

app.MapPost("/api/import", async (HttpRequest request, CarteiraDbContext db) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { erro = "requisição precisa ser multipart/form-data" });

    var form = await request.ReadFormAsync();
    var arquivo = form.Files.FirstOrDefault();
    if (arquivo is null)
        return Results.BadRequest(new { erro = "arquivo obrigatório" });

    var hoje = DateOnly.FromDateTime(DateTime.Now);

    await using var stream = arquivo.OpenReadStream();
    var parseResult = CsvTradeParser.Parse(stream, hoje);

    if (parseResult.Erros.Count > 0)
        return Results.BadRequest(new { erros = parseResult.Erros });

    var titularesExistentes = await db.Titulares.ToListAsync();
    var assetsExistentes = await db.Assets.ToListAsync();
    var tradesExistentes = await db.Trades.ToListAsync();

    var titularesPorSlug = titularesExistentes.ToDictionary(t => t.Slug);
    var assetsPorCodigo = assetsExistentes.ToDictionary(a => a.Codigo);
    var chavesConhecidas = tradesExistentes.Select(t => t.ChaveImportacao).ToHashSet();

    var titularesNovos = new List<Titular>();
    var assetsNovos = new List<Asset>();
    var tradesNovos = new List<Trade>();
    var jaExistentes = 0;

    foreach (var linha in parseResult.Linhas)
    {
        if (!titularesPorSlug.TryGetValue(linha.Titular, out var titular))
        {
            titular = new Titular(Guid.NewGuid(), linha.Titular, linha.Titular);
            titularesPorSlug[linha.Titular] = titular;
            titularesNovos.Add(titular);
        }

        if (!assetsPorCodigo.TryGetValue(linha.Codigo, out var asset))
        {
            asset = new Asset(Guid.NewGuid(), AssetClass.TesouroDireto, linha.Codigo, linha.Titulo, linha.Vencimento);
            assetsPorCodigo[linha.Codigo] = asset;
            assetsNovos.Add(asset);
        }

        if (chavesConhecidas.Contains(linha.ChaveImportacao))
        {
            jaExistentes++;
            continue;
        }

        chavesConhecidas.Add(linha.ChaveImportacao);
        tradesNovos.Add(new Trade(
            Guid.NewGuid(),
            titular.Id,
            asset.Id,
            linha.Data,
            linha.Tipo,
            linha.Quantidade,
            linha.PrecoUnitario,
            linha.Taxas,
            "BRL",
            linha.ChaveImportacao));
    }

    var todosTitulares = titularesExistentes.Concat(titularesNovos).ToList();
    var todosAssets = assetsExistentes.Concat(assetsNovos).ToList();
    var todosTrades = tradesExistentes.Concat(tradesNovos).ToList();

    if (todosTrades.Count > 0)
    {
        try
        {
            PositionCalculator.Calculate(todosTitulares, todosAssets, todosTrades, [], todosTrades.Max(t => t.Data));
        }
        catch (Exception ex) when (ex is InsufficientPositionException or TradeAfterMaturityException)
        {
            return Results.BadRequest(new { erro = ex.Message });
        }
    }

    await using var transaction = await db.Database.BeginTransactionAsync();
    db.Titulares.AddRange(titularesNovos);
    db.Assets.AddRange(assetsNovos);
    db.Trades.AddRange(tradesNovos);
    await db.SaveChangesAsync();
    await transaction.CommitAsync();

    return Results.Ok(new
    {
        importadas = tradesNovos.Count,
        jaExistentes,
        ativosCriados = assetsNovos.Count,
        titularesCriados = titularesNovos.Count
    });
});

app.MapPost("/api/prices/sync", async (PriceSyncJob job, CancellationToken ct) =>
{
    await job.SincronizarAgoraAsync(ct);
    return Results.Ok(new { status = "sincronizado" });
});

app.Run();
