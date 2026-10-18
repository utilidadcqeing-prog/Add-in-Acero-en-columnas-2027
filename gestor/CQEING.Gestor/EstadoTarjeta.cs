using CQEING.Gestor.Core;

namespace CQEING.Gestor;

public enum AccionTarjeta
{
    Instalar,
    Actualizar,
    AlDia,
    NoDisponible,
}

/// <summary>
/// Qué muestra la tarjeta de un add-in en un año de Revit y qué hace su botón. Es una cuenta pura sobre
/// <see cref="EstadoAddin"/>: no toca la red ni el disco, para poder probarla sin abrir la ventana.
/// </summary>
/// <param name="PuedeActuar">El botón principal está habilitado (sin contar si hay otra operación en curso).</param>
/// <param name="NuevaVersion">Etiqueta «Nueva versión X» cuando hay actualización.</param>
/// <param name="Nota">Una línea debajo: notas de la versión nueva o por qué no se puede actuar.</param>
public sealed record EstadoTarjeta(
    AccionTarjeta Accion,
    string TextoBoton,
    bool PuedeActuar,
    string TextoInstalada,
    string? NuevaVersion,
    string? Nota)
{
    public static EstadoTarjeta Calcular(EstadoAddin estado, string anio, bool sinConexion)
    {
        VersionPublicada ultima = estado.Addin.Ultima;
        string? instalada = estado.Instalada(anio);
        string textoInstalada = instalada is null ? "No instalada" : $"Instalada {instalada}";

        if (ultima.Manifiesto.PaqueteParaRevit(anio) is null)
        {
            return instalada is null
                ? new(AccionTarjeta.NoDisponible, "No disponible", false, textoInstalada, null, $"Todavía no está disponible para Revit {anio}.")
                : new(AccionTarjeta.AlDia, "✓ Al día", false, textoInstalada, null, $"La versión {ultima.Version} todavía no está para Revit {anio}.");
        }

        AccionTarjeta accion = instalada is null ? AccionTarjeta.Instalar
            : estado.HayActualizacion(anio) ? AccionTarjeta.Actualizar
            : AccionTarjeta.AlDia;
        if (accion == AccionTarjeta.AlDia)
            return new(accion, "✓ Al día", false, textoInstalada, null, null);

        bool faltaRed = sinConexion && !estado.EstaEnDisco(anio, ultima.Version.ToString());
        string? nota = faltaRed ? "Sin conexión: se podrá descargar cuando vuelva la red."
            : accion == AccionTarjeta.Actualizar ? PrimeraLinea(ultima.Manifiesto.Notas)
            : null;
        return new(
            accion,
            accion == AccionTarjeta.Instalar ? "Instalar" : "Actualizar",
            !faltaRed,
            textoInstalada,
            accion == AccionTarjeta.Actualizar ? $"Nueva versión {ultima.Version}" : null,
            nota);
    }

    private static string? PrimeraLinea(string notas) =>
        notas.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}
