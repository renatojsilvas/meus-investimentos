using System.Text;
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
builder.Services.AddScoped<Importacao>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<CarteiraDbContext>().Database.Migrate();
}

app.UseAntiforgery();

app.MapGet("/health", () => "ok");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/api/import", async (HttpRequest request, Importacao importacao, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { erro = "requisição precisa ser multipart/form-data" });

    var form = await request.ReadFormAsync(ct);
    var arquivo = form.Files.FirstOrDefault();
    if (arquivo is null)
        return Results.BadRequest(new { erro = "arquivo obrigatório" });

    var hoje = Relogio.HojeSaoPaulo();

    await using var stream = arquivo.OpenReadStream();
    var parseResult = CsvTradeParser.Parse(stream, hoje);

    if (parseResult.Erros.Count > 0)
        return Results.BadRequest(new { erros = parseResult.Erros });

    try
    {
        var resultado = await importacao.ExecutarAsync(parseResult.Linhas, ct);
        return Results.Ok(new
        {
            importadas = resultado.Importadas,
            jaExistentes = resultado.JaExistentes,
            ativosCriados = resultado.AtivosCriados,
            titularesCriados = resultado.TitularesCriados
        });
    }
    catch (ImportacaoInvalidaException ex)
    {
        return Results.BadRequest(new { erro = ex.Message });
    }
});

app.MapPost("/api/operacoes", async (OperacaoRequest req, Importacao importacao, CancellationToken ct) =>
{
    var linha = string.Join(';', req.Data, req.Titular, req.Codigo, req.Titulo, req.Vencimento, req.Tipo, req.Quantidade, req.PrecoUnitario, req.Taxas);
    var csv = "data;titular;codigo;titulo;vencimento;tipo;quantidade;preco_unitario;taxas\n" + linha + "\n";

    var hoje = Relogio.HojeSaoPaulo();

    await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
    var parseResult = CsvTradeParser.Parse(stream, hoje);

    if (parseResult.Erros.Count > 0)
        return Results.BadRequest(new { erro = string.Join("; ", parseResult.Erros.Select(e => e.Motivo)) });

    try
    {
        var resultado = await importacao.ExecutarAsync(parseResult.Linhas, ct);
        return Results.Ok(new
        {
            importadas = resultado.Importadas,
            jaExistentes = resultado.JaExistentes,
            ativosCriados = resultado.AtivosCriados,
            titularesCriados = resultado.TitularesCriados
        });
    }
    catch (ImportacaoInvalidaException ex)
    {
        return Results.BadRequest(new { erro = ex.Message });
    }
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
