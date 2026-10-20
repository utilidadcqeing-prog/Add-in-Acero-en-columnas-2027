namespace practicacq.Logica;

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// J2: implementación real de <see cref="IProveedorTipos"/>. Lee los tipos de
/// barra y de gancho que de verdad existen en el documento.
///
/// Orden alfabético porque el documento puede traer decenas de tipos (barras
/// de viga, de losa, de muro, no solo de columna) y sin orden es una lista
/// para perderse.
/// </summary>
public sealed class ProveedorTiposReal : IProveedorTipos
{
    private readonly Document _doc;

    public ProveedorTiposReal(Document doc) => _doc = doc;

    /// <summary>
    /// El diámetro viaja junto al nombre porque el valor por defecto de los
    /// combos se elige por diámetro contra el cuadro de la empresa -- por
    /// nombre no se puede, cada modelo los escribe distinto. Ver
    /// <see cref="TipoBarra.PorDefecto"/>.
    /// </summary>
    /// <remarks>
    /// Las propias del add-in ("#2 CQEING" … "#8 CQEING") van primero y por
    /// número -- alfabético pondría la #2 al lado de la #20 de otra plantilla.
    /// </remarks>
    public IReadOnlyList<TipoBarra> ListarBarras() =>
        new FilteredElementCollector(_doc)
            .OfClass(typeof(RebarBarType))
            .Cast<RebarBarType>()
            .Select(EstandarCQ.ComoTipoBarra)
            .OrderBy(b => CuadroVarillas.PorNombrePropio(b.Nombre)?.Numero ?? int.MaxValue)
            .ThenBy(b => b.Nombre)
            .ToList();

    /// <summary>
    /// Acá se traduce el estilo de Revit al <see cref="EstiloGancho"/> de
    /// <c>Modelo/</c>, que es lo único que ve la ventana. Un gancho de otro
    /// estilo (no hay más en la práctica, pero la API no lo garantiza) se
    /// descarta en vez de ofrecerse: aplicarlo terminaría en un
    /// <c>CreateFromCurves</c> devolviendo null al generar.
    /// </summary>
    public IReadOnlyList<TipoGancho> ListarGanchos() =>
        new FilteredElementCollector(_doc)
            .OfClass(typeof(RebarHookType))
            .Cast<RebarHookType>()
            .Where(t => t.Style is RebarStyle.StirrupTie or RebarStyle.Standard)
            .Select(t => new TipoGancho(
                t.Name,
                t.Style == RebarStyle.StirrupTie ? EstiloGancho.Estribo : EstiloGancho.Estandar,
                t.HookAngle * 180 / System.Math.PI))
            .OrderBy(g => g.Nombre)
            .ToList();
}
