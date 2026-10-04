using Xunit;

namespace Carteira.Core.Tests;

public class DailySeriesTests
{
    static readonly Ativo Selic = new(
        Guid.NewGuid(), ClasseAtivo.TesouroDireto, "tesouro-selic-2029-03-01", "Tesouro Selic 2029", new DateOnly(2029, 3, 1));

    static readonly Ativo Ipca = new(
        Guid.NewGuid(), ClasseAtivo.TesouroDireto, "tesouro-ipca-mais-2035-05-15", "Tesouro IPCA+ 2035", new DateOnly(2035, 5, 15));

    static readonly Ativo[] Ativos = [Selic, Ipca];

    static readonly Titular Renato = new(Guid.NewGuid(), "Renato", "renato");
    static readonly Titular Maria = new(Guid.NewGuid(), "Maria", "maria");
    static readonly Titular[] Titulares = [Renato, Maria];

    static Operacao Aplicacao(Ativo ativo, DateOnly data, decimal quantidade, decimal preco, Titular? titular = null) =>
        new(Guid.NewGuid(), (titular ?? Renato).Id, ativo.Id, data, TipoOperacao.Aplicacao, quantidade, preco, 0m, "BRL", Guid.NewGuid().ToString());

    static Operacao Resgate(Ativo ativo, DateOnly data, decimal quantidade, decimal preco, Titular? titular = null) =>
        new(Guid.NewGuid(), (titular ?? Renato).Id, ativo.Id, data, TipoOperacao.Resgate, quantidade, preco, 0m, "BRL", Guid.NewGuid().ToString());

    static PrecoDiario Preco(Ativo ativo, DateOnly data, decimal preco) => new(ativo.Id, data, preco);

    static readonly Operacao[] Referencia =
    [
        Aplicacao(Selic, new DateOnly(2025, 1, 10), 2.5m, 14000.00m),
        Aplicacao(Selic, new DateOnly(2025, 3, 15), 1.0m, 14300.00m),
        Resgate(Selic, new DateOnly(2025, 6, 20), 1.5m, 14600.00m),
    ];

    static readonly PrecoDiario[] PrecosReferencia =
    [
        Preco(Selic, new DateOnly(2025, 1, 10), 14000.00m),
        Preco(Selic, new DateOnly(2025, 3, 15), 14250.00m),
        Preco(Selic, new DateOnly(2025, 6, 20), 14600.00m),
    ];

    static readonly Operacao IpcaAplicacao = Aplicacao(Ipca, new DateOnly(2025, 2, 5), 3.25m, 3210.50m);

    static decimal Percentual(PontoSerieDiaria p) =>
        p.CustoComPreco == 0m ? 0m : Math.Round(p.Rentabilidade / p.CustoComPreco * 100, 2);

    [Fact]
    public void T33_ExemploDeReferencia_TresPontosComOsValoresDaTabela()
    {
        var serie = DailySeries.Build(Titulares, Ativos, Referencia, PrecosReferencia, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));

        Assert.Equal(3, serie.Count);

        var p1 = serie[0];
        Assert.Equal(new DateOnly(2025, 1, 10), p1.Data);
        Assert.Equal(Renato.Id, p1.TitularId);
        Assert.Equal("renato", p1.Slug);
        Assert.Equal("Renato", p1.NomeTitular);
        Assert.Equal(35000.00m, Math.Round(p1.Custo, 2));
        Assert.Equal(35000.00m, Math.Round(p1.CustoComPreco, 2));
        Assert.Equal(35000.00m, Math.Round(p1.Valor, 2));
        Assert.Equal(0.00m, Math.Round(p1.Rentabilidade, 2));
        Assert.Equal(0.00m, Percentual(p1));
        Assert.Equal(0.00m, Math.Round(p1.ResultadoRealizado, 2));
        Assert.False(p1.TemPosicaoSemPreco);

        var p2 = serie[1];
        Assert.Equal(new DateOnly(2025, 3, 15), p2.Data);
        Assert.Equal(49300.00m, Math.Round(p2.Custo, 2));
        Assert.Equal(49300.00m, Math.Round(p2.CustoComPreco, 2));
        Assert.Equal(49875.00m, Math.Round(p2.Valor, 2));
        Assert.Equal(575.00m, Math.Round(p2.Rentabilidade, 2));
        Assert.Equal(1.17m, Percentual(p2));
        Assert.Equal(0.00m, Math.Round(p2.ResultadoRealizado, 2));
        Assert.False(p2.TemPosicaoSemPreco);

        var p3 = serie[2];
        Assert.Equal(new DateOnly(2025, 6, 20), p3.Data);
        Assert.Equal(28171.43m, Math.Round(p3.Custo, 2));
        Assert.Equal(28171.43m, Math.Round(p3.CustoComPreco, 2));
        Assert.Equal(29200.00m, Math.Round(p3.Valor, 2));
        Assert.Equal(1028.57m, Math.Round(p3.Rentabilidade, 2));
        Assert.Equal(3.65m, Percentual(p3));
        Assert.Equal(771.43m, Math.Round(p3.ResultadoRealizado, 2));
        Assert.False(p3.TemPosicaoSemPreco);
    }

    [Fact]
    public void T34_DiaSemPrecoDeNenhumAtivo_NaoGeraPonto()
    {
        Operacao[] operacoes = [Aplicacao(Selic, new DateOnly(2025, 1, 10), 2.5m, 14000.00m)];
        PrecoDiario[] precos =
        [
            Preco(Selic, new DateOnly(2025, 1, 10), 14000.00m),
            Preco(Selic, new DateOnly(2025, 1, 14), 14020.00m),
        ];

        var serie = DailySeries.Build(Titulares, Ativos, operacoes, precos, new DateOnly(2025, 1, 10), new DateOnly(2025, 1, 14));

        Assert.Equal([new DateOnly(2025, 1, 10), new DateOnly(2025, 1, 14)], serie.Select(p => p.Data));
    }

    [Fact]
    public void T35_PrecoAntesDaPrimeiraOperacaoDoTitular_NaoGeraPonto()
    {
        Operacao[] operacoes = [Aplicacao(Selic, new DateOnly(2025, 1, 10), 2.5m, 14000.00m)];
        PrecoDiario[] precos =
        [
            Preco(Selic, new DateOnly(2025, 1, 9), 13990.00m),
            Preco(Selic, new DateOnly(2025, 1, 10), 14000.00m),
        ];

        var serie = DailySeries.Build(Titulares, Ativos, operacoes, precos, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));

        var ponto = Assert.Single(serie);
        Assert.Equal(new DateOnly(2025, 1, 10), ponto.Data);
        Assert.Equal(Renato.Id, ponto.TitularId);
    }

    [Fact]
    public void T36_DoisTitularesComInicioDiferente_CadaSerieComecaNaSuaData()
    {
        Operacao[] operacoes =
        [
            Aplicacao(Selic, new DateOnly(2025, 1, 10), 2.5m, 14000.00m, Renato),
            Aplicacao(Selic, new DateOnly(2025, 1, 15), 1.0m, 14050.00m, Maria),
        ];
        PrecoDiario[] precos =
        [
            Preco(Selic, new DateOnly(2025, 1, 10), 14000.00m),
            Preco(Selic, new DateOnly(2025, 1, 15), 14050.00m),
            Preco(Selic, new DateOnly(2025, 1, 20), 14100.00m),
        ];

        var serie = DailySeries.Build(Titulares, Ativos, operacoes, precos, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31));

        Assert.Equal(
            [
                (new DateOnly(2025, 1, 10), "renato"),
                (new DateOnly(2025, 1, 15), "maria"),
                (new DateOnly(2025, 1, 15), "renato"),
                (new DateOnly(2025, 1, 20), "maria"),
                (new DateOnly(2025, 1, 20), "renato"),
            ],
            serie.Select(p => (p.Data, p.Slug)));

        Assert.Equal(35000.00m, Math.Round(serie[0].Custo, 2));
        Assert.Equal(35000.00m, Math.Round(serie[0].Valor, 2));

        Assert.Equal(14050.00m, Math.Round(serie[1].Custo, 2));
        Assert.Equal(14050.00m, Math.Round(serie[1].Valor, 2));
        Assert.Equal(35000.00m, Math.Round(serie[2].Custo, 2));
        Assert.Equal(35125.00m, Math.Round(serie[2].Valor, 2));

        Assert.Equal(14050.00m, Math.Round(serie[3].Custo, 2));
        Assert.Equal(14100.00m, Math.Round(serie[3].Valor, 2));
        Assert.Equal(50.00m, Math.Round(serie[3].Rentabilidade, 2));
        Assert.Equal(35000.00m, Math.Round(serie[4].Custo, 2));
        Assert.Equal(35250.00m, Math.Round(serie[4].Valor, 2));
        Assert.Equal(250.00m, Math.Round(serie[4].Rentabilidade, 2));
    }

    [Fact]
    public void T37_UmAtivoComPrecoOutroSemNenhumPreco_CustoIncluiAmbos_ValorSoOComPreco()
    {
        Operacao[] operacoes = [Aplicacao(Selic, new DateOnly(2025, 1, 10), 2.5m, 14000.00m), IpcaAplicacao];
        PrecoDiario[] precos = [Preco(Selic, new DateOnly(2025, 2, 10), 14100.00m)];

        var serie = DailySeries.Build(Titulares, Ativos, operacoes, precos, new DateOnly(2025, 2, 10), new DateOnly(2025, 2, 10));

        var ponto = Assert.Single(serie);
        Assert.Equal(new DateOnly(2025, 2, 10), ponto.Data);
        Assert.Equal(45434.125m, ponto.Custo);
        Assert.Equal(35000.00m, Math.Round(ponto.CustoComPreco, 2));
        Assert.Equal(35250.00m, Math.Round(ponto.Valor, 2));
        Assert.Equal(250.00m, Math.Round(ponto.Rentabilidade, 2));
        Assert.True(ponto.TemPosicaoSemPreco);
    }

    [Fact]
    public void T38_AtivoSemPrecoNoDiaComPrecoAnterior_UsaOAnterior()
    {
        Operacao[] operacoes = [Aplicacao(Selic, new DateOnly(2025, 1, 10), 2.5m, 14000.00m), IpcaAplicacao];
        PrecoDiario[] precos =
        [
            Preco(Ipca, new DateOnly(2025, 2, 7), 3220.00m),
            Preco(Selic, new DateOnly(2025, 2, 10), 14100.00m),
        ];

        var serie = DailySeries.Build(Titulares, Ativos, operacoes, precos, new DateOnly(2025, 2, 10), new DateOnly(2025, 2, 10));

        var ponto = Assert.Single(serie);
        Assert.Equal(new DateOnly(2025, 2, 10), ponto.Data);
        Assert.Equal(45434.125m, ponto.Custo);
        Assert.Equal(45434.125m, ponto.CustoComPreco);
        Assert.Equal(45715.00m, Math.Round(ponto.Valor, 2));
        Assert.Equal(280.875m, ponto.Rentabilidade);
        Assert.False(ponto.TemPosicaoSemPreco);
    }

    [Fact]
    public void T39_ResgateTotalNoMeioDoIntervalo_SerieParaNoDiaDoResgate()
    {
        Operacao[] operacoes = [.. Referencia, Resgate(Selic, new DateOnly(2025, 9, 10), 2.0m, 15000.00m)];
        PrecoDiario[] precos =
        [
            .. PrecosReferencia,
            Preco(Selic, new DateOnly(2025, 9, 10), 15000.00m),
            Preco(Selic, new DateOnly(2025, 9, 11), 15010.00m),
        ];

        var serie = DailySeries.Build(Titulares, Ativos, operacoes, precos, new DateOnly(2025, 1, 1), new DateOnly(2025, 9, 30));

        Assert.Equal(
            [new DateOnly(2025, 1, 10), new DateOnly(2025, 3, 15), new DateOnly(2025, 6, 20), new DateOnly(2025, 9, 10)],
            serie.Select(p => p.Data));

        var ultimo = serie[^1];
        Assert.Equal(0m, ultimo.Custo);
        Assert.Equal(0m, ultimo.CustoComPreco);
        Assert.Equal(0m, ultimo.Valor);
        Assert.Equal(2600.00m, Math.Round(ultimo.ResultadoRealizado, 2));
    }

    [Fact]
    public void T40_DeMaiorQueAteOuSemOperacoes_ListaVazia()
    {
        var invertido = DailySeries.Build(Titulares, Ativos, Referencia, PrecosReferencia, new DateOnly(2025, 6, 30), new DateOnly(2025, 1, 1));
        var semOperacoes = DailySeries.Build(Titulares, Ativos, [], PrecosReferencia, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30));

        Assert.Empty(invertido);
        Assert.Empty(semOperacoes);
    }
}
