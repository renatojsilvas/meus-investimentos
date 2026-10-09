namespace Carteira.Web;

public record ResultadoBackfillPrecos(int Ativos, int PrecosGravados, int Dias);

public record ResultadoBackfillIndices(string Indice, int Registros, DateOnly De, DateOnly Ate);
