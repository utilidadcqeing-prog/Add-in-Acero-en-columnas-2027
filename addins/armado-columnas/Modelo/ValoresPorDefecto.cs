namespace practicacq.Modelo;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Con qué arranca una columna que nadie configuró. Salió de
/// <c>VentanaConfiguradorEstribos</c> para que la interfaz web (#205) arranque
/// igual que la ventana vieja.
/// </summary>
public static class ValoresPorDefecto
{
    /// <summary>Barra por defecto del estribo: #3 (William, 11-sep).</summary>
    public const int NumeroVarillaEstribo = 3;

    /// <summary>Barra por defecto de la longitudinal, esquinas y caras: #4 ("todo número cuatro por defecto").</summary>
    public const int NumeroVarillaLongitudinal = 4;

    /// <summary>Las barras aptas (o aptas con aviso) para estribo según el cuadro (#64).</summary>
    public static IReadOnlyList<TipoBarra> BarrasAptas(IEnumerable<TipoBarra> barras) => barras
        .Where(b => CuadroVarillas.EvaluarParaEstribo(b).Aptitud != AptitudEstribo.NoApta)
        .ToList();

    /// <summary>
    /// El tipo de barra de esa varilla, o el primero de la lista si el
    /// documento no la tiene. Por DIÁMETRO contra el cuadro de la empresa
    /// (#60), no por nombre: los nombres cambian de modelo a modelo.
    /// </summary>
    public static string? BarraPorDefecto(IProveedorTipos tipos, int numeroVarilla)
    {
        IReadOnlyList<TipoBarra> barras = numeroVarilla == NumeroVarillaEstribo
            ? BarrasAptas(tipos.ListarBarras())
            : tipos.ListarBarras();

        return TipoBarra.PorDefecto(barras, numeroVarilla)?.Nombre ?? barras.FirstOrDefault()?.Nombre;
    }

    public static ConfigTramo Tramo(IProveedorTipos tipos) => new()
    {
        TipoBarraNombre = BarraPorDefecto(tipos, NumeroVarillaEstribo),
        // Estribo/tirante de 135°, confirmado por William el 10-sep.
        TipoGanchoNombre = TipoGancho.PorDefecto(tipos.ListarGanchos(), EstiloGancho.Estribo, 135)?.Nombre,
        EspaciadoUniforme = 0.20,
        LongitudConfinamiento = 0.50,
        EspaciadoConfinado = 0.07,
        EspaciadoNoConfinado = 0.14,
    };

    public static ConfigLongitudinal Longitudinal(IProveedorTipos tipos) => new()
    {
        BarrasPorCaraX = 2,
        BarrasPorCaraY = 2,
        TipoBarraCaraXNombre = BarraPorDefecto(tipos, NumeroVarillaLongitudinal),
        TipoBarraCaraYNombre = BarraPorDefecto(tipos, NumeroVarillaLongitudinal),
        TipoBarraEsquinasNombre = BarraPorDefecto(tipos, NumeroVarillaLongitudinal),
        LongitudComercialBarra = 12.0,
        // Anclaje en la zapata: estándar de 90°, también de la reunión del 10-sep.
        TipoGanchoLongitudinalNombre = TipoGancho.PorDefecto(tipos.ListarGanchos(), EstiloGancho.Estandar, 90)?.Nombre,
    };
}
