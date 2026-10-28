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
    /// Les pone la forma recta propia (<see cref="TiposPropios.FormaRecta"/>)
    /// a las piezas longitudinales con gancho, para que el detalle de plegado
    /// mida el gancho (la «00» de la plantilla no ata ninguna cota a él). Va
    /// sobre las piezas ya partidas y acomodadas: crearlas con la forma no
    /// sirve, porque al partir en los traslapos Revit les vuelve a elegir la
    /// «00» (medido el 28-oct, pieza 815298). <c>SetRebarShapeId</c> conserva
    /// la geometría. Una pieza que no la acepte queda como estaba. Devuelve un
    /// aviso si alguna no se pudo, o null.
    /// </summary>
    public static string? PonerFormaRecta(Document doc, IEnumerable<Rebar> piezas, TiposPropios propios)
    {
        string? aviso = null;
        foreach (Rebar r in piezas)
        {
            if (!r.IsValidObject || !r.IsRebarShapeDriven()) continue;
            int inicio = Grados(doc, r.GetHookTypeId(0)), fin = Grados(doc, r.GetHookTypeId(1));
            if (propios.FormaRecta(inicio, fin) is not { } pieza) continue;
            if (pieza.Tipo is not RebarShape forma)
            {
                aviso ??= pieza.Aviso;
                continue;
            }
            if (r.GetShapeId() == forma.Id) continue;
            try
            {
                r.GetShapeDrivenAccessor().SetRebarShapeId(forma.Id);
            }
            catch (Exception ex)
            {
                aviso ??= $"no se pudo poner la forma «{forma.Name}» en la barra {r.Id.Value}: {ex.Message}";
            }
        }
        return aviso;
    }

    /// <summary>El ángulo del gancho en grados enteros; 0 si no hay gancho.</summary>
    private static int Grados(Document doc, ElementId gancho) =>
        doc.GetElement(gancho) is RebarHookType tipo ? (int)Math.Round(tipo.HookAngle * 180 / Math.PI) : 0;

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
