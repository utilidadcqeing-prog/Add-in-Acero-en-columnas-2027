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
///  - La geometría exacta de la "caja" (issue #26/#29-style: rectángulo más
///    chico centrado, usando la columna tildada como alcance en X y la fila
///    como alcance en Y, ver <see cref="Caja"/>) es una interpretación mía a
///    partir de la referencia visual de Luciano -- no la confirmó nadie del
///    equipo contra un plano real. Ver issue de seguimiento.
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

            (CurveLoop contornoCrudo, double _) = GeometriaSeccion.ObtenerContornoHost(host, cacheHost);
            (double xMin, double xMax, double yMin, double yMax) = GeometriaSeccion.Huella(contornoCrudo);
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
                niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra,
                contexto.Marca, res, ref creadas);
        }

        if (barraCaraX is not null && columnas > 0)
        {
            double insetX = recubrimientoPies + barraCaraX.BarNominalDiameter / 2;
            for (int c = 0; c < columnas; c++)
            {
                int cCapturado = c;
                CrearBarraConTraslapos(
                    doc, porHost, footprintPorHost, barraCaraX,
                    fp => (PosicionIntermedia(fp.XMin + insetEsquinas, fp.XMax - insetEsquinas, columnas, cCapturado), fp.YMin + insetX),
                    posicion++ % 2 == 0,
                    niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra,
                    contexto.Marca, res, ref creadas);

                CrearBarraConTraslapos(
                    doc, porHost, footprintPorHost, barraCaraX,
                    fp => (PosicionIntermedia(fp.XMin + insetEsquinas, fp.XMax - insetEsquinas, columnas, cCapturado), fp.YMax - insetX),
                    posicion++ % 2 == 0,
                    niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra,
                    contexto.Marca, res, ref creadas);
            }
        }

        if (barraCaraY is not null && filas > 0)
        {
            double insetY = recubrimientoPies + barraCaraY.BarNominalDiameter / 2;
            for (int f = 0; f < filas; f++)
            {
                int fCapturado = f;
                CrearBarraConTraslapos(
                    doc, porHost, footprintPorHost, barraCaraY,
                    fp => (fp.XMin + insetY, PosicionIntermedia(fp.YMin + insetEsquinas, fp.YMax - insetEsquinas, filas, fCapturado)),
                    posicion++ % 2 == 0,
                    niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra,
                    contexto.Marca, res, ref creadas);

                CrearBarraConTraslapos(
                    doc, porHost, footprintPorHost, barraCaraY,
                    fp => (fp.XMax - insetY, PosicionIntermedia(fp.YMin + insetEsquinas, fp.YMax - insetEsquinas, filas, fCapturado)),
                    posicion++ % 2 == 0,
                    niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra,
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

    /// <summary>
    /// Fase 5 (#7): arma UNA posición longitudinal (una esquina o una barra
    /// de cara) a lo largo de TODA la columna, resolviendo dos tipos de
    /// corte muy distintos entre sí:
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
        string marcaColumna,
        ResultadoArmado res,
        ref int creadas)
    {
        if (zFinTotalMetros - zInicioTotalMetros <= Tolerancia) return;

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

        var limites = new List<double> { zInicioTotalMetros };
        limites.AddRange(cortes.Select(c => c.Z));
        limites.Add(zFinTotalMetros);

        for (int s = 0; s < limites.Count - 1; s++)
        {
            bool iniEsReal = s >= 1 && cortes[s - 1].EsReal;
            bool finEsReal = s < cortes.Count && cortes[s].EsReal;

            double zIniSinSolape = limites[s];
            double zFinSinSolape = limites[s + 1];
            double zIni = iniEsReal ? zIniSinSolape - traslapoMetros / 2 : zIniSinSolape;
            double zFin = finEsReal ? zFinSinSolape + traslapoMetros / 2 : zFinSinSolape;

            if (zFin - zIni <= Tolerancia) continue;

            double medio = (zIniSinSolape + zFinSinSolape) / 2;
            var hostEntry = porHost.FirstOrDefault(h => medio >= h.ZInf - Tolerancia && medio <= h.ZSup + Tolerancia);
            if (hostEntry.IdHost is null || !footprintPorHost.TryGetValue(hostEntry.IdHost, out FootprintHost fp))
                continue; // host inexistente o borrado -- ya avisado al armar footprintPorHost

            (double x, double y) = calcularXY(fp);
            CrearBarraVertical(
                doc, fp.Host, barra, x, y,
                EstandarCQ.APies(zIni), EstandarCQ.APies(zFin),
                marcaColumna, res, ref creadas);
        }
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
        Document doc, FamilyInstance host, RebarBarType barra,
        double x, double y, double zInicio, double zFin,
        string marcaColumna, ResultadoArmado res, ref int creadas)
    {
        try
        {
            Line linea = Line.CreateBound(new XYZ(x, y, zInicio), new XYZ(x, y, zFin));

            // BarTerminationsData "vacío" (sin fijar hooks): recta, sin
            // gancho -- ConfigLongitudinal no tiene campo de gancho propio
            // para longitudinal todavía (ver comentario del archivo). El
            // traslapo en sí (Fase 5, #7) sí está resuelto: quien llama a
            // esto ya le pasa el segmento correcto con o sin solape.
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
            var curvasRectangulo = new List<Curve>
            {
                Line.CreateBound(new XYZ(x1, y1, z), new XYZ(x2, y1, z)),
                Line.CreateBound(new XYZ(x2, y1, z), new XYZ(x2, y2, z)),
                Line.CreateBound(new XYZ(x2, y2, z), new XYZ(x1, y2, z)),
                Line.CreateBound(new XYZ(x1, y2, z), new XYZ(x1, y1, z)),
            };

            // Ver GeometriaSeccion.ConEmpalmeEnMedioDeLado: sin esto, la
            // esquina donde el loop se cierra queda sin el fillet de 90°.
            IList<Curve> curvas = GeometriaSeccion.ConEmpalmeEnMedioDeLado(curvasRectangulo);

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
                curvas,
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
