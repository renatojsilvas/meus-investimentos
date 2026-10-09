using System.Globalization;
using System.Text;
using Carteira.Core;

namespace Carteira.Web;

public static class GraficoSvg
{
    private static readonly string[] CoresSeries = ["#1b9e77", "#d95f02", "#7570b3", "#e7298a"];
    private const string CorTotal = "#333333";
    private const string CorEixo = "#999999";
    private const string CorCdi = "#999999";

    public static string Gerar(List<SnapshotDiario> snapshots, List<Titular> titulares)
    {
        const double vbWidth = 1000;
        const double vbHeight = 340;

        if (snapshots.Count == 0)
        {
            return $"<svg width=\"100%\" height=\"340\" viewBox=\"0 0 {Fmt(vbWidth)} {Fmt(vbHeight)}\" xmlns=\"http://www.w3.org/2000/svg\">" +
                   $"<text x=\"20\" y=\"20\" font-size=\"12\" fill=\"{CorEixo}\">Sem dados suficientes para o gráfico ainda.</text></svg>";
        }

        const double plotLeft = 60;
        const double plotRight = vbWidth - 20;
        const double plotTop = 20;
        const double plotBottom = 190;
        const double xLabelY = plotBottom + 20;
        const double legendaY0 = 250;
        const double legendaPasso = 18;

        var nomesPorId = titulares.ToDictionary(t => t.Id, t => t.Nome);

        var datas = snapshots.Select(s => s.Data).Distinct().OrderBy(d => d).ToList();
        var indicePorData = datas.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);

        var porTitular = snapshots
            .GroupBy(s => s.TitularId)
            .Select(g => new
            {
                Nome = nomesPorId.GetValueOrDefault(g.Key, g.Key.ToString()),
                Pontos = g.OrderBy(s => s.Data).ToList()
            })
            .OrderBy(g => g.Nome)
            .ToList();

        var totalPorData = snapshots
            .GroupBy(s => s.Data)
            .OrderBy(g => g.Key)
            .Select(g => (Data: g.Key, Valor: g.Sum(s => s.Valor)))
            .ToList();

        var totalCdiPorData = snapshots
            .GroupBy(s => s.Data)
            .OrderBy(g => g.Key)
            .Where(g => g.All(s => s.ValorCdi is not null))
            .Select(g => (Data: g.Key, Valor: g.Sum(s => s.ValorCdi!.Value)))
            .ToList();

        var minData = datas[0];
        var maxData = datas[^1];
        var maiorTotal = totalPorData.Count == 0 ? 0m : totalPorData.Max(t => t.Valor);
        var maiorTotalCdi = totalCdiPorData.Count == 0 ? 0m : totalCdiPorData.Max(t => t.Valor);
        var maiorValor = Math.Max(maiorTotal, maiorTotalCdi);
        var escalaValor = maiorValor <= 0 ? 1m : maiorValor;

        double X(DateOnly d)
        {
            if (maxData == minData)
                return plotLeft;
            var total = maxData.DayNumber - minData.DayNumber;
            var pos = d.DayNumber - minData.DayNumber;
            return plotLeft + (plotRight - plotLeft) * pos / total;
        }

        double Y(decimal valor)
        {
            var fracao = (double)(valor / escalaValor);
            return plotBottom - fracao * (plotBottom - plotTop);
        }

        var sb = new StringBuilder();
        sb.Append($"<svg width=\"100%\" height=\"340\" viewBox=\"0 0 {Fmt(vbWidth)} {Fmt(vbHeight)}\" xmlns=\"http://www.w3.org/2000/svg\">");

        sb.Append($"<line x1=\"{Fmt(plotLeft)}\" y1=\"{Fmt(plotTop)}\" x2=\"{Fmt(plotLeft)}\" y2=\"{Fmt(plotBottom)}\" stroke=\"{CorEixo}\" />");
        sb.Append($"<line x1=\"{Fmt(plotLeft)}\" y1=\"{Fmt(plotBottom)}\" x2=\"{Fmt(plotRight)}\" y2=\"{Fmt(plotBottom)}\" stroke=\"{CorEixo}\" />");

        for (var i = 0; i <= 4; i++)
        {
            var valor = maiorValor * i / 4;
            var y = Y(valor);
            sb.Append($"<text x=\"{Fmt(plotLeft - 8)}\" y=\"{Fmt(y + 4)}\" text-anchor=\"end\" font-size=\"11\" fill=\"{CorTotal}\">{valor.ToString("N0", Formato.Cultura)}</text>");
        }

        var qtdRotulosX = Math.Min(5, datas.Count);
        for (var i = 0; i < qtdRotulosX; i++)
        {
            var idx = qtdRotulosX == 1 ? 0 : i * (datas.Count - 1) / (qtdRotulosX - 1);
            var data = datas[idx];
            var x = X(data);
            sb.Append($"<text x=\"{Fmt(x)}\" y=\"{Fmt(xLabelY)}\" text-anchor=\"middle\" font-size=\"11\" fill=\"{CorTotal}\">{data:dd/MM/yy}</text>");
        }

        var pontosTotal = string.Join(" ", totalPorData.Select(t => $"{Fmt(X(t.Data))},{Fmt(Y(t.Valor))}"));
        sb.Append($"<polyline points=\"{pontosTotal}\" fill=\"none\" stroke=\"{CorTotal}\" stroke-width=\"2\" />");

        if (totalCdiPorData.Count >= 2)
        {
            var pontosCdi = string.Join(" ", totalCdiPorData.Select(t => $"{Fmt(X(t.Data))},{Fmt(Y(t.Valor))}"));
            sb.Append($"<polyline points=\"{pontosCdi}\" fill=\"none\" stroke=\"{CorCdi}\" stroke-width=\"2\" stroke-dasharray=\"6,4\" />");
        }

        for (var i = 0; i < porTitular.Count; i++)
        {
            var serie = porTitular[i];
            var cor = CoresSeries[i % CoresSeries.Length];

            var segmento = new List<SnapshotDiario>();
            int? indiceAnterior = null;

            foreach (var ponto in serie.Pontos)
            {
                var indiceAtual = indicePorData[ponto.Data];
                if (indiceAnterior is not null && indiceAtual != indiceAnterior + 1)
                {
                    EmitirSegmento(sb, segmento, cor, X, Y);
                    segmento = [];
                }

                segmento.Add(ponto);
                indiceAnterior = indiceAtual;
            }

            EmitirSegmento(sb, segmento, cor, X, Y);

            var y = legendaY0 + i * legendaPasso;
            sb.Append($"<line x1=\"{Fmt(plotLeft)}\" y1=\"{Fmt(y)}\" x2=\"{Fmt(plotLeft + 20)}\" y2=\"{Fmt(y)}\" stroke=\"{cor}\" stroke-width=\"2\" />");
            sb.Append($"<text x=\"{Fmt(plotLeft + 28)}\" y=\"{Fmt(y + 4)}\" font-size=\"12\" fill=\"{CorTotal}\">{System.Net.WebUtility.HtmlEncode(serie.Nome)}</text>");
        }

        var yTotalLegenda = legendaY0 + porTitular.Count * legendaPasso;
        sb.Append($"<line x1=\"{Fmt(plotLeft)}\" y1=\"{Fmt(yTotalLegenda)}\" x2=\"{Fmt(plotLeft + 20)}\" y2=\"{Fmt(yTotalLegenda)}\" stroke=\"{CorTotal}\" stroke-width=\"2\" />");
        sb.Append($"<text x=\"{Fmt(plotLeft + 28)}\" y=\"{Fmt(yTotalLegenda + 4)}\" font-size=\"12\" fill=\"{CorTotal}\">Total</text>");

        var yCdiLegenda = yTotalLegenda + legendaPasso;
        sb.Append($"<line x1=\"{Fmt(plotLeft)}\" y1=\"{Fmt(yCdiLegenda)}\" x2=\"{Fmt(plotLeft + 20)}\" y2=\"{Fmt(yCdiLegenda)}\" stroke=\"{CorCdi}\" stroke-width=\"2\" stroke-dasharray=\"6,4\" />");
        sb.Append($"<text x=\"{Fmt(plotLeft + 28)}\" y=\"{Fmt(yCdiLegenda + 4)}\" font-size=\"12\" fill=\"{CorTotal}\">CDI</text>");

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void EmitirSegmento(
        StringBuilder sb,
        List<SnapshotDiario> segmento,
        string cor,
        Func<DateOnly, double> x,
        Func<decimal, double> y)
    {
        if (segmento.Count < 2)
            return;

        var pontos = string.Join(" ", segmento.Select(p => $"{Fmt(x(p.Data))},{Fmt(y(p.Valor))}"));
        sb.Append($"<polyline points=\"{pontos}\" fill=\"none\" stroke=\"{cor}\" stroke-width=\"2\" />");
    }

    private static string Fmt(double valor) => valor.ToString("0.##", CultureInfo.InvariantCulture);
}
