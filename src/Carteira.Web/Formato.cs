using System.Globalization;

namespace Carteira.Web;

public static class Formato
{
    public static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");

    public static string Moeda(decimal valor) => valor.ToString("N2", Cultura);

    public static string Numero(decimal valor) => valor.ToString("N2", Cultura);

    public static string Percentual(decimal fracao) => (fracao * 100).ToString("N2", Cultura) + "%";
}
