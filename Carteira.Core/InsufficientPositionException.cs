namespace Carteira.Core;

public class InsufficientPositionException(string message) : Exception(message);
