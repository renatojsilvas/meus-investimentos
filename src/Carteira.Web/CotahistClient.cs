using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Carteira.Web;

public class CotahistClient(HttpClient httpClient) : ICotacoesB3Client
{
    public async Task<IReadOnlyList<CotacaoB3Dto>> GetFechamentosAsync(
        IReadOnlyCollection<string> tickers, DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var alvo = tickers.Select(t => t.Trim().ToUpperInvariant()).ToHashSet();
        var resultado = new List<CotacaoB3Dto>();

        if (de == ate)
        {
            await LerArquivoAsync($"COTAHIST_D{de:ddMMyyyy}.ZIP", alvo, de, ate, resultado, ct);
            return resultado;
        }

        for (var ano = de.Year; ano <= ate.Year; ano++)
            await LerArquivoAsync($"COTAHIST_A{ano}.ZIP", alvo, de, ate, resultado, ct);

        return resultado;
    }

    private async Task LerArquivoAsync(
        string arquivo, HashSet<string> alvo, DateOnly de, DateOnly ate, List<CotacaoB3Dto> resultado, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(arquivo, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return;
        response.EnsureSuccessStatusCode();

        // ZipArchive precisa de stream com seek: o zip (comprimido) vai para um temporário;
        // o .TXT dentro nunca é extraído, só lido em stream.
        var temporario = Path.GetTempFileName();
        try
        {
            await using (var rede = await response.Content.ReadAsStreamAsync(ct))
            await using (var destino = File.Create(temporario))
                await rede.CopyToAsync(destino, ct);

            await using var fs = File.OpenRead(temporario);
            using var zip = OpenZip(fs, arquivo);
            var entrada = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".TXT", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"{arquivo}: zip sem .TXT");

            await using var txt = entrada.Open();
            using var reader = new StreamReader(txt, Encoding.Latin1);

            var cabecalho = await reader.ReadLineAsync(ct);
            if (cabecalho is null || !cabecalho.StartsWith("00COTAHIST", StringComparison.Ordinal))
                throw new InvalidDataException($"{arquivo}: .TXT não começa com 00COTAHIST");

            string? linha;
            while ((linha = await reader.ReadLineAsync(ct)) is not null)
            {
                if (linha.Length < 121 || linha[0] != '0' || linha[1] != '1') continue;
                if (!linha.AsSpan(24, 3).SequenceEqual("010")) continue;

                var ticker = linha.Substring(12, 12).Trim();
                if (!alvo.Contains(ticker)) continue;

                var data = DateOnly.ParseExact(linha.AsSpan(2, 8), "yyyyMMdd", CultureInfo.InvariantCulture);
                if (data < de || data > ate) continue;

                var fechamento = decimal.Parse(linha.AsSpan(108, 13), NumberStyles.None, CultureInfo.InvariantCulture) / 100m;
                resultado.Add(new CotacaoB3Dto(ticker, data, fechamento));
            }
        }
        finally
        {
            File.Delete(temporario);
        }
    }

    private static ZipArchive OpenZip(Stream stream, string arquivo)
    {
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"{arquivo}: corpo não é um zip", ex);
        }
    }
}
