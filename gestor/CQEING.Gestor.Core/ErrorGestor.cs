namespace CQEING.Gestor.Core;

/// <summary>Errores que el usuario del gestor tiene que entender; el mensaje va tal cual a la ventana.</summary>
public enum TipoError
{
    SinConexion,
    LimiteGitHub,
    HashNoCoincide,
    SinPaqueteParaRevit,
    ManifiestoInvalido,
    PaqueteInvalido,
}

public sealed class GestorException(TipoError tipo, string mensaje, Exception? causa = null) : Exception(mensaje, causa)
{
    public TipoError Tipo { get; } = tipo;
}
