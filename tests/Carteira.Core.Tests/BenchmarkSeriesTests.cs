using Xunit;

namespace Carteira.Core.Tests;

public class BenchmarkSeriesTests
{
    static readonly Titular Renato = new(Guid.NewGuid(), "Renato", "renato");
    static readonly Titular Maria = new(Guid.NewGuid(), "Maria", "maria");
    static readonly Titular[] Titulares = [Renato, Maria];

    static readonly Guid AtivoId = Guid.NewGuid();

    static Operacao Aplicacao(DateOnly data, decimal quantidade, decimal preco, decimal taxas = 0m, Titular? titular = null) =>
        new(Guid.NewGuid(), (titular ?? Renato).Id, AtivoId, data, TipoOperacao.Aplicacao, quantidade, preco, taxas, "BRL", Guid.NewGuid().ToString());

    static Operacao Resgate(DateOnly data, decimal quantidade, decimal preco, decimal taxas = 0m, Titular? titular = null) =>
        new(Guid.NewGuid(), (titular ?? Renato).Id, AtivoId, data, TipoOperacao.Resgate, quantidade, preco, taxas, "BRL", Guid.NewGuid().ToString());

    static IndiceDiario Cdi(DateOnly data, decimal valor) => new("CDI", data, valor);

    [Fact]
    public void T41_ExemploDeReferencia_CincoPontosComOsSaldosDaTabela()
    {
        Operacao[] operacoes =
        [
            Aplicacao(new DateOnly(2025, 1, 6), 1.0m, 1000.00m),
            Aplicacao(new DateOnly(2025, 1, 8), 0.5m, 1000.00m),
            Resgate(new DateOnly(2025, 1, 10), 0.3m, 1000.00m),
        ];
        IndiceDiario[] indices =
        [
            Cdi(new DateOnly(2025, 1, 6), 0.05m),
            Cdi(new DateOnly(2025, 1, 7), 0.05m),
            Cdi(new DateOnly(2025, 1, 8), 0.05m),
            Cdi(new DateOnly(2025, 1, 9), 0.05m),
            Cdi(new DateOnly(2025, 1, 10), 0.05m),
        ];

        var serie = BenchmarkSeries.Build(Titulares, operacoes, indices, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 10));

        Assert.Equal(
            [new DateOnly(2025, 1, 6), new DateOnly(2025, 1, 7), new DateOnly(2025, 1, 8), new DateOnly(2025, 1, 9), new DateOnly(2025, 1, 10)],
            serie.Select(p => p.Data));
        Assert.All(serie, p => Assert.Equal(Renato.Id, p.TitularId));
        Assert.All(serie, p => Assert.Equal("renato", p.Slug));

        Assert.Equal(1000.00m, Math.Round(serie[0].ValorCdi, 2));
        Assert.Equal(1000.50m, Math.Round(serie[1].ValorCdi, 2));
        Assert.Equal(1501.00m, Math.Round(serie[2].ValorCdi, 2));
        Assert.Equal(1501.75m, Math.Round(serie[3].ValorCdi, 2));
        Assert.Equal(1202.50m, Math.Round(serie[4].ValorCdi, 2));
    }

    [Fact]
    public void T42_FimDeSemanaSemIndice_SegundaComIndice_PontoSoNosDiasComIndiceOuOperacao()
    {
        Operacao[] operacoes =
        [
            Aplicacao(new DateOnly(2025, 1, 6), 1.0m, 1000.00m),
            Aplicacao(new DateOnly(2025, 1, 8), 0.5m, 1000.00m),
            Resgate(new DateOnly(2025, 1, 10), 0.3m, 1000.00m),
        ];
        IndiceDiario[] indices =
        [
            Cdi(new DateOnly(2025, 1, 6), 0.05m),
            Cdi(new DateOnly(2025, 1, 7), 0.05m),
            Cdi(new DateOnly(2025, 1, 8), 0.05m),
            Cdi(new DateOnly(2025, 1, 9), 0.05m),
            Cdi(new DateOnly(2025, 1, 10), 0.05m),
            Cdi(new DateOnly(2025, 1, 13), 0.05m),
        ];

        var serie = BenchmarkSeries.Build(Titulares, operacoes, indices, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 13));

        Assert.Equal(
            [
                new DateOnly(2025, 1, 6), new DateOnly(2025, 1, 7), new DateOnly(2025, 1, 8),
                new DateOnly(2025, 1, 9), new DateOnly(2025, 1, 10), new DateOnly(2025, 1, 13),
            ],
            serie.Select(p => p.Data));

        Assert.Equal(1203.10m, Math.Round(serie[5].ValorCdi, 2));
    }

    [Fact]
    public void T43_IndiceAntesDaPrimeiraOperacao_NenhumPontoAntes_SaldoComecaEmZero()
    {
        Operacao[] operacoes = [Aplicacao(new DateOnly(2025, 1, 8), 1.0m, 1000.00m)];
        IndiceDiario[] indices =
        [
            Cdi(new DateOnly(2025, 1, 6), 0.05m),
            Cdi(new DateOnly(2025, 1, 7), 0.05m),
            Cdi(new DateOnly(2025, 1, 8), 0.05m),
            Cdi(new DateOnly(2025, 1, 9), 0.05m),
            Cdi(new DateOnly(2025, 1, 10), 0.05m),
        ];

        var serie = BenchmarkSeries.Build(Titulares, operacoes, indices, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 10));

        Assert.Equal(
            [new DateOnly(2025, 1, 8), new DateOnly(2025, 1, 9), new DateOnly(2025, 1, 10)],
            serie.Select(p => p.Data));
        Assert.Equal(1000.00m, Math.Round(serie[0].ValorCdi, 2));
        Assert.Equal(1000.50m, Math.Round(serie[1].ValorCdi, 2));
        Assert.Equal(1001.00m, Math.Round(serie[2].ValorCdi, 2));
    }

    [Fact]
    public void T44_DoisTitularesComOperacoesEmDatasDiferentes_ContasIndependentes()
    {
        Operacao[] operacoes =
        [
            Aplicacao(new DateOnly(2025, 1, 6), 1.0m, 1000.00m, titular: Renato),
            Aplicacao(new DateOnly(2025, 1, 8), 1.0m, 1000.00m, titular: Maria),
        ];
        IndiceDiario[] indices =
        [
            Cdi(new DateOnly(2025, 1, 6), 0.05m),
            Cdi(new DateOnly(2025, 1, 7), 0.05m),
            Cdi(new DateOnly(2025, 1, 8), 0.05m),
            Cdi(new DateOnly(2025, 1, 9), 0.05m),
            Cdi(new DateOnly(2025, 1, 10), 0.05m),
        ];

        var serie = BenchmarkSeries.Build(Titulares, operacoes, indices, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 10));

        var renato = serie.Where(p => p.Slug == "renato").ToList();
        var maria = serie.Where(p => p.Slug == "maria").ToList();

        Assert.Equal(
            [new DateOnly(2025, 1, 6), new DateOnly(2025, 1, 7), new DateOnly(2025, 1, 8), new DateOnly(2025, 1, 9), new DateOnly(2025, 1, 10)],
            renato.Select(p => p.Data));
        Assert.Equal(
            [new DateOnly(2025, 1, 8), new DateOnly(2025, 1, 9), new DateOnly(2025, 1, 10)],
            maria.Select(p => p.Data));

        Assert.Equal(1000.00m, Math.Round(renato[0].ValorCdi, 2));
        Assert.Equal(1002.00m, Math.Round(renato[^1].ValorCdi, 2));

        Assert.Equal(1000.00m, Math.Round(maria[0].ValorCdi, 2));
        Assert.Equal(1001.00m, Math.Round(maria[^1].ValorCdi, 2));
    }

    [Fact]
    public void T45_ResgateMaiorQueOSaldo_SaldoNegativoSemExcecao()
    {
        Operacao[] operacoes =
        [
            Aplicacao(new DateOnly(2025, 1, 6), 0.1m, 1000.00m),
            Resgate(new DateOnly(2025, 1, 7), 1.0m, 1000.00m),
        ];
        IndiceDiario[] indices =
        [
            Cdi(new DateOnly(2025, 1, 6), 0.05m),
            Cdi(new DateOnly(2025, 1, 7), 0.05m),
        ];

        var serie = BenchmarkSeries.Build(Titulares, operacoes, indices, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 7));

        Assert.Equal(100.00m, Math.Round(serie[0].ValorCdi, 2));
        Assert.Equal(-899.95m, Math.Round(serie[1].ValorCdi, 2));
    }

    [Fact]
    public void T46_AplicacaoETaxasDeResgate_FluxosComTaxas()
    {
        Operacao[] operacoes =
        [
            Aplicacao(new DateOnly(2025, 1, 6), 1.0m, 1000.00m, taxas: 10.00m),
            Resgate(new DateOnly(2025, 1, 7), 0.3m, 1000.00m, taxas: 10.00m),
        ];
        IndiceDiario[] indices =
        [
            Cdi(new DateOnly(2025, 1, 6), 0.05m),
            Cdi(new DateOnly(2025, 1, 7), 0.05m),
        ];

        var serie = BenchmarkSeries.Build(Titulares, operacoes, indices, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 7));

        // deposito = 1.000,00 + 10,00 = 1.010,00
        Assert.Equal(1010.00m, serie[0].ValorCdi);
        // saque = 300,00 - 10,00 = 290,00; saldo = 1.010,00 * 1,0005 - 290,00 = 720,505
        Assert.Equal(720.505m, serie[1].ValorCdi);
    }

    [Fact]
    public void T47_OperacaoEmDiaSemIndice_PontoExisteComFatorUm_DiasSeguintesNormais()
    {
        Operacao[] operacoes =
        [
            Aplicacao(new DateOnly(2025, 1, 6), 1.0m, 1000.00m),
            Aplicacao(new DateOnly(2025, 1, 8), 0.5m, 1000.00m),
        ];
        IndiceDiario[] indices =
        [
            Cdi(new DateOnly(2025, 1, 6), 0.05m),
            Cdi(new DateOnly(2025, 1, 7), 0.05m),
            Cdi(new DateOnly(2025, 1, 9), 0.05m),
        ];

        var serie = BenchmarkSeries.Build(Titulares, operacoes, indices, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 9));

        Assert.Equal(
            [new DateOnly(2025, 1, 6), new DateOnly(2025, 1, 7), new DateOnly(2025, 1, 8), new DateOnly(2025, 1, 9)],
            serie.Select(p => p.Data));
        Assert.Equal(1000.00m, Math.Round(serie[0].ValorCdi, 2));
        Assert.Equal(1000.50m, Math.Round(serie[1].ValorCdi, 2));
        Assert.Equal(1500.50m, Math.Round(serie[2].ValorCdi, 2));
        Assert.Equal(1501.25m, Math.Round(serie[3].ValorCdi, 2));
    }

    [Fact]
    public void T48_DeMaiorQueAteOuSemOperacoes_ListaVazia()
    {
        IndiceDiario[] indices = [Cdi(new DateOnly(2025, 1, 6), 0.05m)];
        Operacao[] operacoes = [Aplicacao(new DateOnly(2025, 1, 6), 1.0m, 1000.00m)];

        var invertido = BenchmarkSeries.Build(Titulares, operacoes, indices, new DateOnly(2025, 1, 10), new DateOnly(2025, 1, 1));
        var semOperacoes = BenchmarkSeries.Build(Titulares, [], indices, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 10));

        Assert.Empty(invertido);
        Assert.Empty(semOperacoes);
    }
}
