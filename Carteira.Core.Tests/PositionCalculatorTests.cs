using System.Globalization;
using Xunit;

namespace Carteira.Core.Tests;

public class PositionCalculatorTests
{
    static readonly Asset Selic = new(
        Guid.NewGuid(), AssetClass.TesouroDireto, "tesouro-selic-2029-03-01", "Tesouro Selic 2029", new DateOnly(2029, 3, 1));

    static readonly Asset Ipca = new(
        Guid.NewGuid(), AssetClass.TesouroDireto, "tesouro-ipca-2035-05-15", "Tesouro IPCA+ 2035", new DateOnly(2035, 5, 15));

    static readonly Asset[] Assets = [Selic, Ipca];

    static readonly DateOnly AsOf = new(2026, 9, 26);

    static Trade Aplicacao(Asset asset, DateOnly data, decimal quantidade, decimal preco, decimal taxas = 0m) =>
        new(Guid.NewGuid(), asset.Id, data, TradeType.Aplicacao, quantidade, preco, taxas, "BRL", Guid.NewGuid().ToString());

    static Trade Resgate(Asset asset, DateOnly data, decimal quantidade, decimal preco, decimal taxas = 0m) =>
        new(Guid.NewGuid(), asset.Id, data, TradeType.Resgate, quantidade, preco, taxas, "BRL", Guid.NewGuid().ToString());

    static DailyPrice Preco(Asset asset, DateOnly data, decimal preco) => new(asset.Id, data, preco);

    // Exemplo de referência — Tesouro Selic 2029, taxas = 0
    static readonly Trade RefAplicacao1 = Aplicacao(Selic, new DateOnly(2025, 1, 10), 2.5m, 14000.00m);
    static readonly Trade RefAplicacao2 = Aplicacao(Selic, new DateOnly(2025, 3, 15), 1.0m, 14300.00m);
    static readonly Trade RefResgate = Resgate(Selic, new DateOnly(2025, 6, 20), 1.5m, 14600.00m);
    static readonly Trade[] Referencia = [RefAplicacao1, RefAplicacao2, RefResgate];
    static readonly DailyPrice RefPreco = Preco(Selic, AsOf, 15200.00m);

    // 3,25 IPCA+ 2035 a 3.210,50 (linha 4 do CSV de exemplo)
    static readonly Trade IpcaAplicacao = Aplicacao(Ipca, new DateOnly(2025, 2, 5), 3.25m, 3210.50m);

    static PositionSnapshot Posicao(PortfolioSnapshot snapshot, Asset asset) =>
        Assert.Single(snapshot.Posicoes, p => p.AssetId == asset.Id);

    [Fact]
    public void T01_UmaAplicacaoSemPreco_QuantidadeECustoCorretos_ValorNulo()
    {
        var snapshot = PositionCalculator.Calculate(Assets, [RefAplicacao1], [], AsOf);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(2.5m, p.Quantidade);
        Assert.Equal(35000.00m, p.Custo);
        Assert.Null(p.ValorMercado);
    }

    [Fact]
    public void T02_DuasAplicacoesAPrecosDiferentes_CustoMedioPonderado()
    {
        var snapshot = PositionCalculator.Calculate(Assets, [RefAplicacao1, RefAplicacao2], [], AsOf);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(3.5m, p.Quantidade);
        Assert.Equal(49300.00m, p.Custo);
        Assert.Equal(14085.714286m, Math.Round(p.CustoMedio, 6));
    }

    [Fact]
    public void T03_AplicacaoComTaxas_TaxasEntramNoCusto()
    {
        var comTaxa = Aplicacao(Selic, new DateOnly(2025, 1, 10), 2.5m, 14000.00m, taxas: 10.00m);

        var snapshot = PositionCalculator.Calculate(Assets, [comTaxa], [], AsOf);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(35010.00m, p.Custo);
        Assert.Equal(14004.00m, p.CustoMedio);
    }

    [Fact]
    public void T04_ResgateParcial_CustoMedioInalterado_ResultadoRealizado()
    {
        var snapshot = PositionCalculator.Calculate(Assets, Referencia, [], AsOf);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(2.0m, p.Quantidade);
        Assert.Equal(28171.428571m, Math.Round(p.Custo, 6));
        Assert.Equal(14085.714286m, Math.Round(p.CustoMedio, 6));
        Assert.Equal(771.43m, Math.Round(p.ResultadoRealizado, 2));
    }

    [Fact]
    public void T05_ResgateTotal_PosicaoForaDasAtivas_ResultadoRealizadoMantido()
    {
        // 771,43 do primeiro resgate + (2,0 × 15.000,00 − 28.171,43) = 2.600,00
        var resgateTotal = Resgate(Selic, new DateOnly(2025, 9, 10), 2.0m, 15000.00m);

        var snapshot = PositionCalculator.Calculate(Assets, [.. Referencia, resgateTotal], [], AsOf);

        Assert.Empty(snapshot.Posicoes);
        Assert.Equal(0m, snapshot.CustoTotal);
        Assert.Equal(2600.00m, Math.Round(snapshot.ResultadoRealizadoTotal, 2));
    }

    [Fact]
    public void T06_ResgateMaiorQueAPosicao_LancaInsufficientPosition()
    {
        var aplicacao = Aplicacao(Selic, new DateOnly(2025, 1, 10), 1.0m, 14000.00m);
        var resgate = Resgate(Selic, new DateOnly(2025, 2, 10), 1.5m, 14100.00m);

        Assert.Throws<InsufficientPositionException>(
            () => PositionCalculator.Calculate(Assets, [aplicacao, resgate], [], AsOf));
    }

    [Fact]
    public void T07_OperacaoAposOVencimento_LancaTradeAfterMaturity()
    {
        var aposVencimento = Aplicacao(Selic, new DateOnly(2029, 3, 2), 1.0m, 14000.00m);

        Assert.Throws<TradeAfterMaturityException>(
            () => PositionCalculator.Calculate(Assets, [aposVencimento], [], new DateOnly(2029, 3, 2)));
    }

    [Fact]
    public void T08_MesmaDataAplicacaoEResgate_AplicacaoProcessadaAntes()
    {
        var data = new DateOnly(2025, 1, 10);
        var resgate = Resgate(Selic, data, 1.0m, 14200.00m);
        var aplicacao = Aplicacao(Selic, data, 2.5m, 14000.00m);

        // resgate vem antes na lista; a regra de empate tem que reordenar
        var snapshot = PositionCalculator.Calculate(Assets, [resgate, aplicacao], [], AsOf);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(1.5m, p.Quantidade);
        Assert.Equal(21000.00m, p.Custo);
        Assert.Equal(200.00m, p.ResultadoRealizado);
    }

    [Fact]
    public void T09_AsOfAnteriorAAlgumasOperacoes_SoEntramAsComDataAteAsOf()
    {
        var snapshot = PositionCalculator.Calculate(Assets, Referencia, [], new DateOnly(2025, 3, 1));

        var p = Posicao(snapshot, Selic);
        Assert.Equal(2.5m, p.Quantidade);
        Assert.Equal(35000.00m, p.Custo);
        Assert.Equal(0m, p.ResultadoRealizado);
    }

    [Fact]
    public void T10_PrecoExatoNaData_UsaEssePreco()
    {
        DailyPrice[] precos =
        [
            Preco(Selic, new DateOnly(2026, 9, 25), 15000.00m),
            Preco(Selic, AsOf, 15200.00m),
            Preco(Selic, new DateOnly(2026, 9, 27), 15300.00m),
        ];

        var snapshot = PositionCalculator.Calculate(Assets, Referencia, precos, AsOf);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(15200.00m, p.Preco);
        Assert.Equal(30400.00m, p.ValorMercado);
    }

    [Fact]
    public void T11_SemPrecoNaData_UsaOMaisRecenteAnterior()
    {
        DailyPrice[] precos =
        [
            Preco(Selic, new DateOnly(2026, 9, 1), 15000.00m),
            Preco(Selic, new DateOnly(2026, 9, 20), 15200.00m),
        ];

        var snapshot = PositionCalculator.Calculate(Assets, Referencia, precos, AsOf);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(15200.00m, p.Preco);
        Assert.Equal(30400.00m, p.ValorMercado);
    }

    [Fact]
    public void T12_PrecoSoPosteriorAAsOf_ValorNulo()
    {
        DailyPrice[] precos = [Preco(Selic, new DateOnly(2026, 9, 27), 15200.00m)];

        var snapshot = PositionCalculator.Calculate(Assets, Referencia, precos, AsOf);

        var p = Posicao(snapshot, Selic);
        Assert.Null(p.Preco);
        Assert.Null(p.ValorMercado);
    }

    [Fact]
    public void T13_ExemploDeReferenciaCompleto()
    {
        var snapshot = PositionCalculator.Calculate(Assets, Referencia, [RefPreco], AsOf);

        var p = Posicao(snapshot, Selic);
        Assert.Equal(30400.00m, Math.Round(p.ValorMercado!.Value, 2));
        Assert.Equal(2228.57m, Math.Round(p.Rent!.Value, 2));
        Assert.Equal(7.91m, Math.Round(p.RentPercentual!.Value * 100, 2));
        Assert.Equal(771.43m, Math.Round(p.ResultadoRealizado, 2));
    }

    [Fact]
    public void T14_CarteiraComDoisAtivos_TotaisSaoASoma_RentPercentualSobreCustoTotal()
    {
        DailyPrice[] precos = [RefPreco, Preco(Ipca, AsOf, 3400.00m)];

        var snapshot = PositionCalculator.Calculate(Assets, [.. Referencia, IpcaAplicacao], precos, AsOf);

        var selic = Posicao(snapshot, Selic);
        var ipca = Posicao(snapshot, Ipca);
        Assert.Equal(selic.Custo + ipca.Custo, snapshot.CustoTotal);
        Assert.Equal(selic.ValorMercado!.Value + ipca.ValorMercado!.Value, snapshot.ValorTotal);
        Assert.Equal(selic.Rent!.Value + ipca.Rent!.Value, snapshot.RentTotal);
        Assert.Equal(snapshot.RentTotal / snapshot.CustoTotal, snapshot.RentPercentual);

        Assert.Equal(38605.55m, Math.Round(snapshot.CustoTotal, 2));
        Assert.Equal(41450.00m, Math.Round(snapshot.ValorTotal, 2));
        Assert.Equal(2844.45m, Math.Round(snapshot.RentTotal, 2));
        Assert.Equal(7.37m, Math.Round(snapshot.RentPercentual * 100, 2));
    }

    [Fact]
    public void T15_CarteiraComUmAtivoSemPreco_CustoIncluiValorExcluiComAviso()
    {
        var snapshot = PositionCalculator.Calculate(Assets, [.. Referencia, IpcaAplicacao], [RefPreco], AsOf);

        Assert.Null(Posicao(snapshot, Ipca).ValorMercado);
        Assert.Equal(38605.55m, Math.Round(snapshot.CustoTotal, 2));
        Assert.Equal(30400.00m, Math.Round(snapshot.ValorTotal, 2));
        Assert.Equal(2228.57m, Math.Round(snapshot.RentTotal, 2));
        // base = custo só da posição com preço (Selic 28.171,43): 2.228,57 / 28.171,43 = 7,91%
        Assert.Equal(7.91m, Math.Round(snapshot.RentPercentual * 100, 2));
        Assert.True(snapshot.TemPosicaoSemPreco);
    }

    [Fact]
    public void T16_ListaDeOperacoesVazia_SnapshotVazioTotaisZero()
    {
        var snapshot = PositionCalculator.Calculate(Assets, [], [], AsOf);

        Assert.Empty(snapshot.Posicoes);
        Assert.Equal(0m, snapshot.CustoTotal);
        Assert.Equal(0m, snapshot.ValorTotal);
        Assert.Equal(0m, snapshot.RentTotal);
        Assert.Equal(0m, snapshot.RentPercentual);
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
        var trade = Aplicacao(
            Selic,
            new DateOnly(2025, 1, 10),
            decimal.Parse(quantidade, CultureInfo.InvariantCulture),
            decimal.Parse(preco, CultureInfo.InvariantCulture));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PositionCalculator.Calculate(Assets, [trade], [], AsOf));
    }
}
