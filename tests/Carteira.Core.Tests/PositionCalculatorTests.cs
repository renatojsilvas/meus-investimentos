using System.Globalization;
using Xunit;

namespace Carteira.Core.Tests;

public class PositionCalculatorTests
{
    static readonly Ativo Selic = new(
        Guid.NewGuid(), ClasseAtivo.TesouroDireto, "tesouro-selic-2029-03-01", "Tesouro Selic 2029", new DateOnly(2029, 3, 1));

    static readonly Ativo Ipca = new(
        Guid.NewGuid(), ClasseAtivo.TesouroDireto, "tesouro-ipca-mais-2035-05-15", "Tesouro IPCA+ 2035", new DateOnly(2035, 5, 15));

    static readonly Ativo[] Ativos = [Selic, Ipca];

    static readonly Titular Renato = new(Guid.NewGuid(), "Renato", "renato");
    static readonly Titular Maria = new(Guid.NewGuid(), "Maria", "maria");
    static readonly Titular[] Titulares = [Renato, Maria];

    static readonly DateOnly DataReferencia = new(2026, 9, 26);

    static Operacao Aplicacao(Ativo ativo, DateOnly data, decimal quantidade, decimal preco, decimal taxas = 0m, Titular? titular = null) =>
        new(Guid.NewGuid(), (titular ?? Renato).Id, ativo.Id, data, TipoOperacao.Aplicacao, quantidade, preco, taxas, "BRL", Guid.NewGuid().ToString());

    static Operacao Resgate(Ativo ativo, DateOnly data, decimal quantidade, decimal preco, decimal taxas = 0m, Titular? titular = null) =>
        new(Guid.NewGuid(), (titular ?? Renato).Id, ativo.Id, data, TipoOperacao.Resgate, quantidade, preco, taxas, "BRL", Guid.NewGuid().ToString());

    static PrecoDiario Preco(Ativo ativo, DateOnly data, decimal preco) => new(ativo.Id, data, preco);

    static readonly Operacao RefAplicacao1 = Aplicacao(Selic, new DateOnly(2025, 1, 10), 2.5m, 14000.00m);
    static readonly Operacao RefAplicacao2 = Aplicacao(Selic, new DateOnly(2025, 3, 15), 1.0m, 14300.00m);
    static readonly Operacao RefResgate = Resgate(Selic, new DateOnly(2025, 6, 20), 1.5m, 14600.00m);
    static readonly Operacao[] Referencia = [RefAplicacao1, RefAplicacao2, RefResgate];
    static readonly PrecoDiario RefPreco = Preco(Selic, DataReferencia, 15200.00m);

    static readonly Operacao IpcaAplicacao = Aplicacao(Ipca, new DateOnly(2025, 2, 5), 3.25m, 3210.50m);

    static PosicaoSnapshot Posicao(CarteiraSnapshot snapshot, Ativo ativo) =>
        Assert.Single(snapshot.Posicoes, p => p.AtivoId == ativo.Id);

    [Fact]
    public void T01_UmaAplicacaoSemPreco_QuantidadeECustoCorretos_ValorNulo()
    {
        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, [RefAplicacao1], [], DataReferencia);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(2.5m, p.Quantidade);
        Assert.Equal(35000.00m, p.Custo);
        Assert.Null(p.ValorMercado);
    }

    [Fact]
    public void T02_DuasAplicacoesAPrecosDiferentes_CustoMedioPonderado()
    {
        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, [RefAplicacao1, RefAplicacao2], [], DataReferencia);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(3.5m, p.Quantidade);
        Assert.Equal(49300.00m, p.Custo);
        Assert.Equal(14085.714286m, Math.Round(p.CustoMedio, 6));
    }

    [Fact]
    public void T03_AplicacaoComTaxas_TaxasEntramNoCusto()
    {
        var comTaxa = Aplicacao(Selic, new DateOnly(2025, 1, 10), 2.5m, 14000.00m, taxas: 10.00m);

        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, [comTaxa], [], DataReferencia);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(35010.00m, p.Custo);
        Assert.Equal(14004.00m, p.CustoMedio);
    }

    [Fact]
    public void T04_ResgateParcial_CustoMedioInalterado_ResultadoRealizado()
    {
        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, Referencia, [], DataReferencia);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(2.0m, p.Quantidade);
        Assert.Equal(28171.428571m, Math.Round(p.Custo, 6));
        Assert.Equal(14085.714286m, Math.Round(p.CustoMedio, 6));
        Assert.Equal(771.43m, Math.Round(p.ResultadoRealizado, 2));
    }

    [Fact]
    public void T05_ResgateTotal_PosicaoForaDasAtivas_ResultadoRealizadoMantido()
    {
        var resgateTotal = Resgate(Selic, new DateOnly(2025, 9, 10), 2.0m, 15000.00m);

        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, [.. Referencia, resgateTotal], [], DataReferencia);

        Assert.Empty(snapshot.Posicoes);
        Assert.Equal(0m, snapshot.CustoTotal);
        Assert.Equal(2600.00m, Math.Round(snapshot.ResultadoRealizadoTotal, 2));
    }

    [Fact]
    public void T06_ResgateMaiorQueAPosicao_LancaPosicaoInsuficiente()
    {
        var aplicacao = Aplicacao(Selic, new DateOnly(2025, 1, 10), 1.0m, 14000.00m);
        var resgate = Resgate(Selic, new DateOnly(2025, 2, 10), 1.5m, 14100.00m);

        Assert.Throws<PosicaoInsuficienteException>(
            () => PositionCalculator.Calculate(Titulares, Ativos, [aplicacao, resgate], [], DataReferencia));
    }

    [Fact]
    public void T07_OperacaoAposOVencimento_LancaOperacaoAposVencimento()
    {
        var aposVencimento = Aplicacao(Selic, new DateOnly(2029, 3, 2), 1.0m, 14000.00m);

        Assert.Throws<OperacaoAposVencimentoException>(
            () => PositionCalculator.Calculate(Titulares, Ativos, [aposVencimento], [], new DateOnly(2029, 3, 2)));
    }

    [Fact]
    public void T08_MesmaDataAplicacaoEResgate_AplicacaoProcessadaAntes()
    {
        var data = new DateOnly(2025, 1, 10);
        var resgate = Resgate(Selic, data, 1.0m, 14200.00m);
        var aplicacao = Aplicacao(Selic, data, 2.5m, 14000.00m);

        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, [resgate, aplicacao], [], DataReferencia);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(1.5m, p.Quantidade);
        Assert.Equal(21000.00m, p.Custo);
        Assert.Equal(200.00m, p.ResultadoRealizado);
    }

    [Fact]
    public void T09_DataReferenciaAnteriorAAlgumasOperacoes_SoEntramAsComDataAteDataReferencia()
    {
        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, Referencia, [], new DateOnly(2025, 3, 1));

        var p = Posicao(snapshot, Selic);
        Assert.Equal(2.5m, p.Quantidade);
        Assert.Equal(35000.00m, p.Custo);
        Assert.Equal(0m, p.ResultadoRealizado);
    }

    [Fact]
    public void T10_PrecoExatoNaData_UsaEssePreco()
    {
        PrecoDiario[] precos =
        [
            Preco(Selic, new DateOnly(2026, 9, 25), 15000.00m),
            Preco(Selic, DataReferencia, 15200.00m),
            Preco(Selic, new DateOnly(2026, 9, 27), 15300.00m),
        ];

        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, Referencia, precos, DataReferencia);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(15200.00m, p.Preco);
        Assert.Equal(30400.00m, p.ValorMercado);
    }

    [Fact]
    public void T11_SemPrecoNaData_UsaOMaisRecenteAnterior()
    {
        PrecoDiario[] precos =
        [
            Preco(Selic, new DateOnly(2026, 9, 1), 15000.00m),
            Preco(Selic, new DateOnly(2026, 9, 20), 15200.00m),
        ];

        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, Referencia, precos, DataReferencia);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(15200.00m, p.Preco);
        Assert.Equal(30400.00m, p.ValorMercado);
    }

    [Fact]
    public void T12_PrecoSoPosteriorADataReferencia_ValorNulo()
    {
        PrecoDiario[] precos = [Preco(Selic, new DateOnly(2026, 9, 27), 15200.00m)];

        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, Referencia, precos, DataReferencia);

        var p = Posicao(snapshot, Selic);
        Assert.Null(p.Preco);
        Assert.Null(p.ValorMercado);
    }

    [Fact]
    public void T13_ExemploDeReferenciaCompleto()
    {
        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, Referencia, [RefPreco], DataReferencia);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(30400.00m, Math.Round(p.ValorMercado!.Value, 2));
        Assert.Equal(2228.57m, Math.Round(p.Rentabilidade!.Value, 2));
        Assert.Equal(7.91m, Math.Round(p.RentabilidadePercentual!.Value * 100, 2));
        Assert.Equal(771.43m, Math.Round(p.ResultadoRealizado, 2));
    }

    [Fact]
    public void T14_CarteiraComDoisAtivos_TotaisSaoASoma_RentabilidadePercentualSobreCustoTotal()
    {
        PrecoDiario[] precos = [RefPreco, Preco(Ipca, DataReferencia, 3400.00m)];

        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, [.. Referencia, IpcaAplicacao], precos, DataReferencia);

        var selic = Posicao(snapshot, Selic);
        var ipca = Posicao(snapshot, Ipca);
        Assert.Equal(selic.Custo + ipca.Custo, snapshot.CustoTotal);
        Assert.Equal(selic.ValorMercado!.Value + ipca.ValorMercado!.Value, snapshot.ValorTotal);
        Assert.Equal(selic.Rentabilidade!.Value + ipca.Rentabilidade!.Value, snapshot.RentabilidadeTotal);
        Assert.Equal(snapshot.RentabilidadeTotal / snapshot.CustoTotal, snapshot.RentabilidadePercentual);

        Assert.Equal(38605.55m, Math.Round(snapshot.CustoTotal, 2));
        Assert.Equal(41450.00m, Math.Round(snapshot.ValorTotal, 2));
        Assert.Equal(2844.45m, Math.Round(snapshot.RentabilidadeTotal, 2));
        Assert.Equal(7.37m, Math.Round(snapshot.RentabilidadePercentual * 100, 2));
    }

    [Fact]
    public void T15_CarteiraComUmAtivoSemPreco_CustoIncluiValorExcluiComAviso()
    {
        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, [.. Referencia, IpcaAplicacao], [RefPreco], DataReferencia);

        Assert.Null(Posicao(snapshot, Ipca).ValorMercado);
        Assert.Equal(38605.55m, Math.Round(snapshot.CustoTotal, 2));
        Assert.Equal(30400.00m, Math.Round(snapshot.ValorTotal, 2));
        Assert.Equal(2228.57m, Math.Round(snapshot.RentabilidadeTotal, 2));
        Assert.Equal(7.91m, Math.Round(snapshot.RentabilidadePercentual * 100, 2));
        Assert.True(snapshot.TemPosicaoSemPreco);
    }

    [Fact]
    public void T16_ListaDeOperacoesVazia_SnapshotVazioTotaisZero()
    {
        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, [], [], DataReferencia);

        Assert.Empty(snapshot.Posicoes);
        Assert.Equal(0m, snapshot.CustoTotal);
        Assert.Equal(0m, snapshot.ValorTotal);
        Assert.Equal(0m, snapshot.RentabilidadeTotal);
        Assert.Equal(0m, snapshot.RentabilidadePercentual);
        Assert.Equal(0m, snapshot.ResultadoRealizadoTotal);
        Assert.False(snapshot.TemPosicaoSemPreco);
    }

    [Theory]
    [InlineData("0", "14000.00")]
    [InlineData("-1", "14000.00")]
    [InlineData("1", "0")]
    [InlineData("1", "-14000.00")]
    public void T17_QuantidadeOuPrecoMenorOuIgualAZero_LancaArgumentOutOfRange(string quantidade, string preco)
    {
        var operacao = Aplicacao(
            Selic,
            new DateOnly(2025, 1, 10),
            decimal.Parse(quantidade, CultureInfo.InvariantCulture),
            decimal.Parse(preco, CultureInfo.InvariantCulture));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PositionCalculator.Calculate(Titulares, Ativos, [operacao], [], DataReferencia));
    }

    [Fact]
    public void T31_MesmoTituloEmDoisTitulares_DuasPosicoes_TotaisSomam()
    {
        var trocaRenato = Aplicacao(Selic, new DateOnly(2025, 1, 10), 2.5m, 14000.00m, titular: Renato);
        var trocaMaria = Aplicacao(Selic, new DateOnly(2025, 1, 10), 1.0m, 14300.00m, titular: Maria);

        var snapshot = PositionCalculator.Calculate(Titulares, Ativos, [trocaRenato, trocaMaria], [], DataReferencia);

        Assert.Equal(2, snapshot.Posicoes.Count(p => p.AtivoId == Selic.Id));

        var posRenato = Assert.Single(snapshot.Posicoes, p => p.AtivoId == Selic.Id && p.TitularId == Renato.Id);
        var posMaria = Assert.Single(snapshot.Posicoes, p => p.AtivoId == Selic.Id && p.TitularId == Maria.Id);

        Assert.Equal(2.5m, posRenato.Quantidade);
        Assert.Equal(35000.00m, posRenato.Custo);
        Assert.Equal(1.0m, posMaria.Quantidade);
        Assert.Equal(14300.00m, posMaria.Custo);

        Assert.Equal(posRenato.Custo + posMaria.Custo, snapshot.CustoTotal);
    }
}
