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

    /// <summary>
    /// Las barras del conjunto como un solo conjunto de Revit: la primera, y las demás repartidas a lo ancho hasta la
    /// última, a la misma separación (después <see cref="AcomodarConjunto"/> deja cada una en su punto). Null si Revit no
    /// pudo crearlo. Los ganchos pueden quedar al revés: ver <see cref="CorregirGanchos"/>.
    /// </summary>
    public static Rebar? Crear(Document doc, UbicacionPortico u, ConjuntoBarras c, RebarBarType barra, TiposAcero tipos)
    {
        Rebar? r = Crear(doc, u, c.Primera, barra, tipos);
        if (r is null || c.Piezas.Count == 1)
            return r;
        PiezaLongitudinal p = c.Primera, ultima = c.Piezas[^1];
        double reparto = (Punto(u, p.Desde, ultima.XCorte, p.YCorte) - Punto(u, p.Desde, p.XCorte, p.YCorte)).DotProduct(Lateral(u));
        try
        {
            r.GetShapeDrivenAccessor().SetLayoutAsFixedNumber(c.Piezas.Count, Math.Abs(reparto), reparto > 0, true, true);
        }
        catch
        {
            // Sin esto quedaría una barra suelta, sin marca, que la próxima generación no borra.
            doc.Delete(r.Id);
            throw;
        }
        return r;
    }

    private static Rebar? Crear(Document doc, UbicacionPortico u, PiezaLongitudinal p, RebarBarType barra, TiposAcero tipos)
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

    /// <summary>
    /// Deja la barra en su punto del corte (E10 y E11 del plan). Al regenerar, Revit ata la barra al doblez del estribo
    /// más cercano (restricción <c>ToOtherRebar</c> en el plano y el borde) y la corre hasta 12 mm; un bastón pegado a la
    /// barra de la esquina queda encima de ella. Medido en Revit: crear los estribos antes no lo evita, y antes de
    /// regenerar la barra no tiene restricciones que cambiar. Por eso se ata a las caras de la viga y se mueve por la
    /// diferencia con el plan. Llamar después de regenerar. Devuelve si la movió.
    /// </summary>
    public static bool FijarEnSuPunto(Rebar r, UbicacionPortico u, PiezaLongitudinal p)
    {
        RebarConstraintsManager restricciones = r.GetRebarConstraintsManager();
        foreach (RebarConstrainedHandle h in restricciones.GetAllConstrainedHandles())
        {
            if (h.GetHandleType() is not (RebarHandleType.RebarPlane or RebarHandleType.Edge))
                continue;
            if (restricciones.GetConstraintCandidatesForHandle(h, r.GetHostId())
                    .FirstOrDefault(c => c.GetConstraintType() == RebarConstraintType.FixedDistanceToHostFace) is { } cara)
                restricciones.SetPreferredConstraint(cara);
        }

        if (r.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0)
                .OfType<Line>().MaxBy(l => l.Length) is not { } recta)
            return false;
        XYZ planeado = Punto(u, p.Desde, p.XCorte, p.YCorte);
        XYZ eje = (Punto(u, p.Hasta, p.XCorte, p.YCorte) - planeado).Normalize();
        XYZ diferencia = planeado - recta.GetEndPoint(0);
        diferencia -= eje * diferencia.DotProduct(eje); // solo en el plano del corte, no a lo largo
        if (diferencia.GetLength() < Pies(0.1))
            return false;
        ElementTransformUtils.MoveElement(r.Document, r.Id, diferencia);
        return true;
    }

    /// <summary>
    /// Deja cada barra del conjunto en su punto del corte: Revit las reparte a la misma separación y a la altura de la
    /// primera, pero la de esquina se apoya en la curva del estribo y la que traslapa puede ir abajo o al lado de su
    /// puesto. Se miden con <c>GetTransformedCenterlineCurves</c> (<c>GetCenterlineCurves</c> da siempre la primera) y se
    /// corren con <c>MoveBarInSet</c>, que suma al movimiento que ya tenían (medido en Revit el 24-oct). Las barras se
    /// emparejan con las piezas por su orden a lo ancho. Llamar después de regenerar, con la primera ya en su punto
    /// (<see cref="FijarEnSuPunto"/>).
    /// </summary>
    public static Acomodo AcomodarConjunto(Rebar r, UbicacionPortico u, ConjuntoBarras c)
    {
        if (c.Piezas.Count < 2)
            return Acomodo.Igual;
        if (r.NumberOfBarPositions != c.Piezas.Count)
            return Acomodo.NoSePudo;
        PiezaLongitudinal p = c.Primera;
        XYZ eje = (Punto(u, p.Hasta, p.XCorte, p.YCorte) - Punto(u, p.Desde, p.XCorte, p.YCorte)).Normalize();
        XYZ ancho = Punto(u, p.Desde, c.Piezas[^1].XCorte, p.YCorte) - Punto(u, p.Desde, p.XCorte, p.YCorte);
        var enRevit = new List<(int Indice, XYZ Punto)>();
        for (int i = 0; i < r.NumberOfBarPositions; i++)
        {
            if (r.GetTransformedCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, i)
                    .OfType<Line>().MaxBy(l => l.Length) is not { } recta)
                return Acomodo.NoSePudo;
            enRevit.Add((i, recta.GetEndPoint(0)));
        }
        Acomodo resultado = Acomodo.Igual;
        foreach (((int indice, XYZ actual), PiezaLongitudinal pieza) in enRevit.OrderBy(b => b.Punto.DotProduct(ancho)).Zip(c.Piezas))
        {
            XYZ diferencia = Punto(u, pieza.Desde, pieza.XCorte, pieza.YCorte) - actual;
            diferencia -= eje * diferencia.DotProduct(eje); // solo en el plano del corte, no a lo largo
            if (diferencia.GetLength() < Pies(0.1))
                continue;
            r.MoveBarInSet(indice, Transform.CreateTranslation(diferencia));
            resultado = Acomodo.Movido;
        }
        return resultado;
    }

    /// <summary>Lo que hizo <see cref="AcomodarConjunto"/>.</summary>
    public enum Acomodo
    {
        /// <summary>Todas ya estaban en su punto (o es una sola barra).</summary>
        Igual,
        Movido,
        /// <summary>Revit no dio las barras que se esperaban: quedan repartidas a la misma separación.</summary>
        NoSePudo,
    }

    /// <summary>El largo de una barra en Revit (con los ganchos), en mm.</summary>
    public static int LargoEnRevit(Rebar r) =>
        (int)Math.Round(LectorModelo.Mm(r.get_Parameter(BuiltInParameter.REBAR_ELEM_LENGTH)?.AsDouble() ?? r.TotalLength / Math.Max(1, r.Quantity)));
}
