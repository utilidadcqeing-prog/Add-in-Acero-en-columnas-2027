namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

/// <summary>
/// #176 (docs/planes/2026-10-27-medidas-ganchos-detalle-176.md): que los
/// detalles de plegado de los estribos acoten también los ganchos y se lean
/// bien, sin que nadie tenga que configurar el tipo a mano.
/// </summary>
public static class DetallesPlegado
{
    public const string NombreTipoPropio = "CQ Detalle de plegado";

    /// <summary>
    /// Si se acotan o no los ganchos es una casilla del tipo de detalle de
    /// plegado (no del estribo), así que se enciende en cada tipo del modelo
    /// que ya acota los lados; uno que no acota nada se deja como está, y el
    /// propio también (ver <see cref="AsegurarTipoPropio"/>). Devuelve un
    /// aviso si algún tipo no se pudo cambiar; null si todo bien.
    /// </summary>
    public static string? AcotarGanchos(Document doc)
    {
        string? aviso = null;
        foreach (RebarBendingDetailType tipo in new FilteredElementCollector(doc)
                     .OfClass(typeof(RebarBendingDetailType)).Cast<RebarBendingDetailType>())
        {
            if (tipo.Name == NombreTipoPropio) continue;
            try
            {
                if (tipo.SegmentLengthDimensionsEnabled && !tipo.SegmentLengthDimensionsForHooksEnabled)
                    tipo.SegmentLengthDimensionsForHooksEnabled = true;
            }
            catch (Exception ex)
            {
                aviso = $"no se pudo activar la medida de los ganchos en el detalle de plegado «{tipo.Name}»: {ex.Message}";
            }
        }
        return aviso;
    }

    /// <summary>
    /// Borra las formas «CQ Recta …» que dejó un intento anterior del #176
    /// (28-oct) y que ya no usa ninguna barra. Las longitudinales se crean
    /// pidiéndole a Revit una forma existente que encaje, y Revit las elegía
    /// solo: con ellas perdía los empalmes al partir ("The rebar splice
    /// connection cannot be maintained", reporte de las 15:49, con piezas
    /// «CQ Recta 0-90» aunque el add-in ya no las asignaba). Una que todavía
    /// use alguna barra no se borra: devuelve el aviso.
    /// </summary>
    public static string? BorrarFormasRectas(Document doc)
    {
        var formas = new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).Cast<RebarShape>()
            .Where(f => f.Name.StartsWith("CQ Recta ", StringComparison.Ordinal)).ToList();
        if (formas.Count == 0) return null;

        var usadas = new HashSet<ElementId>(new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
            .Select(r => r.GetShapeId()));
        var enUso = new List<string>();
        foreach (RebarShape forma in formas)
        {
            if (usadas.Contains(forma.Id))
            {
                enUso.Add(forma.Name);
                continue;
            }
            doc.Delete(forma.Id);
        }
        return enUso.Count == 0 ? null
            : $"las formas {string.Join(", ", enUso.Select(n => $"«{n}»"))} las usan barras de otras columnas; " +
              "pueden hacer fallar los traslapos al generar. Regenere esas columnas o cambie la forma de esas barras a «00».";
    }

    /// <summary>
    /// El tipo de detalle de plegado propio, esquemático: dibuja la barra en
    /// un recuadro de tamaño fijo en el papel, así las medidas quedan
    /// proporcionadas en cualquier escala (28-oct: en una planta 1:100 los
    /// textos de 2,5 mm de los tipos realistas medían 25 cm, más que el
    /// estribo). Se configura y se deja como predeterminado SOLO al crearlo:
    /// si después alguien lo edita o elige otro tipo por defecto, se respeta.
    /// Devuelve un aviso si no se pudo, o null.
    /// </summary>
    public static string? AsegurarTipoPropio(Document doc)
    {
        var tipos = new FilteredElementCollector(doc).OfClass(typeof(RebarBendingDetailType))
            .Cast<RebarBendingDetailType>().ToList();
        if (tipos.Any(t => t.Name == NombreTipoPropio)) return null;

        try
        {
            RebarBendingDetailType propio = RebarBendingDetailType.CreateSchematic(doc);
            propio.Name = NombreTipoPropio;
            propio.SegmentLengthDimensionsEnabled = true;
            propio.SegmentLengthDimensionsForHooksEnabled = true;
            propio.SegmentLengthsDisplayOption = BendingDetailSegmentLengthsDisplayOptions.Text;

            // La etiqueta de número y diámetro («7 #3»), la misma que usen los
            // tipos del modelo, si alguno la tiene.
            ElementId etiqueta = tipos.Select(t => t.TagTypeId).FirstOrDefault(id => id != ElementId.InvalidElementId)
                                 ?? ElementId.InvalidElementId;
            if (etiqueta != ElementId.InvalidElementId) propio.TagTypeId = etiqueta;

            foreach (ElementTypeGroup grupo in new[] { ElementTypeGroup.SchematicBendingDetail, ElementTypeGroup.BendingDetail })
                if (doc.IsDefaultElementTypeIdValid(grupo, propio.Id)) doc.SetDefaultElementTypeId(grupo, propio.Id);
            return null;
        }
        catch (Exception ex)
        {
            return $"no se pudo crear el tipo de detalle de plegado «{NombreTipoPropio}»: {ex.Message}";
        }
    }
}
