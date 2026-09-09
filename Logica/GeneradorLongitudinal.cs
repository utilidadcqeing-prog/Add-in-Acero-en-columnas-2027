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

        // Ver GeometriaSeccion.BuscarFormaEstribo -- misma forma que usa
        // GeneradorArmado para los estribos principales, reutilizada acá para
        // las "cajas" (estribos menores), que tienen exactamente el mismo
        // problema de dobladura por ser también un lazo StirrupTie cerrado.
        RebarShape? formaEstribo = GeometriaSeccion.BuscarFormaEstribo(doc);

        // Empalmes reales (issue empalmes reales, ver
        // docs/planes/2026-09-09-empalmes-rebarspliceutils.md): si el
        // documento tiene un RebarSpliceType cargado, los traslapos que caen
        // en la mitad de una columna (no en un cambio de piso) se crean con
        // la API formal de Revit en vez de dos Rebar sueltos con corrimiento
        // a mano -- Revit los reconoce como empalme real. Sin tipo cargado,
        // se sigue con el método anterior para todo, sin romper nada.
        ElementId? tipoEmpalme = GeometriaSeccion.BuscarTipoEmpalme(doc);

        var cacheHost = new Dictionary<ElementId, (CurveLoop ContornoCrudo, double ZBase)>();

        foreach (ColumnaPlan columnaPlan in opciones.Plan.Columnas)
        {
            try
            {
                GenerarColumna(doc, columnaPlan, barrasPorNombre, ganchosPorNombre, formaEstribo, tipoEmpalme, cacheHost, res);
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
        RebarShape? formaEstribo,
        ElementId? tipoEmpalme,
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

        // Gancho del anclaje en la zapata (issue #37) -- no bloquea el resto
        // de la longitudinal si falta: se avisa y esa barra sale recta ahí,
        // igual que hoy, en vez de tirar abajo toda la columna por un gancho.
        RebarHookType? ganchoLongitudinal = null;
        if (contexto.IdZapata is not null)
        {
            if (string.IsNullOrEmpty(cfg.TipoGanchoLongitudinalNombre) ||
                !ganchosPorNombre.TryGetValue(cfg.TipoGanchoLongitudinalNombre, out ganchoLongitudinal))
            {
                res.Avisos.Add(
                    $"{contexto.Marca}: sin tipo de gancho de anclaje válido para la longitudinal; " +
                    "el anclaje en la zapata sale recto, sin gancho.");
            }
        }

        double recubrimientoPies = EstandarCQ.APies(contexto.Recubrimiento);

        // El recubrimiento se mide hasta la cara del ESTRIBO, no hasta la
        // barra longitudinal -- la longitudinal va pegada por DENTRO del
        // estribo (confirmado por William), así que su inset tiene que
        // sumar también el diámetro del estribo entero (atravesarlo),
        // no solo la mitad del diámetro de la barra longitudinal. Sin esto,
        // con un estribo grueso la barra longitudinal queda calculada más
        // cerca del borde de lo real y termina asomando por fuera del
        // estribo o pisándolo -- probado sobre Revit real. Se toma el
        // diámetro más grueso entre los tramos tildados de esta columna: si
        // varía de tramo a tramo, más vale un poco más adentro de lo
        // estrictamente necesario en algún tramo que afuera en otro.
        double diametroEstribo = columnaPlan.Tramos
            .OfType<ConfigTramo>()
            .Select(t => !string.IsNullOrEmpty(t.TipoBarraNombre) && barrasPorNombre.TryGetValue(t.TipoBarraNombre, out RebarBarType? be)
                ? be.BarNominalDiameter
                : 0)
            .DefaultIfEmpty(0)
            .Max();

        double insetEsquinas = recubrimientoPies + diametroEstribo + barraEsquinas.BarNominalDiameter / 2;

        int columnas = Math.Max(0, cfg.BarrasPorCaraX / 2);
        int filas = Math.Max(0, cfg.BarrasPorCaraY / 2);

        double insetX = barraCaraX is not null ? recubrimientoPies + diametroEstribo + barraCaraX.BarNominalDiameter / 2 : 0;
        double insetY = barraCaraY is not null ? recubrimientoPies + diametroEstribo + barraCaraY.BarNominalDiameter / 2 : 0;

        // Un tramo de barra por elemento host de la pila (no se puede
        // hostear un solo Rebar en dos FamilyInstance), agrupando TODOS los
        // tramos físicos de la columna, tildados o no -- confirmado por
        // William (reunión 9-sep): la longitudinal es de la columna física
        // completa (nudos, tramo contra zapata incluido), no solo de los
        // tramos que el usuario tildó para configurar estribos. Antes esta
        // función solo agrupaba tramos tildados, así que si el tramo de
        // arriba o el que toca la zapata no estaban tildados, la barra
        // longitudinal se quedaba corta o no anclaba en la zapata -- sin
        // ningún aviso.
        List<(string IdHost, double ZInf, double ZSup)> porHost = AgruparPorHost(contexto);
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

        // La "posición de referencia" (esquinas/caras) se calcula UNA sola
        // vez con el footprint del host de MÁS ARRIBA de la pila, y esa
        // misma X/Y se usa para toda la barra, en todos los hosts que
        // atraviesa -- antes se recalculaba por cada host con SU PROPIO
        // footprint, así que un elemento de sección distinta (el más común:
        // un dado/pedestal más ancho que la columna típica, apoyado sobre la
        // zapata) hacía que las barras de ESE tramo salieran corridas hacia
        // los bordes de esa sección más ancha, en vez de seguir derechas en
        // la misma posición que el resto de la barra -- probado sobre Revit
        // real, columna con dado ensanchado en la base. Se toma el host de
        // arriba (no el de abajo) porque en la práctica constructiva el
        // ensanche, si lo hay, siempre está en la base -- la columna típica
        // (arriba) es la sección de diseño real. Si por lo que sea ese host
        // no tiene footprint resuelto (se saltó por error), se usa
        // cualquiera de los que sí se pudieron leer.
        FootprintHost? referenciaNullable = footprintPorHost.TryGetValue(porHost[^1].IdHost, out FootprintHost fpTope)
            ? fpTope
            : footprintPorHost.Values.Cast<FootprintHost?>().FirstOrDefault();
        if (referenciaNullable is null)
            return; // ningún host de esta columna tiene footprint válido -- ya avisado arriba por cada uno.
        FootprintHost referencia = referenciaNullable.Value;

        // Solo el host de más abajo de la pila puede tener zapata debajo --
        // el longitudinal entra a anclarse hasta 7.5cm de su cara inferior
        // (#26), a diferencia del estribo.
        double zInicioTotal = contexto.ZInferiorLongitudinal;

        // Tramo de anclaje del longitudinal dentro de la zapata (issue #26):
        // vive DENTRO del sólido de la zapata, no del de la columna, así que
        // necesita hostearse ahí -- Revit no aloja un Rebar cuya curva se
        // sale del sólido de su host (probado sobre Revit real). Antes esto
        // no existía como host en `porHost`, así que ese tramo de barra
        // (justo el que debía anclar 7.5cm dentro de la zapata) nunca se
        // creaba: `CrearBarraConTraslapos` no encontraba ningún host ahí y
        // lo saltaba en silencio.
        if (contexto.IdZapata is not null && zInicioTotal < porHost[0].ZInf - Tolerancia)
        {
            if (long.TryParse(contexto.IdZapata, out long idZapataLong) &&
                doc.GetElement(new ElementId(idZapataLong)) is FamilyInstance zapataHost)
            {
                BoundingBoxXYZ? bbZapata = zapataHost.get_BoundingBox(null);
                if (bbZapata is not null)
                {
                    porHost.Insert(0, (IdHost: contexto.IdZapata, ZInf: zInicioTotal, ZSup: porHost[0].ZInf));
                    footprintPorHost[contexto.IdZapata] = new FootprintHost(
                        zapataHost, bbZapata.Min.X, bbZapata.Max.X, bbZapata.Min.Y, bbZapata.Max.Y);
                }
                else
                {
                    res.Avisos.Add($"{contexto.Marca}: no se pudo leer la geometría de la zapata; el longitudinal no ancla ahí.");
                }
            }
            else
            {
                res.Avisos.Add($"{contexto.Marca}: la zapata detectada ya no existe; el longitudinal no ancla ahí.");
            }
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

        // Corrimiento lateral en cada traslapo REAL (no en los cortes
        // obligados por límite de host que no coinciden con uno real): dos
        // barras que se traslapan no pueden ir exactamente en la misma línea
        // -- probado sobre el modelo de referencia de William (9-sep,
        // "Diagnóstico Rebar"): dos barras de un mismo traslapo salieron con
        // la MISMA X y la Y corrida ~1.9cm, que es casi exacto el diámetro de
        // la barra (#6, 19.1mm) -- se corren su propio diámetro, no una
        // distancia arbitraria.
        //
        // Al principio se asumió (sin confirmar) que el eje que corre es el
        // "fijo de cara" -- Y para esquinas/cara X, X para cara Y. Se corrigió
        // el 9-sep tras medir una barra de cara Y REAL contra el mismo modelo
        // de referencia (misma columna, un piso más arriba): corrió en Y
        // también, no en X. Con las dos mediciones reales (cara X y cara Y)
        // más lo que hace el propio corrimiento automático de Revit
        // (RebarSpliceShiftOption.BarPlane, probado en la sesión de
        // migración a RebarSpliceUtils) coincidiendo siempre en Y, el criterio
        // real del equipo es: siempre se corre en Y, sin importar la cara.
        // Ver docs/planes/2026-09-09-diagnostico-corrimiento-traslapo.md y
        // docs/planes/2026-09-09-empalmes-rebarspliceutils.md.
        // El corrimiento alterna: cada traslapo real vuelve a la posición de
        // diseño o la corre un diámetro hacia el centro de la sección,
        // turnándose -- así no se va acumulando corrimiento tramo tras tramo.
        (double Dx, double Dy) Corrimiento(RebarBarType barraDeEstaPosicion, double y)
        {
            double diametro = barraDeEstaPosicion.BarNominalDiameter;
            double centroY = (referencia.YMin + referencia.YMax) / 2;
            double signo = centroY >= y ? 1 : -1;
            return (0, signo * diametro);
        }

        (double X, double Y)[] esquinas =
        {
            (referencia.XMin + insetEsquinas, referencia.YMin + insetEsquinas),
            (referencia.XMax - insetEsquinas, referencia.YMin + insetEsquinas),
            (referencia.XMin + insetEsquinas, referencia.YMax - insetEsquinas),
            (referencia.XMax - insetEsquinas, referencia.YMax - insetEsquinas),
        };

        foreach ((double x, double y) in esquinas)
        {
            CrearBarraConTraslapos(
                doc, porHost, footprintPorHost, barraEsquinas, (x, y), Corrimiento(barraEsquinas, y),
                posicion++ % 2 == 0,
                niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra, cortesPorBarraYGrupo,
                contexto.IdZapata, ganchoLongitudinal, tipoEmpalme, contexto.Marca, res, ref creadas);
        }

        if (barraCaraX is not null && columnas > 0)
        {
            for (int c = 0; c < columnas; c++)
            {
                double x = PosicionIntermedia(referencia.XMin + insetEsquinas, referencia.XMax - insetEsquinas, columnas, c);
                double yInf = referencia.YMin + insetX;
                double ySup = referencia.YMax - insetX;

                CrearBarraConTraslapos(
                    doc, porHost, footprintPorHost, barraCaraX, (x, yInf), Corrimiento(barraCaraX, yInf),
                    posicion++ % 2 == 0,
                    niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra, cortesPorBarraYGrupo,
                    contexto.IdZapata, ganchoLongitudinal, tipoEmpalme, contexto.Marca, res, ref creadas);

                CrearBarraConTraslapos(
                    doc, porHost, footprintPorHost, barraCaraX, (x, ySup), Corrimiento(barraCaraX, ySup),
                    posicion++ % 2 == 0,
                    niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra, cortesPorBarraYGrupo,
                    contexto.IdZapata, ganchoLongitudinal, tipoEmpalme, contexto.Marca, res, ref creadas);
            }
        }

        if (barraCaraY is not null && filas > 0)
        {
            for (int f = 0; f < filas; f++)
            {
                double y = PosicionIntermedia(referencia.YMin + insetEsquinas, referencia.YMax - insetEsquinas, filas, f);
                double xInf = referencia.XMin + insetY;
                double xSup = referencia.XMax - insetY;

                CrearBarraConTraslapos(
                    doc, porHost, footprintPorHost, barraCaraY, (xInf, y), Corrimiento(barraCaraY, y),
                    posicion++ % 2 == 0,
                    niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra, cortesPorBarraYGrupo,
                    contexto.IdZapata, ganchoLongitudinal, tipoEmpalme, contexto.Marca, res, ref creadas);

                CrearBarraConTraslapos(
                    doc, porHost, footprintPorHost, barraCaraY, (xSup, y), Corrimiento(barraCaraY, y),
                    posicion++ % 2 == 0,
                    niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra, cortesPorBarraYGrupo,
                    contexto.IdZapata, ganchoLongitudinal, tipoEmpalme, contexto.Marca, res, ref creadas);
            }
        }

        if (creadas > 0)
            res.Detalles.Add($"{contexto.Marca}: {creadas} barra(s) longitudinal(es) creada(s) (con traslapos, Fase 5).");

        GenerarMenores(
            doc, columnaPlan, contexto, cfg, barrasPorNombre, ganchosPorNombre, formaEstribo, cacheHost,
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
        RebarShape? formaEstribo,
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

                        CrearCaja(doc, host, barraCaja, gancho, formaEstribo, x1, x2, y1, y2, z, contexto.Marca, res, ref creadas);
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
    /// Agrupa TODOS los tramos físicos de la columna (tildados o no) por
    /// elemento host, quedándose con el rango Z que cubren dentro de cada
    /// uno -- un Rebar longitudinal por host, no por tramo (corre continuo
    /// dentro de un mismo elemento aunque tenga un nudo de viga en el medio).
    /// La longitudinal siempre es de la columna física completa; el tildado
    /// de tramos en la UI es solo para configurar estribos (ver comentario
    /// en el llamador).
    /// </summary>
    private static List<(string IdHost, double ZInf, double ZSup)> AgruparPorHost(ColumnaContexto contexto)
    {
        var porHost = new List<(string IdHost, double ZInf, double ZSup)>();

        foreach (Tramo tramo in contexto.Tramos)
        {
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
        (double X, double Y) posicion,
        (double Dx, double Dy) corrimiento,
        bool esParGrupo,
        List<(int Ordinal, double ZInf, double ZSup)> niveles,
        double zInicioTotalMetros,
        double zFinTotalMetros,
        double longitudComercialMetros,
        Dictionary<(ElementId BarraId, bool EsPar), PlanDeCortes> cortesPorBarraYGrupo,
        string? idZapata,
        RebarHookType? ganchoLongitudinal,
        ElementId? tipoEmpalme,
        string marcaColumna,
        ResultadoArmado res,
        ref int creadas)
    {
        if (zFinTotalMetros - zInicioTotalMetros <= Tolerancia) return;

        // Copia local: un parámetro ref/out/in del método no se puede
        // capturar dentro de una función local (CS1628) -- se sincroniza de
        // vuelta a "creadas" al final.
        int creadasLocal = creadas;

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
        int totalSegmentos = limites.Count - 1;

        // Host de cada segmento, resuelto una vez -- se usa para agrupar
        // segmentos consecutivos del MISMO host. Rebar.SpliceRebar (la API
        // formal de empalmes, issue empalmes reales) solo puede partir una
        // barra en dos piezas que quedan alojadas en el mismo host -- nunca
        // puede unir dos hosts (columnas físicas) distintos. Un corte entre
        // dos segmentos de hosts DISTINTOS es siempre un cambio de piso
        // (forzado por Revit, real o no) y sigue necesitando dos Rebar
        // separados con el corrimiento de siempre; un corte entre dos
        // segmentos del MISMO host es un traslapo real en la mitad de un
        // tramo -- los candidatos de traslapo ya son la mitad de cada tramo
        // físico (ver GenerarColumna/niveles), así que esto es lo normal, no
        // la excepción -- y ahí sí se puede usar la API formal. Ver
        // docs/planes/2026-09-09-empalmes-rebarspliceutils.md.
        var hostDeCadaSegmento = new string?[totalSegmentos];
        for (int s = 0; s < totalSegmentos; s++)
        {
            double medio = (limites[s] + limites[s + 1]) / 2;
            var hostEntry = porHost.FirstOrDefault(h => medio >= h.ZInf - Tolerancia && medio <= h.ZSup + Tolerancia);
            hostDeCadaSegmento[s] = hostEntry.IdHost;
        }

        // Método de siempre: un Rebar por cada segmento del rango [sIni..sFin],
        // con la extensión de solape y el corrimiento a mano en los bordes
        // reales. Se usa para los cambios de piso (nunca aptos para la API
        // formal) y como resguardo si la API formal falla por algún motivo.
        void CrearRangoConMetodoAnterior(int sIni, int sFin)
        {
            for (int s = sIni; s <= sFin; s++)
            {
                bool iniEsReal = s >= 1 && cortes[s - 1].EsReal;
                bool finEsReal = s < cortes.Count && cortes[s].EsReal;

                double zIniSinSolape = limites[s];
                double zFinSinSolape = limites[s + 1];
                double zIni = iniEsReal ? zIniSinSolape - plan.TraslapoMetros / 2 : zIniSinSolape;
                double zFin = finEsReal ? zFinSinSolape + plan.TraslapoMetros / 2 : zFinSinSolape;

                if (zFin - zIni <= Tolerancia) continue;

                string? idHost = hostDeCadaSegmento[s];
                if (idHost is null || !footprintPorHost.TryGetValue(idHost, out FootprintHost fp))
                    continue; // host inexistente o borrado -- ya avisado al armar footprintPorHost
                var hostEntry = porHost.First(h => h.IdHost == idHost);

                // Ver comentario original (recorte a los límites reales del
                // host porque el medio solape es respecto al punto de
                // empalme, no respecto al host).
                zIni = Math.Max(zIni, hostEntry.ZInf);
                zFin = Math.Min(zFin, hostEntry.ZSup);
                if (zFin - zIni <= Tolerancia) continue;

                int traslaposRealesPrevios = cortes.Take(s).Count(c => c.EsReal);
                bool corrido = traslaposRealesPrevios % 2 == 1;
                double x = posicion.X + (corrido ? corrimiento.Dx : 0);
                double y = posicion.Y + (corrido ? corrimiento.Dy : 0);

                RebarHookType? ganchoDeEsteSegmento = idHost == idZapata ? ganchoLongitudinal : null;

                CrearBarraVertical(
                    doc, fp, barra, x, y,
                    EstandarCQ.APies(zIni), EstandarCQ.APies(zFin),
                    ganchoDeEsteSegmento, marcaColumna, res, ref creadasLocal);
            }
        }

        int s0 = 0;
        while (s0 < totalSegmentos)
        {
            int s1 = s0;
            while (s1 + 1 < totalSegmentos
                   && hostDeCadaSegmento[s1] is not null
                   && hostDeCadaSegmento[s1] == hostDeCadaSegmento[s1 + 1])
                s1++;

            string? idHost = hostDeCadaSegmento[s0];
            int cortesInternos = s1 - s0; // cortes ENTRE segmentos de esta corrida -- todos reales, por construcción (ver comentario arriba)

            if (idHost is null || tipoEmpalme is null || cortesInternos == 0
                || !footprintPorHost.TryGetValue(idHost, out FootprintHost fpGrupo))
            {
                // Sin traslapo real en la mitad de este host (un solo
                // segmento, lo normal), sin tipo de empalme cargado en el
                // documento, o host inválido -- método de siempre.
                CrearRangoConMetodoAnterior(s0, s1);
                s0 = s1 + 1;
                continue;
            }

            if (cortesInternos > 1)
            {
                // 2+ traslapos reales en la mitad del MISMO host (columna muy
                // alta -- ver el aviso de "no hay nivel disponible..." de
                // PlanificarSplicesReales). Nunca se pudo confirmar contra
                // Revit real si el corrimiento alterna bien entre más de un
                // empalme de la misma barra (la prueba de estrés del
                // diagnóstico dio un error de reglas, no llegó a ejecutarse
                // -- ver docs/planes/2026-09-09-empalmes-rebarspliceutils.md).
                // Por seguridad se usa el método anterior, no la API formal,
                // hasta confirmar ese caso.
                res.Avisos.Add(
                    $"{marcaColumna}: un tramo necesita más de un traslapo real en la misma barra -- se usa " +
                    "el corrimiento a mano en vez del empalme formal de Revit (caso todavía no verificado).");
                CrearRangoConMetodoAnterior(s0, s1);
                s0 = s1 + 1;
                continue;
            }

            // Exactamente UN traslapo real en la mitad de este host -- el
            // caso ya verificado contra Revit real. Se crea una sola barra
            // para todo el host y se parte con la API formal de empalmes.
            var hostEntryGrupo = porHost.First(h => h.IdHost == idHost);

            bool iniEsRealGrupo = s0 >= 1 && cortes[s0 - 1].EsReal;
            bool finEsRealGrupo = s1 < cortes.Count && cortes[s1].EsReal;
            double zIniGrupo = iniEsRealGrupo ? limites[s0] - plan.TraslapoMetros / 2 : limites[s0];
            double zFinGrupo = finEsRealGrupo ? limites[s1 + 1] + plan.TraslapoMetros / 2 : limites[s1 + 1];
            zIniGrupo = Math.Max(zIniGrupo, hostEntryGrupo.ZInf);
            zFinGrupo = Math.Min(zFinGrupo, hostEntryGrupo.ZSup);

            if (zFinGrupo - zIniGrupo <= Tolerancia)
            {
                s0 = s1 + 1;
                continue;
            }

            int realesAntesGrupo = cortes.Take(s0).Count(c => c.EsReal);
            bool corridoInicial = realesAntesGrupo % 2 == 1;
            double xGrupo = posicion.X + (corridoInicial ? corrimiento.Dx : 0);
            double yGrupo = posicion.Y + (corridoInicial ? corrimiento.Dy : 0);

            RebarHookType? ganchoGrupo = idHost == idZapata ? ganchoLongitudinal : null;

            Rebar? barraCompleta = CrearBarraVertical(
                doc, fpGrupo, barra, xGrupo, yGrupo,
                EstandarCQ.APies(zIniGrupo), EstandarCQ.APies(zFinGrupo),
                ganchoGrupo, marcaColumna, res, ref creadasLocal);

            if (barraCompleta is null)
            {
                // CrearBarraVertical ya avisó el motivo -- con esta misma
                // geometría el método anterior fallaría igual.
                s0 = s1 + 1;
                continue;
            }

            double zCorteMetros = cortes[s0].Z; // el único corte interno de esta corrida
            bool partida = IntentarPartirConSplice(
                doc, barraCompleta, zCorteMetros, tipoEmpalme,
                out Rebar? piezaInferior, out Rebar? piezaSuperior);

            if (partida && piezaInferior is not null && piezaSuperior is not null)
            {
                // SpliceRebar reemplaza la barra original por dos elementos
                // nuevos (ids distintos) -- la marca que puso CrearBarraVertical
                // en la barra original ya no está en ninguna de las dos
                // piezas, hay que volver a marcarlas.
                MarcaEstribos.Marcar(piezaInferior, fpGrupo.Host.Id);
                MarcaEstribos.Marcar(piezaSuperior, fpGrupo.Host.Id);
                creadasLocal++; // la barra original ya contaba 1 -- ahora son 2 piezas reales
            }
            else
            {
                // La API formal no pudo partir esta barra (tipo de empalme
                // sin holgura suficiente, geometría no apta, etc.) -- se
                // borra el intento y se cae al método anterior para no dejar
                // la barra sin el traslapo que hacía falta.
                res.Avisos.Add(
                    $"{marcaColumna}: no se pudo crear el empalme formal de Revit en un traslapo -- " +
                    "se usa el corrimiento a mano en su lugar.");
                try { doc.Delete(barraCompleta.Id); } catch { /* best-effort */ }
                creadasLocal--; // deshace el conteo de la barra original; el método anterior cuenta las suyas
                CrearRangoConMetodoAnterior(s0, s1);
            }

            s0 = s1 + 1;
        }

        creadas = creadasLocal;
    }

    /// <summary>
    /// Parte una barra ya creada en dos, en el punto Z indicado, usando la
    /// API formal de empalmes de Revit (<see cref="RebarSpliceUtils.SpliceRebar"/>,
    /// issue empalmes reales -- ver docs/planes/2026-09-09-empalmes-rebarspliceutils.md).
    /// A diferencia del método anterior (dos Rebar independientes con
    /// corrimiento calculado a mano), esto deja un RebarSplice real que
    /// Revit reconoce, y el corrimiento lateral lo calcula Revit mismo
    /// (RebarSpliceShiftOption del tipo de empalme -- confirmado contra el
    /// modelo real que da ~1 diámetro en Y, igual que el cálculo manual).
    /// Solo sirve para partir una barra en dos piezas que quedan en el MISMO
    /// host -- nunca para el corte entre dos columnas físicas distintas.
    /// Devuelve false (sin tirar excepción) si algo no sale bien, para que
    /// el llamador pueda caer al método anterior sin romper la generación.
    /// </summary>
    private static bool IntentarPartirConSplice(
        Document doc, Rebar barraAPartir, double zCorteMetros, ElementId tipoEmpalme,
        out Rebar? piezaInferior, out Rebar? piezaSuperior)
    {
        piezaInferior = null;
        piezaSuperior = null;
        try
        {
            if (barraAPartir.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0)[0]
                is not Line linea)
                return false;

            var opciones = new RebarSpliceOptions(doc, tipoEmpalme, RebarSplicePosition.Middle);
            XYZ ini = linea.GetEndPoint(0);
            // El punto se proyecta sobre la curva real de la barra (ver
            // RebarSpliceGeometry en la documentación de la API) -- alcanza
            // con que el Z sea el correcto, no hace falta que X/Y coincidan
            // exacto con la barra.
            XYZ punto = new XYZ(ini.X, ini.Y, EstandarCQ.APies(zCorteMetros));
            var geometria = new RebarSpliceGeometry(punto, XYZ.BasisZ);

            IList<ElementId> resultantes = RebarSpliceUtils.SpliceRebar(
                doc, barraAPartir.Id, opciones, new List<RebarSpliceGeometry> { geometria });
            if (resultantes.Count != 2) return false;

            List<Rebar> piezas = resultantes
                .Select(id => doc.GetElement(id) as Rebar)
                .Where(r => r is not null)
                .Select(r => r!)
                .ToList();
            if (piezas.Count != 2) return false;

            double ZMedio(Rebar r)
            {
                Curve c = r.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0)[0];
                return (c.GetEndPoint(0).Z + c.GetEndPoint(1).Z) / 2;
            }

            if (ZMedio(piezas[0]) <= ZMedio(piezas[1]))
                (piezaInferior, piezaSuperior) = (piezas[0], piezas[1]);
            else
                (piezaInferior, piezaSuperior) = (piezas[1], piezas[0]);

            return true;
        }
        catch
        {
            return false;
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

    private static Rebar? CrearBarraVertical(
        Document doc, FootprintHost fp, RebarBarType barra,
        double x, double y, double zInicio, double zFin,
        RebarHookType? ganchoDeAnclaje, string marcaColumna, ResultadoArmado res, ref int creadas)
    {
        FamilyInstance host = fp.Host;
        try
        {
            // zInicio < zFin siempre (todo este archivo arma los segmentos de
            // abajo hacia arriba) -- Start del Line es el extremo INFERIOR.
            Line linea = Line.CreateBound(new XYZ(x, y, zInicio), new XYZ(x, y, zFin));

            // BarTerminationsData: recta en ambos extremos salvo que este
            // segmento sea el que ancla en la zapata (issue #37), en cuyo
            // caso el extremo INFERIOR (Start) lleva el gancho de anclaje --
            // el superior (End) sigue sin gancho siempre, sea cual sea el
            // segmento.
            //
            // Orientación (Left): SIN confirmar todavía contra Revit real.
            // No hay ninguna convención ya probada en este código para
            // "adentro/afuera" en una barra vertical -- los únicos usos que
            // existen (estribos, grapas, cajas) son loops horizontales con
            // normal BasisZ, geometría completamente distinta a esta curva
            // vertical con normal BasisX. Se usa Left como primer intento
            // porque es lo que ya funciona en el resto del código, pero hay
            // que confirmarlo mirando el gancho en 3D (normativa colombiana:
            // el gancho SIEMPRE va hacia adentro, nunca hacia afuera) y
            // cambiar a Right acá si sale al revés.
            using var terminaciones = new BarTerminationsData(doc);
            if (ganchoDeAnclaje is not null)
            {
                terminaciones.HookTypeIdAtStart = ganchoDeAnclaje.Id;
                terminaciones.TerminationOrientationAtStart = RebarTerminationOrientation.Left;
            }

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
            return barraCreada;
        }
        catch (Exception ex)
        {
            res.Avisos.Add($"{marcaColumna}: no se pudo crear una barra longitudinal ({GeometriaSeccion.Explicar(ex)}).");

            // Ver el mismo comentario en GeneradorArmado.GenerarColumna: una
            // creación fallida dentro de la transacción puede arrastrar a
            // las siguientes aunque no tengan relación entre sí.
            try { doc.Regenerate(); } catch { /* best-effort */ }
            return null;
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
        Document doc, FamilyInstance host, RebarBarType barra, RebarHookType gancho, RebarShape? formaEstribo,
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

            using var terminaciones = new BarTerminationsData(doc)
            {
                HookTypeIdAtStart = gancho.Id,
                HookTypeIdAtEnd = gancho.Id,
                TerminationOrientationAtStart = RebarTerminationOrientation.Left,
                TerminationOrientationAtEnd = RebarTerminationOrientation.Left,
            };

            // Camino preferido (ver GeometriaSeccion.BuscarFormaEstribo): sin
            // la dobladura del quiebre a mitad de lado. Mismo respaldo que
            // GeneradorArmado.CrearGrupo si no hay forma o si falla.
            Rebar? caja = formaEstribo is not null
                ? CrearCajaDesdeForma(doc, host, curvasRectangulo, barra, formaEstribo, terminaciones, res, marcaColumna)
                : null;

            IList<Curve>? curvasFallback = null;
            if (caja is null)
            {
                curvasFallback = GeometriaSeccion.ConQuiebreEnMedioDeLado(curvasRectangulo);
                caja = Rebar.CreateFromCurves(
                    doc,
                    RebarStyle.StirrupTie,
                    barra,
                    host,
                    XYZ.BasisZ,
                    curvasFallback,
                    terminaciones,
                    useExistingShapeIfPossible: true,
                    createNewShape: true);
            }

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

    /// <summary>Mismo patrón que <c>GeneradorArmado.CrearDesdeForma</c>, ver ese comentario.</summary>
    private static Rebar? CrearCajaDesdeForma(
        Document doc, FamilyInstance host, IList<Curve> curvasRectangulo,
        RebarBarType barra, RebarShape forma, BarTerminationsData terminaciones,
        ResultadoArmado res, string marcaColumna)
    {
        try
        {
            (double xMin, double xMax, double yMin, double yMax) = GeometriaSeccion.Huella(curvasRectangulo);
            double z = curvasRectangulo[0].GetEndPoint(0).Z;
            XYZ origen = new(xMin, yMin, z);
            XYZ vectorX = new(xMax - xMin, 0, 0);
            XYZ vectorY = new(0, yMax - yMin, 0);

            Rebar? caja = Rebar.CreateFromRebarShape(doc, forma, barra, host, origen, XYZ.BasisX, XYZ.BasisY);
            if (caja is null) return null;

            caja.GetShapeDrivenAccessor().ScaleToBox(origen, vectorX, vectorY);

            ElementId idGancho = terminaciones.HookTypeIdAtStart;
            if (idGancho != ElementId.InvalidElementId)
            {
                try { caja.SetHookTypeId(0, idGancho); caja.SetHookTypeId(1, idGancho); }
                catch { /* la forma puede no tener posiciones de gancho -- no bloquea */ }

                try { caja.SetTerminationOrientation(0, RebarTerminationOrientation.Left); caja.SetTerminationOrientation(1, RebarTerminationOrientation.Left); }
                catch { /* idem */ }
            }

            return caja;
        }
        catch (Exception ex)
        {
            res.Avisos.Add(
                $"{marcaColumna}: no se pudo usar la forma «{forma.Name}» para una caja ({ex.Message}); " +
                "se usa el camino de respaldo.");
            return null;
        }
    }
}
