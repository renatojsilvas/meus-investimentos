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

builder.Services.AddHttpClient<IBcbClient, BcbClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["Bcb:BaseUrl"] ?? "https://api.bcb.gov.br";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
});

builder.Services.AddSingleton<Snapshots>();
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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

IResult ResultadoJson(ResultadoImportacao resultado) => Results.Ok(new
{
    importadas = resultado.Importadas,
    jaExistentes = resultado.JaExistentes,
    ativosCriados = resultado.AtivosCriados,
    titularesCriados = resultado.TitularesCriados
});

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
        return ResultadoJson(resultado);
    }
    catch (ImportacaoInvalidaException ex)
    {
        return Results.BadRequest(new { erro = ex.Message });
    }
});

app.MapPost("/api/operacoes", async (OperacaoRequest req, Importacao importacao, CancellationToken ct) =>
{
    try
    {
        var resultado = await importacao.LancarAsync(req, ct);
        return ResultadoJson(resultado);
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
    var resultado = await job.BackfillPrecosAsync(ct);
    return Results.Ok(resultado);
});

app.MapPost("/api/snapshots/rebuild", async (Snapshots snapshots, CancellationToken ct) =>
{
    await snapshots.ReconstruirSnapshotsAsync(null, ct);
    return Results.Ok(new { status = "reconstruido" });
});

app.MapPost("/api/indices/backfill", async (PriceSyncJob job, CancellationToken ct) =>
{
    var resultado = await job.BackfillIndicesAsync(ct);
    return Results.Ok(resultado);
});

app.Run();
