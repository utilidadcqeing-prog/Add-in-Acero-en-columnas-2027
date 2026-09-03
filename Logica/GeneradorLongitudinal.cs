namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// J4: acero longitudinal (esquinas + caras) y estribos menores (grapas y
/// cajas) a partir de <see cref="ConfigLongitudinal"/>. Corre después de
/// <see cref="GeneradorArmado"/> (estribos) dentro de la misma transacción --
/// ver <see cref="ManejadorGeneracionArmado"/>.
///
/// Simplificaciones deliberadas de esta primera pasada (documentadas para no
/// pasar por errores de cálculo):
///
///  - Un Rebar no puede hostearse en más de un elemento de Revit, así que
///    cada barra longitudinal se corta en un tramo por cada elemento host de
///    la pila (#28) -- SIN traslapo/empalme entre ellos. Eso es Fase 5 (#7),
///    todavía no arrancada.
///  - Las barras longitudinales salen sin gancho (rectas): tampoco hay
///    traslapo/anclaje resuelto (mismo motivo que el punto anterior), y
///    <see cref="ConfigLongitudinal"/> no tiene hoy un tipo de gancho propio
///    para longitudinal.
///  - Grapas y cajas reutilizan el gancho del tramo (<see cref="ConfigTramo.TipoGanchoNombre"/>)
///    en cuyo borde caen -- no hay un campo de gancho propio para
///    estribos menores todavía.
///  - La geometría exacta de la "caja" (issue #26/#29-style: rectángulo más
///    chico centrado, usando la columna tildada como alcance en X y la fila
///    como alcance en Y, ver <see cref="Caja"/>) es una interpretación mía a
///    partir de la referencia visual de Luciano -- no la confirmó nadie del
///    equipo contra un plano real. Ver issue de seguimiento.
/// </summary>
public static class GeneradorLongitudinal
{
    private const double Tolerancia = 1e-6;

    /// <summary>Mismo valor que <see cref="GeneradorArmado"/> -- 5cm de norma en los bordes de cada tramo.</summary>
    private const double SeparacionExtremosMetros = 0.05;

    public static void Generar(Document doc, OpcionesArmado opciones, ResultadoArmado res)
    {
        Dictionary<string, RebarBarType> barrasPorNombre = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
            .GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First());

        Dictionary<string, RebarHookType> ganchosPorNombre = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First());

        var cacheHost = new Dictionary<ElementId, (CurveLoop ContornoCrudo, double ZBase)>();

        foreach (ColumnaPlan columnaPlan in opciones.Plan.Columnas)
        {
            try
            {
                GenerarColumna(doc, columnaPlan, barrasPorNombre, ganchosPorNombre, cacheHost, res);
            }
            catch (Exception ex)
            {
                string marca = columnaPlan.Contexto?.Marca ?? columnaPlan.Id;
                res.Avisos.Add($"{marca} (longitudinal): {GeometriaSeccion.Explicar(ex)}");
            }
        }
    }

    private static void GenerarColumna(
        Document doc,
        ColumnaPlan columnaPlan,
        Dictionary<string, RebarBarType> barrasPorNombre,
        Dictionary<string, RebarHookType> ganchosPorNombre,
        Dictionary<ElementId, (CurveLoop, double)> cacheHost,
        ResultadoArmado res)
    {
        ColumnaContexto? contexto = columnaPlan.Contexto;
        ConfigLongitudinal? cfg = columnaPlan.Longitudinal;
        if (contexto is null || cfg is null) return;

        // El longitudinal es UNO por columna, no por tramo -- pero si nadie
        // tildó ni un solo tramo de esta columna, no es parte de esta
        // corrida (ver ConstruirPlanArmado).
        if (columnaPlan.Tramos.All(t => t is null)) return;

        if (string.IsNullOrEmpty(cfg.TipoBarraEsquinasNombre) ||
            !barrasPorNombre.TryGetValue(cfg.TipoBarraEsquinasNombre, out RebarBarType? barraEsquinas))
        {
            res.Avisos.Add(
                $"{contexto.Marca}: el tipo de barra de esquina «{cfg.TipoBarraEsquinasNombre}» " +
                "no existe en el documento; se omite el acero longitudinal de esta columna.");
            return;
        }

        RebarBarType? barraCaraX = ResolverBarraOpcional(cfg.BarrasPorCaraX, cfg.TipoBarraCaraXNombre, barrasPorNombre, "cara X", contexto.Marca, res);
        RebarBarType? barraCaraY = ResolverBarraOpcional(cfg.BarrasPorCaraY, cfg.TipoBarraCaraYNombre, barrasPorNombre, "cara Y", contexto.Marca, res);

        double recubrimientoPies = EstandarCQ.APies(contexto.Recubrimiento);
        double insetEsquinas = recubrimientoPies + barraEsquinas.BarNominalDiameter / 2;

        int columnas = Math.Max(0, cfg.BarrasPorCaraX / 2);
        int filas = Math.Max(0, cfg.BarrasPorCaraY / 2);

        // Un tramo de barra por elemento host de la pila (no se puede
        // hostear un solo Rebar en dos FamilyInstance), agrupando los
        // tramos TILDADOS que caen en cada uno.
        List<(string IdHost, double ZInf, double ZSup)> porHost = AgruparPorHost(contexto, columnaPlan);
        if (porHost.Count == 0) return;

        int creadas = 0;

        for (int h = 0; h < porHost.Count; h++)
        {
            (string idHostTexto, double zInfMetros, double zSupMetros) = porHost[h];

            if (!long.TryParse(idHostTexto, out long idHostLong) ||
                doc.GetElement(new ElementId(idHostLong)) is not FamilyInstance host)
            {
                res.Avisos.Add($"{contexto.Marca}: el elemento {idHostTexto} ya no existe; se omite ese tramo de longitudinal.");
                continue;
            }

            (CurveLoop contornoCrudo, double _) = GeometriaSeccion.ObtenerContornoHost(host, cacheHost);
            (double xMin, double xMax, double yMin, double yMax) = GeometriaSeccion.Huella(contornoCrudo);

            // Solo el host de más abajo de la pila puede tener zapata debajo
            // -- el longitudinal entra a anclarse hasta 7.5cm de su cara
            // inferior (#26), a diferencia del estribo.
            double zInicio = h == 0
                ? EstandarCQ.APies(contexto.ZInferiorLongitudinal)
                : EstandarCQ.APies(zInfMetros);
            double zFin = EstandarCQ.APies(zSupMetros);

            if (zFin - zInicio <= Tolerancia) continue;

            foreach ((double x, double y) in PosicionesEsquinas(xMin, xMax, yMin, yMax, insetEsquinas))
                CrearBarraVertical(doc, host, barraEsquinas, x, y, zInicio, zFin, contexto.Marca, res, ref creadas);

            if (barraCaraX is not null && columnas > 0)
            {
                double insetX = recubrimientoPies + barraCaraX.BarNominalDiameter / 2;
                for (int c = 0; c < columnas; c++)
                {
                    double x = PosicionIntermedia(xMin + insetEsquinas, xMax - insetEsquinas, columnas, c);
                    CrearBarraVertical(doc, host, barraCaraX, x, yMin + insetX, zInicio, zFin, contexto.Marca, res, ref creadas);
                    CrearBarraVertical(doc, host, barraCaraX, x, yMax - insetX, zInicio, zFin, contexto.Marca, res, ref creadas);
                }
            }

            if (barraCaraY is not null && filas > 0)
            {
                double insetY = recubrimientoPies + barraCaraY.BarNominalDiameter / 2;
                for (int f = 0; f < filas; f++)
                {
                    double y = PosicionIntermedia(yMin + insetEsquinas, yMax - insetEsquinas, filas, f);
                    CrearBarraVertical(doc, host, barraCaraY, xMin + insetY, y, zInicio, zFin, contexto.Marca, res, ref creadas);
                    CrearBarraVertical(doc, host, barraCaraY, xMax - insetY, y, zInicio, zFin, contexto.Marca, res, ref creadas);
                }
            }
        }

        if (creadas > 0)
            res.Detalles.Add($"{contexto.Marca}: {creadas} barra(s) longitudinal(es) creada(s).");

        GenerarMenores(
            doc, columnaPlan, contexto, cfg, barrasPorNombre, ganchosPorNombre, cacheHost,
            columnas, filas, insetEsquinas, res);
    }

    /// <summary>
    /// Grapas y cajas al comienzo y al final de cada tramo LIBRE tildado (no
    /// dentro de una viga): según el criterio que trasladó Luciano, ahí es
    /// donde suele haber traslapo y riesgo de pandeo -- no en cada estribo
    /// principal.
    /// </summary>
    private static void GenerarMenores(
        Document doc,
        ColumnaPlan columnaPlan,
        ColumnaContexto contexto,
        ConfigLongitudinal cfg,
        Dictionary<string, RebarBarType> barrasPorNombre,
        Dictionary<string, RebarHookType> ganchosPorNombre,
        Dictionary<ElementId, (CurveLoop, double)> cacheHost,
        int columnas,
        int filas,
        double insetEsquinas,
        ResultadoArmado res)
    {
        if (cfg.GrapasFila.Count == 0 && cfg.GrapasColumna.Count == 0 && cfg.Cajas.Count == 0) return;

        barrasPorNombre.TryGetValue(cfg.TipoBarraCaraXNombre ?? "", out RebarBarType? barraCaraX);
        barrasPorNombre.TryGetValue(cfg.TipoBarraCaraYNombre ?? "", out RebarBarType? barraCaraY);
        RebarBarType? barraCaja = barraCaraX ?? barraCaraY;

        if (barraCaraX is null && cfg.GrapasColumna.Count > 0)
            res.Avisos.Add($"{contexto.Marca}: sin tipo de barra de cara X válido, no se generan grapas de columna.");
        if (barraCaraY is null && cfg.GrapasFila.Count > 0)
            res.Avisos.Add($"{contexto.Marca}: sin tipo de barra de cara Y válido, no se generan grapas de fila.");
        if (barraCaja is null && cfg.Cajas.Count > 0)
            res.Avisos.Add($"{contexto.Marca}: sin tipo de barra de cara válido, no se generan cajas.");

        double separacionExtremos = EstandarCQ.APies(SeparacionExtremosMetros);
        int creadas = 0;

        for (int i = 0; i < contexto.Tramos.Count && i < columnaPlan.Tramos.Count; i++)
        {
            if (columnaPlan.Tramos[i] is not ConfigTramo cfgTramo) continue;

            Tramo tramo = contexto.Tramos[i];
            if (tramo.Tipo == TipoTramo.DentroDeViga) continue;
            if (tramo.IdElementoHost is null) continue;
            if (!long.TryParse(tramo.IdElementoHost, out long idHost) ||
                doc.GetElement(new ElementId(idHost)) is not FamilyInstance host) continue;

            // El gancho del propio tramo -- no hay un campo de gancho propio
            // para grapas/cajas todavía (ver comentario del archivo).
            if (string.IsNullOrEmpty(cfgTramo.TipoGanchoNombre) ||
                !ganchosPorNombre.TryGetValue(cfgTramo.TipoGanchoNombre, out RebarHookType? gancho))
                continue;

            (CurveLoop contornoCrudo, double _) = GeometriaSeccion.ObtenerContornoHost(host, cacheHost);
            (double xMin, double xMax, double yMin, double yMax) = GeometriaSeccion.Huella(contornoCrudo);

            double zInf = EstandarCQ.APies(tramo.ZInferior) + separacionExtremos;
            double zSup = EstandarCQ.APies(tramo.ZSuperior) - separacionExtremos;
            if (zSup <= zInf) continue;

            foreach (double z in new[] { zInf, zSup })
            {
                if (barraCaraY is not null)
                    foreach (int fila in cfg.GrapasFila.Where(f => f < filas))
                    {
                        double y = PosicionIntermedia(yMin + insetEsquinas, yMax - insetEsquinas, filas, fila);
                        CrearGrapa(doc, host, barraCaraY, gancho, new XYZ(xMin, y, z), new XYZ(xMax, y, z), contexto.Marca, res, ref creadas);
                    }

                if (barraCaraX is not null)
                    foreach (int columna in cfg.GrapasColumna.Where(c => c < columnas))
                    {
                        double x = PosicionIntermedia(xMin + insetEsquinas, xMax - insetEsquinas, columnas, columna);
                        CrearGrapa(doc, host, barraCaraX, gancho, new XYZ(x, yMin, z), new XYZ(x, yMax, z), contexto.Marca, res, ref creadas);
                    }

                if (barraCaja is not null)
                {
                    double centroX = (xMin + xMax) / 2;
                    double centroY = (yMin + yMax) / 2;

                    foreach (CajaMenor caja in cfg.Cajas.Where(c => c.Fila < filas && c.Columna < columnas))
                    {
                        double xColumna = PosicionIntermedia(xMin + insetEsquinas, xMax - insetEsquinas, columnas, caja.Columna);
                        double yFila = PosicionIntermedia(yMin + insetEsquinas, yMax - insetEsquinas, filas, caja.Fila);

                        double x1 = centroX - Math.Abs(xColumna - centroX);
                        double x2 = centroX + Math.Abs(xColumna - centroX);
                        double y1 = centroY - Math.Abs(yFila - centroY);
                        double y2 = centroY + Math.Abs(yFila - centroY);

                        CrearCaja(doc, host, barraCaja, gancho, x1, x2, y1, y2, z, contexto.Marca, res, ref creadas);
                    }
                }
            }
        }

        if (creadas > 0)
            res.Detalles.Add($"{contexto.Marca}: {creadas} grapa(s)/caja(s) creada(s).");
    }

    private static RebarBarType? ResolverBarraOpcional(
        int cantidad, string? nombre, Dictionary<string, RebarBarType> barrasPorNombre,
        string etiqueta, string marcaColumna, ResultadoArmado res)
    {
        if (cantidad <= 0) return null;

        if (string.IsNullOrEmpty(nombre) || !barrasPorNombre.TryGetValue(nombre, out RebarBarType? barra))
        {
            res.Avisos.Add($"{marcaColumna}: el tipo de barra de {etiqueta} «{nombre}» no existe en el documento; se omiten esas barras.");
            return null;
        }

        return barra;
    }

    /// <summary>
    /// Agrupa los tramos TILDADOS de la columna por elemento host, quedándose
    /// con el rango Z que cubren dentro de cada uno -- un Rebar longitudinal
    /// por host, no por tramo (corre continuo dentro de un mismo elemento
    /// aunque tenga un nudo de viga en el medio).
    /// </summary>
    private static List<(string IdHost, double ZInf, double ZSup)> AgruparPorHost(
        ColumnaContexto contexto, ColumnaPlan columnaPlan)
    {
        var porHost = new List<(string IdHost, double ZInf, double ZSup)>();

        for (int i = 0; i < contexto.Tramos.Count && i < columnaPlan.Tramos.Count; i++)
        {
            if (columnaPlan.Tramos[i] is null) continue;

            Tramo tramo = contexto.Tramos[i];
            if (tramo.IdElementoHost is null) continue;

            int idx = porHost.FindIndex(h => h.IdHost == tramo.IdElementoHost);
            if (idx < 0)
            {
                porHost.Add((tramo.IdElementoHost, tramo.ZInferior, tramo.ZSuperior));
            }
            else
            {
                (string id, double zInf, double zSup) = porHost[idx];
                porHost[idx] = (id, Math.Min(zInf, tramo.ZInferior), Math.Max(zSup, tramo.ZSuperior));
            }
        }

        porHost.Sort((a, b) => a.ZInf.CompareTo(b.ZInf));
        return porHost;
    }

    private static IEnumerable<(double X, double Y)> PosicionesEsquinas(
        double xMin, double xMax, double yMin, double yMax, double inset)
    {
        yield return (xMin + inset, yMin + inset);
        yield return (xMax - inset, yMin + inset);
        yield return (xMin + inset, yMax - inset);
        yield return (xMax - inset, yMax - inset);
    }

    /// <summary>
    /// Reparte <paramref name="n"/> posiciones parejo entre los dos extremos,
    /// sin pisarlos -- mismo criterio que <c>VentanaEstribosMenores.X/Y</c>
    /// (fila/columna 0 es la más cercana a la esquina de índice bajo).
    /// </summary>
    private static double PosicionIntermedia(double desde, double hasta, int n, int indice) =>
        desde + (indice + 1) * (hasta - desde) / (n + 1);

    private static void CrearBarraVertical(
        Document doc, FamilyInstance host, RebarBarType barra,
        double x, double y, double zInicio, double zFin,
        string marcaColumna, ResultadoArmado res, ref int creadas)
    {
        try
        {
            Line linea = Line.CreateBound(new XYZ(x, y, zInicio), new XYZ(x, y, zFin));

            // BarTerminationsData "vacío" (sin fijar hooks): recta, sin
            // gancho -- ver el porqué en el comentario del archivo (no hay
            // traslapo/anclaje resuelto todavía, eso es Fase 5).
            using var terminaciones = new BarTerminationsData(doc);

            Rebar barraCreada = Rebar.CreateFromCurves(
                doc,
                RebarStyle.Standard,
                barra,
                host,
                XYZ.BasisZ,
                new List<Curve> { linea },
                terminaciones,
                useExistingShapeIfPossible: true,
                createNewShape: true);

            MarcaEstribos.Marcar(barraCreada, host.Id);
            creadas++;
        }
        catch (Exception ex)
        {
            res.Avisos.Add($"{marcaColumna}: no se pudo crear una barra longitudinal ({GeometriaSeccion.Explicar(ex)}).");
        }
    }

    private static void CrearGrapa(
        Document doc, FamilyInstance host, RebarBarType barra, RebarHookType gancho,
        XYZ desde, XYZ hasta, string marcaColumna, ResultadoArmado res, ref int creadas)
    {
        try
        {
            Line linea = Line.CreateBound(desde, hasta);

            using var terminaciones = new BarTerminationsData(doc)
            {
                HookTypeIdAtStart = gancho.Id,
                HookTypeIdAtEnd = gancho.Id,
                TerminationOrientationAtStart = RebarTerminationOrientation.Left,
                TerminationOrientationAtEnd = RebarTerminationOrientation.Left,
            };

            Rebar grapa = Rebar.CreateFromCurves(
                doc,
                RebarStyle.StirrupTie,
                barra,
                host,
                XYZ.BasisZ,
                new List<Curve> { linea },
                terminaciones,
                useExistingShapeIfPossible: true,
                createNewShape: true);

            MarcaEstribos.Marcar(grapa, host.Id);
            creadas++;
        }
        catch (Exception ex)
        {
            res.Avisos.Add($"{marcaColumna}: no se pudo crear una grapa ({GeometriaSeccion.Explicar(ex)}).");
        }
    }

    private static void CrearCaja(
        Document doc, FamilyInstance host, RebarBarType barra, RebarHookType gancho,
        double x1, double x2, double y1, double y2, double z,
        string marcaColumna, ResultadoArmado res, ref int creadas)
    {
        try
        {
            var curvas = new List<Curve>
            {
                Line.CreateBound(new XYZ(x1, y1, z), new XYZ(x2, y1, z)),
                Line.CreateBound(new XYZ(x2, y1, z), new XYZ(x2, y2, z)),
                Line.CreateBound(new XYZ(x2, y2, z), new XYZ(x1, y2, z)),
                Line.CreateBound(new XYZ(x1, y2, z), new XYZ(x1, y1, z)),
            };
            CurveLoop loop = CurveLoop.Create(curvas);

            using var terminaciones = new BarTerminationsData(doc)
            {
                HookTypeIdAtStart = gancho.Id,
                HookTypeIdAtEnd = gancho.Id,
                TerminationOrientationAtStart = RebarTerminationOrientation.Left,
                TerminationOrientationAtEnd = RebarTerminationOrientation.Left,
            };

            Rebar caja = Rebar.CreateFromCurves(
                doc,
                RebarStyle.StirrupTie,
                barra,
                host,
                XYZ.BasisZ,
                loop.ToList(),
                terminaciones,
                useExistingShapeIfPossible: true,
                createNewShape: true);

            MarcaEstribos.Marcar(caja, host.Id);
            creadas++;
        }
        catch (Exception ex)
        {
            res.Avisos.Add($"{marcaColumna}: no se pudo crear una caja ({GeometriaSeccion.Explicar(ex)}).");
        }
    }
}
