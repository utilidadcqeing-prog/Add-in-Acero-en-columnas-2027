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
/// TEMPORAL -- mediciones contra Revit real para el issue #64 (formas y
/// ganchos propios del add-in para estribos, cajas y grapas), con las medidas
/// del cuadro "Dimensiones mínimas para ganchos estándar" (issue #65).
///
/// Responde, antes de diseñar nada:
///  1. si un gancho creado de cero con <c>RebarHookType.Create</c> admite que
///     se le fije el estilo de estribo (el 10-sep uno así salió con otro
///     estilo y Revit rechazó la armadura entera);
///  2. si una forma de estribo creada por código (<c>RebarShape.Create</c>)
///     funciona con <c>CreateFromRebarShape</c> + <c>ScaleToBox</c>, también
///     en cajas angostas, y cómo sale comparada con la forma del modelo;
///  3. si una forma de grapa propia funciona, y si el largo del gancho de 180°
///     se puede fijar barra por barra (el cuadro pide un mínimo de 6,5 cm, que
///     no es un múltiplo fijo del diámetro);
///  4. qué diámetro de doblado tienen las barras del modelo frente al cuadro;
///  5. de dónde salen los parámetros compartidos que una forma necesita, y si
///     se pueden crear en un modelo que no tenga ninguno.
///
/// Al terminar pregunta si se dejan las barras de prueba para mirarlas; por
/// defecto se deshace todo. Sacar el comando una vez implementado el #64.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ComandoDiagnosticoFormasPropias : IExternalCommand
{
    private const string Titulo = "Diagnóstico formas y ganchos propios (#64)";
    private const string Prefijo = "CQ Diag ";
    private const double InsetMetros = 0.05;
    private const double PasoZMetros = 0.10;

    // Reglas del cuadro "Dimensiones mínimas para ganchos estándar":
    // estribo de 135° = extensión 6 diámetros, doblado 4 diámetros;
    // 180° = extensión 4 diámetros con mínimo de 6,5 cm, doblado 6 diámetros.
    private const double MultiplicadorEstribo135 = 6;
    private const double Multiplicador180 = 4;
    private const double MinimoExtension180Metros = 0.065;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        Reference r;
        try
        {
            r = uidoc.Selection.PickObject(
                ObjectType.Element, new FiltroCualquierElemento(),
                "Selecciona UNA columna rectangular (mejor si no tiene armadura todavía).");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }

        var sb = new StringBuilder();
        Element host = doc.GetElement(r);
        BoundingBoxXYZ? caja = host.get_BoundingBox(null);
        if (caja is null)
        {
            sb.AppendLine("El elemento seleccionado no tiene geometría.");
            DiagnosticoUI.MostrarTexto(sb.ToString(), Titulo);
            return Result.Failed;
        }

        double inset = Pies(InsetMetros);
        double x1 = caja.Min.X + inset, x2 = caja.Max.X - inset;
        double y1 = caja.Min.Y + inset, y2 = caja.Max.Y - inset;
        double ancho = x2 - x1, alto = y2 - y1;
        sb.AppendLine($"Host: {host.Category?.Name} {host.Id.Value} ({host.Name}).");
        sb.AppendLine($"Rectángulo de prueba (sección menos {InsetMetros * 100:0} cm por lado): {M(ancho) * 100:0.#} × {M(alto) * 100:0.#} cm.");
        if (ancho < Pies(0.15) || alto < Pies(0.15))
        {
            sb.AppendLine("La sección es demasiado chica para la prueba.");
            DiagnosticoUI.MostrarTexto(sb.ToString(), Titulo);
            return Result.Failed;
        }

        List<(int Numero, RebarBarType Tipo)> barras = BarrasDelCuadro(doc);
        Inventario(doc, barras, sb);

        List<RebarBarType> barrasDePrueba = barras.Where(b => b.Numero is 3 or 2).OrderByDescending(b => b.Numero)
            .Select(b => b.Tipo).ToList();
        if (barrasDePrueba.Count == 0)
        {
            RebarBarType? cualquiera = new FilteredElementCollector(doc)
                .OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                .OrderBy(b => Math.Abs(M(b.BarNominalDiameter) - 0.0095)).FirstOrDefault();
            if (cualquiera is not null) barrasDePrueba.Add(cualquiera);
        }

        if (barrasDePrueba.Count == 0)
        {
            sb.AppendLine("El documento no tiene ningún tipo de barra (RebarBarType).");
            DiagnosticoUI.MostrarTexto(sb.ToString(), Titulo);
            return Result.Failed;
        }

        using var grupo = new TransactionGroup(doc, Titulo);
        grupo.Start();

        RebarHookType? ganchoEstribo = null, ganchoGrapa = null;
        RebarShape? formaEstribo = null, formaGrapa = null;

        sb.AppendLine();
        sb.AppendLine("=== 1. GANCHOS CREADOS DE CERO (RebarHookType.Create) ===");
        EnTransaccion(doc, "Diagnóstico #64: ganchos", sb, () =>
        {
            ganchoEstribo = CrearGancho(doc, Prefijo + "Estribo 135", 135, MultiplicadorEstribo135, RebarStyle.StirrupTie, sb);
            ganchoGrapa = CrearGancho(doc, Prefijo + "Grapa 180", 180, Multiplicador180, RebarStyle.Standard, sb);
        });
        if (ganchoEstribo is { IsValidObject: false }) ganchoEstribo = null;
        if (ganchoGrapa is { IsValidObject: false }) ganchoGrapa = null;

        sb.AppendLine();
        sb.AppendLine("=== 2. LARGO DE GANCHO POR BARRA (lo que calcula Revit vs. el cuadro) ===");
        EnTransaccion(doc, "Diagnóstico #64: largos de gancho", sb, () =>
        {
            foreach ((int numero, RebarBarType tipo) in barras)
            {
                double d = tipo.BarNominalDiameter;
                if (ganchoEstribo is not null)
                {
                    sb.AppendLine($"  #{numero} «{tipo.Name}» con el gancho de estribo 135°:");
                    LeerGancho(tipo, ganchoEstribo, "    ", sb);
                    CuadroVarillas.Varilla? v = EstandarCQ.Buscar(tipo);
                    sb.AppendLine(
                        $"    cuadro: E = {M(MultiplicadorEstribo135 * d) * 100:0.0} cm (6 diámetros), B = {v?.GanchoEstribo * 100:0.0} cm");
                }

                if (ganchoGrapa is null) continue;

                double objetivo = Math.Max(Multiplicador180 * d, Pies(MinimoExtension180Metros));
                sb.AppendLine($"  #{numero} «{tipo.Name}» con el gancho de grapa 180° -- objetivo del cuadro E = {M(objetivo) * 100:0.0} cm:");
                sb.AppendLine("    ANTES de tocar nada:");
                LeerGancho(tipo, ganchoGrapa, "      ", sb);
                try
                {
                    double extension = ganchoGrapa.GetHookExtensionLength(tipo);
                    double largo = tipo.GetHookLength(ganchoGrapa.Id);
                    tipo.SetAutoCalcHookLengths(ganchoGrapa.Id, false);
                    tipo.SetHookLength(ganchoGrapa.Id, largo + (objetivo - extension));
                    doc.Regenerate();
                    sb.AppendLine("    DESPUÉS de apagar el cálculo automático y fijar el largo a mano:");
                    LeerGancho(tipo, ganchoGrapa, "      ", sb);
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"    No se pudo fijar el largo a mano: {ex.GetType().Name}: {ex.Message}");
                }
            }
        });

        sb.AppendLine();
        sb.AppendLine("=== 3. PARÁMETROS COMPARTIDOS PARA LAS FORMAS ===");
        var parametros = new List<ElementId>();
        EnTransaccion(doc, "Diagnóstico #64: parámetros", sb, () =>
        {
            parametros = ParametrosDeForma(doc, commandData.Application.Application, sb);
        });

        sb.AppendLine();
        sb.AppendLine("=== 4. FORMAS CREADAS DE CERO (RebarShape.Create) ===");
        if (parametros.Count < 4)
        {
            sb.AppendLine("  No hay 4 parámetros de forma disponibles: no se pueden crear las formas.");
        }
        else
        {
            foreach (bool porFuera in new[] { true, false })
            {
                if (formaEstribo is not null) break;
                EnTransaccion(doc, "Diagnóstico #64: forma de estribo", sb, () =>
                    formaEstribo = CrearFormaEstribo(doc, parametros, porFuera, sb));
                if (formaEstribo is { IsValidObject: false }) formaEstribo = null;
            }

            EnTransaccion(doc, "Diagnóstico #64: forma de grapa", sb, () =>
                formaGrapa = CrearFormaGrapa(doc, parametros[0], sb));
            if (formaGrapa is { IsValidObject: false }) formaGrapa = null;
        }

        RebarShape? formaEstriboModelo = GeometriaSeccion.BuscarFormaEstribo(doc);
        RebarHookType? ganchoEstriboModelo = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .Where(g => g.Style == RebarStyle.StirrupTie && !g.Name.StartsWith(Prefijo))
            .OrderBy(g => Math.Abs(g.HookAngle - 3 * Math.PI / 4)).FirstOrDefault();
        RebarShape? formaGrapaModelo = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarShape)).Cast<RebarShape>()
            .FirstOrDefault(f => !f.Name.StartsWith(Prefijo) && f.RebarStyle == RebarStyle.Standard && Segmentos(f) == 1);
        RebarHookType? ganchoGrapaModelo = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .FirstOrDefault(g => !g.Name.StartsWith(Prefijo) && g.Style == RebarStyle.Standard &&
                                 Math.Abs(g.HookAngle - Math.PI) < 1e-3);

        var juegosLazo = new (string Nombre, RebarShape? Forma, RebarHookType? Gancho)[]
        {
            ("PROPIOS", formaEstribo, ganchoEstribo),
            ("DEL MODELO (lo que se usa hoy)", formaEstriboModelo, ganchoEstriboModelo),
        };

        // Lazos: el estribo de toda la sección y tres cajas cada vez más
        // angostas -- las cajas son las que hoy salen deformes.
        var lazos = new (string Nombre, double Ancho, double Alto)[]
        {
            ("estribo de toda la sección", ancho, alto),
            ("caja de 15 cm de ancho", Pies(0.15), alto),
            ("caja de 10 cm de ancho", Pies(0.10), alto),
            ("caja de 12 × 12 cm", Pies(0.12), Pies(0.12)),
        };

        double z = caja.Min.Z + Pies(0.50);
        double paso = Pies(PasoZMetros);

        sb.AppendLine();
        sb.AppendLine("=== 5. ESTRIBOS Y CAJAS (CreateFromRebarShape + ScaleToBox + gancho) ===");
        foreach (RebarBarType barra in barrasDePrueba)
        foreach ((string nombreJuego, RebarShape? forma, RebarHookType? gancho) in juegosLazo)
        foreach ((string nombreLazo, double w, double h) in lazos)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"--- {nombreJuego} | {nombreLazo} ({M(w) * 100:0.#} × {M(h) * 100:0.#} cm) | barra «{barra.Name}» | " +
                $"forma «{forma?.Name ?? "(no hay)"}» | gancho «{gancho?.Name ?? "(no hay)"}» | Z = {M(z):0.00} m ---");
            if (forma is null)
            {
                sb.AppendLine("  Sin forma: no se prueba.");
                continue;
            }

            double zEste = z;
            z += paso;
            EnTransaccion(doc, "Diagnóstico #64: lazo", sb, () =>
                CrearLazo(doc, host, barra, forma, gancho, x1, y1, w, h, zEste, sb));
        }

        var juegosGrapa = new (string Nombre, RebarShape? Forma, RebarHookType? Gancho)[]
        {
            ("PROPIOS", formaGrapa, ganchoGrapa),
            ("DEL MODELO (lo que se usa hoy)", formaGrapaModelo, ganchoGrapaModelo),
        };

        sb.AppendLine();
        sb.AppendLine("=== 6. GRAPAS (CreateFromCurvesAndShape, un tramo recto con gancho en las dos puntas) ===");
        foreach (RebarBarType barra in barrasDePrueba)
        foreach ((string nombreJuego, RebarShape? forma, RebarHookType? gancho) in juegosGrapa)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"--- {nombreJuego} | grapa de {M(ancho) * 100:0.#} cm | barra «{barra.Name}» | " +
                $"forma «{forma?.Name ?? "(no hay)"}» | gancho «{gancho?.Name ?? "(no hay)"}» | Z = {M(z):0.00} m ---");
            if (forma is null || gancho is null)
            {
                sb.AppendLine("  Falta la forma o el gancho: no se prueba.");
                continue;
            }

            double zEste = z;
            z += paso;
            EnTransaccion(doc, "Diagnóstico #64: grapa", sb, () =>
                CrearGrapa(doc, host, barra, forma, gancho, new XYZ(x1, (y1 + y2) / 2, zEste), new XYZ(x2, (y1 + y2) / 2, zEste), sb));
        }

        DiagnosticoUI.MostrarTexto(sb.ToString(), Titulo);

        TaskDialogResult respuesta = TaskDialog.Show(
            Titulo,
            "¿Dejar en el modelo las barras, formas y ganchos de prueba para mirarlos en 3D?\n\n" +
            "Sí: quedan en el modelo y se sacan todos juntos con un solo Ctrl+Z.\n" +
            "No: se deshace todo ahora y el modelo queda como estaba.",
            TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            TaskDialogResult.No);

        if (respuesta == TaskDialogResult.Yes) grupo.Assimilate();
        else grupo.RollBack();

        return Result.Succeeded;
    }

    private static List<(int Numero, RebarBarType Tipo)> BarrasDelCuadro(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
            .Select(t => (Varilla: EstandarCQ.Buscar(t), Tipo: t))
            .Where(p => p.Varilla is not null)
            .GroupBy(p => p.Varilla!.Numero)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.OrderBy(p => p.Tipo.Name).First().Tipo))
            .ToList();

    private static void Inventario(Document doc, List<(int Numero, RebarBarType Tipo)> barras, StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("=== 0. LO QUE TIENE EL MODELO ===");
        try
        {
            ReinforcementSettings ajustes = ReinforcementSettings.GetReinforcementSettings(doc);
            sb.AppendLine($"  ¿La forma define los ganchos? (RebarShapeDefinesHooks) = {ajustes.RebarShapeDefinesHooks}");
        }
        catch (Exception ex) { sb.AppendLine($"  No se pudo leer ReinforcementSettings: {ex.Message}"); }

        int formas = new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).GetElementCount();
        int ganchos = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).GetElementCount();
        sb.AppendLine($"  Formas (RebarShape): {formas}. Tipos de gancho (RebarHookType): {ganchos}.");

        sb.AppendLine("  Diámetro de doblado de cada barra del cuadro (en diámetros de barra):");
        sb.AppendLine("    el cuadro pide 4 para estribos de 135° y 6 para ganchos de 90° y 180°.");
        foreach ((int numero, RebarBarType t) in barras)
        {
            double d = t.BarNominalDiameter;
            sb.AppendLine(
                $"    #{numero} «{t.Name}» Ø {M(d) * 1000:0.#} mm | estribo (StirrupTieBendDiameter) = {t.StirrupTieBendDiameter / d:0.##} | " +
                $"gancho estándar (StandardHookBendDiameter) = {t.StandardHookBendDiameter / d:0.##} | " +
                $"doblez estándar (StandardBendDiameter) = {t.StandardBendDiameter / d:0.##}");
        }
    }

    private static RebarHookType? CrearGancho(
        Document doc, string nombre, double grados, double multiplicador, RebarStyle estilo, StringBuilder sb)
    {
        string paso = "RebarHookType.Create";
        try
        {
            RebarHookType gancho = RebarHookType.Create(doc, grados * Math.PI / 180, multiplicador);
            sb.AppendLine($"  Creado «{gancho.Name}»: {grados}°, multiplicador {multiplicador}, estilo de fábrica = {gancho.Style}.");

            paso = "cambiarle el nombre";
            gancho.Name = nombre;

            if (gancho.Style != estilo)
            {
                paso = $"fijarle el estilo {estilo}";
                gancho.Style = estilo;
            }

            doc.Regenerate();
            sb.AppendLine(
                $"    Quedó «{gancho.Name}»: estilo = {gancho.Style}, ángulo = {gancho.HookAngle * 180 / Math.PI:0.#}°, " +
                $"multiplicador = {gancho.StraightLineMultiplier:0.##}.");
            return gancho;
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  FALLÓ al {paso} («{nombre}»): {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void LeerGancho(RebarBarType barra, RebarHookType gancho, string sangria, StringBuilder sb)
    {
        try
        {
            sb.AppendLine(
                $"{sangria}permitido = {barra.GetHookPermission(gancho.Id)} | automático = {barra.GetAutoCalcHookLengths(gancho.Id)} | " +
                $"largo (GetHookLength) = {M(barra.GetHookLength(gancho.Id)) * 100:0.00} cm | " +
                $"tangente (GetHookTangentLength) = {M(barra.GetHookTangentLength(gancho.Id)) * 100:0.00} cm | " +
                $"extensión recta (GetHookExtensionLength) = {M(gancho.GetHookExtensionLength(barra)) * 100:0.00} cm");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"{sangria}no se pudo leer: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Una forma necesita parámetros COMPARTIDOS para sus medidas. Se reusan
    /// los que ya tengan las formas del modelo (A, B, C, D si existen) y,
    /// aparte, se prueba crear uno nuevo desde un archivo temporal de
    /// parámetros compartidos -- que es lo que haría falta en un modelo sin
    /// ninguna forma cargada.
    /// </summary>
    private static List<ElementId> ParametrosDeForma(
        Document doc, Autodesk.Revit.ApplicationServices.Application app, StringBuilder sb)
    {
        var porNombre = new Dictionary<string, ElementId>();
        foreach (ElementId id in RebarShapeParameters.GetAllRebarShapeParameters(doc))
        {
            string? nombre = doc.GetElement(id)?.Name;
            if (!string.IsNullOrEmpty(nombre)) porNombre.TryAdd(nombre, id);
        }

        sb.AppendLine($"  Parámetros de forma que ya hay en el modelo ({porNombre.Count}): {string.Join(", ", porNombre.Keys.OrderBy(n => n))}");

        var elegidos = new List<ElementId>();
        foreach (string nombre in new[] { "A", "B", "C", "D" })
            if (porNombre.TryGetValue(nombre, out ElementId? id)) elegidos.Add(id);
        if (elegidos.Count < 4)
            elegidos = porNombre.OrderBy(p => p.Key).Select(p => p.Value).Take(4).ToList();

        sb.AppendLine("  Prueba de crear parámetros NUEVOS desde un archivo temporal (para un modelo sin formas):");
        var nuevos = new List<ElementId>();
        string anterior = app.SharedParametersFilename ?? "";
        string ruta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cq_diagnostico_parametros.txt");
        try
        {
            System.IO.File.WriteAllText(ruta, "");
            app.SharedParametersFilename = ruta;
            DefinitionFile archivo = app.OpenSharedParameterFile();
            DefinitionGroup grupo = archivo.Groups.Create("CQEING");
            foreach (string nombre in new[] { "CQ Diag A", "CQ Diag B", "CQ Diag C", "CQ Diag D" })
            {
                var opciones = new ExternalDefinitionCreationOptions(nombre, SpecTypeId.ReinforcementLength);
                var definicion = (ExternalDefinition)grupo.Definitions.Create(opciones);
                ElementId id = RebarShapeParameters.GetOrCreateElementIdForExternalDefinition(doc, definicion);
                nuevos.Add(id);
                sb.AppendLine($"    «{nombre}» creado: id {id.Value}, ¿válido para formas? {RebarShapeParameters.IsValidExternalDefinition(definicion)}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"    FALLÓ: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { app.SharedParametersFilename = anterior; }
            catch (Exception ex) { sb.AppendLine($"    OJO: no se pudo restaurar el archivo de parámetros compartidos («{anterior}»): {ex.Message}"); }
            try { System.IO.File.Delete(ruta); } catch { /* best-effort */ }
        }

        // Los nuevos tienen prioridad: si con ellos las formas salen bien, el
        // add-in no depende de lo que traiga el modelo.
        if (nuevos.Count == 4)
        {
            sb.AppendLine("  Las formas de prueba se arman con los parámetros NUEVOS.");
            return nuevos;
        }

        sb.AppendLine(elegidos.Count == 4
            ? "  Las formas de prueba se arman con parámetros que ya tenía el modelo."
            : "  No se consiguieron 4 parámetros.");
        return elegidos;
    }

    /// <summary>
    /// Rectángulo cerrado de cuatro lados recorrido en sentido antihorario,
    /// con gancho de 135° en las dos puntas (que se encuentran en la esquina
    /// de arranque).
    /// </summary>
    private static RebarShape? CrearFormaEstribo(Document doc, List<ElementId> p, bool porFuera, StringBuilder sb)
    {
        string paso = "crear la definición";
        sb.AppendLine($"  Estribo rectangular, midiendo cada lado {(porFuera ? "por FUERA del doblez" : "donde EMPIEZA el doblez")}:");
        try
        {
            var definicion = new RebarShapeDefinitionBySegments(doc, 4);
            double[] porDefecto = { Pies(0.30), Pies(0.40), Pies(0.30), Pies(0.40) };
            (double X, double Y)[] direcciones = { (1, 0), (0, 1), (-1, 0), (0, -1) };

            paso = "agregar los parámetros";
            for (int i = 0; i < 4; i++) definicion.AddParameter(p[i], porDefecto[i]);

            for (int i = 0; i < 4; i++)
            {
                paso = $"fijar la dirección del lado {i}";
                definicion.SetSegmentFixedDirection(i, direcciones[i].X, direcciones[i].Y);
                paso = $"acotar el lado {i}";
                definicion.AddConstraintParallelToSegment(i, p[i], porFuera, porFuera);
            }

            for (int v = 1; v <= 3; v++)
            {
                paso = $"agregar el doblez {v}";
                definicion.AddBendDefaultRadius(v, RebarShapeVertexTurn.Left, RebarShapeBendAngle.Right);
            }

            paso = "comprobar la definición";
            sb.AppendLine(
                $"    ¿definición completa? {definicion.Complete} | ¿se resuelve con los valores por defecto? " +
                $"{definicion.CheckDefaultParameterValues(0, 0)}");

            paso = "RebarShape.Create";
            using var terminaciones = new RebarShapeTerminationsData(doc)
            {
                HookAngleAtStart = 135,
                HookAngleAtEnd = 135,
                TerminationOrientationAtStart = RebarTerminationOrientation.Left,
                TerminationOrientationAtEnd = RebarTerminationOrientation.Left,
            };
            RebarShape forma = RebarShape.Create(
                doc, definicion, null, RebarStyle.StirrupTie, StirrupTieAttachmentType.InteriorFace, 0, terminaciones);

            paso = "cambiarle el nombre";
            forma.Name = Prefijo + "Estribo";
            doc.Regenerate();
            sb.AppendLine($"    Creada «{forma.Name}» (id {forma.Id.Value}), estilo {forma.RebarStyle}.");
            return forma;
        }
        catch (Exception ex)
        {
            sb.AppendLine($"    FALLÓ al {paso}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static RebarShape? CrearFormaGrapa(Document doc, ElementId parametro, StringBuilder sb)
    {
        string paso = "crear la definición";
        sb.AppendLine("  Grapa: un solo tramo recto con gancho de 180° en las dos puntas:");
        try
        {
            var definicion = new RebarShapeDefinitionBySegments(doc, 1);
            paso = "agregar el parámetro";
            definicion.AddParameter(parametro, Pies(0.30));
            paso = "fijar la dirección";
            definicion.SetSegmentFixedDirection(0, 1, 0);
            paso = "acotar el tramo";
            definicion.AddConstraintParallelToSegment(0, parametro, false, false);

            paso = "comprobar la definición";
            sb.AppendLine(
                $"    ¿definición completa? {definicion.Complete} | ¿se resuelve con los valores por defecto? " +
                $"{definicion.CheckDefaultParameterValues(0, 0)}");

            paso = "RebarShape.Create";
            using var terminaciones = new RebarShapeTerminationsData(doc)
            {
                HookAngleAtStart = 180,
                HookAngleAtEnd = 180,
            };
            RebarShape forma = RebarShape.Create(
                doc, definicion, null, RebarStyle.Standard, StirrupTieAttachmentType.InteriorFace, 0, terminaciones);

            paso = "cambiarle el nombre";
            forma.Name = Prefijo + "Grapa";
            doc.Regenerate();
            sb.AppendLine($"    Creada «{forma.Name}» (id {forma.Id.Value}), estilo {forma.RebarStyle}.");
            return forma;
        }
        catch (Exception ex)
        {
            sb.AppendLine($"    FALLÓ al {paso}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Mismos pasos que <c>GeneradorLongitudinal.CrearCajaDesdeForma</c>, midiendo después de cada uno.</summary>
    private static void CrearLazo(
        Document doc, Element host, RebarBarType barra, RebarShape forma, RebarHookType? gancho,
        double x, double y, double ancho, double alto, double z, StringBuilder sb)
    {
        XYZ origen = new(x, y, z);
        string paso = "CreateFromRebarShape";
        Rebar? lazo = null;
        try
        {
            lazo = Rebar.CreateFromRebarShape(doc, forma, barra, host, origen, XYZ.BasisX, XYZ.BasisY);
            if (lazo is null)
            {
                sb.AppendLine("  CreateFromRebarShape devolvió null.");
                return;
            }

            paso = "ScaleToBox";
            lazo.GetShapeDrivenAccessor().ScaleToBox(origen, new XYZ(ancho, 0, 0), new XYZ(0, alto, 0));

            if (gancho is not null)
            {
                paso = "SetHookTypeId";
                lazo.SetHookTypeId(0, gancho.Id);
                lazo.SetHookTypeId(1, gancho.Id);
                paso = "SetTerminationOrientation";
                lazo.SetTerminationOrientation(0, RebarTerminationOrientation.Left);
                lazo.SetTerminationOrientation(1, RebarTerminationOrientation.Left);
            }

            paso = "Regenerate";
            doc.Regenerate();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  FALLÓ en {paso}: {ex.GetType().Name}: {ex.Message}");
            if (lazo is null || !lazo.IsValidObject) return;
            try { doc.Regenerate(); } catch { /* best-effort */ }
        }

        Medir(doc, lazo, barra, forma, gancho, new XYZ(x, y, z), new XYZ(x + ancho, y + alto, z), sb);
    }

    private static void CrearGrapa(
        Document doc, Element host, RebarBarType barra, RebarShape forma, RebarHookType gancho,
        XYZ desde, XYZ hasta, StringBuilder sb)
    {
        try
        {
            using var terminaciones = new BarTerminationsData(doc)
            {
                HookTypeIdAtStart = gancho.Id,
                HookTypeIdAtEnd = gancho.Id,
                TerminationOrientationAtStart = RebarTerminationOrientation.Left,
                TerminationOrientationAtEnd = RebarTerminationOrientation.Left,
            };
            Rebar? grapa = Rebar.CreateFromCurvesAndShape(
                doc, forma, barra, host, XYZ.BasisZ, new List<Curve> { Line.CreateBound(desde, hasta) }, terminaciones);
            if (grapa is null)
            {
                sb.AppendLine("  CreateFromCurvesAndShape devolvió null.");
                return;
            }

            doc.Regenerate();
            Medir(doc, grapa, barra, forma, gancho, desde, hasta, sb);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  FALLÓ: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Lo que Revit creó de verdad: con qué forma quedó, si tiene diagonales,
    /// cuánto se sale del rectángulo pedido, el radio de los dobleces y el
    /// largo de las dos colas (la parte recta de cada gancho) contra el cuadro.
    /// </summary>
    private static void Medir(
        Document doc, Rebar rebar, RebarBarType barra, RebarShape formaPedida, RebarHookType? gancho,
        XYZ minimo, XYZ maximo, StringBuilder sb)
    {
        try
        {
            string formaReal = doc.GetElement(rebar.GetShapeId())?.Name ?? "(sin forma)";
            sb.AppendLine(
                $"  Rebar {rebar.Id.Value}: forma con la que quedó = «{formaReal}»" +
                (formaReal == formaPedida.Name ? "" : "  <-- Revit la CAMBIÓ por otra") +
                $" | ¿lado diagonal? {GeometriaSeccion.TieneLadoDiagonal(rebar)}");

            IList<Curve> rectas = rebar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
            IList<Curve> todo = rebar.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);

            (double xa, double xb, double ya, double yb) = Envolvente(rectas);
            sb.AppendLine(
                $"  Lados rectos (eje de la barra): {M(xb - xa) * 100:0.00} × {M(yb - ya) * 100:0.00} cm; " +
                $"pedido {M(maximo.X - minimo.X) * 100:0.00} × {M(maximo.Y - minimo.Y) * 100:0.00} cm.");

            (double ta, double tb, double tc, double td) = Envolvente(todo);
            double seSale = new[] { minimo.X - ta, tb - maximo.X, minimo.Y - tc, td - maximo.Y }.Max();
            sb.AppendLine(
                $"  Con ganchos y dobleces ocupa {M(tb - ta) * 100:0.00} × {M(td - tc) * 100:0.00} cm; " +
                (seSale > Pies(0.001)
                    ? $"se SALE {M(seSale) * 100:0.00} cm del rectángulo pedido."
                    : "queda dentro del rectángulo pedido."));

            double d = barra.BarNominalDiameter;
            List<Arc> arcos = todo.OfType<Arc>().ToList();
            if (arcos.Count > 0)
                sb.AppendLine(
                    "  Dobleces (diámetro interior, en diámetros de barra): " +
                    string.Join(", ", arcos.Select(a => $"{(2 * a.Radius - d) / d:0.##} ({Barrido(a):0}°)")));

            if (gancho is not null && todo.Count >= 3 && todo[0] is Line colaInicio && todo[^1] is Line colaFin)
            {
                double objetivo = Math.Abs(gancho.HookAngle - Math.PI) < 1e-3
                    ? Math.Max(Multiplicador180 * d, Pies(MinimoExtension180Metros))
                    : MultiplicadorEstribo135 * d;
                sb.AppendLine(
                    $"  Colas de los ganchos (parte recta): {M(colaInicio.Length) * 100:0.00} y {M(colaFin.Length) * 100:0.00} cm; " +
                    $"el cuadro pide E = {M(objetivo) * 100:0.00} cm.");
            }

            foreach (Curve c in todo) sb.AppendLine($"    {Describir(c)}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  No se pudo medir: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static (double XMin, double XMax, double YMin, double YMax) Envolvente(IList<Curve> curvas)
    {
        double xa = double.MaxValue, xb = double.MinValue, ya = double.MaxValue, yb = double.MinValue;
        foreach (Curve c in curvas)
        foreach (XYZ p in c.Tessellate())
        {
            xa = Math.Min(xa, p.X); xb = Math.Max(xb, p.X);
            ya = Math.Min(ya, p.Y); yb = Math.Max(yb, p.Y);
        }

        return (xa, xb, ya, yb);
    }

    private static double Barrido(Arc a) => (a.GetEndParameter(1) - a.GetEndParameter(0)) * 180 / Math.PI;

    private static string Describir(Curve c) =>
        c is Arc a
            ? $"ARCO  radio={M(a.Radius) * 100:0.00} cm barrido={Barrido(a):0.#}°  {Fmt(c.GetEndPoint(0))} -> {Fmt(c.GetEndPoint(1))}"
            : $"RECTA largo={M(c.Length) * 100:0.00} cm  {Fmt(c.GetEndPoint(0))} -> {Fmt(c.GetEndPoint(1))}";

    private static int Segmentos(RebarShape forma)
    {
        try { return forma.GetRebarShapeDefinition() is RebarShapeDefinitionBySegments s ? s.NumberOfSegments : 0; }
        catch { return 0; }
    }

    /// <summary>
    /// Cada prueba en su propia transacción, para que una que Revit rechace
    /// no se lleve puestas a las demás.
    /// </summary>
    private static void EnTransaccion(Document doc, string nombre, StringBuilder sb, Action accion)
    {
        using Transaction t = new(doc, nombre);
        t.Start();
        FailureHandlingOptions opciones = t.GetFailureHandlingOptions();
        opciones.SetFailuresPreprocessor(new RegistroDeFallas(sb));
        opciones.SetClearAfterRollback(true);
        t.SetFailureHandlingOptions(opciones);

        try
        {
            accion();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  EXCEPCIÓN no prevista: {ex.GetType().Name}: {ex.Message}");
            t.RollBack();
            return;
        }

        TransactionStatus estado = t.Commit();
        if (estado != TransactionStatus.Committed)
            sb.AppendLine($"  Revit REVIRTIÓ este paso al confirmar (estado {estado}): lo de arriba no quedó.");
    }

    private sealed class RegistroDeFallas(StringBuilder sb) : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            bool hayError = false;
            foreach (FailureMessageAccessor falla in failuresAccessor.GetFailureMessages())
            {
                FailureSeverity severidad = falla.GetSeverity();
                sb.AppendLine($"  [Revit, {severidad}] {falla.GetDescriptionText()}");

                if (severidad == FailureSeverity.Warning)
                    failuresAccessor.DeleteWarning(falla);
                else
                    hayError = true;
            }

            return hayError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }

    private static string Fmt(XYZ p) => $"({M(p.X):0.###}, {M(p.Y):0.###}, {M(p.Z):0.###})";

    private static double M(double pies) => UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Meters);

    private static double Pies(double metros) => UnitUtils.ConvertToInternalUnits(metros, UnitTypeId.Meters);
}
