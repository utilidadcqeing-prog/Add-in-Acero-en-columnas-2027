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
///  - Fase 5 (#7, traslapos): implementada en <see cref="CrearBarraConTraslapos"/>.
///    Un Rebar no puede hostearse en más de un elemento de Revit, así que
///    cada límite entre elementos host de la pila (#28) es un corte
///    OBLIGADO -- pero eso no consume barra de más: las dos piezas se tocan
///    ahí sin solape, salvo que justo caiga un traslapo real ahí. Los
///    traslapos reales (con el solape del cuadro de la empresa) solo se
///    generan donde la longitud comercial de la barra (<see cref="ConfigLongitudinal.LongitudComercialBarra"/>,
///    6 o 12 m) obliga a cortar, y solo a la mitad de un nivel, alternando
///    niveles pares/impares entre las posiciones longitudinales de la
///    sección (ver <see cref="GenerarColumna"/>, contador <c>posicion</c>).
///    Dos cosas de esto NO están confirmadas por el equipo, ver issue de
///    seguimiento de Fase 5: (a) qué criterio real separa las barras en el
///    grupo par/impar -- acá es simplemente el orden en que ya se recorrían
///    esquinas/cara X/cara Y; (b) qué pasa cuando un corte obligado por
///    límite de host no coincide con ningún traslapo real de ese grupo --
///    acá se resuelve como junta a tope, sin solape, simulando que la barra
///    real es continua a través de esa junta de hormigonado y que es Revit
///    (no la obra) quien exige partir el objeto ahí.
///  - Las barras longitudinales salen sin gancho (rectas) -- no hay campo de
///    gancho propio para longitudinal en <see cref="ConfigLongitudinal"/> hoy.
///  - Grapas y cajas reutilizan el gancho del tramo (<see cref="ConfigTramo.TipoGanchoNombre"/>)
///    en el que se generan -- no hay un campo de gancho propio para
///    estribos menores todavía.
///  - Geometría de la "caja" (issue #30, resuelto): rectángulo literal entre
///    los bordes del rango de filas/columnas tildadas para caja -- YA NO
///    centrado/espejado respecto al centro de la sección, esa era una
///    interpretación anterior sin confirmar. Filas/columnas contiguas
///    tildadas para caja se fusionan en un solo rango grande, confirmado por
///    William (reunión 4-sep, 21:08-23:18: "tomaría el área más grande...
///    porque están juntos"); ver <c>VentanaEstribosMenores.Rangos</c> (quien
///    arma los rangos) y <see cref="Modelo.CajaMenor"/>. Cuando una
///    fila/columna con grapa tildada también queda cubierta por una caja,
///    gana la caja y la grapa no se genera ahí (prioridad confirmada por
///    Luciano).
/// </summary>
public static class GeneradorLongitudinal
{
    private const double Tolerancia = 1e-6;

    /// <summary>Footprint (huella) del host ya resuelto, para no repetir la geometría por cada posición longitudinal que lo cruza.</summary>
    private readonly record struct FootprintHost(FamilyInstance Host, double XMin, double XMax, double YMin, double YMax);

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

        double insetX = barraCaraX is not null ? recubrimientoPies + barraCaraX.BarNominalDiameter / 2 : 0;
        double insetY = barraCaraY is not null ? recubrimientoPies + barraCaraY.BarNominalDiameter / 2 : 0;

        // Un tramo de barra por elemento host de la pila (no se puede
        // hostear un solo Rebar en dos FamilyInstance), agrupando los
        // tramos TILDADOS que caen en cada uno.
        List<(string IdHost, double ZInf, double ZSup)> porHost = AgruparPorHost(contexto, columnaPlan);
        if (porHost.Count == 0) return;

        // Footprint de cada host, resuelto una sola vez -- Fase 5 (#7) ya no
        // crea una barra por host de forma independiente: una posición
        // longitudinal (una esquina, una barra de cara) es UNA corrida que
        // puede atravesar varios hosts, así que su footprint hace falta por
        // adelantado para cada uno de ellos, no solo el que se está
        // recorriendo en el momento.
        var footprintPorHost = new Dictionary<string, FootprintHost>();
        foreach ((string idHostTexto, double _, double _) in porHost)
        {
            if (!long.TryParse(idHostTexto, out long idHostLong) ||
                doc.GetElement(new ElementId(idHostLong)) is not FamilyInstance host)
            {
                res.Avisos.Add($"{contexto.Marca}: el elemento {idHostTexto} ya no existe; se omite ese tramo de longitudinal.");
                continue;
            }

            // Mismo chequeo que ya hace GeneradorArmado antes de intentar
            // CreateFromCurves para estribos -- acá nunca se hacía. Si el
            // host no admite armadura, ninguna geometría por más que entre
            // en la sección va a lograr crear un Rebar sobre él.
            if (RebarHostData.GetRebarHostData(host) is null)
            {
                res.Avisos.Add($"{contexto.Marca}: el elemento {idHostTexto} no admite armadura (host no válido para rebar); se omite el longitudinal de ese tramo.");
                continue;
            }

            (CurveLoop contornoCrudo, double _) = GeometriaSeccion.ObtenerContornoHost(host, cacheHost);
            (double xMin, double xMax, double yMin, double yMax) = GeometriaSeccion.Huella(contornoCrudo);

            // Probado sobre Revit real: si la sección es muy chica para la
            // cantidad/grosor de barra pedida, el inset de esquina o de cara
            // puede terminar cruzando el punto medio de la sección (posición
            // negativa o invertida) -- Rebar.CreateFromCurves devuelve null
            // sin avisar por qué. Se corta acá con un mensaje claro por host
            // en vez de intentarlo igual.
            double anchoDisponibleX = (xMax - xMin) - 2 * insetEsquinas;
            double anchoDisponibleY = (yMax - yMin) - 2 * insetEsquinas;
            if (anchoDisponibleX <= Tolerancia || anchoDisponibleY <= Tolerancia)
            {
                res.Avisos.Add(
                    $"{contexto.Marca}: la sección de {idHostTexto} ({GeometriaSeccion.Metros(xMax - xMin) * 100:0.#}×" +
                    $"{GeometriaSeccion.Metros(yMax - yMin) * 100:0.#} cm) es demasiado chica para el recubrimiento + " +
                    $"la barra de esquina «{barraEsquinas.Name}»; se omite el longitudinal de ese tramo.");
                continue;
            }

            if (barraCaraX is not null && (xMax - xMin) - 2 * insetX <= Tolerancia)
                res.Avisos.Add(
                    $"{contexto.Marca}: la sección de {idHostTexto} es demasiado chica para la barra de cara X " +
                    $"«{barraCaraX.Name}»; sus posiciones pueden quedar mal.");

            if (barraCaraY is not null && (yMax - yMin) - 2 * insetY <= Tolerancia)
                res.Avisos.Add(
                    $"{contexto.Marca}: la sección de {idHostTexto} es demasiado chica para la barra de cara Y " +
                    $"«{barraCaraY.Name}»; sus posiciones pueden quedar mal.");

            footprintPorHost[idHostTexto] = new FootprintHost(host, xMin, xMax, yMin, yMax);
        }

        // "Niveles" para Fase 5 (#7): un candidato de traslapo por tramo
        // LIBRE/CONTRA ZAPATA de TODA la columna (no solo los tildados en
        // esta corrida) -- el número de nivel (para el intercalado par/impar)
        // es una propiedad estable de la columna real, no debería cambiar
        // según qué tramos se tildaron hoy.
        List<(int Ordinal, double ZInf, double ZSup)> niveles = contexto.Tramos
            .Where(t => t.Tipo != TipoTramo.DentroDeViga)
            .Select((t, idx) => (Ordinal: idx + 1, t.ZInferior, t.ZSuperior))
            .ToList();

        // Solo el host de más abajo de la pila puede tener zapata debajo --
        // el longitudinal entra a anclarse hasta 7.5cm de su cara inferior
        // (#26), a diferencia del estribo.
        double zInicioTotal = contexto.ZInferiorLongitudinal;
        double zFinTotal = porHost[^1].ZSup;

        int creadas = 0;

        // Un contador corrido de posición alrededor del perímetro decide el
        // grupo par/impar del intercalado (issue #7 y #29-style: "mitad de
        // las barras empalma en niveles impares, la mitad en pares") -- cada
        // posición longitudinal (esquina, barra de cara X, barra de cara Y)
        // suma uno, en el mismo orden en que ya se recorrían antes de esta
        // fase. No es una convención confirmada por nadie del equipo, ver
        // issue de seguimiento de Fase 5.
        int posicion = 0;

        // El plan de cortes (traslapos reales + límites de host) es el mismo
        // para todas las barras que comparten tipo de barra Y grupo par/impar
        // -- no depende de la posición X/Y, solo de niveles/porHost/longitud
        // comercial, que son iguales para toda la columna. Se cachea acá en
        // vez de recalcularlo por cada una de las (hasta 2 + 2*columnas +
        // 2*filas) barras -- perfila igual, sin este cache se repetía la
        // misma cuenta (y el mismo aviso, si la barra no está en el cuadro)
        // hasta 20 veces por cara.
        var cortesPorBarraYGrupo = new Dictionary<(ElementId BarraId, bool EsPar), PlanDeCortes>();

        Func<FootprintHost, (double X, double Y)>[] esquinas =
        {
            fp => (fp.XMin + insetEsquinas, fp.YMin + insetEsquinas),
            fp => (fp.XMax - insetEsquinas, fp.YMin + insetEsquinas),
            fp => (fp.XMin + insetEsquinas, fp.YMax - insetEsquinas),
            fp => (fp.XMax - insetEsquinas, fp.YMax - insetEsquinas),
        };

        foreach (Func<FootprintHost, (double X, double Y)> esquinaFn in esquinas)
        {
            CrearBarraConTraslapos(
                doc, porHost, footprintPorHost, barraEsquinas, esquinaFn, posicion++ % 2 == 0,
                niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra, cortesPorBarraYGrupo,
                contexto.Marca, res, ref creadas);
        }

        if (barraCaraX is not null && columnas > 0)
        {
            for (int c = 0; c < columnas; c++)
            {
                int cCapturado = c;
                CrearBarraConTraslapos(
                    doc, porHost, footprintPorHost, barraCaraX,
                    fp => (PosicionIntermedia(fp.XMin + insetEsquinas, fp.XMax - insetEsquinas, columnas, cCapturado), fp.YMin + insetX),
                    posicion++ % 2 == 0,
                    niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra, cortesPorBarraYGrupo,
                    contexto.Marca, res, ref creadas);

                CrearBarraConTraslapos(
                    doc, porHost, footprintPorHost, barraCaraX,
                    fp => (PosicionIntermedia(fp.XMin + insetEsquinas, fp.XMax - insetEsquinas, columnas, cCapturado), fp.YMax - insetX),
                    posicion++ % 2 == 0,
                    niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra, cortesPorBarraYGrupo,
                    contexto.Marca, res, ref creadas);
            }
        }

        if (barraCaraY is not null && filas > 0)
        {
            for (int f = 0; f < filas; f++)
            {
                int fCapturado = f;
                CrearBarraConTraslapos(
                    doc, porHost, footprintPorHost, barraCaraY,
                    fp => (fp.XMin + insetY, PosicionIntermedia(fp.YMin + insetEsquinas, fp.YMax - insetEsquinas, filas, fCapturado)),
                    posicion++ % 2 == 0,
                    niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra, cortesPorBarraYGrupo,
                    contexto.Marca, res, ref creadas);

                CrearBarraConTraslapos(
                    doc, porHost, footprintPorHost, barraCaraY,
                    fp => (fp.XMax - insetY, PosicionIntermedia(fp.YMin + insetEsquinas, fp.YMax - insetEsquinas, filas, fCapturado)),
                    posicion++ % 2 == 0,
                    niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra, cortesPorBarraYGrupo,
                    contexto.Marca, res, ref creadas);
            }
        }

        if (creadas > 0)
            res.Detalles.Add($"{contexto.Marca}: {creadas} barra(s) longitudinal(es) creada(s) (con traslapos, Fase 5).");

        GenerarMenores(
            doc, columnaPlan, contexto, cfg, barrasPorNombre, ganchosPorNombre, cacheHost,
            columnas, filas, insetEsquinas, res);
    }

    /// <summary>
    /// Grapas y cajas en cada tramo LIBRE tildado (no dentro de una viga), en
    /// las mismas posiciones Z que los estribos principales de ese tramo --
    /// issue #32: William confirmó en la reunión del 4-sep que van "en la
    /// misma parte donde van los estribos normales", no solo en los extremos
    /// del tramo (el supuesto anterior, atribuido a un criterio de Luciano,
    /// quedó descartado). Las posiciones se leen de
    /// <see cref="ResultadoArmado.PosicionesEstribosPorTramo"/>, que llena
    /// <see cref="GeneradorArmado"/> al crear cada estribo real.
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

        int creadas = 0;

        // Prioridad caja-sobre-gancho: si una fila/columna con grapa tildada
        // también queda cubierta por una caja (rango fusionado), gana la
        // caja y la grapa no se genera ahí -- confirmado por Luciano, mismo
        // criterio que aplica VentanaEstribosMenores en la previsualización.
        List<int> grapasFila = cfg.GrapasFila
            .Where(f => f < filas && !cfg.Cajas.Any(c => f >= c.FilaDesde && f <= c.FilaHasta))
            .ToList();
        List<int> grapasColumna = cfg.GrapasColumna
            .Where(c => c < columnas && !cfg.Cajas.Any(caja => c >= caja.ColumnaDesde && c <= caja.ColumnaHasta))
            .ToList();

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

            // Sin estribos principales registrados en este tramo (por ejemplo,
            // GeneradorArmado lo omitió por algún motivo), no hay dónde poner
            // los menores -- issue #32, ya no se calcula un zInf/zSup propio.
            if (!res.PosicionesEstribosPorTramo.TryGetValue((tramo.IdElementoHost, tramo.Indice), out List<double>? posicionesZ) ||
                posicionesZ.Count == 0)
                continue;

            (CurveLoop contornoCrudo, double _) = GeometriaSeccion.ObtenerContornoHost(host, cacheHost);
            (double xMin, double xMax, double yMin, double yMax) = GeometriaSeccion.Huella(contornoCrudo);

            foreach (double z in posicionesZ)
            {
                if (barraCaraY is not null)
                    foreach (int fila in grapasFila)
                    {
                        double y = PosicionIntermedia(yMin + insetEsquinas, yMax - insetEsquinas, filas, fila);
                        CrearGrapa(doc, host, barraCaraY, gancho, new XYZ(xMin, y, z), new XYZ(xMax, y, z), contexto.Marca, res, ref creadas);
                    }

                if (barraCaraX is not null)
                    foreach (int columna in grapasColumna)
                    {
                        double x = PosicionIntermedia(xMin + insetEsquinas, xMax - insetEsquinas, columnas, columna);
                        CrearGrapa(doc, host, barraCaraX, gancho, new XYZ(x, yMin, z), new XYZ(x, yMax, z), contexto.Marca, res, ref creadas);
                    }

                if (barraCaja is not null)
                {
                    // Rectángulo literal entre los bordes del rango fusionado
                    // -- ya NO centrado/espejado respecto al centro de la
                    // sección (issue #30: esa era una interpretación sin
                    // confirmar de una versión anterior).
                    foreach (CajaMenor caja in cfg.Cajas.Where(c => c.FilaHasta < filas && c.ColumnaHasta < columnas))
                    {
                        double x1 = PosicionIntermedia(xMin + insetEsquinas, xMax - insetEsquinas, columnas, caja.ColumnaDesde);
                        double x2 = PosicionIntermedia(xMin + insetEsquinas, xMax - insetEsquinas, columnas, caja.ColumnaHasta);
                        double y1 = PosicionIntermedia(yMin + insetEsquinas, yMax - insetEsquinas, filas, caja.FilaDesde);
                        double y2 = PosicionIntermedia(yMin + insetEsquinas, yMax - insetEsquinas, filas, caja.FilaHasta);

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

    /// <summary>Resultado de <see cref="PlanificarCortes"/>, cacheado por (tipo de barra, grupo par/impar).</summary>
    private sealed record PlanDeCortes(double TraslapoMetros, List<(double Z, bool EsReal)> Cortes);

    /// <summary>
    /// Fase 5 (#7): arma UNA posición longitudinal (una esquina o una barra
    /// de cara) a lo largo de TODA la columna a partir del plan de cortes de
    /// su (tipo de barra, grupo par/impar) -- ver <see cref="PlanificarCortes"/>
    /// para qué es cada tipo de corte.
    /// </summary>
    private static void CrearBarraConTraslapos(
        Document doc,
        List<(string IdHost, double ZInf, double ZSup)> porHost,
        Dictionary<string, FootprintHost> footprintPorHost,
        RebarBarType barra,
        Func<FootprintHost, (double X, double Y)> calcularXY,
        bool esParGrupo,
        List<(int Ordinal, double ZInf, double ZSup)> niveles,
        double zInicioTotalMetros,
        double zFinTotalMetros,
        double longitudComercialMetros,
        Dictionary<(ElementId BarraId, bool EsPar), PlanDeCortes> cortesPorBarraYGrupo,
        string marcaColumna,
        ResultadoArmado res,
        ref int creadas)
    {
        if (zFinTotalMetros - zInicioTotalMetros <= Tolerancia) return;

        var claveGrupo = (barra.Id, esParGrupo);
        if (!cortesPorBarraYGrupo.TryGetValue(claveGrupo, out PlanDeCortes? plan))
        {
            plan = PlanificarCortes(
                barra, esParGrupo, porHost, niveles, zInicioTotalMetros, zFinTotalMetros,
                longitudComercialMetros, marcaColumna, res);
            cortesPorBarraYGrupo[claveGrupo] = plan;
        }

        List<(double Z, bool EsReal)> cortes = plan.Cortes;
        var limites = new List<double> { zInicioTotalMetros };
        limites.AddRange(cortes.Select(c => c.Z));
        limites.Add(zFinTotalMetros);

        for (int s = 0; s < limites.Count - 1; s++)
        {
            bool iniEsReal = s >= 1 && cortes[s - 1].EsReal;
            bool finEsReal = s < cortes.Count && cortes[s].EsReal;

            double zIniSinSolape = limites[s];
            double zFinSinSolape = limites[s + 1];
            double zIni = iniEsReal ? zIniSinSolape - plan.TraslapoMetros / 2 : zIniSinSolape;
            double zFin = finEsReal ? zFinSinSolape + plan.TraslapoMetros / 2 : zFinSinSolape;

            if (zFin - zIni <= Tolerancia) continue;

            double medio = (zIniSinSolape + zFinSinSolape) / 2;
            var hostEntry = porHost.FirstOrDefault(h => medio >= h.ZInf - Tolerancia && medio <= h.ZSup + Tolerancia);
            if (hostEntry.IdHost is null || !footprintPorHost.TryGetValue(hostEntry.IdHost, out FootprintHost fp))
                continue; // host inexistente o borrado -- ya avisado al armar footprintPorHost

            // El medio solape que se suma en zIni/zFin es respecto al PUNTO DE
            // EMPALME, no respecto al host -- si ese empalme cae cerca del
            // límite real entre dos hosts (dos FamilyInstance distintos,
            // columna con cambio de sección), el agregado puede empujar la
            // línea más allá del sólido del host que se acaba de elegir.
            // Revit no puede alojar un Rebar que se sale de su host
            // (CreateFromCurves devuelve null sin decir por qué -- probado
            // sobre Revit real). Se recorta acá a los límites reales de ese
            // host: el resto del solape ya lo cubre la barra del host
            // vecino, que arranca/termina desde el otro lado del mismo
            // empalme.
            zIni = Math.Max(zIni, hostEntry.ZInf);
            zFin = Math.Min(zFin, hostEntry.ZSup);
            if (zFin - zIni <= Tolerancia) continue;

            (double x, double y) = calcularXY(fp);
            CrearBarraVertical(
                doc, fp, barra, x, y,
                EstandarCQ.APies(zIni), EstandarCQ.APies(zFin),
                marcaColumna, res, ref creadas);
        }
    }

    /// <summary>
    /// El plan de cortes de un (tipo de barra, grupo par/impar): dónde corta
    /// y con qué solape, resolviendo dos tipos de corte muy distintos entre
    /// sí --
    ///
    ///  - Corte OBLIGADO por límite de host (ya existía desde #28: Revit no
    ///    permite un mismo Rebar en dos FamilyInstance). Las dos piezas
    ///    simplemente se tocan ahí, sin gastar barra de más -- en la
    ///    realidad la barra es continua a través de esa junta de hormigonado,
    ///    Revit es el que obliga a partir el objeto, no la obra.
    ///  - Traslapo REAL (con el solape de <see cref="EstandarCQ.Varilla.Traslapo"/>):
    ///    solo donde hace falta por la longitud comercial de la barra
    ///    (<paramref name="longitudComercialMetros"/>), y solo a la mitad de
    ///    un "nivel" (issue #7), alternando niveles pares/impares según
    ///    <paramref name="esParGrupo"/> (issue #29-style, "traslapos
    ///    intercalados").
    ///
    /// Cuando un corte obligado cae muy cerca de un traslapo real elegido
    /// para este grupo, se resuelve como real (el solape ya cubre esa junta).
    ///
    /// No depende de la posición X/Y de la barra -- solo del tipo de barra
    /// (por su Traslapo del cuadro) y del grupo par/impar -- por eso
    /// <see cref="CrearBarraConTraslapos"/> lo cachea y lo calcula una sola
    /// vez por combinación en vez de una vez por barra.
    /// </summary>
    private static PlanDeCortes PlanificarCortes(
        RebarBarType barra,
        bool esParGrupo,
        List<(string IdHost, double ZInf, double ZSup)> porHost,
        List<(int Ordinal, double ZInf, double ZSup)> niveles,
        double zInicioTotalMetros,
        double zFinTotalMetros,
        double longitudComercialMetros,
        string marcaColumna,
        ResultadoArmado res)
    {
        EstandarCQ.Varilla? varilla = EstandarCQ.Buscar(barra);
        double traslapoMetros = varilla?.Traslapo ?? 0;
        if (varilla is null)
            res.Avisos.Add(
                $"{marcaColumna}: la barra «{barra.Name}» no está en el cuadro de la empresa; " +
                "los traslapos de esa barra se resuelven sin solape (empalme a tope).");

        List<double> candidatos = niveles
            .Where(n => (n.Ordinal % 2 == 0) == esParGrupo)
            .Select(n => RedondearArriba10Cm((n.ZInf + n.ZSup) / 2))
            .Where(z => z > zInicioTotalMetros + Tolerancia && z < zFinTotalMetros - Tolerancia)
            .OrderBy(z => z)
            .ToList();

        List<double> reales = PlanificarSplicesReales(
            candidatos, zInicioTotalMetros, zFinTotalMetros, longitudComercialMetros, marcaColumna, res);

        var cortes = reales.Select(z => (Z: z, EsReal: true)).ToList();
        for (int i = 0; i < porHost.Count - 1; i++)
        {
            double zLimite = porHost[i].ZSup;
            if (cortes.Any(c => Math.Abs(c.Z - zLimite) < 0.03)) continue; // ya cubierto por un traslapo real
            cortes.Add((zLimite, false));
        }
        cortes.Sort((a, b) => a.Z.CompareTo(b.Z));

        return new PlanDeCortes(traslapoMetros, cortes);
    }

    /// <summary>
    /// De los candidatos de traslapo de UN grupo (par o impar), elige el
    /// mínimo necesario para que ningún tramo entre dos traslapos reales
    /// consecutivos (ni el primero desde el arranque, ni el último hasta el
    /// remate) supere <paramref name="longitudComercialMetros"/> -- avanza
    /// siempre al candidato más lejano todavía alcanzable (voraz: minimiza
    /// la cantidad de traslapos).
    /// </summary>
    private static List<double> PlanificarSplicesReales(
        List<double> candidatosOrdenados, double zInicio, double zFin,
        double longitudComercialMetros, string marcaColumna, ResultadoArmado res)
    {
        var reales = new List<double>();
        double ultimoCorte = zInicio;
        int i = 0;

        while (i < candidatosOrdenados.Count)
        {
            double? mejor = null;
            while (i < candidatosOrdenados.Count && candidatosOrdenados[i] - ultimoCorte <= longitudComercialMetros)
            {
                mejor = candidatosOrdenados[i];
                i++;
            }

            if (mejor is null)
            {
                // El próximo nivel disponible para este grupo ya se pasa de
                // la barra comercial desde el último traslapo -- se usa
                // igual (mejor eso que dejar un tramo sin ningún traslapo
                // donde hacía falta), pero queda avisado.
                res.Avisos.Add(
                    $"{marcaColumna}: no hay un nivel disponible para el traslapo intercalado dentro de los " +
                    $"{longitudComercialMetros:0.#} m de barra comercial (el siguiente queda a " +
                    $"{candidatosOrdenados[i] - ultimoCorte:0.##} m); se traslapa igual ahí.");
                mejor = candidatosOrdenados[i];
                i++;
            }

            reales.Add(mejor.Value);
            ultimoCorte = mejor.Value;
        }

        if (zFin - ultimoCorte > longitudComercialMetros + Tolerancia)
            res.Avisos.Add(
                $"{marcaColumna}: el tramo final ({zFin - ultimoCorte:0.#} m) supera la barra comercial de " +
                $"{longitudComercialMetros:0.#} m y no queda ningún nivel disponible para traslaparlo intercalado.");

        return reales;
    }

    /// <summary>El punto de traslape real se redondea hacia arriba a un múltiplo de 10 cm (issue #7).</summary>
    private static double RedondearArriba10Cm(double metros) =>
        Math.Ceiling(metros / 0.10 - Tolerancia) * 0.10;

    /// <summary>
    /// Reparte <paramref name="n"/> posiciones parejo entre los dos extremos,
    /// sin pisarlos -- mismo criterio que <c>VentanaEstribosMenores.X/Y</c>
    /// (fila/columna 0 es la más cercana a la esquina de índice bajo).
    /// </summary>
    private static double PosicionIntermedia(double desde, double hasta, int n, int indice) =>
        desde + (indice + 1) * (hasta - desde) / (n + 1);

    private static void CrearBarraVertical(
        Document doc, FootprintHost fp, RebarBarType barra,
        double x, double y, double zInicio, double zFin,
        string marcaColumna, ResultadoArmado res, ref int creadas)
    {
        FamilyInstance host = fp.Host;
        try
        {
            Line linea = Line.CreateBound(new XYZ(x, y, zInicio), new XYZ(x, y, zFin));

            // BarTerminationsData "vacío" (sin fijar hooks): recta, sin
            // gancho -- ConfigLongitudinal no tiene campo de gancho propio
            // para longitudinal todavía (ver comentario del archivo). El
            // traslapo en sí (Fase 5, #7) sí está resuelto: quien llama a
            // esto ya le pasa el segmento correcto con o sin solape.
            using var terminaciones = new BarTerminationsData(doc);

            // La normal define el plano de la forma de la barra -- tiene que
            // ser PERPENDICULAR a la curva, no paralela. Para los estribos y
            // grapas (curvas horizontales) BasisZ es perpendicular y anda
            // bien, pero acá la curva es vertical: usar BasisZ como normal
            // deja el plano degenerado (normal paralela a la propia línea) y
            // Rebar.CreateFromCurves devuelve null siempre, sin importar la
            // posición ni el tamaño de la sección -- confirmado en Revit
            // real: fallaba el 100% de las barras longitudinales aunque el
            // footprint y el bounding box del host dieran bien. BasisX es
            // perpendicular a cualquier barra vertical.
            Rebar? barraCreada = Rebar.CreateFromCurves(
                doc,
                RebarStyle.Standard,
                barra,
                host,
                XYZ.BasisX,
                new List<Curve> { linea },
                terminaciones,
                useExistingShapeIfPossible: true,
                createNewShape: true);

            // Rebar.CreateFromCurves puede devolver null en vez de tirar
            // excepción (probado sobre Revit real) -- sin este chequeo,
            // MarcaEstribos.Marcar de la línea siguiente revienta con un
            // NullReferenceException que no dice nada útil.
            //
            // Diagnóstico temporal (sacar junto con el resto de #7-crash):
            // se agrega el footprint del host y si X/Y quedan dentro de él,
            // para distinguir un problema de encaje geométrico de otra causa
            // (familia de forma de armadura no cargada, etc.) sin adivinar.
            if (barraCreada is null)
            {
                bool xDentro = x >= fp.XMin - 1e-6 && x <= fp.XMax + 1e-6;
                bool yDentro = y >= fp.YMin - 1e-6 && y <= fp.YMax + 1e-6;

                // El bounding box REAL del elemento de Revit, para comparar
                // contra el ZInf/ZSup que le calculamos nosotros desde los
                // tramos -- si no coinciden, el límite que usamos para
                // recortar el traslapo no es el límite real del sólido, y
                // por eso la línea sigue quedando afuera aunque el recorte
                // por tramo la dé "adentro".
                BoundingBoxXYZ? bb = host.get_BoundingBox(null);
                string bbTexto = bb is null
                    ? "sin bounding box"
                    : $"Z[{GeometriaSeccion.Metros(bb.Min.Z):0.###}, {GeometriaSeccion.Metros(bb.Max.Z):0.###}]";

                throw new InvalidOperationException(
                    $"Revit no pudo crear la barra con la línea pedida (Rebar.CreateFromCurves devolvió null) " +
                    $"con la barra «{barra.Name}». Diagnóstico: línea de {GeometriaSeccion.Metros(linea.Length) * 100:0.#}cm, " +
                    $"X={GeometriaSeccion.Metros(x):0.###} Y={GeometriaSeccion.Metros(y):0.###} " +
                    $"Z {GeometriaSeccion.Metros(zInicio):0.###}→{GeometriaSeccion.Metros(zFin):0.###} m. " +
                    $"Footprint del host: X[{GeometriaSeccion.Metros(fp.XMin):0.###}, {GeometriaSeccion.Metros(fp.XMax):0.###}] " +
                    $"Y[{GeometriaSeccion.Metros(fp.YMin):0.###}, {GeometriaSeccion.Metros(fp.YMax):0.###}] " +
                    $"(X {(xDentro ? "adentro" : "AFUERA")}, Y {(yDentro ? "adentro" : "AFUERA")}), " +
                    $"host {host.Id}, bounding box real del elemento: {bbTexto}.");
            }

            MarcaEstribos.Marcar(barraCreada, host.Id);
            creadas++;
        }
        catch (Exception ex)
        {
            res.Avisos.Add($"{marcaColumna}: no se pudo crear una barra longitudinal ({GeometriaSeccion.Explicar(ex)}).");

            // Ver el mismo comentario en GeneradorArmado.GenerarColumna: una
            // creación fallida dentro de la transacción puede arrastrar a
            // las siguientes aunque no tengan relación entre sí.
            try { doc.Regenerate(); } catch { /* best-effort */ }
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

            Rebar? grapa = Rebar.CreateFromCurves(
                doc,
                RebarStyle.StirrupTie,
                barra,
                host,
                XYZ.BasisZ,
                new List<Curve> { linea },
                terminaciones,
                useExistingShapeIfPossible: true,
                createNewShape: true);

            if (grapa is null)
                throw new InvalidOperationException(
                    $"Revit no pudo crear la grapa con esta geometría (Rebar.CreateFromCurves devolvió null) " +
                    $"con la barra «{barra.Name}». Prueba otro tipo de gancho de 135° o una barra más delgada.");

            MarcaEstribos.Marcar(grapa, host.Id);
            creadas++;
        }
        catch (Exception ex)
        {
            res.Avisos.Add($"{marcaColumna}: no se pudo crear una grapa ({GeometriaSeccion.Explicar(ex)}).");
            try { doc.Regenerate(); } catch { /* best-effort */ }
        }
    }

    private static void CrearCaja(
        Document doc, FamilyInstance host, RebarBarType barra, RebarHookType gancho,
        double x1, double x2, double y1, double y2, double z,
        string marcaColumna, ResultadoArmado res, ref int creadas)
    {
        try
        {
            // Rectángulo cerrado, sin cortar ningún lado a la mitad -- ver el
            // comentario en GeneradorArmado.CrearGrupo: partir un lado deja
            // dos curvas colineales sin ángulo real entre ellas, y
            // Rebar.CreateFromCurves rechaza esa geometría (null) en cajas
            // no cuadradas. Prioriza no crashear sobre el detalle cosmético
            // de la esquina de cierre sin fillet.
            IList<Curve> curvas = new List<Curve>
            {
                Line.CreateBound(new XYZ(x1, y1, z), new XYZ(x2, y1, z)),
                Line.CreateBound(new XYZ(x2, y1, z), new XYZ(x2, y2, z)),
                Line.CreateBound(new XYZ(x2, y2, z), new XYZ(x1, y2, z)),
                Line.CreateBound(new XYZ(x1, y2, z), new XYZ(x1, y1, z)),
            };

            using var terminaciones = new BarTerminationsData(doc)
            {
                HookTypeIdAtStart = gancho.Id,
                HookTypeIdAtEnd = gancho.Id,
                TerminationOrientationAtStart = RebarTerminationOrientation.Left,
                TerminationOrientationAtEnd = RebarTerminationOrientation.Left,
            };

            Rebar? caja = Rebar.CreateFromCurves(
                doc,
                RebarStyle.StirrupTie,
                barra,
                host,
                XYZ.BasisZ,
                curvas,
                terminaciones,
                useExistingShapeIfPossible: true,
                createNewShape: true);

            if (caja is null)
                throw new InvalidOperationException(
                    $"Revit no pudo crear la caja con esta geometría (Rebar.CreateFromCurves devolvió null) " +
                    $"con la barra «{barra.Name}». Prueba otro tipo de gancho de 135° o una barra más delgada.");

            MarcaEstribos.Marcar(caja, host.Id);
            creadas++;
        }
        catch (Exception ex)
        {
            res.Avisos.Add($"{marcaColumna}: no se pudo crear una caja ({GeometriaSeccion.Explicar(ex)}).");
            try { doc.Regenerate(); } catch { /* best-effort */ }
        }
    }
}
