namespace practicacq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using practicacq.Logica;
using practicacq.Modelo;

/// <summary>
/// TEMPORAL -- mediciones contra Revit real para el issue #41 (una sola barra
/// de punta a punta). La primera versión (7-oct) confirmó que un
/// <see cref="Rebar"/> hosteado en una columna puede recorrer toda la pila,
/// llevar varios empalmes en una llamada y volver a unirse -- ver
/// docs/planes/2026-10-07-factibilidad-barra-continua-41.md.
///
/// Esta versión mide lo que el diseño todavía supone:
///  1. que el tipo de empalme propio del add-in (<see cref="TiposEmpalme"/>)
///     da el solape exacto del cuadro de la empresa;
///  2. que la pieza corrida de cada empalme se corre en el SENTIDO de la
///     normal de la barra -- cuatro varillas, cada una con una normal distinta
///     apuntando al centro de la sección.
///
/// Las barras de prueba QUEDAN en el modelo, para mirarlas en 3D -- se sacan
/// con un solo Ctrl+Z. Sacar el comando una vez implementado el #41.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ComandoDiagnosticoBarraContinua : IExternalCommand
{
    private const double MargenExtremosMetros = 0.10;
    private const double InsetMetros = 0.07;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        IList<Reference> refs;
        try
        {
            refs = uidoc.Selection.PickObjects(
                ObjectType.Element, new FiltroCualquierElemento(),
                "Selecciona TODAS las columnas apiladas de una pila (y la zapata, si hay) -- Finish (Enter) para terminar.");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }

        var sb = new StringBuilder();

        List<Pieza> pila = refs
            .Select(r => doc.GetElement(r))
            .Where(e => e is not Rebar)
            .Select(e => (Elemento: e, Caja: e.get_BoundingBox(null)))
            .Where(p => p.Caja is not null)
            .Select(p => new Pieza(p.Elemento, p.Caja!))
            .OrderBy(p => p.Caja.Min.Z)
            .ToList();

        if (pila.Count == 0)
        {
            sb.AppendLine("No se seleccionó ningún elemento con geometría.");
            DiagnosticoUI.MostrarTexto(sb.ToString(), "Diagnóstico barra continua (#41)");
            return Result.Failed;
        }

        sb.AppendLine("=== PILA SELECCIONADA (de abajo hacia arriba) ===");
        foreach (Pieza p in pila)
            sb.AppendLine(
                $"  {p.Elemento.Category?.Name ?? "Elemento"} {p.Elemento.Id.Value} ({p.Elemento.Name}): " +
                $"Z[{M(p.Caja.Min.Z):0.###}, {M(p.Caja.Max.Z):0.###}] " +
                $"X[{M(p.Caja.Min.X):0.###}, {M(p.Caja.Max.X):0.###}] Y[{M(p.Caja.Min.Y):0.###}, {M(p.Caja.Max.Y):0.###}]");
        if (pila.Count < 2)
            sb.AppendLine("  OJO: hay un solo elemento -- esta corrida no prueba el cruce entre columnas.");

        // Solo las columnas definen la sección común: la zapata es mucho más
        // ancha y no la achica, pero si fuera lo único seleccionado se usa igual.
        List<Pieza> columnas = pila.Where(p => EsColumna(p.Elemento)).ToList();
        if (columnas.Count == 0) columnas = pila;

        double xMin = columnas.Max(p => p.Caja.Min.X), xMax = columnas.Min(p => p.Caja.Max.X);
        double yMin = columnas.Max(p => p.Caja.Min.Y), yMax = columnas.Min(p => p.Caja.Max.Y);
        double inset = Pies(InsetMetros);
        if (xMax - xMin < 3 * inset || yMax - yMin < 3 * inset)
        {
            sb.AppendLine("Las columnas seleccionadas no comparten una sección común en planta -- no forman una pila.");
            DiagnosticoUI.MostrarTexto(sb.ToString(), "Diagnóstico barra continua (#41)");
            return Result.Failed;
        }

        RebarBarType? barra = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
            .OrderBy(b => Math.Abs(M(b.BarNominalDiameter) - 0.0127))
            .FirstOrDefault();
        if (barra is null)
        {
            sb.AppendLine("El documento no tiene ningún tipo de barra (RebarBarType).");
            DiagnosticoUI.MostrarTexto(sb.ToString(), "Diagnóstico barra continua (#41)");
            return Result.Failed;
        }

        sb.AppendLine($"Barra de prueba: «{barra.Name}» (Ø {M(barra.BarNominalDiameter) * 1000:0.#} mm).");

        double zInicio = pila[0].Caja.Min.Z + Pies(MargenExtremosMetros);
        double zFin = pila[^1].Caja.Max.Z - Pies(MargenExtremosMetros);

        // Un empalme a media altura de cada columna: con dos o más columnas,
        // al menos uno cae fuera del host de la barra.
        List<double> zEmpalmes = columnas
            .Select(p => (p.Caja.Min.Z + p.Caja.Max.Z) / 2)
            .Where(z => z > zInicio + Pies(1.0) && z < zFin - Pies(1.0))
            .ToList();

        // Cuarta medición (8-oct): la primera corrida real del generador dejó
        // varillas sin partir (SpliceRebar devolvió 0 piezas con
        // CanRebarBeSpliced = Success) que la prueba aislada nunca mostró. Lo
        // que el generador hace distinto: ganchos en las dos puntas, arranque
        // dentro de la zapata (por debajo del host) y otras alturas de
        // empalme. Acá se reproduce el caso que falló y se cambia UNA cosa por
        // vez, para ver cuál es la que lo rompe.
        RebarHookType? gancho = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .Where(g => g.Style == RebarStyle.Standard)
            .OrderBy(g => Math.Abs(g.HookAngle - Math.PI / 2))
            .FirstOrDefault();
        sb.AppendLine($"Gancho de prueba: {(gancho is null ? "(ninguno)" : $"«{gancho.Name}»")}.");

        double zEnZapata = pila[0].Caja.Min.Z - Pies(0.275);
        double zTope = pila[^1].Caja.Max.Z;
        double zBajo = columnas.Count > 1 ? columnas[1].Caja.Min.Z + Pies(1.6) : zInicio + Pies(1.6);
        double zAlto = columnas.Count > 2 ? columnas[2].Caja.Min.Z + Pies(1.6) : zBajo;

        var casos = new (string Nombre, bool GanchoAbajo, bool GanchoArriba, double ZInicio, double ZFin, double[] Empalmes)[]
        {
            ("1: IGUAL AL QUE FALLÓ (2 ganchos, desde la zapata, empalme bajo)", true, true, zEnZapata, zTope, new[] { zBajo }),
            ("2: sin ganchos", false, false, zEnZapata, zTope, new[] { zBajo }),
            ("3: solo gancho abajo", true, false, zEnZapata, zTope, new[] { zBajo }),
            ("4: solo gancho arriba", false, true, zEnZapata, zTope, new[] { zBajo }),
            ("5: 2 ganchos, arrancando DENTRO del dado", true, true, zInicio, zTope, new[] { zBajo }),
            ("6: 2 ganchos, desde la zapata, empalme ALTO", true, true, zEnZapata, zTope, new[] { zAlto }),
            ("7: 2 ganchos, desde la zapata, DOS empalmes", true, true, zEnZapata, zTope, new[] { zBajo, zAlto }),
            ("8: 2 ganchos, desde la zapata, tope 10 cm más abajo", true, true, zEnZapata, zFin, new[] { zBajo }),
        };

        var registro = new RegistroDeFallas(sb);
        using (Transaction t = new(doc, "Diagnóstico barra continua (#41)"))
        {
            t.Start();
            FailureHandlingOptions opciones = t.GetFailureHandlingOptions();
            opciones.SetFailuresPreprocessor(registro);
            opciones.SetClearAfterRollback(true);
            t.SetFailureHandlingOptions(opciones);

            try
            {
                Pieza hostAbajo = columnas[0];

                // Ocho posiciones separadas dentro de la zona común, lejos del borde.
                for (int i = 0; i < casos.Length; i++)
                {
                    (string nombre, bool conGanchoAbajo, bool conGanchoArriba, double z0, double z1, double[] empalmes) = casos[i];
                    var punto = new XYZ(
                        xMin + inset + (i % 4) * Pies(0.035),
                        yMin + inset + (i / 4) * Pies(0.05), 0);

                    sb.AppendLine();
                    sb.AppendLine($"=== Caso {nombre} ===");
                    Rebar? creada = CrearYMedir(
                        doc, hostAbajo, barra, punto, XYZ.BasisY, z0, z1, nombre, sb,
                        ganchoAbajo: conGanchoAbajo ? gancho : null, ganchoArriba: conGanchoArriba ? gancho : null);
                    if (creada is null) continue;

                    ElementId idOriginal = creada.Id;
                    List<Rebar> piezas = Empalmar(doc, creada, barra, empalmes.ToList(), pila, sb, XYZ.BasisZ, null);
                    if (piezas.Count == 0 && doc.GetElement(idOriginal) is Rebar intacta && Extremos(intacta) is { } e)
                        sb.AppendLine(
                            $"  NO SE PARTIÓ. La barra original sigue: host {intacta.GetHostId().Value}, {Fmt(e.Abajo)} -> {Fmt(e.Arriba)}.");
                    Rehostear(doc, piezas, columnas, sb);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"EXCEPCIÓN no prevista: {ex.GetType().Name}: {ex.Message}");
            }

            sb.AppendLine();
            sb.AppendLine("=== AVISOS/ERRORES DE REVIT AL CONFIRMAR ===");
            TransactionStatus estado = t.Commit();
            if (registro.Cantidad == 0) sb.AppendLine("  (ninguno)");
            sb.AppendLine($"Estado final de la transacción: {estado}.");
            sb.AppendLine(estado == TransactionStatus.Committed
                ? "Las barras de prueba QUEDARON en el modelo -- se sacan con Ctrl+Z."
                : "Revit revirtió todo: no quedó ninguna barra de prueba en el modelo.");
        }

        DiagnosticoUI.MostrarTexto(sb.ToString(), "Diagnóstico barra continua (#41)");
        return Result.Succeeded;
    }

    private sealed record Pieza(Element Elemento, BoundingBoxXYZ Caja);

    private enum Variante { NormalDeEmpalmeInvertida, BarraInvertida, SinCorrimientoYMover, CorregirElDeRevit }

    /// <summary>
    /// Un tipo de empalme con el mismo solape que el propio del add-in pero SIN
    /// corrimiento automático, para probar ponerlo nosotros.
    /// </summary>
    private static ElementId? TipoSinCorrimiento(Document doc, RebarBarType barra, StringBuilder sb)
    {
        const string nombre = "CQ Diagnóstico sin corrimiento";
        try
        {
            ElementId? id = RebarSpliceTypeUtils.GetAllRebarSpliceTypes(doc).FirstOrDefault(t => doc.GetElement(t)?.Name == nombre)
                ?? RebarSpliceTypeUtils.CreateRebarSpliceType(doc, nombre).Id;
            CuadroVarillas.Varilla? varilla = EstandarCQ.Buscar(barra);
            if (varilla?.Traslapo is { } traslapo)
                RebarSpliceTypeUtils.SetLapLengthMultiplier(doc, id, EstandarCQ.APies(traslapo) / barra.BarNominalDiameter);
            RebarSpliceTypeUtils.SetShiftOption(doc, id, RebarSpliceShiftOption.None);
            return id;
        }
        catch (Exception ex)
        {
            sb.AppendLine($"No se pudo preparar el tipo sin corrimiento: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Mueve a mano las piezas intermedias (una sí, una no, empezando por la
    /// segunda) y mide si Revit las deja donde se pidió y si el empalme sigue
    /// reconocido después.
    /// </summary>
    private static void MoverIntermedias(Document doc, List<Rebar> piezas, XYZ desplazamiento, StringBuilder sb)
    {
        if (piezas.Count < 2) return;

        sb.AppendLine(
            $"Mover a mano las piezas intermedias ({M(desplazamiento.X) * 100:0.##}, {M(desplazamiento.Y) * 100:0.##}) cm en (X, Y):");
        for (int i = 1; i < piezas.Count; i += 2)
        {
            try
            {
                ElementTransformUtils.MoveElement(doc, piezas[i].Id, desplazamiento);
                doc.Regenerate();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  Rebar {piezas[i].Id.Value}: MoveElement tiró {ex.GetType().Name}: {ex.Message}");
                try { doc.Regenerate(); } catch { /* best-effort */ }
            }
        }

        foreach (Rebar pieza in piezas)
        {
            if (!pieza.IsValidObject)
            {
                sb.AppendLine("  Una pieza dejó de existir después de mover.");
                continue;
            }

            (XYZ Abajo, XYZ Arriba)? e = Extremos(pieza);
            string geo = e is null ? "(sin geometría)" : $"{Fmt(e.Value.Abajo)} -> {Fmt(e.Value.Arriba)}";
            sb.AppendLine(
                $"  Rebar {pieza.Id.Value}: {geo} | empalme abajo: {TieneEmpalme(pieza, 0)}, arriba: {TieneEmpalme(pieza, 1)}");
        }

        try
        {
            sb.AppendLine($"  Cadena de empalme después de mover: {RebarSpliceUtils.GetSpliceChain(piezas[0]).Count} barra(s).");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  GetSpliceChain tiró {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool EsColumna(Element e) =>
        e.Category is not null && e.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;

    private static Rebar? CrearYMedir(
        Document doc, Pieza host, RebarBarType barra, XYZ planta, XYZ normal, double zInicio, double zFin, string nombre, StringBuilder sb,
        bool deArribaHaciaAbajo = false, RebarHookType? ganchoAbajo = null, RebarHookType? ganchoArriba = null)
    {
        sb.AppendLine($"--- Barra {nombre} ---");
        sb.AppendLine(
            $"  Pedido: X={M(planta.X):0.###} Y={M(planta.Y):0.###}, Z {M(zInicio):0.###} -> {M(zFin):0.###} " +
            $"({M(zFin - zInicio):0.###} m). El host solo abarca Z[{M(host.Caja.Min.Z):0.###}, {M(host.Caja.Max.Z):0.###}]. " +
            $"Normal ({normal.X:0}, {normal.Y:0}, {normal.Z:0}).");
        try
        {
            XYZ abajo = new XYZ(planta.X, planta.Y, zInicio), arriba = new XYZ(planta.X, planta.Y, zFin);
            Line linea = deArribaHaciaAbajo ? Line.CreateBound(arriba, abajo) : Line.CreateBound(abajo, arriba);
            if (deArribaHaciaAbajo) sb.AppendLine("  La línea se dibuja de ARRIBA hacia ABAJO.");
            using var terminaciones = new BarTerminationsData(doc);
            if (ganchoAbajo is not null)
            {
                terminaciones.HookTypeIdAtStart = ganchoAbajo.Id;
                terminaciones.TerminationOrientationAtStart = RebarTerminationOrientation.Right;
            }

            if (ganchoArriba is not null)
            {
                terminaciones.HookTypeIdAtEnd = ganchoArriba.Id;
                terminaciones.TerminationOrientationAtEnd = RebarTerminationOrientation.Right;
            }

            sb.AppendLine($"  Gancho abajo: {(ganchoAbajo is null ? "no" : "sí")}, arriba: {(ganchoArriba is null ? "no" : "sí")}.");
            Rebar? creada = Rebar.CreateFromCurves(
                doc, RebarStyle.Standard, barra, host.Elemento, normal,
                new List<Curve> { linea }, terminaciones,
                useExistingShapeIfPossible: true, createNewShape: true);

            if (creada is null)
            {
                sb.AppendLine("  RESULTADO: Rebar.CreateFromCurves devolvió null -- Revit NO la creó.");
                return null;
            }

            doc.Regenerate();
            sb.AppendLine($"  RESULTADO: creada, Rebar {creada.Id.Value}, host {creada.GetHostId().Value}.");
            Medir(creada, zInicio, zFin, sb);
            return creada;
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  RESULTADO: excepción {ex.GetType().Name}: {ex.Message}");
            try { doc.Regenerate(); } catch { /* best-effort */ }
            return null;
        }
    }

    private static void Medir(Rebar rebar, double zInicio, double zFin, StringBuilder sb)
    {
        (XYZ Abajo, XYZ Arriba)? extremos = Extremos(rebar);
        if (extremos is null)
        {
            sb.AppendLine("  No se pudo leer la geometría real de la barra.");
            return;
        }

        (XYZ abajo, XYZ arriba) = extremos.Value;
        bool recortada = Math.Abs(abajo.Z - zInicio) > Pies(0.005) || Math.Abs(arriba.Z - zFin) > Pies(0.005);
        sb.AppendLine(
            $"  Geometría real: {Fmt(abajo)} -> {Fmt(arriba)} ({M(arriba.Z - abajo.Z):0.###} m). " +
            (recortada ? "RECORTADA respecto de lo pedido." : "COMPLETA, igual a lo pedido."));
    }

    private static List<Rebar> Empalmar(
        Document doc, Rebar rebar, RebarBarType barra, List<double> zEmpalmes, List<Pieza> pila, StringBuilder sb,
        XYZ normalDeEmpalme, ElementId? tipoForzado)
    {
        var piezas = new List<Rebar>();
        if (zEmpalmes.Count == 0)
        {
            sb.AppendLine("No hay ninguna altura de empalme válida (columnas demasiado cortas).");
            return piezas;
        }

        TiposEmpalme.Resultado tipo = new TiposEmpalme(doc).Resolver(barra, null);
        if (tipo.Aviso is not null) sb.AppendLine($"Aviso del tipo de empalme: {tipo.Aviso}");
        if (tipo.Tipo is null)
        {
            sb.AppendLine("No hay tipo de empalme -- no se puede probar.");
            return piezas;
        }

        ElementId tipoId = tipoForzado ?? tipo.Tipo;
        sb.AppendLine($"Normal de la cara de empalme: ({normalDeEmpalme.X:0}, {normalDeEmpalme.Y:0}, {normalDeEmpalme.Z:0}).");
        CuadroVarillas.Varilla? varilla = EstandarCQ.Buscar(barra);
        sb.AppendLine(
            $"Tipo de empalme: «{doc.GetElement(tipoId)?.Name}» (id {tipoId.Value}), multiplicador " +
            $"{RebarSpliceTypeUtils.GetLapLengthMultiplier(doc, tipoId):0.###}. Solape esperado por el cuadro: " +
            $"{(varilla?.Traslapo is { } esperado ? $"{esperado:0.###} m" : "(el cuadro no da traslapo para esta barra)")}.");

        (XYZ Abajo, XYZ Arriba)? extremos = Extremos(rebar);
        if (extremos is null)
        {
            sb.AppendLine("No se pudo leer la geometría de la barra.");
            return piezas;
        }

        ElementId hostId = rebar.GetHostId();
        var opciones = new RebarSpliceOptions(doc, tipoId, RebarSplicePosition.Middle);
        var geometrias = new List<RebarSpliceGeometry>();
        foreach (double z in zEmpalmes)
        {
            // X/Y leídos de la barra ya creada, no los pedidos: el punto tiene
            // que tocar la barra exacto (aviso del Dynamo de referencia).
            var punto = new XYZ(extremos.Value.Abajo.X, extremos.Value.Abajo.Y, z);
            var geometria = new RebarSpliceGeometry(punto, normalDeEmpalme);
            Pieza? donde = pila.FirstOrDefault(p => z >= p.Caja.Min.Z && z <= p.Caja.Max.Z && EsColumna(p.Elemento));
            string lugar = donde is null ? "fuera de toda columna"
                : donde.Elemento.Id == hostId ? $"dentro del HOST ({hostId.Value})"
                : $"dentro de OTRA columna ({donde.Elemento.Id.Value}), fuera del host";

            string puede;
            try { puede = RebarSpliceUtils.CanRebarBeSpliced(rebar, opciones, geometria).ToString(); }
            catch (Exception ex) { puede = $"excepción {ex.GetType().Name}: {ex.Message}"; }

            sb.AppendLine($"  Empalme pedido en Z={M(z):0.###} ({lugar}): CanRebarBeSpliced = {puede}");
            geometrias.Add(geometria);
        }

        try
        {
            IList<ElementId> resultantes = RebarSpliceUtils.SpliceRebar(doc, rebar.Id, opciones, geometrias);
            doc.Regenerate();
            sb.AppendLine(
                $"SpliceRebar con {geometrias.Count} empalme(s) en UNA llamada devolvió {resultantes.Count} barra(s) " +
                $"(se esperaban {geometrias.Count + 1}).");

            piezas = resultantes.Select(id => doc.GetElement(id) as Rebar).Where(r => r is not null).Select(r => r!)
                .OrderBy(r => Extremos(r)?.Abajo.Z ?? 0).ToList();
            foreach (Rebar pieza in piezas)
            {
                (XYZ Abajo, XYZ Arriba)? e = Extremos(pieza);
                string geo = e is null ? "(sin geometría)" : $"{Fmt(e.Value.Abajo)} -> {Fmt(e.Value.Arriba)}";
                sb.AppendLine(
                    $"  Rebar {pieza.Id.Value} (host {pieza.GetHostId().Value}): {geo} | " +
                    $"empalme abajo: {TieneEmpalme(pieza, 0)}, arriba: {TieneEmpalme(pieza, 1)}");
            }

            for (int i = 1; i < piezas.Count; i++)
            {
                if (Extremos(piezas[i - 1]) is not { } abajo || Extremos(piezas[i]) is not { } arriba) continue;
                sb.AppendLine(
                    $"  Empalme {i}: solape medido {M(abajo.Arriba.Z - arriba.Abajo.Z):0.###} m; la pieza de arriba quedó corrida " +
                    $"({M(arriba.Abajo.X - abajo.Abajo.X) * 100:0.##}, {M(arriba.Abajo.Y - abajo.Abajo.Y) * 100:0.##}) cm en (X, Y) respecto de la de abajo.");
            }

            if (piezas.Count > 0)
                sb.AppendLine($"  Cadena de empalme reconocida por Revit: {RebarSpliceUtils.GetSpliceChain(piezas[0]).Count} barra(s).");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"SpliceRebar tiró {ex.GetType().Name}: {ex.Message}");
            try { doc.Regenerate(); } catch { /* best-effort */ }
        }

        return piezas;
    }

    /// <summary>
    /// Después de partir, Revit dejó la pieza de más arriba hosteada en una
    /// columna que no la contiene ("Rebar is placed completely outside of its
    /// host", corrida del 7-oct). Se prueba pasar cada pieza a la columna que
    /// contiene su punto medio y ver si el empalme sigue reconocido.
    /// </summary>
    private static void Rehostear(Document doc, List<Rebar> piezas, List<Pieza> columnas, StringBuilder sb)
    {
        if (piezas.Count == 0) return;

        sb.AppendLine("Cambio de host de cada pieza a la columna que la contiene:");
        foreach (Rebar pieza in piezas)
        {
            if (Extremos(pieza) is not { } e) continue;

            double zMedio = (e.Abajo.Z + e.Arriba.Z) / 2;
            Pieza? destino = columnas.FirstOrDefault(c => zMedio >= c.Caja.Min.Z && zMedio <= c.Caja.Max.Z);
            if (destino is null || destino.Elemento.Id == pieza.GetHostId()) continue;

            try
            {
                long antes = pieza.GetHostId().Value;
                pieza.SetHostId(doc, destino.Elemento.Id);
                doc.Regenerate();
                (XYZ Abajo, XYZ Arriba)? d = Extremos(pieza);
                string geo = d is null ? "(sin geometría)" : $"{Fmt(d.Value.Abajo)} -> {Fmt(d.Value.Arriba)}";
                sb.AppendLine(
                    $"  Rebar {pieza.Id.Value}: host {antes} -> {pieza.GetHostId().Value}. Geometría después: {geo} | " +
                    $"empalme abajo: {TieneEmpalme(pieza, 0)}, arriba: {TieneEmpalme(pieza, 1)}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  Rebar {pieza.Id.Value}: SetHostId tiró {ex.GetType().Name}: {ex.Message}");
                try { doc.Regenerate(); } catch { /* best-effort */ }
            }
        }

        try
        {
            sb.AppendLine($"  Cadena de empalme después del cambio de host: {RebarSpliceUtils.GetSpliceChain(piezas[0]).Count} barra(s).");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  GetSpliceChain tiró {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string TieneEmpalme(Rebar rebar, int extremo)
    {
        try { return rebar.GetRebarSplice(extremo) is null ? "no" : "sí"; }
        catch { return "no"; }
    }

    /// <summary>Extremos inferior y superior de la línea central, sin ganchos ni radios.</summary>
    private static (XYZ Abajo, XYZ Arriba)? Extremos(Rebar rebar)
    {
        IList<Curve> curvas = rebar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
        if (curvas.Count == 0) return null;

        List<XYZ> puntos = curvas.SelectMany(c => new[] { c.GetEndPoint(0), c.GetEndPoint(1) }).OrderBy(p => p.Z).ToList();
        return (puntos[0], puntos[^1]);
    }

    private static string Fmt(XYZ p) => $"({M(p.X):0.###}, {M(p.Y):0.###}, {M(p.Z):0.###})";

    private static double M(double pies) => UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Meters);

    private static double Pies(double metros) => UnitUtils.ConvertToInternalUnits(metros, UnitTypeId.Meters);

    /// <summary>
    /// Anota en el reporte cada aviso o error que Revit levanta al confirmar,
    /// y descarta los avisos para que no abran un diálogo a mitad de la prueba.
    /// </summary>
    private sealed class RegistroDeFallas(StringBuilder sb) : IFailuresPreprocessor
    {
        public int Cantidad { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            bool hayError = false;
            foreach (FailureMessageAccessor falla in failuresAccessor.GetFailureMessages())
            {
                Cantidad++;
                FailureSeverity severidad = falla.GetSeverity();
                string ids = string.Join(", ", falla.GetFailingElementIds().Select(id => id.Value));
                sb.AppendLine($"  [{severidad}] {falla.GetDescriptionText()} (elementos: {ids})");

                if (severidad == FailureSeverity.Warning)
                    failuresAccessor.DeleteWarning(falla);
                else
                    hayError = true;
            }

            return hayError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
