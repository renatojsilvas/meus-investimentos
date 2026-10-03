namespace Carteira.Core;

public static class DailySeries
{
    public static IReadOnlyList<PontoSerieDiaria> Build(
        IReadOnlyList<Titular> titulares,
        IReadOnlyList<Ativo> ativos,
        IReadOnlyList<Operacao> operacoes,
        IReadOnlyList<PrecoDiario> precos,
        DateOnly de,
        DateOnly ate)
    {
        throw new NotImplementedException();
    }
}
