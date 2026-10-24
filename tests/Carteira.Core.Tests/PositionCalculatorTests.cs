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

    [Fact]
    public void T49_RendaFixaComPuManual_ExemploDeReferencia()
    {
        var cdb = new Ativo(Guid.NewGuid(), ClasseAtivo.RendaFixa, "cdb-inter-2027-03", "CDB Inter 2027", new DateOnly(2027, 3, 1));
        Ativo[] ativos = [cdb];
        Operacao[] operacoes = [Aplicacao(cdb, new DateOnly(2025, 1, 10), 10m, 1000.00m)];
        PrecoDiario[] precos = [Preco(cdb, new DateOnly(2025, 6, 30), 1047.32m), Preco(cdb, new DateOnly(2025, 12, 31), 1101.50m)];

        var setembro = PositionCalculator.Calculate(Titulares, ativos, operacoes, precos, new DateOnly(2025, 9, 15));
        var pSetembro = Posicao(setembro, cdb);
        Assert.Equal(10473.20m, Math.Round(pSetembro.ValorMercado!.Value, 2));
        Assert.Equal(473.20m, Math.Round(pSetembro.Rentabilidade!.Value, 2));
        Assert.Equal(0.0473m, Math.Round(pSetembro.RentabilidadePercentual!.Value, 4));

        var janeiro = PositionCalculator.Calculate(Titulares, ativos, operacoes, precos, new DateOnly(2026, 1, 15));
        var pJaneiro = Posicao(janeiro, cdb);
        Assert.Equal(11015.00m, Math.Round(pJaneiro.ValorMercado!.Value, 2));
        Assert.Equal(1015.00m, Math.Round(pJaneiro.Rentabilidade!.Value, 2));
        Assert.Equal(0.1015m, Math.Round(pJaneiro.RentabilidadePercentual!.Value, 4));

        var antes = PositionCalculator.Calculate(Titulares, ativos, operacoes, precos, new DateOnly(2025, 1, 5));
        Assert.Empty(antes.Posicoes);

        var comoTesouro = PositionCalculator.Calculate(
            Titulares, [cdb with { Classe = ClasseAtivo.TesouroDireto }], operacoes, precos, new DateOnly(2025, 9, 15));
        Assert.Equal(pSetembro, Assert.Single(comoTesouro.Posicoes));
        Assert.Equal(setembro.ValorTotal, comoTesouro.ValorTotal);
    }

    [Fact]
    public void T52_AtivoSemVencimento_AceitaQualquerData_ComVencimentoLancaOperacaoAposVencimento()
    {
        var cofrinho = new Ativo(Guid.NewGuid(), ClasseAtivo.RendaFixa, "cofrinho-nubank", "Caixinha Nubank", null);
        var aplicacao = Aplicacao(cofrinho, new DateOnly(2030, 1, 10), 10m, 100.00m);
        var resgate = Resgate(cofrinho, new DateOnly(2040, 6, 30), 4m, 150.00m);
        var dataReferencia = new DateOnly(2040, 6, 30);

        var snapshot = PositionCalculator.Calculate(Titulares, [cofrinho], [aplicacao, resgate], [], dataReferencia);

        var p = Posicao(snapshot, cofrinho);
        Assert.Equal(6m, p.Quantidade);
        Assert.Equal(600.00m, p.Custo);
        Assert.Equal(200.00m, p.ResultadoRealizado);

        var comVencimento = cofrinho with { Vencimento = new DateOnly(2035, 1, 1) };
        Assert.Throws<OperacaoAposVencimentoException>(
            () => PositionCalculator.Calculate(Titulares, [comVencimento], [aplicacao, resgate], [], dataReferencia));
    }

    static readonly Ativo AcaoX = new(Guid.NewGuid(), ClasseAtivo.Acao, "acao-x", "Ação X ON", null);

    static Operacao Evento(Ativo ativo, DateOnly data, TipoOperacao tipo, decimal quantidade, decimal preco = 0m) =>
        new(Guid.NewGuid(), Renato.Id, ativo.Id, data, tipo, quantidade, preco, 0m, "BRL", Guid.NewGuid().ToString());

    [Fact]
    public void T54_EventosDeCapital_ExemploDeReferencia()
    {
        var desdobramento = Evento(AcaoX, new DateOnly(2025, 3, 14), TipoOperacao.Desdobramento, 900m);
        Operacao[] operacoes =
        [
            Aplicacao(AcaoX, new DateOnly(2025, 3, 10), 100m, 40.00m),
            desdobramento,
            Evento(AcaoX, new DateOnly(2025, 4, 1), TipoOperacao.Bonificacao, 100m, 2.00m),
            Evento(AcaoX, new DateOnly(2025, 4, 10), TipoOperacao.Grupamento, 990m),
            Resgate(AcaoX, new DateOnly(2025, 4, 15), 10m, 52.00m),
        ];
        PrecoDiario[] precos =
        [
            Preco(AcaoX, new DateOnly(2025, 3, 13), 50.00m),
            Preco(AcaoX, new DateOnly(2025, 3, 14), 5.00m),
            Preco(AcaoX, new DateOnly(2025, 3, 20), 5.20m),
            Preco(AcaoX, new DateOnly(2025, 4, 1), 5.00m),
            Preco(AcaoX, new DateOnly(2025, 4, 10), 50.00m),
            Preco(AcaoX, new DateOnly(2025, 4, 15), 52.00m),
        ];

        PosicaoSnapshot Em(DateOnly data, IReadOnlyList<Operacao>? ops = null) =>
            Posicao(PositionCalculator.Calculate(Titulares, [AcaoX], ops ?? operacoes, precos, data), AcaoX);

        var p13 = Em(new DateOnly(2025, 3, 13));
        Assert.Equal(100m, p13.Quantidade);
        Assert.Equal(4000.00m, Math.Round(p13.Custo, 2));
        Assert.Equal(40.0000m, Math.Round(p13.CustoMedio, 4));
        Assert.Equal(5000.00m, Math.Round(p13.ValorMercado!.Value, 2));
        Assert.Equal(1000.00m, Math.Round(p13.Rentabilidade!.Value, 2));
        Assert.Equal(0.2500m, Math.Round(p13.RentabilidadePercentual!.Value, 4));

        var p20 = Em(new DateOnly(2025, 3, 20));
        Assert.Equal(1000m, p20.Quantidade);
        Assert.Equal(4000.00m, Math.Round(p20.Custo, 2));
        Assert.Equal(4.0000m, Math.Round(p20.CustoMedio, 4));
        Assert.Equal(5200.00m, Math.Round(p20.ValorMercado!.Value, 2));
        Assert.Equal(1200.00m, Math.Round(p20.Rentabilidade!.Value, 2));
        Assert.Equal(0.3000m, Math.Round(p20.RentabilidadePercentual!.Value, 4));

        var p01 = Em(new DateOnly(2025, 4, 1));
        Assert.Equal(1100m, p01.Quantidade);
        Assert.Equal(4200.00m, Math.Round(p01.Custo, 2));
        Assert.Equal(3.8182m, Math.Round(p01.CustoMedio, 4));
        Assert.Equal(5500.00m, Math.Round(p01.ValorMercado!.Value, 2));
        Assert.Equal(1300.00m, Math.Round(p01.Rentabilidade!.Value, 2));
        Assert.Equal(0.3095m, Math.Round(p01.RentabilidadePercentual!.Value, 4));

        var p10 = Em(new DateOnly(2025, 4, 10));
        Assert.Equal(110m, p10.Quantidade);
        Assert.Equal(4200.00m, Math.Round(p10.Custo, 2));
        Assert.Equal(38.1818m, Math.Round(p10.CustoMedio, 4));
        Assert.Equal(5500.00m, Math.Round(p10.ValorMercado!.Value, 2));
        Assert.Equal(1300.00m, Math.Round(p10.Rentabilidade!.Value, 2));

        var p15 = Em(new DateOnly(2025, 4, 15));
        Assert.Equal(100m, p15.Quantidade);
        Assert.Equal(3818.18m, Math.Round(p15.Custo, 2));
        Assert.Equal(38.1818m, Math.Round(p15.CustoMedio, 4));
        Assert.Equal(5200.00m, Math.Round(p15.ValorMercado!.Value, 2));
        Assert.Equal(1381.82m, Math.Round(p15.Rentabilidade!.Value, 2));
        Assert.Equal(0.3619m, Math.Round(p15.RentabilidadePercentual!.Value, 4));
        Assert.Equal(138.18m, Math.Round(p15.ResultadoRealizado, 2));

        var semDesdobramento = Em(new DateOnly(2025, 3, 20), operacoes.Where(o => o != desdobramento).ToList());
        Assert.Equal(100m, semDesdobramento.Quantidade);
        Assert.Equal(520.00m, Math.Round(semDesdobramento.ValorMercado!.Value, 2));
        Assert.Equal(-3480.00m, Math.Round(semDesdobramento.Rentabilidade!.Value, 2));
    }

    [Fact]
    public void T56_EventoSemPosicao_GrupamentoMaiorQueAPosicao_DesdobramentoEResgateNoMesmoDia()
    {
        var dataEvento = new DateOnly(2025, 3, 14);
        var aplicacao = Aplicacao(AcaoX, new DateOnly(2025, 3, 10), 100m, 40.00m);

        var semPosicao = Assert.Throws<PosicaoInsuficienteException>(
            () => PositionCalculator.Calculate(
                Titulares, [AcaoX], [Evento(AcaoX, dataEvento, TipoOperacao.Desdobramento, 900m)], [], dataEvento));
        Assert.Contains("sem posição", semPosicao.Message);

        Assert.Throws<PosicaoInsuficienteException>(
            () => PositionCalculator.Calculate(
                Titulares, [AcaoX], [aplicacao, Evento(AcaoX, dataEvento, TipoOperacao.Grupamento, 200m)], [], dataEvento));

        var resgate = Resgate(AcaoX, dataEvento, 500m, 5.00m);
        var desdobramento = Evento(AcaoX, dataEvento, TipoOperacao.Desdobramento, 900m);

        var snapshot = PositionCalculator.Calculate(Titulares, [AcaoX], [aplicacao, resgate, desdobramento], [], dataEvento);

        var p = Posicao(snapshot, AcaoX);
        Assert.Equal(500m, p.Quantidade);
        Assert.Equal(2000.00m, Math.Round(p.Custo, 2));
        Assert.Equal(500.00m, Math.Round(p.ResultadoRealizado, 2));
    }
}
