using System.Text;
using Xunit;

namespace Carteira.Core.Tests;

public class CsvTradeParserTests
{
    static readonly DateOnly Hoje = new(2026, 9, 26);

    const string Cabecalho = "data;titular;codigo;titulo;vencimento;tipo;quantidade;preco_unitario;taxas";

    const string LinhaSelic1 = "10/01/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;APLICACAO;2,5;14000,00;0";
    const string LinhaSelic2 = "15/03/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;APLICACAO;1,0;14300,00;0";
    const string LinhaSelic3 = "20/06/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;RESGATE;1,5;14600,00;0";
    const string LinhaIpca = "05/02/2025;renato;tesouro-ipca-mais-2035-05-15;Tesouro IPCA+ 2035;15/05/2035;APLICACAO;3,25;3210,50;0";

    static ParseResult Parse(params string[] linhas)
    {
        var csv = string.Join("\n", linhas);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return CsvTradeParser.Parse(stream, Hoje);
    }

    static ParseResult ParseComCabecalho(params string[] linhas) =>
        Parse([Cabecalho, .. linhas]);

    [Fact]
    public void T18_ArquivoDeExemplo_QuatroLinhasValidas_DoisCodigosDistintos()
    {
        var result = ParseComCabecalho(LinhaSelic1, LinhaSelic2, LinhaSelic3, LinhaIpca);

        Assert.Empty(result.Erros);
        Assert.Equal(4, result.Linhas.Count);
        Assert.Equal(2, result.Linhas.Select(l => l.Codigo).Distinct().Count());
    }

    [Theory]
    [InlineData(LinhaSelic1)]
    [InlineData("data;codigo;titulo;vencimento;tipo;quantidade;preco_unitario")]
    public void T19_CabecalhoFaltandoOuColunaAMenos_ErroNaLinha1(string primeiraLinha)
    {
        var result = Parse(primeiraLinha, LinhaSelic2);

        var erro = Assert.Single(result.Erros, e => e.Linha == 1);
        Assert.False(string.IsNullOrWhiteSpace(erro.Motivo));
        Assert.Empty(result.Linhas);
    }

    [Fact]
    public void T20_DataInvalida_ErroComNumeroDaLinha()
    {
        var result = ParseComCabecalho(
            "31/02/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;APLICACAO;2,5;14000,00;0");

        var erro = Assert.Single(result.Erros);
        Assert.Equal(2, erro.Linha);
    }

    [Fact]
    public void T21_DataFutura_Erro()
    {
        var result = ParseComCabecalho(
            "27/09/2026;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;APLICACAO;2,5;14000,00;0");

        var erro = Assert.Single(result.Erros);
        Assert.Equal(2, erro.Linha);
    }

    [Fact]
    public void T22_TipoDesconhecido_Erro()
    {
        var result = ParseComCabecalho(
            "10/01/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;COMPRA;2,5;14000,00;0");

        var erro = Assert.Single(result.Erros);
        Assert.Equal(2, erro.Linha);
    }

    [Fact]
    public void T23_AplicacaoEmMinusculas_Aceito()
    {
        var result = ParseComCabecalho(
            "10/01/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;aplicacao;2,5;14000,00;0");

        Assert.Empty(result.Erros);
        var linha = Assert.Single(result.Linhas);
        Assert.Equal(TipoOperacao.Aplicacao, linha.Tipo);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1,5")]
    public void T24_QuantidadeZeroOuNegativa_Erro(string quantidade)
    {
        var result = ParseComCabecalho(
            $"10/01/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;APLICACAO;{quantidade};14000,00;0");

        var erro = Assert.Single(result.Erros);
        Assert.Equal(2, erro.Linha);
    }

    [Fact]
    public void T25_TaxasVazio_InterpretadoComoZero()
    {
        var result = ParseComCabecalho(
            "10/01/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;APLICACAO;2,5;14000,00;");

        Assert.Empty(result.Erros);
        var linha = Assert.Single(result.Linhas);
        Assert.Equal(0m, linha.Taxas);
    }

    [Theory]
    [InlineData("2.5", "14000,00")]
    [InlineData("2,5", "14000.00")]
    public void T26_DecimalComPontoEmVezDeVirgula_Erro(string quantidade, string preco)
    {
        var result = ParseComCabecalho(
            $"10/01/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;APLICACAO;{quantidade};{preco};0");

        var erro = Assert.Single(result.Erros);
        Assert.Equal(2, erro.Linha);
    }

    [Fact]
    public void T27_VencimentoDiferenteParaMesmoCodigo_ErroNaSegundaLinha()
    {
        var result = ParseComCabecalho(
            LinhaSelic1,
            "15/03/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/04/2029;APLICACAO;1,0;14300,00;0");

        var erro = Assert.Single(result.Erros);
        Assert.Equal(3, erro.Linha);
    }

    [Fact]
    public void T28_DuasLinhasIdenticas_DuasLinhaOperacaoComMesmaChaveImportacao()
    {
        var result = ParseComCabecalho(LinhaSelic1, LinhaSelic1);

        Assert.Empty(result.Erros);
        Assert.Equal(2, result.Linhas.Count);
        Assert.False(string.IsNullOrEmpty(result.Linhas[0].ChaveImportacao));
        Assert.Equal(result.Linhas[0].ChaveImportacao, result.Linhas[1].ChaveImportacao);
    }

    [Fact]
    public void T29_UmaLinhaInvalidaNoMeio_NenhumaLinhaValida_SoErros()
    {
        var result = ParseComCabecalho(
            LinhaSelic1,
            "31/02/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;APLICACAO;1,0;14300,00;0",
            LinhaSelic3);

        Assert.Empty(result.Linhas);
        var erro = Assert.Single(result.Erros);
        Assert.Equal(3, erro.Linha);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tesouro selic-2029-03-01")]
    [InlineData("Tesouro-Selic-2029-03-01")]
    public void T30_CodigoVazioComEspacoOuComMaiuscula_Erro(string codigo)
    {
        var result = ParseComCabecalho(
            $"10/01/2025;renato;{codigo};Tesouro Selic 2029;01/03/2029;APLICACAO;2,5;14000,00;0");

        var erro = Assert.Single(result.Erros);
        Assert.Equal(2, erro.Linha);
    }

    [Theory]
    [InlineData("")]
    [InlineData("renato silva")]
    [InlineData("Renato")]
    public void T32_TitularVazioComEspacoOuComMaiuscula_Erro(string titular)
    {
        var result = ParseComCabecalho(
            $"10/01/2025;{titular};tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;APLICACAO;2,5;14000,00;0");

        var erro = Assert.Single(result.Erros);
        Assert.Equal(2, erro.Linha);
    }
}
