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

    public IReadOnlyList<string> ListarTiposBarra() =>
        new FilteredElementCollector(_doc)
            .OfClass(typeof(RebarBarType))
            .Cast<RebarBarType>()
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

    public IReadOnlyList<string> ListarTiposGancho() =>
        new FilteredElementCollector(_doc)
            .OfClass(typeof(RebarHookType))
            .Cast<RebarHookType>()
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();
}
