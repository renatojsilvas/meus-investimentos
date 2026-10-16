using System.Globalization;
using System.Text.RegularExpressions;
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

app.MapPost("/api/precos", async (PrecoRequest req, CarteiraDbContext db, Snapshots snapshots, CancellationToken ct) =>
{
    var codigo = req.Codigo?.Trim() ?? "";
    var ativo = await db.Ativos.FirstOrDefaultAsync(a => a.Codigo == codigo, ct);
    if (ativo is null)
        return Results.BadRequest(new { erro = $"código '{codigo}' não cadastrado" });

    if (!DateOnly.TryParseExact(req.Data?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
        return Results.BadRequest(new { erro = "data inválida (use dd/MM/yyyy)" });
    if (data > Relogio.HojeSaoPaulo())
        return Results.BadRequest(new { erro = "data no futuro" });

    var puTexto = req.Pu?.Trim() ?? "";
    if (!Regex.IsMatch(puTexto, @"^\d+(,\d{1,6})?$")
        || !decimal.TryParse(puTexto.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var pu)
        || pu <= 0)
        return Results.BadRequest(new { erro = "pu inválido (use número maior que zero, ex.: 1047,32)" });

    var existente = await db.PrecosDiarios.FindAsync([ativo.Id, data], ct);
    if (existente is not null)
        db.PrecosDiarios.Remove(existente);
    db.PrecosDiarios.Add(new PrecoDiario(ativo.Id, data, pu));
    await db.SaveChangesAsync(ct);

    await snapshots.ReconstruirSnapshotsAsync(data, ct);

    return Results.Ok(new { codigo, data = req.Data!.Trim(), pu = puTexto });
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
    try
    {
        var resultado = await job.BackfillIndicesAsync(ct);
        return Results.Ok(resultado);
    }
    catch (BcbRespostaInvalidaException ex)
    {
        return Results.Json(
            new { erro = ex.Message, fatia = new { de = ex.DataInicial, ate = ex.DataFinal } },
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();
