using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Revit;

/// <summary>
/// Crea en Revit una corrida de estribos del plan (#85, tarea 8 del plan docs/planes/2026-10-19-generar-acero-vigas-85.md):
/// un conjunto de Revit en el plano del corte, repartido a lo largo de la viga. Lo que se trae del add-in de columnas
/// (decisión 11), medido allá contra Revit real:
/// <list type="bullet">
/// <item>el cerrado sale mejor con la forma de estribo del documento (<c>CreateFromRebarShape</c> + <c>ScaleToBox</c>);
/// una forma elegida por nombre puede traer diagonales, así que se mide y se descarta si las tiene;</item>
/// <item>sin forma, <c>CreateFromCurves</c> con el lazo de cuatro lados. En columnas arrancaba a mitad de un lado para que
/// el gancho no se comiera el doblez de la esquina; en la viga eso dejaba abierto el lado de arriba (hallazgo E2), así
/// que arranca en la esquina de arriba a la izquierda, donde los ganchos de 135° abrazan la barra de esquina;</item>
/// <item>después de repartir hay que regenerar antes de leer cuántos quedaron.</item>
/// </list>
/// El estribo de la viga es vertical y perpendicular a su eje (el de la columna era horizontal).
/// </summary>
internal static class CreadorEstribos
{
    /// <summary>1 mm: un lado que se mueve en las dos direcciones del corte más que esto es una diagonal.</summary>
    private const double ToleranciaDiagonalMm = 1;

    /// <summary>La forma de estribo cerrado del documento (mismo criterio por nombre que columnas); null si no hay.</summary>
    public static RebarShape? FormaEstribo(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).Cast<RebarShape>()
            .FirstOrDefault(f => f.RebarStyle == RebarStyle.StirrupTie &&
                (f.Name.Contains("T1") || f.Name == "51" || f.Name.Contains("Estribo") || f.Name.Contains("Stirrup")));

    /// <summary>
    /// Null si Revit no pudo. <paramref name="forma"/> es la de <see cref="FormaEstribo"/>; si resulta tener diagonales se
    /// agrega a <paramref name="formasMalas"/> y se usa el camino de curvas.
    /// </summary>
    public static Rebar? Crear(Document doc, UbicacionPortico u, GrupoEstribos g, RebarBarType barra, TiposAcero tipos,
        RebarShape? forma, HashSet<ElementId> formasMalas)
    {
        if (doc.GetElement(new ElementId(long.Parse(g.Host))) is not Element host)
            return null;
        double d = LectorModelo.Mm(barra.BarNominalDiameter);
        Rebar? r = g.Tipo == TipoEstribo.Cerrado
            ? Cerrado(doc, u, g, barra, tipos, host, d, forma, formasMalas)
            : Abierto(doc, u, g, barra, tipos, host, d);
        if (r is null)
            return null;

        RebarShapeDrivenAccessor acceso = r.GetShapeDrivenAccessor();
        if (g.Corrida.Cantidad > 1)
            acceso.SetLayoutAsNumberWithSpacing(g.Corrida.Cantidad, CreadorBarras.Pies(g.Corrida.Separacion),
                barsOnNormalSide: true, includeFirstBar: true, includeLastBar: true);
        else
            acceso.SetLayoutAsSingle();
        return r;
    }

    private static Rebar? Cerrado(Document doc, UbicacionPortico u, GrupoEstribos g, RebarBarType barra, TiposAcero tipos,
        Element host, double d, RebarShape? forma, HashSet<ElementId> formasMalas)
    {
        // El eje del estribo: las caras exteriores del corte, medio diámetro hacia adentro.
        double izq = g.Izquierda + d / 2, der = g.Derecha - d / 2, abajo = g.Abajo + d / 2, arriba = g.Arriba - d / 2;
        double x = g.Corrida.Primero;
        RebarHookType gancho = tipos.GanchoEstribo(g.Gancho, g.Diametro);

        if (forma is not null && !formasMalas.Contains(forma.Id))
        {
            // xVec hacia la cara izquierda (+n) e yVec hacia arriba: la normal (xVec × yVec) queda hacia +x, que es hacia
            // donde se reparten. El origen es la esquina derecha de abajo.
            // ScaleToBox mide la caja por la cara exterior del estribo, no por su eje (medido en Revit: con la caja por el
            // eje el estribo salía un radio por dentro de cada cara y se cruzaba con las barras de esquina).
            XYZ origen = CreadorBarras.Punto(u, x, g.Derecha, g.Abajo);
            XYZ n = CreadorBarras.Lateral(u);
            Rebar? conForma = Rebar.CreateFromRebarShape(doc, forma, barra, host, origen, n, XYZ.BasisZ);
            if (conForma is not null)
            {
                conForma.GetShapeDrivenAccessor().ScaleToBox(origen, n * CreadorBarras.Pies(g.Derecha - g.Izquierda), XYZ.BasisZ * CreadorBarras.Pies(g.Arriba - g.Abajo));
                try
                {
                    conForma.SetHookTypeId(0, gancho.Id);
                    conForma.SetHookTypeId(1, gancho.Id);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // la forma puede no tener posiciones de gancho: no bloquea
                }
                doc.Regenerate();
                if (!TieneDiagonal(conForma, u))
                    return conForma;
                doc.Delete(conForma.Id);
                formasMalas.Add(forma.Id);
            }
        }

        // Sin forma (o con una que trae diagonales): el lazo por curvas, arrancando y cerrando en la esquina de arriba a la
        // izquierda. Ahí no hay doblez de 90°: los dos ganchos de 135° abrazan la barra de esquina, como en obra
        // (hallazgo E2: en columnas arrancaba a mitad de un lado y en la viga dejaba abierto el lado de arriba, con la
        // barra del medio sin estribo). Todos los ganchos en la misma esquina ("corrido", #90).
        XYZ P(double xc, double yc) => CreadorBarras.Punto(u, x, xc, yc);
        var curvas = new List<Curve>
        {
            Line.CreateBound(P(izq, arriba), P(der, arriba)),
            Line.CreateBound(P(der, arriba), P(der, abajo)),
            Line.CreateBound(P(der, abajo), P(izq, abajo)),
            Line.CreateBound(P(izq, abajo), P(izq, arriba)),
        };
        using var terminaciones = new BarTerminationsData(doc)
        {
            HookTypeIdAtStart = gancho.Id,
            HookTypeIdAtEnd = gancho.Id,
        };
        return Rebar.CreateFromCurves(doc, RebarStyle.StirrupTie, barra, host, Direccion(u), curvas, terminaciones,
            useExistingShapeIfPossible: true, createNewShape: true);
    }

    /// <summary>
    /// Una rama vertical al lado de las dos barras que abraza, con un gancho en cada punta que da la vuelta a su barra
    /// (hallazgo E13: antes la rama iba por el eje de las barras y los ganchos se abrían hacia afuera). La rama queda del
    /// lado del centro de la viga, a un radio de doblado del eje de las barras: así el doblez del gancho queda centrado en
    /// la barra y la cola entra al núcleo. Los ganchos que doblen hacia el otro lado se voltean después de regenerar
    /// (<see cref="CorregirAbierto"/>).
    /// </summary>
    private static Rebar? Abierto(Document doc, UbicacionPortico u, GrupoEstribos g, RebarBarType barra, TiposAcero tipos, Element host, double d)
    {
        if (g.Rama is not { } rama)
            return null;
        double radio = LectorModelo.Mm(barra.StandardHookBendDiameter) / 2 + d / 2;
        double xRama = rama.X + LadoDeLaRama(u, rama) * radio;
        double x = g.Corrida.Primero;
        // Revit toma la punta de la línea como la cara exterior del doblez (medido con las barras corridas): para que el
        // doblez quede centrado en la barra, la línea pasa un radio más medio diámetro de su centro (sexta prueba: el
        // gancho daba la vuelta 30 mm por encima de la barra).
        double alDoblez = radio + d / 2;
        Line linea = Line.CreateBound(CreadorBarras.Punto(u, x, xRama, rama.YAbajo - alDoblez), CreadorBarras.Punto(u, x, xRama, rama.YArriba + alDoblez));
        RebarHookType? gancho = tipos.GanchoGrapa(g.Gancho, g.Diametro);
        using var terminaciones = new BarTerminationsData(doc);
        if (gancho is not null)
        {
            terminaciones.HookTypeIdAtStart = gancho.Id;
            terminaciones.HookTypeIdAtEnd = gancho.Id;
            terminaciones.TerminationOrientationAtStart = RebarTerminationOrientation.Right;
            terminaciones.TerminationOrientationAtEnd = RebarTerminationOrientation.Right;
        }
        return Rebar.CreateFromCurves(doc, RebarStyle.Standard, barra, host, Direccion(u), new List<Curve> { linea }, terminaciones,
            useExistingShapeIfPossible: true, createNewShape: true);
    }

    /// <summary>+1 si la rama va a la derecha de las barras (en la X del corte), −1 si a la izquierda: hacia el centro.</summary>
    private static int LadoDeLaRama(UbicacionPortico u, Rama rama) => rama.X <= u.Ancho / 2 ? 1 : -1;

    /// <summary>
    /// Deja los dos ganchos del abierto dando la vuelta a su barra: la punta de cada gancho tiene que quedar del lado de la
    /// barra, no del lado de la rama. Llamar después de regenerar. Devuelve si volteó alguno.
    /// </summary>
    public static bool CorregirAbierto(Rebar r, UbicacionPortico u, GrupoEstribos g)
    {
        if (g.Tipo != TipoEstribo.Abierto || g.Rama is not { } rama)
            return false;
        IList<Curve> curvas = r.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
        if (curvas.Count < 2)
            return false;
        XYZ haciaLaBarra = CreadorBarras.Lateral(u) * LadoDeLaRama(u, rama); // −X del corte si la rama está a la derecha
        XYZ inicioRama = curvas.First(c => c.Length > 0).GetEndPoint(0);
        bool volteo = false;
        volteo |= Voltear(r, 0, curvas[0].GetEndPoint(0), curvas.Count > 2 ? curvas[2].GetEndPoint(0) : inicioRama, haciaLaBarra);
        volteo |= Voltear(r, 1, curvas[^1].GetEndPoint(1), curvas.Count > 2 ? curvas[^3].GetEndPoint(1) : inicioRama, haciaLaBarra);
        return volteo;
    }

    /// <summary>Voltea el gancho del extremo si su punta no queda del lado de la barra respecto de la rama.</summary>
    private static bool Voltear(Rebar r, int extremo, XYZ punta, XYZ enLaRama, XYZ haciaLaBarra)
    {
        if ((punta - enLaRama).DotProduct(haciaLaBarra) > 0)
            return false;
        RebarTerminationOrientation actual = r.GetTerminationOrientation(extremo);
        r.SetTerminationOrientation(extremo, actual == RebarTerminationOrientation.Right ? RebarTerminationOrientation.Left : RebarTerminationOrientation.Right);
        return true;
    }

    private static XYZ Direccion(UbicacionPortico u) => new(u.Ux, u.Uy, 0);

    /// <summary>Un lado recto que se mueve a la vez de lado y de alto en el corte: la forma trae una diagonal.</summary>
    private static bool TieneDiagonal(Rebar r, UbicacionPortico u)
    {
        XYZ n = CreadorBarras.Lateral(u);
        foreach (Curve c in r.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0))
        {
            if (c is not Line linea)
                continue;
            XYZ v = linea.GetEndPoint(1) - linea.GetEndPoint(0);
            if (Math.Abs(LectorModelo.Mm(v.DotProduct(n))) > ToleranciaDiagonalMm && Math.Abs(LectorModelo.Mm(v.Z)) > ToleranciaDiagonalMm)
                return true;
        }
        return false;
    }
}
