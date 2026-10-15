namespace practicacq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

/// <summary>
/// TEMPORAL -- comando de diagnóstico, no forma parte del flujo del add-in.
/// Sirve para medir a mano geometría real contra lo que lee el código:
/// coordenadas de barras (Rebar, para traslapos) y extensión de sólidos en
/// planta/altura (vigas/viguetas/columnas, para el filtro de nudo de
/// <see cref="Logica.LectorContexto"/>). Sacar una vez que se confirmen esos
/// datos.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class ComandoDiagnosticoRebar : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        IList<Reference> refs;
        try
        {
            refs = uidoc.Selection.PickObjects(
                ObjectType.Element, new FiltroCualquierElemento(),
                "Selecciona uno o más elementos (Rebar, vigas, columnas...) -- Finish (Enter) para terminar.");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }

        var sb = new StringBuilder();
        CatalogoFormasYGanchos(doc, sb);

        foreach (Reference r in refs)
        {
            Element el = doc.GetElement(r);

            if (el is Rebar rebar)
            {
                DiagnosticarRebar(rebar, sb);
                continue;
            }

            DiagnosticarSolido(el, sb);
        }

        DiagnosticoUI.MostrarTexto(sb.ToString(), "Diagnóstico geometría (metros)");
        return Result.Succeeded;
    }

    /// <summary>
    /// Qué formas de armadura y qué tipos de gancho tiene cargados el
    /// documento. Sirve para decidir si hay una forma NATIVA de grapa
    /// (barra recta con dos ganchos) que se pueda usar con
    /// <c>Rebar.CreateFromRebarShape</c>, como ya se hace con el estribo
    /// principal, en vez de armar la grapa a mano con curvas; y para ver si
    /// el gancho que heredan grapas y cajas es simplemente uno
    /// sobredimensionado. Ver docs/planes/2026-09-10-cajas-por-eje-y-diagnostico-ganchos.md.
    /// </summary>
    private static void CatalogoFormasYGanchos(Document doc, StringBuilder sb)
    {
        sb.AppendLine("=== FORMAS (RebarShape) del documento ===");
        foreach (RebarShape forma in new FilteredElementCollector(doc)
            .OfClass(typeof(RebarShape)).Cast<RebarShape>().OrderBy(f => f.Name))
        {
            string segmentos;
            try
            {
                segmentos = forma.GetRebarShapeDefinition() is RebarShapeDefinitionBySegments porSegmentos
                    ? $"{porSegmentos.NumberOfSegments} segmento(s)"
                    : "definida por arco";
            }
            catch (Exception ex) { segmentos = $"(no se pudo leer: {ex.Message})"; }

            sb.AppendLine($"  {forma.Name} | {forma.RebarStyle} | {segmentos}");
        }

        sb.AppendLine();
        sb.AppendLine("=== TIPOS DE GANCHO (RebarHookType) del documento ===");
        foreach (RebarHookType tipo in new FilteredElementCollector(doc)
            .OfClass(typeof(RebarHookType)).Cast<RebarHookType>().OrderBy(t => t.Name))
        {
            sb.AppendLine($"  {tipo.Name}");
            foreach (Parameter p in tipo.Parameters)
            {
                if (!p.HasValue) continue;
                string? valor = p.AsValueString() ?? p.AsString();
                if (!string.IsNullOrWhiteSpace(valor))
                    sb.AppendLine($"      {p.Definition.Name} = {valor}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== ELEMENTOS SELECCIONADOS ===");
    }

    /// <summary>
    /// Qué gancho tiene puesto realmente esta barra y de dónde sale su largo.
    /// La clave es <c>AutoCalcHookLengths</c>: si está apagado en el tipo de
    /// barra, el largo del gancho lo manda el propio <c>RebarBarType</c> y
    /// **ignora** el multiplicador del <c>RebarHookType</c> -- o sea que achicar
    /// el multiplicador no cambia nada. Sin este dato no se puede saber por qué
    /// un gancho sigue saliendo grande.
    /// </summary>
    /// <summary>
    /// Para una barra VERTICAL (longitudinal de columna): hacia qué lado dobla
    /// el gancho de cada extremo, y si eso es hacia el centro de la sección o
    /// hacia afuera.
    ///
    /// Es la medición que pedía la regla del CLAUDE.md para los issues #43 y
    /// #59: <c>RebarTerminationOrientation.Left/Right</c> no dice en qué
    /// dirección del espacio queda la pata, y eso no se deduce de la
    /// documentación. Acá se lee de la geometría REAL que devolvió Revit, no de
    /// lo que se le pidió.
    ///
    /// Si el veredicto dice AFUERA, hay que invertir OrientacionPositiva /
    /// OrientacionNegativa en <c>Logica/GeneradorLongitudinal</c> -- están las
    /// dos juntas y es el único lugar donde se decide.
    /// </summary>
    private static void DiagnosticarOrientacionDeGanchos(Rebar rebar, StringBuilder sb)
    {
        IList<Curve> curvas;
        try { curvas = rebar.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, 0); }
        catch (Exception ex)
        {
            sb.AppendLine($"  Orientación de ganchos: no se pudieron leer las curvas ({ex.Message})");
            return;
        }

        if (curvas.Count == 0) return;

        // El eje de la barra: la recta más larga que va en Z. Si no hay
        // ninguna, esto no es una longitudinal de columna y no hay nada que
        // medir acá (los estribos son lazos horizontales).
        Line? eje = curvas
            .OfType<Line>()
            .Where(l => Math.Abs(l.Direction.Z) > 0.9)
            .OrderByDescending(l => l.Length)
            .FirstOrDefault();

        if (eje is null)
        {
            sb.AppendLine("  Orientación de ganchos: la barra no es vertical, no aplica (esto es para longitudinales de columna).");
            return;
        }

        XYZ ejeXY = eje.GetEndPoint(0);

        // El centro de la sección del host, para saber qué es "adentro".
        BoundingBoxXYZ? caja = null;
        try { caja = rebar.Document.GetElement(rebar.GetHostId())?.get_BoundingBox(null); }
        catch { /* se reporta abajo */ }

        if (caja is null)
        {
            sb.AppendLine("  Orientación de ganchos: no se pudo leer el sólido del host, no hay contra qué comparar.");
            return;
        }

        double cx = (caja.Min.X + caja.Max.X) / 2;
        double cy = (caja.Min.Y + caja.Max.Y) / 2;
        double haciaElCentroX = cx - ejeXY.X;
        double haciaElCentroY = cy - ejeXY.Y;

        sb.AppendLine(
            $"  Orientación de ganchos: barra en (X {Metros(ejeXY.X):0.000}, Y {Metros(ejeXY.Y):0.000}), " +
            $"centro del host en (X {Metros(cx):0.000}, Y {Metros(cy):0.000})");

        void Reportar(string queExtremo, XYZ punta)
        {
            double dx = punta.X - ejeXY.X;
            double dy = punta.Y - ejeXY.Y;
            double desvio = Math.Sqrt(dx * dx + dy * dy);

            if (desvio < 1e-6)
            {
                sb.AppendLine($"    {queExtremo}: recto (la punta no se sale del eje), sin gancho que medir.");
                return;
            }

            double proyeccion = dx * haciaElCentroX + dy * haciaElCentroY;
            string veredicto = proyeccion > 0 ? "ADENTRO (correcto)" : "AFUERA (hay que invertir la polaridad)";

            sb.AppendLine(
                $"    {queExtremo}: la punta se corre {Metros(desvio) * 100:0.#} cm " +
                $"(ΔX {Metros(dx) * 100:+0.#;-0.#;0} cm, ΔY {Metros(dy) * 100:+0.#;-0.#;0} cm) -> {veredicto}");
        }

        // Las curvas vienen de principio a fin de la barra, y este archivo las
        // crea siempre de abajo hacia arriba: la primera es el gancho inferior
        // y la última el superior.
        Reportar("Extremo 0 (abajo, anclaje en zapata)", curvas[0].GetEndPoint(0));
        Reportar("Extremo 1 (arriba, remate)", curvas[^1].GetEndPoint(1));
    }

    private static void DiagnosticarGanchosDe(Rebar rebar, StringBuilder sb)
    {
        Document doc = rebar.Document;

        RebarBarType? tipoBarra = null;
        try { tipoBarra = doc.GetElement(rebar.GetTypeId()) as RebarBarType; }
        catch { /* se reporta abajo */ }

        if (tipoBarra is null)
        {
            sb.AppendLine("  (no se pudo leer el tipo de barra)");
            return;
        }

        sb.AppendLine($"  Barra: «{tipoBarra.Name}», diámetro nominal {Metros(tipoBarra.BarNominalDiameter) * 1000:0.#} mm, " +
                      $"de modelo {Metros(tipoBarra.BarModelDiameter) * 1000:0.#} mm");

        // Los diámetros de DOBLADO son lo que hace bulto en un gancho de 180°:
        // el semicírculo del gancho tiene ese radio. Nunca se habían mirado --
        // todas las conclusiones sobre "el gancho es muy grande" salieron de
        // GetHookLength, que es el tramo recto, no el arco.
        string Doblado(Func<double> leer)
        {
            try { return $"{Metros(leer()) * 100:0.00} cm"; }
            catch (Exception ex) { return $"(no se pudo leer: {ex.Message})"; }
        }

        sb.AppendLine(
            $"  Diámetros de doblado: estándar={Doblado(() => tipoBarra.StandardBendDiameter)}, " +
            $"gancho estándar={Doblado(() => tipoBarra.StandardHookBendDiameter)}, " +
            $"estribo/tirante={Doblado(() => tipoBarra.StirrupTieBendDiameter)}");

        sb.AppendLine("  Parámetros del tipo de barra:");
        foreach (Parameter p in tipoBarra.Parameters)
        {
            if (!p.HasValue) continue;
            string? valor = p.AsValueString() ?? p.AsString();
            if (!string.IsNullOrWhiteSpace(valor))
                sb.AppendLine($"      {p.Definition.Name} = {valor}");
        }

        try
        {
            if (doc.GetElement(rebar.GetShapeId()) is RebarShape forma)
                sb.AppendLine($"  Forma: «{forma.Name}» | estilo {forma.RebarStyle}");
        }
        catch { /* la barra puede no tener forma asociada */ }

        for (int extremo = 0; extremo < 2; extremo++)
        {
            ElementId idGancho;
            try { idGancho = rebar.GetHookTypeId(extremo); }
            catch { continue; }

            if (idGancho == ElementId.InvalidElementId)
            {
                sb.AppendLine($"  Extremo {extremo}: sin gancho");
                continue;
            }

            if (doc.GetElement(idGancho) is not RebarHookType gancho)
            {
                sb.AppendLine($"  Extremo {extremo}: gancho {idGancho.Value} (no se pudo leer)");
                continue;
            }

            // Si "largo real" NO coincide con multiplicador × diámetro, quiere
            // decir que el tipo de barra tiene un largo de gancho propio que
            // pisa al multiplicador -- y entonces achicar el multiplicador del
            // RebarHookType no cambia nada.
            string largo;
            try { largo = $"{Metros(tipoBarra.GetHookLength(gancho.Id)) * 100:0.#} cm"; }
            catch (Exception ex) { largo = $"(no se pudo leer: {ex.Message})"; }

            double esperado = gancho.StraightLineMultiplier * tipoBarra.BarNominalDiameter;

            // El giro del gancho fuera del plano no se lee acá: Rebar no expone
            // GetHookRotationAngle en la API de 2027. Si el gancho quedara fuera
            // del plano se nota igual en la normal del ARCO que imprime
            // DescribirCurva.
            sb.AppendLine(
                $"  Extremo {extremo}: «{gancho.Name}» | ángulo {gancho.HookAngle * 180 / Math.PI:0.#}° | " +
                $"Style={gancho.Style} | multiplicador={gancho.StraightLineMultiplier:0.##} | " +
                $"largo real={largo} | esperado por multiplicador={Metros(esperado) * 100:0.#} cm");
        }
    }

    private static void DiagnosticarRebar(Rebar rebar, StringBuilder sb)
    {
        sb.AppendLine($"--- Rebar {rebar.Id.Value} (host {rebar.GetHostId().Value}) ---");

        DiagnosticarGanchosDe(rebar, sb);
        DiagnosticarOrientacionDeGanchos(rebar, sb);

        int posiciones;
        try { posiciones = rebar.NumberOfBarPositions; }
        catch { posiciones = 1; }

        for (int pos = 0; pos < Math.Max(1, posiciones); pos++)
        {
            sb.AppendLine($"  [posición {pos}] SIN ganchos ni radios (solo los tramos rectos, como en los reportes anteriores)");
            VolcarCurvas(rebar, pos, suprimir: true, MultiplanarOption.IncludeOnlyPlanarCurves, sb);

            sb.AppendLine($"  [posición {pos}] CON ganchos y radios REALES");
            VolcarCurvas(rebar, pos, suprimir: false, MultiplanarOption.IncludeAllMultiplanarCurves, sb);

            // Confirma si Revit reconoce esta barra como parte de un empalme
            // formal (RebarSplice) en cada extremo -- para distinguir un par
            // que salió por RebarSpliceUtils.SpliceRebar (issue empalmes
            // reales) de dos barras sueltas sin relación entre Revit.
            for (int extremo = 0; extremo < 2; extremo++)
            {
                RebarSplice? splice;
                try { splice = rebar.GetRebarSplice(extremo); }
                catch { splice = null; }

                if (splice is not null)
                    sb.AppendLine(
                        $"    Extremo {extremo}: RebarSplice reconocido -- SplicePosition={splice.SplicePosition}, " +
                        $"SourceRebarId={splice.SourceRebarId}, ConnectedRebarId={splice.ConnectedRebarId}");
            }
        }
    }

    /// <summary>
    /// Hasta el 10-sep el diagnóstico pedía las curvas con
    /// <c>suppressHooks</c>/<c>suppressBendRadius</c> en <c>true</c>, así que
    /// Revit devolvía SOLO el tramo recto: el gancho nunca se midió, y todas
    /// las conclusiones sobre su tamaño salieron de <c>GetHookLength</c> (un
    /// número), no de la geometría. Con los flags en <c>false</c> aparecen los
    /// arcos, y con ellos el radio de doblado (lo que hace bulto en un gancho
    /// de 180°) y el CENTRO del arco -- que es donde tiene que caer la barra
    /// longitudinal para que la grapa la abrace.
    /// </summary>
    private static void VolcarCurvas(Rebar rebar, int pos, bool suprimir, MultiplanarOption opcion, StringBuilder sb)
    {
        IList<Curve> curvas;
        try
        {
            curvas = rebar.GetCenterlineCurves(false, suprimir, suprimir, opcion, pos);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"    no se pudo leer geometría: {ex.Message}");
            return;
        }

        foreach (Curve c in curvas)
            sb.AppendLine($"    {DescribirCurva(c)}");
    }

    private static string DescribirCurva(Curve c)
    {
        if (c is Arc arco)
        {
            double barrido = arco.GetEndParameter(1) - arco.GetEndParameter(0);
            XYZ n = arco.Normal;
            return $"ARCO  centro={Fmt(arco.Center)} radio={Metros(arco.Radius) * 100:0.00} cm " +
                   $"barrido={barrido * 180 / Math.PI:0.#}° normal=({n.X:0.00}, {n.Y:0.00}, {n.Z:0.00})  " +
                   $"{Fmt(c.GetEndPoint(0))} -> {Fmt(c.GetEndPoint(1))}";
        }

        return $"RECTA largo={Metros(c.Length) * 100:0.00} cm  " +
               $"{Fmt(c.GetEndPoint(0))} -> {Fmt(c.GetEndPoint(1))}";
    }

    /// <summary>
    /// Mismo cálculo que <c>LectorContexto.HuellaPlanta</c>/<c>ExtensionZ</c> --
    /// sólido mayor del elemento, caja en planta (XMin/XMax/YMin/YMax) y
    /// extensión en Z, para comparar contra lo que el add-in ve realmente de
    /// una viga/vigueta/columna.
    /// </summary>
    /// <summary>
    /// LectorContexto.Recubrimiento hoy solo lee CLEAR_COVER_OTHER, asumiendo
    /// que es el único parámetro de cobertura relevante para una columna
    /// (Exterior/Interior serían "solo para muros", según lo que se encontró
    /// buscando -- sin confirmar contra la API real). Esto imprime los TRES
    /// parámetros (Exterior/Interior/Other) del elemento seleccionado para
    /// ver cuál existe de verdad en un host que no es un muro, y si tienen
    /// valores distintos entre sí -- lo que explicaría un recubrimiento
    /// distinto al esperado sin que sea un error de carga del modelo.
    /// </summary>
    private static void DiagnosticarCoberturas(Element el, StringBuilder sb)
    {
        foreach ((BuiltInParameter bip, string nombre) in new[]
                 {
                     (BuiltInParameter.CLEAR_COVER_EXTERIOR, "Exterior"),
                     (BuiltInParameter.CLEAR_COVER_INTERIOR, "Interior"),
                     (BuiltInParameter.CLEAR_COVER_OTHER, "Other"),
                 })
        {
            Parameter? p = el.get_Parameter(bip);
            if (p is null)
            {
                sb.AppendLine($"  Cobertura [{nombre}]: el parámetro no existe en este elemento.");
                continue;
            }

            if (el.Document.GetElement(p.AsElementId()) is RebarCoverType tipo)
                sb.AppendLine($"  Cobertura [{nombre}]: «{tipo.Name}» = {Metros(tipo.CoverDistance) * 100:0.#} cm");
            else
                sb.AppendLine($"  Cobertura [{nombre}]: existe el parámetro pero sin RebarCoverType asignado (AsElementId={p.AsElementId().Value}).");
        }
    }

    private static void DiagnosticarSolido(Element el, StringBuilder sb)
    {
        sb.AppendLine($"--- {el.Category?.Name ?? "Elemento"} {el.Id.Value} ({el.Name}) ---");

        DiagnosticarCoberturas(el, sb);

        Solid? solido = SolidoMayor(el.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }));
        if (solido is null)
        {
            sb.AppendLine("  (sin sólido legible)");
            return;
        }

        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;
        double zMin = double.MaxValue, zMax = double.MinValue;

        foreach (Face cara in solido.Faces)
        {
            foreach (EdgeArray bucle in cara.EdgeLoops)
            {
                foreach (Edge borde in bucle)
                {
                    foreach (XYZ p in borde.Tessellate())
                    {
                        xMin = Math.Min(xMin, p.X); xMax = Math.Max(xMax, p.X);
                        yMin = Math.Min(yMin, p.Y); yMax = Math.Max(yMax, p.Y);
                        zMin = Math.Min(zMin, p.Z); zMax = Math.Max(zMax, p.Z);
                    }
                }
            }
        }

        double anchoX = Metros(xMax - xMin);
        double anchoY = Metros(yMax - yMin);
        double alturaZ = Metros(zMax - zMin);

        sb.AppendLine($"  Huella en planta: X[{Metros(xMin):0.000}, {Metros(xMax):0.000}] ({anchoX:0.000} m)  " +
                      $"Y[{Metros(yMin):0.000}, {Metros(yMax):0.000}] ({anchoY:0.000} m)");
        sb.AppendLine($"  Extensión Z: [{Metros(zMin):0.000}, {Metros(zMax):0.000}] ({alturaZ:0.000} m)");
        sb.AppendLine($"  Menor lado en planta (lo que usa el filtro de vigueta): {Math.Min(anchoX, anchoY):0.000} m");
        sb.AppendLine($"  Volumen del sólido leído: {UnitUtils.ConvertFromInternalUnits(solido.Volume, UnitTypeId.CubicMeters):0.000} m3");
    }

    internal static Solid? SolidoMayor(GeometryElement? ge)
    {
        if (ge is null) return null;

        Solid? mejor = null;
        foreach (GeometryObject go in ge)
        {
            Solid? candidato = go switch
            {
                Solid s when s.Volume > 1e-6 => s,
                GeometryInstance gi => SolidoMayor(gi.GetInstanceGeometry()),
                _ => null
            };

            if (candidato is not null && (mejor is null || candidato.Volume > mejor.Volume))
                mejor = candidato;
        }
        return mejor;
    }

    private static string Fmt(XYZ p) =>
        $"({Metros(p.X):0.000}, {Metros(p.Y):0.000}, {Metros(p.Z):0.000})";

    private static double Metros(double pies) => UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Meters);
}

internal sealed class FiltroCualquierElemento : ISelectionFilter
{
    public bool AllowElement(Element elem) => true;
    public bool AllowReference(Reference reference, XYZ position) => true;
}

/// <summary>
/// TEMPORAL -- reemplazo de MessageBox.Show para los diagnósticos: un
/// MessageBox normal no se puede agrandar ni hacer scroll, y con varias
/// pasadas el texto no entra. Esto es una ventana con un cuadro de texto de
/// verdad (con scroll y seleccionable/copiable) en su lugar.
/// </summary>
internal static class DiagnosticoUI
{
    public static void MostrarTexto(string texto, string titulo)
    {
        var caja = new System.Windows.Controls.TextBox
        {
            Text = texto,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(8),
        };

        var ventana = new Window
        {
            Title = titulo,
            Content = caja,
            Width = 950,
            Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };

        ventana.ShowDialog();
    }
}

/// <summary>
/// TEMPORAL -- prueba <see cref="RebarSpliceUtils"/> (la API formal de
/// empalmes de Revit, desde 2025) contra una barra longitudinal real ya
/// generada por el add-in, para decidir si <c>GeneradorLongitudinal</c> debe
/// migrar a ella en vez de seguir creando dos <see cref="Rebar"/> sueltos con
/// corrimiento calculado a mano. Ver docs/planes/2026-09-09-empalmes-rebarspliceutils.md.
/// Todo lo que hace esta corrida se REVIERTE al final (no deja cambios
/// permanentes en el modelo) -- es solo para leer cómo se comporta Revit.
/// Sacar una vez confirmada (o descartada) la migración.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ComandoDiagnosticoSplice : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        Reference r;
        try
        {
            r = uidoc.Selection.PickObject(
                ObjectType.Element, new FiltroSoloRebar(),
                "Selecciona una barra longitudinal (Rebar) real generada por el add-in, sin traslapo todavía en el punto medio.");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }

        var sb = new StringBuilder();

        if (doc.GetElement(r) is not Rebar rebar)
        {
            sb.AppendLine("El elemento seleccionado no es un Rebar.");
            DiagnosticoUI.MostrarTexto(sb.ToString(), "Diagnóstico RebarSpliceUtils");
            return Result.Failed;
        }

        // Los 3 intentos de partir esta barra devolvieron 0 piezas en un caso
        // real (host 152865) sin tirar excepción -- posible sospechoso: un
        // gancho de anclaje en algún extremo (zapata) que RebarSpliceUtils no
        // pueda partir bien. Se reporta antes de intentar nada.
        for (int extremo = 0; extremo < 2; extremo++)
        {
            ElementId? ganchoId;
            try { ganchoId = rebar.GetHookTypeId(extremo); } catch { ganchoId = null; }
            if (ganchoId is not null && ganchoId != ElementId.InvalidElementId)
                sb.AppendLine($"Extremo {extremo}: tiene gancho de anclaje (tipo id {ganchoId.Value}).");
        }

        // Dos pasadas independientes sobre la MISMA barra original, cada una
        // en su propia transacción revertida -- así la segunda pasada
        // encuentra la barra intacta otra vez. La primera usa la longitud
        // comercial real (6 m); la segunda achica el máximo a propósito
        // (prueba de estrés, no un valor real) para forzar 2+ empalmes en la
        // misma barra y ver si Revit alterna el lado del corrimiento entre
        // ellos (igual que hace hoy el código a mano, T1/T2) o siempre lo
        // corre para el mismo lado.
        foreach ((double maximoMetros, string etiqueta) in new[]
                 {
                     (6.0, "longitud comercial real (6 m)"),
                     (2.5, "PRUEBA DE ESTRÉS -- máximo artificial de 2.5 m, para forzar varios empalmes")
                 })
        {
            sb.AppendLine();
            sb.AppendLine($"=== Pasada con {etiqueta} ===");

            using Transaction t = new(doc, "Diagnóstico RebarSpliceUtils (se revierte)");
            t.Start();
            try
            {
                DiagnosticarSplice(doc, rebar, maximoMetros, sb);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"EXCEPCIÓN: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                // A propósito: nunca Commit acá. Esto es solo para leer el
                // comportamiento real de la API contra el modelo del usuario
                // -- no debe dejar ningún RebarSpliceType/Rebar de prueba en
                // su proyecto real.
                t.RollBack();
            }
        }

        // Tercera pasada: geometría de empalme EXPLÍCITA (nuestro propio punto
        // Z, no el que calcula Revit solo) -- es el mecanismo real que usaría
        // producción, para no depender del algoritmo automático que ya
        // probamos que ignora nudos/vigas.
        sb.AppendLine();
        sb.AppendLine("=== Pasada con geometría EXPLÍCITA (nuestro propio punto de corte, no automático) ===");
        using (Transaction t = new(doc, "Diagnóstico RebarSpliceUtils explícito (se revierte)"))
        {
            t.Start();
            try
            {
                DiagnosticarSpliceExplicito(doc, rebar, sb);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"EXCEPCIÓN: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                t.RollBack();
            }
        }

        // Cuarta pasada: el corrimiento automático (BarPlane) salió en el eje
        // equivocado en producción (confirmado sobre Revit real: corrió en X
        // en una posición de cara X, donde tiene que correr en Y) -- se
        // prueba dejar el tipo de empalme SIN corrimiento (None) y mover
        // nosotros mismos una de las dos piezas con el mismo vector ya
        // verificado (ElementTransformUtils.MoveElement), para confirmar que
        // el empalme formal sigue reconocido por Revit después de mover la
        // pieza a mano.
        sb.AppendLine();
        sb.AppendLine("=== Pasada con ShiftOption.None + mover una pieza a mano ===");
        using (Transaction t = new(doc, "Diagnóstico RebarSpliceUtils sin shift automático (se revierte)"))
        {
            t.Start();
            try
            {
                DiagnosticarSpliceSinShiftAutomatico(doc, rebar, sb);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"EXCEPCIÓN: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                t.RollBack();
            }
        }

        sb.AppendLine();
        sb.AppendLine("(Todas las transacciones se revirtieron -- no se dejó ningún cambio permanente en el modelo.)");

        DiagnosticoUI.MostrarTexto(sb.ToString(), "Diagnóstico RebarSpliceUtils");
        return Result.Succeeded;
    }

    private static void DiagnosticarSpliceSinShiftAutomatico(Document doc, Rebar rebar, StringBuilder sb)
    {
        sb.AppendLine($"--- Rebar {rebar.Id.Value} (host {rebar.GetHostId().Value}) ---");

        ElementId tipoSpliceId = BuscarOCrearTipoSplice(doc, sb);
        if (tipoSpliceId == ElementId.InvalidElementId)
        {
            sb.AppendLine("No se pudo obtener/crear un RebarSpliceType.");
            return;
        }

        // Se fuerza ShiftOption.None en el tipo -- OJO: esto es un cambio
        // sobre un tipo COMPARTIDO del documento, revertido al final de esta
        // transacción de prueba, pero si se lleva a producción habría que
        // usar un tipo propio del addin, no mutar el que ya usa el equipo
        // para trabajo manual.
        RebarSpliceTypeUtils.SetShiftOption(doc, tipoSpliceId, RebarSpliceShiftOption.None);

        if (rebar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0)[0] is not Line linea)
        {
            sb.AppendLine("La barra no tiene una curva recta simple -- no apta.");
            return;
        }

        var opciones = new RebarSpliceOptions(doc, tipoSpliceId, RebarSplicePosition.Middle);
        XYZ ini = linea.GetEndPoint(0), fin = linea.GetEndPoint(1);
        XYZ puntoCorte = ini + (fin - ini) * 0.6;

        var geometria = new RebarSpliceGeometry(puntoCorte, XYZ.BasisZ);
        IList<ElementId> resultantes = RebarSpliceUtils.SpliceRebar(doc, rebar.Id, opciones, new List<RebarSpliceGeometry> { geometria });
        sb.AppendLine($"SpliceRebar (ShiftOption.None) devolvió {resultantes.Count} barra(s): {string.Join(", ", resultantes.Select(id => id.Value))}");

        List<Rebar> piezas = resultantes.Select(id => doc.GetElement(id) as Rebar).Where(r => r is not null).Select(r => r!).ToList();
        if (piezas.Count != 2)
        {
            sb.AppendLine("No se obtuvieron las dos piezas esperadas.");
            ReportarWarnings(doc, sb);
            return;
        }

        double ZMedio(Rebar r) => r.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0)[0] is Line l
            ? (l.GetEndPoint(0).Z + l.GetEndPoint(1).Z) / 2 : 0;
        Rebar superior = ZMedio(piezas[0]) >= ZMedio(piezas[1]) ? piezas[0] : piezas[1];

        sb.AppendLine("Antes de mover (deberían coincidir en X/Y, sin corrimiento):");
        foreach (Rebar r in piezas)
            foreach (Curve c in r.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0))
                sb.AppendLine($"  Rebar {r.Id.Value}: {Fmt(c.GetEndPoint(0))} -> {Fmt(c.GetEndPoint(1))}");

        // Mismo vector que ya usa GeneradorLongitudinal.Corrimiento: un
        // diámetro de barra, en Y.
        // "rebar" (el parámetro original) ya no es válido acá -- SpliceRebar
        // lo reemplazó por las dos piezas nuevas; hay que sacar el diámetro
        // de una de ELLAS, no del original ya consumido (esto tiraba
        // InvalidObjectException: "referenced object is not valid").
        double diametro = doc.GetElement(superior.GetTypeId()) is RebarBarType tipoBarra ? tipoBarra.BarNominalDiameter : 0;
        ElementTransformUtils.MoveElement(doc, superior.Id, new XYZ(0, diametro, 0));

        sb.AppendLine($"Después de mover la pieza superior (Rebar {superior.Id.Value}) {Metros(diametro) * 100:0.0} cm en Y a mano:");
        foreach (Rebar r in piezas)
            foreach (Curve c in r.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0))
                sb.AppendLine($"  Rebar {r.Id.Value}: {Fmt(c.GetEndPoint(0))} -> {Fmt(c.GetEndPoint(1))}");

        foreach (Rebar r in piezas)
        {
            for (int extremo = 0; extremo < 2; extremo++)
            {
                RebarSplice? splice;
                try { splice = r.GetRebarSplice(extremo); } catch { splice = null; }
                if (splice is not null)
                    sb.AppendLine($"  Rebar {r.Id.Value} extremo {extremo}: RebarSplice sigue reconocido " +
                                  $"(SourceRebarId={splice.SourceRebarId}, ConnectedRebarId={splice.ConnectedRebarId}).");
            }
        }

        sb.AppendLine("Confirmar: ¿el empalme sigue reconocido después de mover la pieza a mano, " +
                      "y el corrimiento final quedó donde se pidió (en Y, la magnitud correcta)?");
    }

    private static void DiagnosticarSpliceExplicito(Document doc, Rebar rebar, StringBuilder sb)
    {
        sb.AppendLine($"--- Rebar {rebar.Id.Value} (host {rebar.GetHostId().Value}) ---");

        ElementId tipoSpliceId = BuscarOCrearTipoSplice(doc, sb);
        if (tipoSpliceId == ElementId.InvalidElementId)
        {
            sb.AppendLine("No se pudo obtener/crear un RebarSpliceType.");
            return;
        }

        var opciones = new RebarSpliceOptions(doc, tipoSpliceId, RebarSplicePosition.Middle);

        if (rebar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0)[0] is not Line linea)
        {
            sb.AppendLine("La barra no tiene una curva recta simple -- no apta.");
            return;
        }

        double diametroMetros = doc.GetElement(rebar.GetTypeId()) is RebarBarType tipoBarra
            ? Metros(tipoBarra.BarNominalDiameter) : 0;
        double multiplicadorSolape = RebarSpliceTypeUtils.GetLapLengthMultiplier(doc, tipoSpliceId);
        double longitudSolapeMetros = diametroMetros * multiplicadorSolape;

        // Punto elegido a mano: 60% de la altura de la barra (no el medio
        // exacto), simulando que el punto real lo decidió PlanificarCortes,
        // no un cálculo geométrico simple -- con margen suficiente para el
        // solape a cada lado.
        XYZ ini = linea.GetEndPoint(0), fin = linea.GetEndPoint(1);
        XYZ puntoCorte = ini + (fin - ini) * 0.6;
        double margenDisponibleMetros = Math.Min(Metros((puntoCorte - ini).GetLength()), Metros((fin - puntoCorte).GetLength()));

        sb.AppendLine($"Punto de corte elegido a mano (60% de la altura): {Fmt(puntoCorte)}. " +
                      $"Margen disponible al lado más corto: {margenDisponibleMetros:0.000} m " +
                      $"(necesita >= {longitudSolapeMetros / 2:0.000} m de cada lado).");

        if (margenDisponibleMetros < longitudSolapeMetros / 2)
        {
            sb.AppendLine("No hay margen suficiente en ese punto para el solape -- no se ejecuta SpliceRebar.");
            return;
        }

        var geometria = new RebarSpliceGeometry(puntoCorte, XYZ.BasisZ);
        IList<ElementId> resultantes = RebarSpliceUtils.SpliceRebar(doc, rebar.Id, opciones, new List<RebarSpliceGeometry> { geometria });
        sb.AppendLine($"SpliceRebar (geometría explícita) devolvió {resultantes.Count} barra(s): {string.Join(", ", resultantes.Select(id => id.Value))}");
        if (resultantes.Count != 2) ReportarWarnings(doc, sb);

        foreach (ElementId id in resultantes)
        {
            if (doc.GetElement(id) is not Rebar barraResultante) continue;
            sb.AppendLine($"  Rebar {id.Value}:");
            foreach (Curve c in barraResultante.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0))
                sb.AppendLine($"    {Fmt(c.GetEndPoint(0))} -> {Fmt(c.GetEndPoint(1))}");
        }

        sb.AppendLine("Confirmar: ¿el punto de corte real (centro del solape) coincide con el 60% elegido, " +
                      "y el corrimiento lateral sigue siendo ~1 diámetro en Y, igual que con la geometría automática?");
    }

    private static void DiagnosticarSplice(Document doc, Rebar rebar, double maximoMetros, StringBuilder sb)
    {
        sb.AppendLine($"--- Rebar {rebar.Id.Value} (host {rebar.GetHostId().Value}) ---");

        ElementId tipoSpliceId = BuscarOCrearTipoSplice(doc, sb);
        if (tipoSpliceId == ElementId.InvalidElementId)
        {
            sb.AppendLine("No se pudo obtener/crear un RebarSpliceType -- no se puede continuar la prueba.");
            return;
        }

        var opciones = new RebarSpliceOptions(doc, tipoSpliceId, RebarSplicePosition.Middle);

        IList<Curve> curvas;
        try
        {
            curvas = rebar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"No se pudo leer la curva de la barra: {ex.Message}");
            return;
        }

        if (curvas.Count == 0 || curvas[0] is not Line linea)
        {
            sb.AppendLine("La barra no tiene una curva recta simple en la posición 0 -- no apta para esta prueba.");
            return;
        }

        string tipoPosicion = ClasificarPosicion(doc, rebar, linea, sb);

        // RebarSplicePosition.Middle necesita espacio libre de media longitud
        // de traslapo A CADA LADO del punto de corte -- si la barra es más
        // corta que eso, Revit no tiene dónde poner el solape y devuelve
        // SpliceGeometryDoesNotIntersectAllTheBarsInTheSet. Se calcula acá
        // para no adivinar el punto de corte a ciegas.
        double longitudBarraMetros = Metros(linea.Length);
        double multiplicadorSolape = RebarSpliceTypeUtils.GetLapLengthMultiplier(doc, tipoSpliceId);
        double diametroMetros = 0;
        if (doc.GetElement(rebar.GetTypeId()) is RebarBarType tipoBarra)
            diametroMetros = Metros(tipoBarra.BarNominalDiameter);
        double longitudSolapeMetros = diametroMetros * multiplicadorSolape;

        sb.AppendLine($"Longitud de la barra: {longitudBarraMetros:0.000} m. " +
                       $"Longitud de solape del tipo de empalme (\"{doc.GetElement(tipoSpliceId)?.Name}\", " +
                       $"multiplicador {multiplicadorSolape:0.#} x diámetro {diametroMetros * 1000:0.#}mm): " +
                       $"{longitudSolapeMetros:0.000} m.");

        if (longitudBarraMetros < longitudSolapeMetros)
        {
            sb.AppendLine("Esta barra es más corta que la longitud de solape requerida -- no cabe un empalme " +
                          "en el medio de esta barra. Probar con otra barra más larga (de un host más alto).");
            return;
        }

        // En vez de adivinar a mano el plano de corte (falló las dos veces
        // con SpliceGeometryDoesNotIntersectAllTheBarsInTheSet -- construir
        // esa línea/plano bien no es trivial), se usa la variante que
        // calcula Revit mismo: RebarSpliceRules + GetSpliceGeometries. Se
        // fija el máximo en la misma longitud comercial (6 m) que ya usa
        // PlanificarSplicesReales, para poder comparar resultados.
        RebarSpliceRules reglas = RebarSpliceRules.Create(doc);
        reglas.SetMaximumAndMinimumBarLength(
            UnitUtils.ConvertToInternalUnits(maximoMetros, UnitTypeId.Meters),
            Math.Min(reglas.MinimumBarLength, UnitUtils.ConvertToInternalUnits(maximoMetros, UnitTypeId.Meters)));
        sb.AppendLine($"Reglas: longitud máxima {Metros(reglas.MaximumBarLength):0.000} m, " +
                      $"mínima {Metros(reglas.MinimumBarLength):0.000} m, RunOutPosition={reglas.RunOutPosition}.");

        RebarSpliceByRulesResult resultadoReglas = RebarSpliceUtils.GetSpliceGeometries(doc, rebar.Id, opciones, reglas);
        sb.AppendLine($"GetSpliceGeometries: {resultadoReglas.Error}");

        if (resultadoReglas.Error != RebarSpliceByRulesError.Success)
        {
            sb.AppendLine("Revit no pudo calcular geometrías de empalme automáticas para esta barra con estas reglas.");
            return;
        }

        IList<RebarSpliceGeometry> geometrias = resultadoReglas.GetSpliceGeometries();
        sb.AppendLine($"Geometrías de empalme calculadas por Revit: {geometrias.Count}");

        IList<ElementId> resultantes = RebarSpliceUtils.SpliceRebar(doc, rebar.Id, opciones, geometrias);
        sb.AppendLine($"SpliceRebar devolvió {resultantes.Count} barra(s): {string.Join(", ", resultantes.Select(id => id.Value))}");
        if (resultantes.Count != 2) ReportarWarnings(doc, sb);

        var puntos = new List<XYZ>();
        foreach (ElementId id in resultantes)
        {
            if (doc.GetElement(id) is not Rebar barraResultante) continue;

            sb.AppendLine($"  Rebar {id.Value}:");
            IList<Curve> curvasResultantes =
                barraResultante.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
            foreach (Curve c in curvasResultantes)
            {
                sb.AppendLine($"    {Fmt(c.GetEndPoint(0))} -> {Fmt(c.GetEndPoint(1))}");
                puntos.Add(c.GetEndPoint(0));
            }

            RebarSplice? splice = barraResultante.GetRebarSplice(0);
            if (splice is not null)
                sb.AppendLine(
                    $"    RebarSplice: SplicePosition={splice.SplicePosition}, " +
                    $"SourceRebarId={splice.SourceRebarId}, ConnectedRebarId={splice.ConnectedRebarId}");
        }

        sb.AppendLine();
        if (puntos.Count >= 2 && tipoPosicion != "sin clasificar")
        {
            double dx = Math.Abs(Metros(puntos[0].X - puntos[1].X));
            double dy = Math.Abs(Metros(puntos[0].Y - puntos[1].Y));
            string ejeObservado = dx > dy ? "X" : (dy > dx ? "Y" : "ninguno (X e Y iguales)");
            // Regla real confirmada (9-sep, contra el modelo de la empresa):
            // SIEMPRE en Y, sin importar la cara -- ver Corrimiento en
            // GeneradorLongitudinal.cs y docs/planes/2026-09-09-diagnostico-corrimiento-traslapo.md.
            const string ejeEsperado = "Y";

            sb.AppendLine($"Corrimiento observado entre las dos barras resultantes: ΔX={dx * 100:0.00} cm, ΔY={dy * 100:0.00} cm " +
                          $"-> corrió en {ejeObservado}.");
            sb.AppendLine($"Posición clasificada como \"{tipoPosicion}\" -> la regla real confirmada del equipo " +
                          $"es que SIEMPRE corre en Y (sin importar la cara) -- se esperaba {ejeEsperado}.");
            sb.AppendLine(ejeObservado == ejeEsperado
                ? "COINCIDE con la regla del equipo."
                : "NO COINCIDE con la regla del equipo -- RebarSpliceShiftOption.BarPlane no es confiable para esta posición.");
        }
        else
        {
            sb.AppendLine("No se pudo clasificar la posición de esta barra contra la huella de la columna " +
                          "(no está sobre ningún borde reconocible) -- no se puede comparar el eje del corrimiento.");
        }
    }

    /// <summary>
    /// Clasifica la barra como "esquina", "cara X" o "cara Y" comparando su
    /// X/Y fijo contra la huella real de la columna (mismo cálculo que
    /// DiagnosticarSolido/SolidoMayor), para no depender de que el usuario
    /// adivine visualmente qué posición seleccionó.
    /// </summary>
    private static string ClasificarPosicion(Document doc, Rebar rebar, Line linea, StringBuilder sb)
    {
        if (doc.GetElement(rebar.GetHostId()) is not Element host)
            return "sin clasificar";

        Solid? solido = ComandoDiagnosticoRebar.SolidoMayor(host.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }));
        if (solido is null)
            return "sin clasificar";

        double xMin = double.MaxValue, xMax = double.MinValue, yMin = double.MaxValue, yMax = double.MinValue;
        foreach (Face cara in solido.Faces)
            foreach (EdgeArray bucle in cara.EdgeLoops)
                foreach (Edge borde in bucle)
                    foreach (XYZ p in borde.Tessellate())
                    {
                        xMin = Math.Min(xMin, p.X); xMax = Math.Max(xMax, p.X);
                        yMin = Math.Min(yMin, p.Y); yMax = Math.Max(yMax, p.Y);
                    }

        double x = linea.GetEndPoint(0).X, y = linea.GetEndPoint(0).Y;
        // Tolerancia generosa (10cm) porque la posición real de la barra ya
        // descuenta recubrimiento + estribo + medio diámetro del eje de la
        // huella exterior de la columna -- no va a coincidir exacto con
        // XMin/XMax/YMin/YMax, solo estar cerca de ese borde.
        double tolerancia = UnitUtils.ConvertToInternalUnits(0.10, UnitTypeId.Meters);
        bool enBordeX = Math.Abs(x - xMin) < tolerancia || Math.Abs(x - xMax) < tolerancia;
        bool enBordeY = Math.Abs(y - yMin) < tolerancia || Math.Abs(y - yMax) < tolerancia;

        sb.AppendLine($"Huella de la columna (host {host.Id.Value}): X[{Metros(xMin):0.000}, {Metros(xMax):0.000}] " +
                      $"Y[{Metros(yMin):0.000}, {Metros(yMax):0.000}]. Barra en X={Metros(x):0.000}, Y={Metros(y):0.000}.");

        string tipo = (enBordeX, enBordeY) switch
        {
            (true, true) => "esquina",
            (true, false) => "cara Y",
            (false, true) => "cara X",
            _ => "sin clasificar"
        };
        sb.AppendLine($"Posición clasificada: {tipo}.");
        return tipo;
    }

    private static ElementId BuscarOCrearTipoSplice(Document doc, StringBuilder sb)
    {
        ICollection<ElementId> existentes = RebarSpliceTypeUtils.GetAllRebarSpliceTypes(doc);
        if (existentes.Count > 0)
        {
            ElementId id = existentes.First();
            sb.AppendLine($"RebarSpliceType existente encontrado: \"{doc.GetElement(id)?.Name}\" (id {id.Value}). Se usa ese.");
            return id;
        }

        sb.AppendLine("No hay ningún RebarSpliceType en el documento -- se crea uno de prueba temporal " +
                       "(\"CQ Diagnóstico Splice\", se revierte al final).");
        try
        {
            ElementId nuevo = RebarSpliceTypeUtils.CreateRebarSpliceType(doc, "CQ Diagnóstico Splice").Id;
            RebarSpliceTypeUtils.SetShiftOption(doc, nuevo, RebarSpliceShiftOption.BarPlane);
            return nuevo;
        }
        catch (Exception ex)
        {
            sb.AppendLine($"No se pudo crear un RebarSpliceType: {ex.Message}");
            return ElementId.InvalidElementId;
        }
    }

    private static string Fmt(XYZ p) =>
        $"({Metros(p.X):0.000}, {Metros(p.Y):0.000}, {Metros(p.Z):0.000})";

    private static double Metros(double pies) => UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Meters);

    /// <summary>
    /// SpliceRebar puede devolver una lista vacía en vez de tirar excepción
    /// cuando no puede partir la barra (confirmado sobre Revit real, host
    /// 152865) -- sin ningún detalle en el valor de retorno. Se vuelcan los
    /// avisos/errores acumulados en el documento por si Revit deja ahí la
    /// razón real, en vez de seguir adivinando.
    /// </summary>
    private static void ReportarWarnings(Document doc, StringBuilder sb)
    {
        IList<FailureMessage> avisos = doc.GetWarnings();
        if (avisos.Count == 0)
        {
            sb.AppendLine("(Sin warnings acumulados en el documento en este momento.)");
            return;
        }

        sb.AppendLine($"Warnings acumulados en el documento ({avisos.Count}):");
        foreach (FailureMessage a in avisos)
            sb.AppendLine($"  - {a.GetDescriptionText()}");
    }
}

internal sealed class FiltroSoloRebar : ISelectionFilter
{
    public bool AllowElement(Element elem) => elem is Rebar;
    public bool AllowReference(Reference reference, XYZ position) => true;
}
