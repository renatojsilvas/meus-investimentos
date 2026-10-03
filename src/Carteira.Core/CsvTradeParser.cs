using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Carteira.Core;

public static class CsvTradeParser
{
    const string Cabecalho = "data;titular;codigo;titulo;vencimento;tipo;quantidade;preco_unitario;taxas";
    const int Colunas = 9;

    static readonly Regex Slug = new("^[a-z0-9-]+$", RegexOptions.Compiled);
    static readonly Regex DecimalBr = new(@"^-?\d+(,\d+)?$", RegexOptions.Compiled);

    public static ParseResult Parse(Stream stream, DateOnly hoje)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var linhas = new List<LinhaOperacao>();
        var erros = new List<ParseError>();
        var vencimentos = new Dictionary<string, DateOnly>();

        var primeira = reader.ReadLine();
        if (primeira is null || primeira.Trim() != Cabecalho)
        {
            erros.Add(new ParseError(1, $"cabeçalho inválido; esperado: {Cabecalho}"));
            return new ParseResult([], erros);
        }

        var numero = 1;
        while (reader.ReadLine() is { } texto)
        {
            numero++;
            if (string.IsNullOrWhiteSpace(texto)) continue;

            var campos = texto.Split(';');
            if (campos.Length != Colunas)
            {
                erros.Add(new ParseError(numero, $"esperadas {Colunas} colunas, encontradas {campos.Length}"));
                continue;
            }

            var antes = erros.Count;
            void Erro(string motivo) => erros.Add(new ParseError(numero, motivo));

            var dataTexto = campos[0].Trim();
            DateOnly data = default;
            if (!DateOnly.TryParseExact(dataTexto, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out data))
                Erro($"data inválida: '{dataTexto}'");
            else if (data > hoje)
                Erro($"data futura: '{dataTexto}'");

            var titular = campos[1];
            if (!Slug.IsMatch(titular))
                Erro($"titular inválido: '{titular}' (minúsculas, sem espaço)");

            var codigo = campos[2];
            if (!Slug.IsMatch(codigo))
                Erro($"codigo inválido: '{codigo}' (minúsculas, sem espaço, exatamente o slug da API)");

            var titulo = campos[3].Trim();
            if (titulo.Length == 0)
                Erro("titulo obrigatório");

            var vencimentoTexto = campos[4].Trim();
            DateOnly vencimento = default;
            var temVencimento = false;
            if (vencimentoTexto.Length > 0)
            {
                if (!DateOnly.TryParseExact(vencimentoTexto, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out vencimento))
                    Erro($"vencimento inválido: '{vencimentoTexto}'");
                else
                    temVencimento = true;
            }
            if (Slug.IsMatch(codigo))
            {
                if (vencimentos.TryGetValue(codigo, out var conhecido))
                {
                    if (temVencimento && vencimento != conhecido)
                        Erro($"vencimento {vencimentoTexto} difere do informado antes para '{codigo}' ({conhecido:dd/MM/yyyy})");
                    vencimento = conhecido;
                }
                else if (temVencimento)
                    vencimentos[codigo] = vencimento;
                else if (vencimentoTexto.Length == 0)
                    Erro($"vencimento obrigatório na primeira ocorrência de '{codigo}'");
            }

            var tipoTexto = campos[5].Trim();
            TipoOperacao tipo = default;
            if (tipoTexto.Equals("APLICACAO", StringComparison.OrdinalIgnoreCase))
                tipo = TipoOperacao.Aplicacao;
            else if (tipoTexto.Equals("RESGATE", StringComparison.OrdinalIgnoreCase))
                tipo = TipoOperacao.Resgate;
            else
                Erro($"tipo desconhecido: '{tipoTexto}' (APLICACAO ou RESGATE)");

            var quantidade = LerDecimal(campos[6], "quantidade", 8, positivo: true, Erro);
            var preco = LerDecimal(campos[7], "preco_unitario", 6, positivo: true, Erro);
            var taxas = campos[8].Trim().Length == 0 ? 0m : LerDecimal(campos[8], "taxas", null, positivo: false, Erro);

            if (erros.Count > antes) continue;

            linhas.Add(new LinhaOperacao(data, titular, codigo, titulo, vencimento, tipo, quantidade, preco, taxas,
                CalcularChave(titular, codigo, data, tipo, quantidade, preco)));
        }

        return erros.Count > 0
            ? new ParseResult([], erros)
            : new ParseResult(linhas, []);
    }

    static decimal LerDecimal(string texto, string coluna, int? maxCasas, bool positivo, Action<string> erro)
    {
        texto = texto.Trim();
        if (!DecimalBr.IsMatch(texto))
        {
            erro($"{coluna} inválida: '{texto}' (formato 1234,56)");
            return 0m;
        }

        var valor = decimal.Parse(texto.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture);
        if (positivo && valor <= 0)
            erro($"{coluna} deve ser maior que zero: '{texto}'");
        else if (!positivo && valor < 0)
            erro($"{coluna} não pode ser negativa: '{texto}'");

        var virgula = texto.IndexOf(',');
        if (maxCasas is { } max && virgula >= 0 && texto.Length - virgula - 1 > max)
            erro($"{coluna} com mais de {max} casas decimais: '{texto}'");

        return valor;
    }

    static string CalcularChave(string titular, string codigo, DateOnly data, TipoOperacao tipo, decimal quantidade, decimal preco)
    {
        var texto = string.Join('|',
            titular,
            codigo,
            data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            tipo,
            Normalizar(quantidade),
            Normalizar(preco));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto))).ToLowerInvariant();
    }

    static string Normalizar(decimal valor) =>
        valor.ToString("0.############################", CultureInfo.InvariantCulture);
}
