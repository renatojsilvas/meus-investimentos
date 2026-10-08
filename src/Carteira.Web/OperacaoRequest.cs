using System.Text.Json.Serialization;

namespace Carteira.Web;

public record OperacaoRequest(
    string Data,
    string Titular,
    string Codigo,
    string Titulo,
    string Vencimento,
    string Tipo,
    string Quantidade,
    [property: JsonPropertyName("preco_unitario")] string PrecoUnitario,
    string Taxas);
