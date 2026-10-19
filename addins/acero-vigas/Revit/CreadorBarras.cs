using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Revit;

/// <summary>
/// Crea en Revit una barra corrida o un bastón del plan (#85, tarea 7 del plan
/// docs/planes/2026-10-19-generar-acero-vigas-85.md): una línea recta en el marco del pórtico, con sus ganchos. Lo que
/// aprendió el add-in de columnas contra Revit real (<c>BarraContinua.cs</c>):
/// <list type="bullet">
/// <item>la normal tiene que ser perpendicular a la barra o <c>CreateFromCurves</c> devuelve null siempre;</item>
/// <item><c>CreateFromCurves</c> devuelve null en vez de lanzar cuando no puede: se revisa.</item>
/// </list>
/// </summary>
internal static class CreadorBarras
{
    /// <summary>mm del motor a pies de Revit.</summary>
    public static double Pies(double mm) => mm / 304.8;

    public static XYZ Punto(UbicacionPortico u, double x, double xCorte, double yCorte)
    {
        Punto3 p = MarcoPortico.AlModelo(u, x, xCorte, yCorte);
        return new XYZ(Pies(p.X), Pies(p.Y), Pies(p.Z));
    }

    /// <summary>La horizontal perpendicular al pórtico: el plano de la barra queda vertical y el gancho dobla arriba o abajo.</summary>
    public static XYZ Lateral(UbicacionPortico u) => new(-u.Uy, u.Ux, 0);

    /// <summary>Null si Revit no pudo crearla. Los ganchos pueden quedar al revés: ver <see cref="CorregirGanchos"/>.</summary>
    public static Rebar? Crear(Document doc, UbicacionPortico u, PiezaLongitudinal p, RebarBarType barra, TiposAcero tipos)
    {
        if (doc.GetElement(new ElementId(long.Parse(p.Host))) is not Element host)
            return null;
        Line linea = Line.CreateBound(Punto(u, p.Desde, p.XCorte, p.YCorte), Punto(u, p.Hasta, p.XCorte, p.YCorte));

        using var terminaciones = new BarTerminationsData(doc);
        if (tipos.Gancho(p.GanchoInicio, p.Diametro) is { } inicio)
        {
            terminaciones.HookTypeIdAtStart = inicio.Id;
            terminaciones.TerminationOrientationAtStart = RebarTerminationOrientation.Right;
        }
        if (tipos.Gancho(p.GanchoFin, p.Diametro) is { } fin)
        {
            terminaciones.HookTypeIdAtEnd = fin.Id;
            terminaciones.TerminationOrientationAtEnd = RebarTerminationOrientation.Right;
        }
        return Rebar.CreateFromCurves(doc, RebarStyle.Standard, barra, host, Lateral(u), new List<Curve> { linea }, terminaciones,
            useExistingShapeIfPossible: true, createNewShape: true);
    }

    /// <summary>
    /// Deja cada gancho doblando hacia adentro de la viga (hacia abajo en la capa superior, hacia arriba en la inferior).
    /// Revit decide hacia qué lado es "derecha" según la normal y el sentido de la barra; en vez de suponerlo, se mira dónde
    /// quedó la punta del gancho y, si quedó afuera, se voltea. Llamar después de regenerar. Devuelve si volteó alguno.
    /// </summary>
    public static bool CorregirGanchos(Rebar r, PiezaLongitudinal p, double zBarra)
    {
        IList<Curve> curvas = r.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
        if (curvas.Count < 2)
            return false;
        bool volteo = false;
        if (p.GanchoInicio != TipoGancho.Sin)
            volteo |= Voltear(r, 0, curvas[0].GetEndPoint(0).Z, zBarra, p.Capa);
        if (p.GanchoFin != TipoGancho.Sin)
            volteo |= Voltear(r, 1, curvas[^1].GetEndPoint(1).Z, zBarra, p.Capa);
        return volteo;
    }

    private static bool Voltear(Rebar r, int extremo, double zPunta, double zBarra, Posicion capa)
    {
        bool haciaAdentro = capa == Posicion.Superior ? zPunta < zBarra : zPunta > zBarra;
        if (haciaAdentro)
            return false;
        RebarTerminationOrientation actual = r.GetTerminationOrientation(extremo);
        r.SetTerminationOrientation(extremo, actual == RebarTerminationOrientation.Right ? RebarTerminationOrientation.Left : RebarTerminationOrientation.Right);
        return true;
    }

    /// <summary>El largo de una barra en Revit (con los ganchos), en mm.</summary>
    public static int LargoEnRevit(Rebar r) =>
        (int)Math.Round(LectorModelo.Mm(r.get_Parameter(BuiltInParameter.REBAR_ELEM_LENGTH)?.AsDouble() ?? r.TotalLength / Math.Max(1, r.Quantity)));
}
