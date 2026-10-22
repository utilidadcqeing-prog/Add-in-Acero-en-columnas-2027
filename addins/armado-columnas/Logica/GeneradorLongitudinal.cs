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
///  - Fase 5 (#7, traslapos) e issue #41: cada posición longitudinal es UNA
///    sola varilla de la zapata al tope, creada por <see cref="BarraContinua"/>
///    y partida después con empalmes nativos de Revit. Un Rebar hosteado en
///    una columna SÍ puede recorrer toda la pila -- medido contra Revit real
///    el 7-oct (docs/planes/2026-10-07-factibilidad-barra-continua-41.md); la
///    suposición contraria, que obligaba a cortar en cada cambio de columna,
///    era falsa. Dónde va cada traslapo y cuánto mide cada varilla lo decide
///    <see cref="PlanTraslapos"/>, el mismo cálculo que dibuja la ventana
///    (docs/planes/2026-10-17-diseno-metodologia-traslapos.md): nunca en un
///    nudo, varillas en múltiplos del redondeo sin pasarse del largo
///    comercial, solape del cuadro como mínimo. Las posiciones se alternan
///    "una sí, una no" en el ANILLO del perímetro (ver
///    <see cref="ConfigLongitudinal.Anillo"/>): las pares son tipo A y las
///    impares tipo B, y traslapan en tramos distintos -- regla del equipo,
///    confirmada por Luciano el 11-sep (ver
///    docs/planes/2026-09-11-alternancia-de-traslapos-por-anillo.md). El
///    número de barra NO sale de A/B sino de la posición (esquinas / cara X /
///    cara Y), así que cada grupo puede tener varios diámetros, cada uno con
///    su propio plan de traslapos (Luciano, 19-oct). El
///    solape y el corrimiento lateral los pone Revit con un tipo de empalme
///    propio por largo (<see cref="TiposEmpalme"/>).
///  - Si la columna arranca sobre zapata, las barras longitudinales llevan
///    un gancho de 90° en las DOS puntas -- abajo el anclaje en la zapata
///    (issue #37) y arriba el remate (issue #59) -- y en ninguna junta
///    intermedia. Es el propio del add-in (<see cref="GanchoLongitudinal"/>),
///    ajustado al largo del cuadro; el de
///    <see cref="ConfigLongitudinal.TipoGanchoLongitudinalNombre"/> queda de
///    respaldo. Los dos miran hacia el centro de la sección (issue #43).
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

        // La misma forma que los estribos principales para los cerrados: la
        // propia del add-in (#64) o, si no se pudo, la del modelo
        // (GeometriaSeccion.BuscarFormaEstribo). El aviso ya lo dio GeneradorArmado.
        var propios = new TiposPropios(doc);
        RebarShape? formaEstribo = propios.FormaEstribo().Tipo ?? GeometriaSeccion.BuscarFormaEstribo(doc);

        // Mismo criterio que GeneradorArmado.Generar: una sola vez por
        // corrida, no por cada caja -- ver GeometriaSeccion.TieneLadoDiagonal.
        var formasRechazadas = new HashSet<ElementId>();

        // Un solo resolvedor por corrida: cachea los tipos de empalme propios
        // del add-in para no crearlos ni reconfigurarlos una vez por varilla.
        var tiposEmpalme = new TiposEmpalme(doc);

        // Igual: el gancho propio de 90° se crea y se ajusta una vez por barra
        // y por corrida, no una vez por columna.
        var ganchoPropio = new GanchoLongitudinal(doc);

        var cacheHost = new Dictionary<ElementId, (CurveLoop ContornoCrudo, double ZBase)>();

        // TEMPORAL -- ver BarraContinua.Traza. Queda en un archivo de texto
        // para poder leerlo sin copiar nada a mano desde Revit.
        // Solo en Debug: el paquete publicado (Release) no deja registros.
        var traza = new System.Text.StringBuilder();
#if DEBUG
        BarraContinua.Traza = linea => traza.AppendLine(linea);
        ganchoPropio.Traza = linea => traza.AppendLine(linea);
#endif

        foreach (ColumnaPlan columnaPlan in opciones.Plan.Columnas)
        {
            try
            {
                GenerarColumna(doc, columnaPlan, barrasPorNombre, ganchosPorNombre, formaEstribo, formasRechazadas, propios, tiposEmpalme, ganchoPropio, cacheHost, res);
            }
            catch (Exception ex)
            {
                string marca = columnaPlan.Contexto?.Marca ?? columnaPlan.Id;
                res.Avisos.Add($"{marca} (longitudinal): {GeometriaSeccion.Explicar(ex)}");
                traza.AppendLine($"EXCEPCIÓN en {marca}: {ex}");
            }
        }

#if DEBUG
        BarraContinua.Traza = null;
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "practicacq-longitudinal.log"), traza.ToString());
        }
        catch { /* best-effort */ }
#endif
    }

    private static void GenerarColumna(
        Document doc,
        ColumnaPlan columnaPlan,
        Dictionary<string, RebarBarType> barrasPorNombre,
        Dictionary<string, RebarHookType> ganchosPorNombre,
        RebarShape? formaEstribo,
        HashSet<ElementId> formasRechazadas,
        TiposPropios propios,
        TiposEmpalme tiposEmpalme,
        GanchoLongitudinal ganchoPropio,
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

        // Una configuración guardada con barras por tipo A/B (17 al 19-oct)
        // puede llegar sin las de cara (la ventana ya la migra al cargarla;
        // esto es por si acaso).
        cfg.MigrarBarrasPorCara();

        // El número de barra va por posición; A/B solo decide dónde traslapa.
        IReadOnlyList<PosicionBarra> posiciones = cfg.Anillo();
        var barraPorPosicion = new Dictionary<PosicionBarra, RebarBarType>();
        foreach (PosicionBarra posicion in posiciones.Distinct())
        {
            string? nombre = cfg.BarraDe(posicion);
            if (string.IsNullOrEmpty(nombre) || !barrasPorNombre.TryGetValue(nombre, out RebarBarType? barra))
            {
                res.Avisos.Add(
                    $"{contexto.Marca}: el tipo de barra de {RotuloPosicion(posicion)} «{nombre}» no existe en el documento; " +
                    "se omite el acero longitudinal de esta columna.");
                return;
            }

            barraPorPosicion[posicion] = barra;
        }

        List<RebarBarType> barrasUsadas = barraPorPosicion.Values.DistinctBy(b => b.Id).ToList();

        // Gancho del anclaje en la zapata (issue #37) y del remate (#59), solo
        // si hay zapata: el propio del add-in (GanchoLongitudinal), que se
        // ajusta más abajo, cuando ya se conoce la pila. El configurado del
        // modelo queda de respaldo.
        RebarHookType? ganchoDelModelo = null;
        if (contexto.IdZapata is not null && !string.IsNullOrEmpty(cfg.TipoGanchoLongitudinalNombre))
            ganchosPorNombre.TryGetValue(cfg.TipoGanchoLongitudinalNombre, out ganchoDelModelo);

        // El mismo cálculo que dibuja la pestaña «Traslapos» de la ventana:
        // lo que se vio es lo que se genera. Sin solución, la columna no lleva
        // longitudinal, ni grapas ni cajas (abrazarían barras que no están) --
        // docs/planes/2026-10-17-diseno-metodologia-traslapos.md, sección 6.
        PlanTraslapos.Resultado planTraslapos = PlanTraslapos.Calcular(
            new PlanTraslapos.Columna(contexto.Tramos, contexto.ZInferiorLongitudinal, contexto.ZSuperior, contexto.IdZapata is not null),
            posiciones.Where((_, i) => i % 2 == 0).Select(p => EstandarCQ.DiametroMm(barraPorPosicion[p])),
            posiciones.Where((_, i) => i % 2 == 1).Select(p => EstandarCQ.DiametroMm(barraPorPosicion[p])),
            new PlanTraslapos.Opciones(cfg.LongitudComercialBarra, cfg.RedondeoVarillas, cfg.SolapeMayorDeLosDos));
        if (!planTraslapos.TieneSolucion)
        {
            string motivos = string.Join(" ", planTraslapos.Todos.Select(p => p.Motivo).OfType<string>().Distinct());
            res.Avisos.Add(
                $"{contexto.Marca}: no se genera el acero longitudinal (ni sus grapas y cajas) porque los traslapos no tienen solución. {motivos}");
            return;
        }

        double recubrimientoPies = EstandarCQ.APies(columnaPlan.Recubrimiento ?? contexto.Recubrimiento);

        // El recubrimiento se mide hasta la cara del ESTRIBO, no hasta la
        // barra longitudinal -- la longitudinal va pegada por DENTRO del
        // estribo (confirmado por William), así que su inset tiene que
        // sumar también el diámetro del estribo entero (atravesarlo),
        // no solo la mitad del diámetro de la barra longitudinal. Sin esto,
        // con un estribo grueso la barra longitudinal queda calculada más
        // cerca del borde de lo real y termina asomando por fuera del
        // estribo o pisándolo -- probado sobre Revit real. Se toma el
        // diámetro más grueso entre los tramos aceptados de esta columna: si
        // varía de tramo a tramo, más vale un poco más adentro de lo
        // estrictamente necesario en algún tramo que afuera en otro.
        double diametroEstribo = columnaPlan.Tramos
            .OfType<ConfigTramo>()
            .Select(t => !string.IsNullOrEmpty(t.TipoBarraNombre) && barrasPorNombre.TryGetValue(t.TipoBarraNombre, out RebarBarType? be)
                ? be.BarNominalDiameter
                : 0)
            .DefaultIfEmpty(0)
            .Max();

        // Misma cuenta que la vista previa del panel (SeccionPlanta).
        double Inset(RebarBarType barra) => SeccionPlanta.Inset(recubrimientoPies, diametroEstribo, barra.BarNominalDiameter);

        // Las esquinas "de referencia" (para repartir las intermedias, los
        // límites del corrimiento y las grapas/cajas) usan la más gruesa de
        // las barras de la columna: la diferencia es de unos milímetros, y así
        // ninguna queda pisando el estribo. Cada barra se separa de su cara
        // según SU diámetro (ver el anillo, más abajo).
        RebarBarType barraGruesa = barrasUsadas.MaxBy(b => b.BarNominalDiameter)!;
        double insetEsquinas = Inset(barraGruesa);

        int columnas = Math.Max(0, cfg.BarrasPorCaraX / 2);
        int filas = Math.Max(0, cfg.BarrasPorCaraY / 2);

        double insetX = insetEsquinas;
        double insetY = insetEsquinas;

        // La longitudinal es de la columna física completa (William, 9-sep):
        // recorre TODOS los tramos, aceptados o no. Lo aceptado en la UI es
        // solo para configurar estribos.
        List<(string IdHost, double ZInf, double ZSup)> porHost = AgruparPorHost(contexto);

        // #44: un tramo sin columna del modelo detrás ya no corta la varilla
        // (es una sola de punta a punta), pero tampoco se calla.
        foreach (Tramo sinHost in contexto.Tramos.Where(t => t.IdElementoHost is null))
            res.Avisos.Add(
                $"{contexto.Marca}: el tramo de {sinHost.ZInferior:0.##} a {sinHost.ZSuperior:0.##} m no tiene ninguna " +
                "columna del modelo asociada; la barra longitudinal lo atraviesa igual, pero conviene revisar el modelo ahí.");

        // La pila: las columnas apiladas que existen y admiten armadura, de
        // abajo hacia arriba. La varilla se hostea en la primera y, una vez
        // partida, cada pieza pasa a la que la contiene (ver BarraContinua).
        //
        // La posición de las varillas sale de la ZONA COMÚN en planta de todas
        // ellas -- la intersección de sus huellas, o sea la sección más chica,
        // esté donde esté (#42). Antes salía de la columna de más arriba,
        // suponiendo que el ensanche siempre está en la base. Con una sola
        // varilla recta para toda la pila, tiene que caber en todas.
        var pila = new List<BarraContinua.ColumnaDePila>();
        double xMin = double.MinValue, xMax = double.MaxValue;
        double yMin = double.MinValue, yMax = double.MaxValue;
        foreach ((string idHostTexto, double zInf, double zSup) in porHost)
        {
            if (!long.TryParse(idHostTexto, out long idHostLong) ||
                doc.GetElement(new ElementId(idHostLong)) is not FamilyInstance host)
            {
                res.Avisos.Add(
                    $"{contexto.Marca}: el elemento {idHostTexto} ya no existe; la barra longitudinal atraviesa ese tramo igual.");
                continue;
            }

            if (RebarHostData.GetRebarHostData(host) is null)
            {
                res.Avisos.Add(
                    $"{contexto.Marca}: el elemento {idHostTexto} no admite armadura; la barra longitudinal atraviesa ese " +
                    "tramo igual, hosteada en otra columna de la pila.");
                continue;
            }

            (CurveLoop contornoCrudo, double _) = GeometriaSeccion.ObtenerContornoHost(host, cacheHost);
            (double hXMin, double hXMax, double hYMin, double hYMax) = GeometriaSeccion.Huella(contornoCrudo);
            xMin = Math.Max(xMin, hXMin);
            xMax = Math.Min(xMax, hXMax);
            yMin = Math.Max(yMin, hYMin);
            yMax = Math.Min(yMax, hYMax);

            pila.Add(new BarraContinua.ColumnaDePila(host, EstandarCQ.APies(zInf), EstandarCQ.APies(zSup)));
        }

        if (pila.Count == 0)
        {
            res.Avisos.Add($"{contexto.Marca}: ninguna columna de la pila admite armadura; se omite el acero longitudinal.");
            return;
        }

        string zonaComun =
            $"{GeometriaSeccion.Metros(Math.Max(0, xMax - xMin)) * 100:0.#}×{GeometriaSeccion.Metros(Math.Max(0, yMax - yMin)) * 100:0.#} cm";

        // Si la sección común es muy chica para el recubrimiento + la barra,
        // el inset cruza el medio de la sección y CreateFromCurves devuelve
        // null sin decir por qué (probado sobre Revit real). Incluye el caso
        // de columnas desalineadas, donde la zona común sale vacía.
        if ((xMax - xMin) - 2 * insetEsquinas <= Tolerancia || (yMax - yMin) - 2 * insetEsquinas <= Tolerancia)
        {
            res.Avisos.Add(
                $"{contexto.Marca}: la zona común a todas las columnas de la pila ({zonaComun}) es demasiado chica para el " +
                $"recubrimiento + la barra «{barraGruesa.Name}»; se omite el acero longitudinal de esta columna.");
            return;
        }

        // El gancho de cada barra: el propio, ajustado al cuadro para que el
        // largo de corte en Revit sea el de la pestaña «Traslapos»; si no se
        // pudo, el configurado del modelo (y si tampoco hay, sale recta).
        var ganchoPorBarra = new Dictionary<ElementId, RebarHookType?>();
        if (contexto.IdZapata is not null)
        {
            var puntoDeMedicion = new XYZ((xMin + xMax) / 2, (yMin + yMax) / 2, pila[0].ZInf + EstandarCQ.APies(0.10));
            foreach (RebarBarType barra in barrasUsadas)
            {
                GanchoLongitudinal.Resultado propio = ganchoPropio.Resolver(barra, pila[0].Host, puntoDeMedicion);
                if (propio.Aviso is not null) res.Avisos.Add($"{contexto.Marca}: {propio.Aviso}");
                ganchoPorBarra[barra.Id] = propio.Gancho ?? ganchoDelModelo;

                if (propio.Gancho is null)
                    res.Avisos.Add(ganchoDelModelo is null
                        ? $"{contexto.Marca}: sin gancho de anclaje para «{barra.Name}»; el anclaje en la zapata y el remate salen rectos."
                        : $"{contexto.Marca}: «{barra.Name}» usa el gancho del modelo «{ganchoDelModelo.Name}»; su largo en Revit " +
                          "puede no coincidir con el de la pestaña «Traslapos».");
            }
        }

        RebarHookType? GanchoDe(RebarBarType barra) => ganchoPorBarra.TryGetValue(barra.Id, out RebarHookType? g) ? g : null;

        var referencia = new FootprintHost(pila[0].Host, xMin, xMax, yMin, yMax);

        BarraContinua.Traza?.Invoke($"=== COLUMNA {contexto.Marca} ===");
        foreach (Tramo t in contexto.Tramos)
            BarraContinua.Traza?.Invoke(
                $"tramo {t.Indice}: {t.Tipo} Z {t.ZInferior:0.###} -> {t.ZSuperior:0.###} host {t.IdElementoHost ?? "(ninguno)"}");
        foreach (BarraContinua.ColumnaDePila c in pila)
            BarraContinua.Traza?.Invoke(
                $"pila: {c.Host.Id.Value} Z {GeometriaSeccion.Metros(c.ZInf):0.###} -> {GeometriaSeccion.Metros(c.ZSup):0.###}");
        BarraContinua.Traza?.Invoke(
            $"zona común X[{GeometriaSeccion.Metros(xMin):0.###}, {GeometriaSeccion.Metros(xMax):0.###}] " +
            $"Y[{GeometriaSeccion.Metros(yMin):0.###}, {GeometriaSeccion.Metros(yMax):0.###}]; zapata: {contexto.IdZapata ?? "no"}; " +
            $"ZInferiorLongitudinal {contexto.ZInferiorLongitudinal:0.###}, ZSuperior {contexto.ZSuperior:0.###}; " +
            $"longitud comercial {cfg.LongitudComercialBarra:0.#} m, redondeo {cfg.RedondeoVarillas:0.00} m");

        // El grupo par/impar del traslapo intercalado sale de la posición de
        // cada barra EN EL ANILLO del perímetro (ver más abajo, "anillo"): la
        // regla del equipo, confirmada por Luciano el 11-sep con un dibujo, es
        // "uno sí, uno no" dando la vuelta -- dos barras VECINAS nunca
        // traslapan a la misma altura.

        // Las posiciones longitudinales EN ORDEN DE ANILLO, dando una vuelta
        // completa al perímetro: esquina inferior izquierda, cara inferior de
        // izquierda a derecha, esquina inferior derecha, cara derecha de abajo
        // hacia arriba, esquina superior derecha, cara superior de derecha a
        // izquierda, esquina superior izquierda, y cara izquierda de arriba
        // hacia abajo.
        //
        // El ORDEN es lo que importa: de acá sale el grupo par/impar del
        // traslapo intercalado. Antes salía del orden en que se creaban las
        // barras (las 4 esquinas, después los pares de cara X, después los de
        // cara Y), que no recorre el perímetro -- dejaba barras vecinas en el
        // mismo grupo, incluso en el caso más simple de solo 4 esquinas
        // (quedaban juntas las dos de la derecha).
        var anillo = new List<(RebarBarType Barra, double X, double Y, XYZ Normal, bool Esquina)>();

        double xEsqIzq = referencia.XMin + insetEsquinas;
        double xEsqDer = referencia.XMax - insetEsquinas;
        double yEsqAbajo = referencia.YMin + insetEsquinas;
        double yEsqArriba = referencia.YMax - insetEsquinas;

        // En cada traslapo las dos piezas no pueden ir en la misma línea: una
        // se corre un diámetro HACIA EL LADO, a lo largo de la cara donde apoya
        // la barra -- nunca hacia adentro del hormigón (regla del equipo,
        // dibujo de Luciano del 9-sep y reconfirmada el 7-oct; ver
        // docs/planes/2026-09-09-corrimiento-paralelo-a-la-cara.md).
        //
        // El corrimiento lo pone Revit (empalme nativo), y lo único que se le
        // puede elegir es el EJE: va a lo largo de la normal de la barra,
        // siempre hacia su lado positivo (+X o +Y), sin importar el sentido de
        // la normal -- medido el 7-oct, ver
        // docs/planes/2026-10-07-factibilidad-barra-continua-41.md. Por eso:
        //  - barras de las caras inferior/superior (varían en X): normal X;
        //  - barras de las caras izquierda/derecha (varían en Y): normal Y;
        //  - esquinas: apoyan en dos caras, así que se elige el eje cuyo lado
        //    positivo va a lo largo de una cara y no se sale por la punta: Y
        //    en las dos de abajo, X en las dos de arriba. En la esquina
        //    superior derecha los dos lados positivos se salen: ahí
        //    BarraContinua corre la varilla entera un diámetro para el otro
        //    lado (ver BarraContinua.Alinear y los límites de abajo). Este es
        //    solo el eje de partida: desde el 21-oct el sentido del dibujo
        //    elige hacia qué lado se corre, y en las esquinas se prueban los
        //    dos ejes (ver más abajo, AcomodoTraslapos.Cuatro).
        //
        // La barra de cada posición sale de cfg.Anillo(), el mismo orden en
        // que se recorre acá: así A/B (dónde traslapa) y la cara (qué número
        // lleva) no se pueden desincronizar.
        RebarBarType BarraDe(int indice) => barraPorPosicion[posiciones[indice]];

        // Los estribos principales ya están creados: dónde quedaron sus
        // dobleces y sus ganchos de verdad.
        EstribosMedidos estribos = MedirEstribos(doc, pila.Select(c => c.Host));
        IReadOnlyList<AcomodoTraslapos.Pieza> ganchos = estribos.Ganchos;

        void AgregarEsquina(bool izquierda, bool abajo)
        {
            RebarBarType barra = BarraDe(anillo.Count);
            double inset = Inset(barra);
            double x = izquierda ? referencia.XMin + inset : referencia.XMax - inset;
            double y = abajo ? referencia.YMin + inset : referencia.YMax - inset;

            // La barra de esquina va dentro del doblez del estribo, tocándolo
            // (como la de una grapa, #130). La cuenta del inset la deja unos
            // milímetros afuera, pisando el doblez: con la normal en X o en Y
            // Revit la arrimaba sola, pero con la normal diagonal no (medido
            // el 21-oct en la columna 387983: quedó en (3,307; -0,088) en vez
            // de (3,314; -0,081), metida 8,8 mm en el doblez del gancho).
            (double xd, double yd) = EnElDoblez(estribos.Dobleces, x, y, barra.BarNominalDiameter / 2);
            if (Math.Abs(xd - x) + Math.Abs(yd - y) > 1e-6)
                BarraContinua.Traza?.Invoke(
                    $"ESQUINA «{barra.Name}» dentro del doblez del estribo: ({GeometriaSeccion.Metros(x):0.####}, {GeometriaSeccion.Metros(y):0.####}) -> " +
                    $"({GeometriaSeccion.Metros(xd):0.####}, {GeometriaSeccion.Metros(yd):0.####}).");
            anillo.Add((barra, xd, yd, abajo ? XYZ.BasisY : XYZ.BasisX, true));
        }

        // Intermedias de las caras inferior/superior: varían en X.
        void AgregarDeCaraX(int c, bool abajo)
        {
            RebarBarType barra = BarraDe(anillo.Count);
            double x = PosicionIntermedia(xEsqIzq, xEsqDer, columnas, c);
            double y = abajo ? referencia.YMin + Inset(barra) : referencia.YMax - Inset(barra);
            anillo.Add((barra, x, y, XYZ.BasisX, false));
        }

        // Intermedias de las caras izquierda/derecha: varían en Y.
        void AgregarDeCaraY(int f, bool izquierda)
        {
            RebarBarType barra = BarraDe(anillo.Count);
            double y = PosicionIntermedia(yEsqAbajo, yEsqArriba, filas, f);
            double x = izquierda ? referencia.XMin + Inset(barra) : referencia.XMax - Inset(barra);
            anillo.Add((barra, x, y, XYZ.BasisY, false));
        }

        AgregarEsquina(izquierda: true, abajo: true);
        for (int c = 0; c < columnas; c++) AgregarDeCaraX(c, abajo: true);
        AgregarEsquina(izquierda: false, abajo: true);
        for (int f = 0; f < filas; f++) AgregarDeCaraY(f, izquierda: false);
        AgregarEsquina(izquierda: false, abajo: false);
        for (int c = columnas - 1; c >= 0; c--) AgregarDeCaraX(c, abajo: false);
        AgregarEsquina(izquierda: true, abajo: false);
        for (int f = filas - 1; f >= 0; f--) AgregarDeCaraY(f, izquierda: true);

        // En un anillo CERRADO la alternancia estricta solo existe si la
        // cantidad de posiciones es par. Con el modelo de hoy siempre lo es
        // (4 + 2*columnas + 2*filas), pero no se deja implícito.
        if (anillo.Count % 2 != 0)
            res.Avisos.Add(
                $"{contexto.Marca}: la sección tiene {anillo.Count} barras longitudinales (cantidad impar), " +
                "así que dos barras vecinas van a traslapar a la misma altura -- con un número impar " +
                "alrededor del perímetro no hay forma de alternarlas todas.");

        // Los avisos de empalme son los mismos para todas las varillas de un
        // mismo tipo de barra: uno por columna y motivo, no uno por varilla.
        var avisosDeBarras = new HashSet<string>();

        int piezas = 0;
        // Las varillas creadas: se terminan de acomodar todas juntas al final,
        // para que las piezas corridas de los traslapos no choquen entre sí.
        var creadas = new List<(BarraContinua.Pedido Pedido, IReadOnlyList<Rebar> Cadena)>();

        // Primero se arma el pedido de cada varilla y se elige, ANTES de crear
        // nada, cómo dejar cada una para que las piezas corridas no choquen:
        // de cuál pieza se corre y hacia dónde sale el sentido del dibujo
        // (AcomodoTraslapos.Cuatro). Después, con las piezas ya en el modelo,
        // BarraContinua.Acomodar afina el lado con las posiciones reales.
        var pedidos = new List<BarraContinua.Pedido>();
        var maneras = new List<IReadOnlyList<(AcomodoTraslapos.Manera Manera, XYZ Normal)>?>();
        for (int i = 0; i < anillo.Count; i++)
        {
            (RebarBarType barraDeLaPosicion, double x, double y, XYZ normal, bool esquina) = anillo[i];

            PlanTraslapos.PlanTipo plan = planTraslapos.De(grupoA: i % 2 == 0, EstandarCQ.DiametroMm(barraDeLaPosicion));

            // Un tipo de empalme por largo de traslapo; solo se crean los que
            // de verdad se usan.
            var traslapos = new List<BarraContinua.TraslapoPedido>();
            foreach (PlanTraslapos.Traslapo t in plan.Traslapos)
            {
                TiposEmpalme.Resultado tipo = tiposEmpalme.Resolver(barraDeLaPosicion, t.Largo);
                if (tipo.Aviso is not null) avisosDeBarras.Add(tipo.Aviso);
                if (tipo.Tipo is not null) traslapos.Add(new BarraContinua.TraslapoPedido(EstandarCQ.APies(t.Centro), tipo.Tipo));
            }

            (RebarTerminationOrientation orientacionAbajo, RebarTerminationOrientation orientacionArriba) =
                OrientacionesHaciaAdentro(referencia, x, y, normal);

            // De esquina a esquina de la armadura; se abre hasta la propia
            // barra por si su inset es menor que el de las esquinas.
            var limites = new BarraContinua.Limites(
                Math.Min(xEsqIzq, x), Math.Max(xEsqDer, x), Math.Min(yEsqAbajo, y), Math.Max(yEsqArriba, y));

            var pedido = new BarraContinua.Pedido(
                barraDeLaPosicion, x, y, normal,
                EstandarCQ.APies(contexto.ZInferiorLongitudinal), EstandarCQ.APies(contexto.ZSuperior),
                traslapos, pila,
                GanchoDe(barraDeLaPosicion), GanchoDe(barraDeLaPosicion), orientacionAbajo, orientacionArriba,
                limites);
            pedidos.Add(pedido);

            // Las cuatro maneras solo están medidas con un traslapo (dos
            // piezas); con más, la varilla va como siempre. Una esquina apoya
            // en dos caras, así que su pieza corrida puede ir a lo largo de
            // cualquiera de las dos: se prueban los dos ejes (primero el de
            // siempre). Medido el 21-oct en la columna 387983: con eje X en las
            // dos de arriba, la corrida de la esquina iba a lo largo de la cara
            // de arriba, contra la vecina (se metían 8,8 y 12 mm), mientras que
            // abajo, con eje Y, la cara quedaba limpia.
            //
            // Además, la diagonal hacia adentro: en la esquina donde cierra el
            // estribo, las colas de los dos ganchos entran en diagonal, una a
            // cada lado de la barra, y lo único libre es correrla entre ellas
            // (Luciano, 21-oct: "que se mueva diagonalmente, pero pegada al
            // gancho").
            //
            // Medido el 21-oct en la columna 387983 con normales diagonales:
            // Revit corre la pieza como si la normal tuviera siempre la Y
            // positiva -- con (0,707, 0,707) la corrió hacia (+, +), pero con
            // (−0,707, −0,707) también hacia (+, +), y con (0,707, −0,707)
            // hacia (−, +). Si se le pasa al revés, la pieza sale para el otro
            // lado y Alinear tiene que mover la cadena en diagonal, cosa que
            // Revit no respeta (en la esquina de arriba a la derecha la corrió
            // 36 mm en Y en vez de 18 y 18, y la barra quedó atravesando el
            // estribo). Por eso la diagonal se pasa con la Y positiva: Cuatro
            // prueba los dos sentidos igual, y así lo que se planea es lo que
            // Revit hace. Los ejes X e Y ya van positivos.
            //
            // Si la barra lleva gancho, no se prueba la diagonal: el gancho
            // dobla perpendicular a la normal y con la diagonal no miraría al
            // centro.
            double radio = barraDeLaPosicion.BarNominalDiameter / 2;
            if (traslapos.Count == 1 && plan.Piezas.Count == 2)
            {
                var deEstaVarilla = new List<(AcomodoTraslapos.Manera, XYZ)>();
                XYZ diagonal = new XYZ(
                    Math.Sign((referencia.XMin + referencia.XMax) / 2 - x), Math.Sign((referencia.YMin + referencia.YMax) / 2 - y), 0).Normalize();
                if (diagonal.Y < 0) diagonal = diagonal.Negate();
                bool conGancho = pedido.GanchoAbajo is not null || pedido.GanchoArriba is not null;
                XYZ otroEje = normal.IsAlmostEqualTo(XYZ.BasisX) ? XYZ.BasisY : XYZ.BasisX;
                XYZ[] ejes = !esquina ? new[] { normal }
                    : conGancho ? new[] { normal, otroEje }
                    : new[] { normal, otroEje, diagonal };
                foreach (XYZ eje in ejes)
                    foreach (AcomodoTraslapos.Manera m in AcomodoTraslapos.Cuatro(
                                 x, y, radio, (eje.X, eje.Y), barraDeLaPosicion.BarNominalDiameter,
                                 (EstandarCQ.APies(plan.Piezas[0].ZDesde), EstandarCQ.APies(plan.Piezas[0].ZHasta)),
                                 (EstandarCQ.APies(plan.Piezas[1].ZDesde), EstandarCQ.APies(plan.Piezas[1].ZHasta)),
                                 (limites.XMin, limites.XMax, limites.YMin, limites.YMax)))
                        deEstaVarilla.Add((m, eje));
                maneras.Add(deEstaVarilla);
            }
            else
            {
                maneras.Add(null);
            }
        }

        // Ninguna pieza corrida puede atravesar los ganchos del estribo
        // principal (Luciano, 21-oct).
        int[] elegidas = AcomodoTraslapos.Elegir(maneras.Select((m, i) => m is { Count: > 0 }
            ? new AcomodoTraslapos.Varilla(m.Select(x => x.Manera.Piezas).ToList())
            : new AcomodoTraslapos.Varilla(new[] { new[] {
                new AcomodoTraslapos.Pieza(pedidos[i].X, pedidos[i].Y, pedidos[i].Barra.BarNominalDiameter / 2, pedidos[i].ZInicio, pedidos[i].ZFin) } })).ToList(),
            ganchos);
        if (!AcomodoTraslapos.UltimaFueCompleta)
            BarraContinua.Traza?.Invoke("ACOMODO: llegó al tope de pasos; quedan las maneras más buenas que encontró hasta ahí.");

        for (int i = 0; i < pedidos.Count; i++)
        {
            (AcomodoTraslapos.Manera Manera, XYZ Normal)? elegida = maneras[i] is { Count: > 0 } m ? m[elegidas[i]] : null;
            AcomodoTraslapos.Manera? manera = elegida?.Manera;
            BarraContinua.Pedido pedido = pedidos[i];
            if (elegida is { } e)
            {
                pedido = pedido with { DeArribaAAbajo = e.Manera.DeArribaAAbajo };

                // Con la normal diagonal, Revit no respeta mover la cadena en
                // diagonal después (medido el 21-oct: de dos esquinas, una vez
                // quedó bien una y otra vez la otra; la que no, corrida 36 mm en
                // Y y atravesando el estribo). Si la corrida tiene que quedar
                // hacia −normal, se dibuja la varilla ya corrida un diámetro
                // hacia −normal: Revit corre la pieza donde empieza el dibujo
                // +1 diámetro, que la deja en la posición pedida, y la otra
                // queda corrida donde se planeó. Así no hay nada que mover.
                bool diagonal = !e.Normal.IsAlmostEqualTo(XYZ.BasisX) && !e.Normal.IsAlmostEqualTo(XYZ.BasisY);
                if (diagonal && e.Manera.Signo < 0)
                    pedido = pedido with { Desfase = e.Normal.Negate() * pedido.Barra.BarNominalDiameter };
                if (!e.Normal.IsAlmostEqualTo(pedido.Normal))
                {
                    // El gancho dobla según la normal: se recalcula hacia adentro.
                    (RebarTerminationOrientation abajo, RebarTerminationOrientation arriba) =
                        OrientacionesHaciaAdentro(referencia, pedido.X, pedido.Y, e.Normal);
                    pedido = pedido with { Normal = e.Normal, OrientacionAbajo = abajo, OrientacionArriba = arriba };
                    BarraContinua.Traza?.Invoke(
                        $"EJE ({e.Normal.X:0.###}, {e.Normal.Y:0.###}) para la esquina «{pedido.Barra.Name}» en ({GeometriaSeccion.Metros(pedido.X):0.###}, " +
                        $"{GeometriaSeccion.Metros(pedido.Y):0.###}): así su pieza corrida va por la otra cara y no choca.");
                }
            }
            if (manera is not null && pedido.DeArribaAAbajo)
                BarraContinua.Traza?.Invoke(
                    $"DIBUJO de arriba hacia abajo para la varilla «{pedido.Barra.Name}» en ({GeometriaSeccion.Metros(pedido.X):0.###}, " +
                    $"{GeometriaSeccion.Metros(pedido.Y):0.###}): así se corre su pieza de " +
                    $"{(manera.CorridaLaDeArriba ? "arriba" : "abajo")} y no choca.");

            BarraContinua.Resultado creada = BarraContinua.Crear(doc, pedido);

            piezas += creada.Piezas;
            if (creada.Aviso is not null) avisosDeBarras.Add(creada.Aviso);
            if (creada.Cadena is { Count: > 0 } cadena) creadas.Add((pedido, cadena));
        }

        // Dónde quedó de verdad cada varilla, ya acomodada: las grapas y los
        // cerrados la buscan acá (#130).
        IReadOnlyList<IReadOnlyList<BarraContinua.PiezaReal>> acomodadas = BarraContinua.Acomodar(doc, creadas, ganchos);
        var barrasReales = new List<BarraReal>();
        for (int i = 0; i < creadas.Count; i++)
            if (acomodadas[i].Count > 0)
                barrasReales.Add(new BarraReal(creadas[i].Pedido.Barra.BarNominalDiameter / 2, acomodadas[i]));

        // Si aun así alguna pieza atraviesa el gancho del estribo, se avisa
        // (Luciano, 21-oct). Puede ser la pieza corrida de un traslapo o la
        // barra misma, en su posición, si la cola del gancho le pasa encima.
        double tolerancia = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Millimeters);
        foreach (BarraReal b in barrasReales)
        {
            var piezasReales = b.Piezas.Select(q => new AcomodoTraslapos.Pieza(q.X, q.Y, b.Radio, q.ZInf, q.ZSup)).ToList();
            double metida = AcomodoTraslapos.MaximaMetida(piezasReales, ganchos);
            if (metida <= tolerancia) continue;
            BarraContinua.PiezaReal primera = b.Piezas[0];
            res.Avisos.Add(
                $"{contexto.Marca}: la barra longitudinal en ({GeometriaSeccion.Metros(primera.X):0.###}, {GeometriaSeccion.Metros(primera.Y):0.###}) m " +
                $"atraviesa el gancho del estribo ({GeometriaSeccion.Metros(metida) * 1000:0.#} mm); no hay lugar para dejarla sin tocarlo. " +
                "Revisar la separación de las barras o la esquina donde cierra el estribo.");
        }

        // Y si alguna pieza quedó fuera de las esquinas, está atravesando el
        // estribo: pasó el 21-oct cuando Revit no respetó el movimiento en
        // diagonal de una cadena (ver arriba, la normal diagonal).
        for (int i = 0; i < creadas.Count; i++)
        {
            BarraContinua.Limites l = creadas[i].Pedido.Limites;
            foreach (BarraContinua.PiezaReal q in acomodadas[i])
            {
                double afuera = Math.Max(
                    Math.Max(l.XMin - q.X, q.X - l.XMax),
                    Math.Max(l.YMin - q.Y, q.Y - l.YMax));
                if (afuera <= tolerancia) continue;
                res.Avisos.Add(
                    $"{contexto.Marca}: una pieza de la barra longitudinal en ({GeometriaSeccion.Metros(creadas[i].Pedido.X):0.###}, " +
                    $"{GeometriaSeccion.Metros(creadas[i].Pedido.Y):0.###}) m quedó en ({GeometriaSeccion.Metros(q.X):0.###}, {GeometriaSeccion.Metros(q.Y):0.###}) m, " +
                    $"{GeometriaSeccion.Metros(afuera) * 1000:0.#} mm por fuera de las esquinas: atraviesa el estribo. Revisarla en el modelo.");
                break;
            }
        }

        foreach (string aviso in avisosDeBarras)
            res.Avisos.Add($"{contexto.Marca}: {aviso}");

        if (piezas > 0)
            res.Detalles.Add(
                $"{contexto.Marca}: {anillo.Count} barra(s) longitudinal(es) de punta a punta, {piezas} pieza(s) con sus traslapos.");

        GenerarMenores(
            doc, columnaPlan, contexto, cfg, barrasPorNombre, ganchosPorNombre, formaEstribo, formasRechazadas, propios,
            columnas, filas, insetEsquinas, insetX, insetY, recubrimientoPies, diametroEstribo,
            barraPorPosicion.GetValueOrDefault(PosicionBarra.CaraX)?.BarNominalDiameter ?? 0,
            barraPorPosicion.GetValueOrDefault(PosicionBarra.CaraY)?.BarNominalDiameter ?? 0,
            barrasReales, referencia, res);
    }

    /// <summary>
    /// Los ganchos de los estribos de la columna, en trocitos, para que las
    /// piezas corridas de los traslapos no los atraviesen: en la esquina donde
    /// cierra el estribo, el gancho entra en diagonal hacia el centro y una
    /// pieza corrida a lo largo de la cara le queda encima (Luciano, 21-oct).
    ///
    /// Se mide el estribo de verdad: la línea con ganchos menos la línea sin
    /// ganchos dice cuántos tramos son gancho en cada punta. Cada trocito es un
    /// círculo del radio de la barra del estribo (medio milímetro menos, para
    /// que la barra de esquina, que el gancho abraza, no cuente como choque) y
    /// cubre las alturas del juego entero: en planta todos los estribos del
    /// juego son iguales.
    /// </summary>
    /// <remarks>
    /// También devuelve los dobleces (los arcos, con sus ganchos incluidos):
    /// centro en planta y radio por dentro, para poner la barra de esquina
    /// dentro del doblez (<see cref="EnElDoblez"/>).
    /// </remarks>
    private static EstribosMedidos MedirEstribos(Document doc, IEnumerable<FamilyInstance> hosts)
    {
        var ids = new HashSet<ElementId>(hosts.Select(h => h.Id));
        var trocitos = new List<AcomodoTraslapos.Pieza>();
        var dobleces = new List<(double X, double Y, double RadioInterior)>();
        double tolerancia = UnitUtils.ConvertToInternalUnits(0.5, UnitTypeId.Millimeters);
        foreach (Rebar r in new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>())
        {
            try
            {
                if (!ids.Contains(r.GetHostId())) continue;
                if (doc.GetElement(r.GetShapeId()) is not RebarShape { RebarStyle: RebarStyle.StirrupTie }) continue;
                if (doc.GetElement(r.GetTypeId()) is not RebarBarType barra) continue;
                if (r.get_BoundingBox(null) is not { } caja) continue;

                IList<Curve> con = r.GetTransformedCenterlineCurves(true, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                foreach (Arc arco in con.OfType<Arc>())
                    dobleces.Add((arco.Center.X, arco.Center.Y, arco.Radius - barra.BarNominalDiameter / 2));

                bool ganchoInicio = r.GetHookTypeId(0) != ElementId.InvalidElementId;
                bool ganchoFin = r.GetHookTypeId(1) != ElementId.InvalidElementId;
                if (!ganchoInicio && !ganchoFin) continue;

                IList<Curve> sin = r.GetTransformedCenterlineCurves(true, true, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                int extra = con.Count - sin.Count;
                int puntas = (ganchoInicio ? 1 : 0) + (ganchoFin ? 1 : 0);
                if (extra <= 0 || extra % puntas != 0) continue;
                int porPunta = extra / puntas;

                var deGancho = new List<Curve>();
                if (ganchoInicio) deGancho.AddRange(con.Take(porPunta));
                if (ganchoFin) deGancho.AddRange(con.Skip(con.Count - porPunta));

                double radio = barra.BarNominalDiameter / 2;
                foreach (Curve c in deGancho)
                {
                    int pasos = Math.Max(1, (int)Math.Ceiling(c.Length / (radio / 2)));
                    for (int k = 0; k <= pasos; k++)
                    {
                        XYZ q = c.Evaluate((double)k / pasos, normalized: true);
                        trocitos.Add(new AcomodoTraslapos.Pieza(q.X, q.Y, radio - tolerancia, caja.Min.Z, caja.Max.Z));
                    }
                }

                BarraContinua.Traza?.Invoke(
                    $"GANCHOS del estribo {r.Id.Value}: {porPunta} tramo(s) por punta, Z {GeometriaSeccion.Metros(caja.Min.Z):0.###} -> " +
                    $"{GeometriaSeccion.Metros(caja.Max.Z):0.###}; " +
                    string.Join("; ", deGancho.Select(c =>
                        $"({GeometriaSeccion.Metros(c.GetEndPoint(0).X) * 1000:0.0}, {GeometriaSeccion.Metros(c.GetEndPoint(0).Y) * 1000:0.0}) -> " +
                        $"({GeometriaSeccion.Metros(c.GetEndPoint(1).X) * 1000:0.0}, {GeometriaSeccion.Metros(c.GetEndPoint(1).Y) * 1000:0.0}) mm")));
            }
            catch
            {
                // best-effort: ese estribo no se mira
            }
        }

        return new EstribosMedidos(trocitos, dobleces);
    }

    /// <summary>Lo medido de los estribos principales: sus ganchos en trocitos y sus dobleces.</summary>
    private sealed record EstribosMedidos(
        IReadOnlyList<AcomodoTraslapos.Pieza> Ganchos,
        IReadOnlyList<(double X, double Y, double RadioInterior)> Dobleces);

    /// <summary>
    /// Dónde va una barra de esquina de radio <paramref name="radio"/> pedida en
    /// (<paramref name="x"/>, <paramref name="y"/>) para quedar dentro del
    /// doblez más cercano del estribo, tocándolo por dentro: sobre la línea
    /// del centro del doblez a la barra, a (radio interior − radio) del centro.
    /// Si no hay un doblez a menos de 4 cm, o la barra no entra en él (radio
    /// interior menor que el de la barra: ahí la tocan los dos lados rectos),
    /// queda donde se pidió.
    /// </summary>
    private static (double X, double Y) EnElDoblez(
        IReadOnlyList<(double X, double Y, double RadioInterior)> dobleces, double x, double y, double radio)
    {
        double alcance = UnitUtils.ConvertToInternalUnits(0.04, UnitTypeId.Meters);
        (double X, double Y, double RadioInterior)? cercano = null;
        double mejor = alcance;
        foreach (var d in dobleces)
        {
            double distancia = Math.Sqrt((d.X - x) * (d.X - x) + (d.Y - y) * (d.Y - y));
            if (distancia < mejor) (cercano, mejor) = (d, distancia);
        }

        if (cercano is not { } c || c.RadioInterior <= radio || mejor < 1e-9) return (x, y);
        double t = (c.RadioInterior - radio) / mejor;
        return (c.X + (x - c.X) * t, c.Y + (y - c.Y) * t);
    }

    /// <summary>Una varilla longitudinal ya creada: su radio y dónde quedó cada pieza (pies).</summary>
    private sealed record BarraReal(double Radio, IReadOnlyList<BarraContinua.PiezaReal> Piezas);

    /// <summary>
    /// Los estribos menores (grapas en C y en S, y cerrados) de cada tramo
    /// LIBRE aceptado (no dentro de una viga), en cada posición de estribo
    /// principal -- issue #32: William confirmó el 4-sep que van "en la misma
    /// parte donde van los estribos normales". Desde el 20-oct no van a la
    /// MISMA altura sino apilados en el paquete, justo encima o justo debajo
    /// del principal (docs/planes/2026-10-20-diseno-estribos-menores-visual-y-64.md,
    /// sección 2): cada pieza va a la altura de su nivel en
    /// <see cref="ResultadoArmado.PaquetePorTramo"/>, respecto de las
    /// posiciones del principal en <see cref="ResultadoArmado.PosicionesEstribosPorTramo"/>.
    /// Formas y ganchos propios del cuadro (#64), con los del modelo de respaldo.
    /// </summary>
    private static void GenerarMenores(
        Document doc,
        ColumnaPlan columnaPlan,
        ColumnaContexto contexto,
        ConfigLongitudinal cfg,
        Dictionary<string, RebarBarType> barrasPorNombre,
        Dictionary<string, RebarHookType> ganchosPorNombre,
        RebarShape? formaEstribo,
        HashSet<ElementId> formasRechazadas,
        TiposPropios propios,
        int columnas,
        int filas,
        double insetEsquinas,
        double insetX,
        double insetY,
        double recubrimiento,
        double diametroEstribo,
        double diametroCaraX,
        double diametroCaraY,
        List<BarraReal> barrasReales,
        FootprintHost referencia,
        ResultadoArmado res)
    {
        List<EstriboMenor> piezas = cfg.MenoresParaGenerar();
        if (piezas.Count == 0) return;

        // Una pieza que no se puede crear falla igual en todos los tramos de la
        // columna, con el mismo texto: al final se deja cada aviso una sola vez.
        int avisosAntes = res.Avisos.Count;

        // Los avisos que se repetirían en cada tramo (o en cada columna, para
        // lo que es de la corrida entera) salen una sola vez.
        var avisos = new HashSet<string>();
        void Avisar(string aviso)
        {
            if (avisos.Add(aviso) && !res.Avisos.Contains(aviso)) res.Avisos.Add(aviso);
        }

        // Las piezas que engancha una punta de grapa a la altura z: las de la
        // varilla REAL más cercana a la posición teórica (Revit las corre unos
        // milímetros, #130). Sin varilla real cerca, la teórica.
        List<GanchoGrapa.Pieza> PiezasEn(XYZ teorica, double radioTeorico, double z)
        {
            static double Distancia(BarraContinua.PiezaReal p, XYZ q) => Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));

            BarraReal? cercana = barrasReales.MinBy(b => b.Piezas.Min(p => Distancia(p, teorica)));
            if (cercana is not null && cercana.Piezas.Min(p => Distancia(p, teorica)) <= EstandarCQ.APies(0.03))
            {
                List<GanchoGrapa.Pieza> enZ = cercana.Piezas
                    .Where(p => p.ZInf - Tolerancia <= z && z <= p.ZSup + Tolerancia)
                    .Select(p => new GanchoGrapa.Pieza(p.X, p.Y, cercana.Radio, p.ZSup))
                    .ToList();
                if (enZ.Count > 0) return enZ;
            }

            return new List<GanchoGrapa.Pieza> { new(teorica.X, teorica.Y, radioTeorico, z) };
        }

        // La línea de la grapa para que cada gancho ABRACE su barra: la barra
        // adentro del doblez, tocando la cara interior en la punta (#130).
        // Antes el arco se ponía pasando por el eje de la barra y la
        // atravesaba (docs/planes/2026-10-21-diagnostico-grapas-130.md).
        // Si en las dos puntas las piezas que hay a esa altura entran juntas
        // en un doblez de ese radio interior (en un traslapo son dos).
        bool CabenEnElDoblez((XYZ desde, XYZ hasta) teoricas, double z, EstriboMenor pieza, double radioInterior)
        {
            double radioTeorico = (pieza.Eje == EjeCaja.Fila ? diametroCaraY : diametroCaraX) / 2;
            return GanchoGrapa.Entran(PiezasEn(teoricas.desde, radioTeorico, z), radioInterior) &&
                   GanchoGrapa.Entran(PiezasEn(teoricas.hasta, radioTeorico, z), radioInterior);
        }

        (XYZ, XYZ) LineaQueAbraza((XYZ desde, XYZ hasta) teoricas, double z, GanchoMedido medido, EstriboMenor pieza, XYZ centroSeccion)
        {
            XYZ u = (teoricas.hasta - teoricas.desde).Normalize();
            double radioTeorico = (pieza.Eje == EjeCaja.Fila ? diametroCaraY : diametroCaraX) / 2;

            (double X, double Y) Centro(XYZ teorica, (double X, double Y) haciaAdentro)
            {
                (IReadOnlyList<GanchoGrapa.Pieza> abrazadas, bool caben) =
                    GanchoGrapa.Elegir(PiezasEn(teorica, radioTeorico, z), medido.RadioInterior);
                if (!caben)
                    Avisar($"{contexto.Marca}: en algunos traslapos las dos piezas de la barra no entran juntas en el doblez " +
                           $"de la grapa ({GeometriaSeccion.Metros(2 * medido.RadioInterior) * 1000:0} mm por dentro), ni con el " +
                           "doblez del cuadro; ahí la grapa abraza solo la pieza que sigue hacia arriba y choca con la otra.");
                return GanchoGrapa.CentroDelDoblez(abrazadas, medido.RadioInterior, haciaAdentro);
            }

            (double X, double Y) centroInicio = Centro(teoricas.desde, (u.X, u.Y));
            (double X, double Y) centroFin = Centro(teoricas.hasta, (-u.X, -u.Y));

            // Hacia qué lado abre cada gancho, igual que en IntentarGrapa: la C
            // hacia el centro; "Voltear" invierte las dos puntas y la S, la del fin.
            int ladoCentro = XYZ.BasisZ.CrossProduct(u).DotProduct(centroSeccion - teoricas.desde) >= 0 ? 1 : -1;
            int ladoInicio = pieza.Volteado ? -ladoCentro : ladoCentro;
            int ladoFin = (pieza.Forma == FormaMenor.GrapaS) != pieza.Volteado ? -ladoCentro : ladoCentro;

            ((double X, double Y) d, (double X, double Y) h) =
                GanchoGrapa.Linea(centroInicio, centroFin, medido.Axial, medido.Lateral, ladoInicio, ladoFin);
            return (new XYZ(d.X, d.Y, z), new XYZ(h.X, h.Y, z));
        }

        // Prioridad caja-sobre-grapa: una grapa dentro de un cerrado de su
        // mismo eje no se genera (confirmado por Luciano; el panel la muestra
        // en gris). El paquete ya la dejó afuera, así que acá no aparece.
        barrasPorNombre.TryGetValue(cfg.TipoBarraEsquinasNombre ?? "", out RebarBarType? barraRespaldo);

        TiposPropios.Pieza<RebarHookType> ganchoCajaPropio = propios.GanchoEstribo();
        TiposPropios.Pieza<RebarHookType> ganchoGrapaPropio = propios.GanchoGrapa();
        if (ganchoCajaPropio.Aviso is not null) Avisar($"Estribos menores: {ganchoCajaPropio.Aviso}; las cajas usan el gancho del tramo.");
        if (ganchoGrapaPropio.Aviso is not null) Avisar($"Estribos menores: {ganchoGrapaPropio.Aviso}; las grapas usan un gancho del modelo.");

        RebarShape? FormaGrapa(FormaMenor forma)
        {
            TiposPropios.Pieza<RebarShape> propia = propios.FormaGrapa(forma);
            if (propia.Aviso is not null) Avisar($"Estribos menores: {propia.Aviso}; se usa la forma de grapa del modelo.");
            return propia.Tipo ?? GeometriaSeccion.BuscarFormaGrapa(doc);
        }

        // Dónde queda REALMENTE el gancho de una grapa se mide una vez por
        // barra y gancho (ver MedirGanchoDeGrapa): no depende del tramo ni
        // de si es C o S (diagnóstico del 20-oct: misma geometría de gancho).
        // Qué gancho usan las grapas de cada barra y eje, y su medida:
        // primero dobladas como el estribo (Luciano, 20-oct); si Revit no las
        // acepta, con el 180° del cuadro.
        var juegos = new Dictionary<(ElementId Barra, EjeCaja Eje), (bool Estribo, RebarHookType Gancho, GanchoMedido? Medida)>();
        // En los traslapos donde las dos piezas no entran en el doblez del
        // estribo, la grapa va con el 180° del cuadro, más grande (Luciano,
        // 21-oct): también se mide una vez por barra y eje.
        var juegosGrandes = new Dictionary<(ElementId Barra, EjeCaja Eje), (RebarHookType Gancho, GanchoMedido Medida)?>();
        TiposPropios.Pieza<RebarHookType> ganchoGrapaEstribo = propios.GanchoGrapaEstribo();

        // Una pieza que falla en un tramo falla igual en todas sus alturas: se
        // deja de intentar en ese tramo, para no repetir el aviso por altura.
        int creadas = 0;

        for (int i = 0; i < contexto.Tramos.Count && i < columnaPlan.Tramos.Count; i++)
        {
            if (columnaPlan.Tramos[i] is not ConfigTramo cfgTramo) continue;

            Tramo tramo = contexto.Tramos[i];
            // Ni en los nudos ni contra la zapata (Luciano, 20-oct).
            if (tramo.Tipo is TipoTramo.DentroDeViga or TipoTramo.ContraZapata) continue;
            if (tramo.IdElementoHost is null) continue;
            if (!long.TryParse(tramo.IdElementoHost, out long idHost) ||
                doc.GetElement(new ElementId(idHost)) is not FamilyInstance host) continue;

            // Sin estribos principales registrados (GeneradorArmado omitió el
            // tramo por algún motivo, ya avisado) no hay dónde poner los menores.
            var clave = (tramo.IdElementoHost, tramo.Indice);
            if (!res.PosicionesEstribosPorTramo.TryGetValue(clave, out List<double>? posicionesZ) || posicionesZ.Count == 0 ||
                !res.PaquetePorTramo.TryGetValue(clave, out PaqueteEstribos.Paquete? paquete))
                continue;

            // Paquetes que se pisarían (separación más chica que el alto del
            // paquete): ese tramo va sin menores; los principales quedan.
            List<double> ordenadas = posicionesZ.OrderBy(z => z).ToList();
            double separacionMinima = ordenadas.Zip(ordenadas.Skip(1), (a, b) => b - a).DefaultIfEmpty(double.MaxValue).Min();
            if (separacionMinima < EstandarCQ.APies(paquete.Alto) - Tolerancia)
            {
                res.Avisos.Add(
                    $"{contexto.Marca}: tramo {tramo.Indice}: el paquete de estribos menores mide {paquete.Alto * 100:0.#} cm " +
                    $"y la separación es de {GeometriaSeccion.Metros(separacionMinima) * 100:0.#} cm; se omiten los estribos menores de este tramo.");
                continue;
            }

            // Grapas y cajas son armadura TRANSVERSAL: por defecto van con la
            // barra del ESTRIBO del tramo (10-sep); cada pieza puede tener la suya.
            RebarBarType? barraEstribo = null;
            if (!string.IsNullOrEmpty(cfgTramo.TipoBarraNombre))
                barrasPorNombre.TryGetValue(cfgTramo.TipoBarraNombre, out barraEstribo);
            barraEstribo ??= barraRespaldo;
            if (barraEstribo is null)
            {
                res.Avisos.Add(
                    $"{contexto.Marca}: el tramo {tramo.Indice} no tiene ningún tipo de barra válido para estribos menores; se omiten.");
                continue;
            }

            // El gancho configurado en el tramo: solo de respaldo de los propios.
            RebarHookType? ganchoTramo = null;
            if (!string.IsNullOrEmpty(cfgTramo.TipoGanchoNombre))
                ganchosPorNombre.TryGetValue(cfgTramo.TipoGanchoNombre, out ganchoTramo);
            RebarHookType? ganchoCaja = ganchoCajaPropio.Tipo ?? ganchoTramo;
            RebarHookType? ganchoGrapa = ganchoGrapaPropio.Tipo ?? GeometriaSeccion.BuscarGanchoGrapa(doc) ?? ganchoTramo;

            // Grapas y cajas contra la zona común de la columna, la misma que
            // usan las barras longitudinales que conectan.
            (double xMin, double xMax, double yMin, double yMax) = (referencia.XMin, referencia.XMax, referencia.YMin, referencia.YMax);
            double z0 = ordenadas[0];

            foreach (PaqueteEstribos.NivelPieza nivel in paquete.Piezas)
            {
                EstriboMenor pieza = nivel.Pieza;
                RebarBarType barra = BarraDePieza(pieza, barraEstribo, barrasPorNombre, out string? avisoBarra);
                if (avisoBarra is not null) Avisar($"{contexto.Marca}: {avisoBarra}");

                // La altura de la pieza respecto del eje del principal.
                double corrimiento = EstandarCQ.APies(nivel.Desplazamiento - paquete.DesplazamientoPrincipal);
                CuadroVarillas.Varilla? varilla = EstandarCQ.Buscar(barra);

                if (pieza.Forma == FormaMenor.Cerrado)
                {
                    if (ganchoCaja is not null && ganchoCaja == ganchoCajaPropio.Tipo && varilla?.GanchoEstribo135 is { } e135 &&
                        propios.AjustarExtension(barra, ganchoCaja, e135) is string avisoAjuste)
                        Avisar($"{contexto.Marca}: {avisoAjuste}");

                    // El cerrado ABRAZA por fuera las barras de las filas (o
                    // columnas) de su rango, en las dos caras (Luciano, 20-oct):
                    // cada lado toca la pieza REAL que más sale para ese lado, a
                    // la altura de cada cerrado -- con las dos piezas si cae en
                    // un traslapo (#130). Antes salía de la posición teórica y
                    // del recubrimiento: atravesaba una pieza corrida 5 mm y
                    // quedaba 4,75 mm despegado de las barras de las caras
                    // (medido el 21-oct en la columna 387983). Son las medidas
                    // por FUERA (ScaleToBox, medido el 20-oct: una caja pedida
                    // de 18,5 cm sale con CQ A = 0,19).
                    double diametroAbrazada = pieza.Eje == EjeCaja.Fila ? diametroCaraY : diametroCaraX;
                    double insetAbrazada = SeccionPlanta.Inset(recubrimiento, diametroEstribo, diametroAbrazada);
                    IEnumerable<XYZ> TeoricasDelRango()
                    {
                        for (int k = pieza.Desde; k <= pieza.Hasta; k++)
                        {
                            if (pieza.Eje == EjeCaja.Fila)
                            {
                                double y = PosicionIntermedia(yMin + insetEsquinas, yMax - insetEsquinas, filas, k);
                                yield return new XYZ(xMin + insetAbrazada, y, 0);
                                yield return new XYZ(xMax - insetAbrazada, y, 0);
                            }
                            else
                            {
                                double x = PosicionIntermedia(xMin + insetEsquinas, xMax - insetEsquinas, columnas, k);
                                yield return new XYZ(x, yMin + insetAbrazada, 0);
                                yield return new XYZ(x, yMax - insetAbrazada, 0);
                            }
                        }
                    }

                    foreach (double z in ordenadas)
                    {
                        (double x1, double x2, double y1, double y2) = EstriboMenor.CajaQueAbraza(
                            TeoricasDelRango().SelectMany(t => PiezasEn(t, diametroAbrazada / 2, z + corrimiento)),
                            barra.BarNominalDiameter, barra.StirrupTieBendDiameter / 2);
                        int antes = creadas;
                        CrearCaja(doc, host, barra, ganchoCaja, formaEstribo, formasRechazadas, x1, x2, y1, y2, z + corrimiento,
                            contexto.Marca, res, ref creadas);
                        if (creadas == antes) break;
                    }

                    continue;
                }

                // Dónde CREE la cuenta que están las dos barras que engancha la
                // grapa, una en cada punta (la posición teórica, sin traslapo).
                // Solo sirve para encontrar la varilla real (ver PiezasEn) y
                // para la grapa de prueba.
                double diametroEnganchada = pieza.Eje == EjeCaja.Fila ? diametroCaraY : diametroCaraX;
                double insetEnganchada = SeccionPlanta.Inset(recubrimiento, diametroEstribo, diametroEnganchada);
                (XYZ desde, XYZ hasta) Teoricas(double z)
                {
                    if (pieza.Eje == EjeCaja.Fila)
                    {
                        double y = PosicionIntermedia(yMin + insetEsquinas, yMax - insetEsquinas, filas, pieza.Desde);
                        return (new XYZ(xMin + insetEnganchada, y, z), new XYZ(xMax - insetEnganchada, y, z));
                    }

                    double x = PosicionIntermedia(xMin + insetEsquinas, xMax - insetEsquinas, columnas, pieza.Desde);
                    return (new XYZ(x, yMin + insetEnganchada, z), new XYZ(x, yMax - insetEnganchada, z));
                }

                var centroPrueba = new XYZ((xMin + xMax) / 2, (yMin + yMax) / 2, z0 + corrimiento);
                var claveJuego = (barra.Id, pieza.Eje);
                if (!juegos.TryGetValue(claveJuego, out var juego))
                {
                    (XYZ pd, XYZ ph) = Teoricas(z0 + corrimiento);
                    TiposPropios.Pieza<RebarShape> formaEstriboC = propios.FormaGrapaEstribo(FormaMenor.GrapaC);
                    string? motivoEstribo = ganchoGrapaEstribo.Aviso ?? formaEstriboC.Aviso;
                    bool listo = false;
                    if (ganchoGrapaEstribo.Tipo is { } gE && formaEstriboC.Tipo is { } fE)
                    {
                        if (varilla is not null && propios.AjustarExtension(barra, gE, varilla.Gancho180Cuadro) is string avisoE)
                            Avisar($"{contexto.Marca}: {avisoE}");
                        if (MedirGanchoDeGrapa(doc, host, barra, gE, fE, pd, ph, centroPrueba,
                                contexto.Marca, res, out string motivoE) is { } medida)
                        {
                            juego = (true, gE, medida);
                            listo = true;
                        }
                        else motivoEstribo = motivoE;
                    }

                    if (!listo)
                    {
                        Avisar($"{contexto.Marca}: las grapas dobladas como el estribo no le sirvieron a Revit ({motivoEstribo}); " +
                               "se usan con el doblez del gancho de 180° del cuadro, que es más grande.");
                        if (ganchoGrapa is null)
                        {
                            Avisar($"{contexto.Marca}: no hay ningún gancho para las grapas; no se generan.");
                            continue;
                        }

                        if (ganchoGrapa == ganchoGrapaPropio.Tipo && varilla is not null &&
                            propios.AjustarExtension(barra, ganchoGrapa, varilla.Gancho180Cuadro) is string avisoGrapa)
                            Avisar($"{contexto.Marca}: {avisoGrapa}");

                        GanchoMedido? medida = MedirGanchoDeGrapa(doc, host, barra, ganchoGrapa,
                            FormaGrapa(FormaMenor.GrapaC), pd, ph, centroPrueba, contexto.Marca, res, out string motivoC);
                        if (medida is null)
                            Avisar($"{contexto.Marca}: no se pudo medir dónde queda el gancho de las grapas ({motivoC}); " +
                                   "se generan de recubrimiento a recubrimiento y el gancho puede no abrazar la barra longitudinal.");
                        juego = (false, ganchoGrapa, medida);
                    }

                    juegos[claveJuego] = juego;
                }

                RebarShape? formaGrapa = juego.Estribo
                    ? propios.FormaGrapaEstribo(pieza.Forma).Tipo
                    : FormaGrapa(pieza.Forma);

                // El juego grande (180° del cuadro) para los traslapos: solo
                // si el normal es el doblado como el estribo, que es más chico.
                (RebarHookType Gancho, GanchoMedido Medida)? Grande()
                {
                    if (juegosGrandes.TryGetValue(claveJuego, out var ya)) return ya;

                    (RebarHookType, GanchoMedido)? grande = null;
                    if (juego.Estribo && ganchoGrapa is not null)
                    {
                        if (ganchoGrapa == ganchoGrapaPropio.Tipo && varilla is not null &&
                            propios.AjustarExtension(barra, ganchoGrapa, varilla.Gancho180Cuadro) is string avisoGrande)
                            Avisar($"{contexto.Marca}: {avisoGrande}");
                        (XYZ pd, XYZ ph) = Teoricas(z0 + corrimiento);
                        if (MedirGanchoDeGrapa(doc, host, barra, ganchoGrapa, FormaGrapa(FormaMenor.GrapaC), pd, ph, centroPrueba,
                                contexto.Marca, res, out string motivoGrande) is { } medidaGrande &&
                            medidaGrande.RadioInterior > (juego.Medida?.RadioInterior ?? 0))
                            grande = (ganchoGrapa, medidaGrande);
                        else if (motivoGrande.Length > 0)
                            Avisar($"{contexto.Marca}: no se pudo medir el gancho de 180° del cuadro para las grapas de los traslapos ({motivoGrande}).");
                    }

                    juegosGrandes[claveJuego] = grande;
                    return grande;
                }

                foreach (double z in ordenadas)
                {
                    var centro = new XYZ((xMin + xMax) / 2, (yMin + yMax) / 2, z + corrimiento);
                    (XYZ desde, XYZ hasta) teoricas = Teoricas(z + corrimiento);

                    RebarHookType gancho = juego.Gancho;
                    RebarShape? forma = formaGrapa;
                    GanchoMedido? medido = juego.Medida;
                    if (medido is not null && !CabenEnElDoblez(teoricas, z + corrimiento, pieza, medido.RadioInterior) &&
                        Grande() is { } grande)
                    {
                        (gancho, forma, medido) = (grande.Gancho, FormaGrapa(pieza.Forma), grande.Medida);
                        Avisar($"{contexto.Marca}: en los traslapos, las grapas van con el doblez del gancho de 180° del cuadro " +
                               $"({GeometriaSeccion.Metros(2 * grande.Medida.RadioInterior) * 1000:0} mm por dentro) para abrazar " +
                               "las dos piezas de la barra.");
                    }

                    (XYZ desde, XYZ hasta) = medido is not null
                        ? LineaQueAbraza(teoricas, z + corrimiento, medido, pieza, centro)
                        : DeRecubrimientoARecubrimiento(teoricas);
                    int antes = creadas;
                    CrearGrapa(doc, host, barra, gancho, ganchoTramo, forma, pieza.Forma, pieza.Volteado,
                        desde, hasta, centro, contexto.Marca, res, ref creadas);
                    if (creadas == antes) break;
                }

                // Sin medida del gancho: la línea cruda, como antes del 20-oct.
                (XYZ, XYZ) DeRecubrimientoARecubrimiento((XYZ desde, XYZ hasta) t)
                {
                    XYZ u = (t.hasta - t.desde).Normalize();
                    return (t.desde - u * (insetEnganchada - recubrimiento), t.hasta + u * (insetEnganchada - recubrimiento));
                }
            }
        }

        List<string> nuevos = res.Avisos.Skip(avisosAntes).Distinct().ToList();
        res.Avisos.RemoveRange(avisosAntes, res.Avisos.Count - avisosAntes);
        res.Avisos.AddRange(nuevos);

        if (creadas > 0)
            res.Detalles.Add($"{contexto.Marca}: {creadas} estribo(s) menor(es) creado(s).");
    }

    /// <summary>
    /// La barra de un estribo menor: la propia de la pieza, si existe en el
    /// documento y es apta para estribo según el cuadro (#64); si no, la del
    /// estribo del tramo ("Igual al estribo"), con aviso cuando la pieza pedía
    /// otra. La usan también <see cref="GeneradorArmado"/> (para el alto del
    /// paquete) y este generador, así los dos calculan con la misma barra.
    /// </summary>
    internal static RebarBarType BarraDePieza(
        EstriboMenor pieza, RebarBarType barraEstribo, Dictionary<string, RebarBarType> barrasPorNombre, out string? aviso)
    {
        aviso = null;
        if (pieza.TipoBarraNombre is not string nombre) return barraEstribo;

        if (!barrasPorNombre.TryGetValue(nombre, out RebarBarType? propia))
        {
            aviso = $"la barra «{nombre}» de un estribo menor no existe en este modelo; se usa «{barraEstribo.Name}», la del estribo.";
            return barraEstribo;
        }

        (AptitudEstribo aptitud, string? motivo) = CuadroVarillas.EvaluarParaEstribo(EstandarCQ.ComoTipoBarra(propia));
        if (aptitud == AptitudEstribo.NoApta)
        {
            aviso = $"la barra «{nombre}» de un estribo menor no es apta según el cuadro ({motivo}); se usa «{barraEstribo.Name}».";
            return barraEstribo;
        }

        return propia;
    }

    private static string RotuloPosicion(PosicionBarra posicion) => posicion switch
    {
        PosicionBarra.Esquina => "las esquinas",
        PosicionBarra.CaraX => "la cara X",
        _ => "la cara Y",
    };

    /// <summary>
    /// Agrupa TODOS los tramos físicos de la columna (aceptados o no) por
    /// elemento host, quedándose con el rango Z que cubren dentro de cada
    /// uno -- son las columnas apiladas que la varilla atraviesa, y a las que
    /// se reasigna cada pieza después de partirla.
    /// La longitudinal siempre es de la columna física completa; lo aceptado
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

    /// <summary>
    /// Reparte <paramref name="n"/> posiciones parejo entre los dos extremos,
    /// sin pisarlos -- mismo criterio que <c>VentanaEstribosMenores.X/Y</c>
    /// (fila/columna 0 es la más cercana a la esquina de índice bajo).
    /// </summary>
    private static double PosicionIntermedia(double desde, double hasta, int n, int indice) =>
        SeccionPlanta.PosicionIntermedia(desde, hasta, n, indice);

    /// <summary>
    /// Hacia dónde tiene que doblar el gancho para que mire al CENTRO de la
    /// sección, no siempre al mismo lado (issues #43 y #59; normativa
    /// colombiana: el gancho siempre va hacia adentro).
    ///
    /// El gancho dobla dentro del plano de la barra, que define la normal:
    /// perpendicular a la barra y a la normal, o sea a lo largo de
    /// <c>tangente × normal</c>. Con la barra vertical (tangente = Z):
    /// normal X → el gancho va en ±Y; normal Y → va en ∓X. Left/Right elige
    /// cuál de los dos signos.
    ///
    /// <b>Confirmado contra Revit real</b> (regla del CLAUDE.md), 11-sep: con
    /// dos barras medidas por <c>ComandoDiagnosticoRebar</c> -- una de cara X
    /// (normal=BasisX, haciaDondeDobla=+Y) y otra de cara Y (normal=BasisY,
    /// haciaDondeDobla=-X) -- las dos pedían el MISMO swap (lo que tenía
    /// asignado <c>Left</c> dobla hacia -haciaDondeDobla, y lo que tenía
    /// <c>Right</c> dobla hacia +haciaDondeDobla; es al revés de lo que se
    /// había asumido al escribir esto). No depende de si la normal es X o Y:
    /// <c>Left</c>/<c>Right</c> de Revit son relativos al par (tangente,
    /// normal) local de cada barra, no a los ejes globales.
    /// </summary>
    private static (RebarTerminationOrientation Inferior, RebarTerminationOrientation Superior)
        OrientacionesHaciaAdentro(FootprintHost fp, double x, double y, XYZ normal)
    {
        XYZ haciaDondeDobla = XYZ.BasisZ.CrossProduct(normal);

        double dxAlCentro = (fp.XMin + fp.XMax) / 2 - x;
        double dyAlCentro = (fp.YMin + fp.YMax) / 2 - y;

        // > 0: el centro de la sección cae del lado positivo de haciaDondeDobla.
        double proyeccion = dxAlCentro * haciaDondeDobla.X + dyAlCentro * haciaDondeDobla.Y;

        // Una barra que cae justo en el eje (por ejemplo la intermedia central
        // de una cara) no tiene "adentro" en esa dirección: cualquiera de los
        // dos lados es igual de bueno, se deja el de siempre.
        if (Math.Abs(proyeccion) < 1e-9) return (OrientacionPositiva, OrientacionPositiva);

        return proyeccion > 0
            ? (OrientacionPositiva, OrientacionPositiva)
            : (OrientacionNegativa, OrientacionNegativa);
    }

    /// <summary>
    /// Gancho hacia el lado positivo de <c>tangente × normal</c>. Confirmado
    /// contra Revit real el 11-sep (ver <see cref="OrientacionesHaciaAdentro"/>):
    /// es <c>Right</c>, no <c>Left</c> como se había asumido al escribir esto.
    /// </summary>
    private const RebarTerminationOrientation OrientacionPositiva = RebarTerminationOrientation.Right;

    /// <summary>El opuesto de <see cref="OrientacionPositiva"/>.</summary>
    private const RebarTerminationOrientation OrientacionNegativa = RebarTerminationOrientation.Left;

    /// <summary>
    /// Si el gancho preferido (el de los menores, más chico) no le sirve a
    /// Revit, se reintenta con <paramref name="ganchoRespaldo"/> -- el del
    /// tramo, que ya se sabe que funciona. Un gancho nuevo que Revit no acepte
    /// no puede dejar la columna SIN grapas: eso es peor que una grapa con el
    /// gancho grande de antes (pasó el 10-sep, con un gancho creado de cero).
    /// </summary>
    private static void CrearGrapa(
        Document doc, FamilyInstance host, RebarBarType barra, RebarHookType gancho, RebarHookType? ganchoRespaldo,
        RebarShape? formaGrapa, FormaMenor forma, bool volteado,
        XYZ desde, XYZ hasta, XYZ centroSeccion,
        string marcaColumna, ResultadoArmado res, ref int creadas)
    {
        if (IntentarGrapa(doc, host, barra, gancho, formaGrapa, forma, volteado, desde, hasta, centroSeccion, out string motivo) is not null)
        {
            creadas++;
            return;
        }

        if (ganchoRespaldo is not null && ganchoRespaldo.Id != gancho.Id &&
            IntentarGrapa(doc, host, barra, ganchoRespaldo, formaGrapa, forma, volteado, desde, hasta, centroSeccion, out _) is not null)
        {
            res.Avisos.Add(
                $"{marcaColumna}: el gancho «{gancho.Name}» no le sirvió a Revit para una grapa ({motivo}); " +
                $"esa grapa se generó con «{ganchoRespaldo.Name}», el del estribo.");
            creadas++;
            return;
        }

        res.Avisos.Add($"{marcaColumna}: no se pudo crear una grapa ({motivo}).");
        try { doc.Regenerate(); } catch { /* best-effort */ }
    }

    /// <param name="forma">C: los dos ganchos hacia el mismo lado; S: uno para cada lado.</param>
    /// <param name="volteado">Invierte los dos ganchos (botón "Voltear" del panel).</param>
    private static Rebar? IntentarGrapa(
        Document doc, FamilyInstance host, RebarBarType barra, RebarHookType gancho,
        RebarShape? formaGrapa, FormaMenor forma, bool volteado,
        XYZ desde, XYZ hasta, XYZ centroSeccion, out string motivo)
    {
        motivo = "";
        try
        {

            // Los dos ganchos tienen que mirar hacia el CENTRO de la sección
            // (pedido del usuario sobre el modelo de referencia, 10-sep).
            // Left/Right no son direcciones fijas: se interpretan respecto del
            // sentido de recorrido de la curva (mismo problema que documenta
            // GeometriaSeccion.Normalizar), así que con una constante para
            // todas, la grapa de la fila de arriba y la de abajo terminan
            // apuntando a lados opuestos. Se calcula cuál de las dos cae del
            // lado del centro en vez de fijarla.
            //
            // PENDIENTE de confirmar contra Revit real que Left es el giro
            // antihorario visto desde +Z; si sale al revés, se invierte acá.
            //
            // Con un gancho de 180° la regla se DA VUELTA, y no es un detalle:
            // ese gancho da la vuelta completa alrededor de la longitudinal, así
            // que el centro del semicírculo es la barra y el tramo recto de la
            // grapa queda a un radio de ese eje, del lado contrario al que gira
            // el gancho. Si girara hacia el centro, el tramo recto se iría hacia
            // AFUERA, contra el recubrimiento. Girando hacia afuera, la grapa
            // queda dentro del núcleo y el gancho abraza la barra desde el lado
            // de la cara. Un gancho de 135°, en cambio, apoya su cola en el
            // núcleo: ese sí mira al centro (criterio del 10-sep).
            XYZ direccion = (hasta - desde).Normalize();
            XYZ haciaLaIzquierda = XYZ.BasisZ.CrossProduct(direccion);
            // Desde el 20-oct TODOS los ganchos se abren hacia el centro: la
            // línea va sobre las barras (no un radio más adentro) y el 180°
            // abierto hacia afuera se salía del estribo por el costado
            // (medido con el MCP en la columna 781265). Lo de arriba sobre el
            // 180° queda como historia.
            const bool haciaElCentro = true;
            bool izquierdaMiraAlCentro = haciaLaIzquierda.DotProduct(centroSeccion - desde) > 0;
            RebarTerminationOrientation orientacion =
                izquierdaMiraAlCentro == haciaElCentro
                    ? RebarTerminationOrientation.Left
                    : RebarTerminationOrientation.Right;

            // Esa es la C de siempre. La S y el volteo cambian la orientación de
            // cada punta por separado: Revit la respeta tal cual y no cambia la
            // forma (diagnóstico del 20-oct, docs/planes/2026-10-20-diagnostico-grapa-s.md).
            RebarTerminationOrientation otra = orientacion == RebarTerminationOrientation.Left
                ? RebarTerminationOrientation.Right
                : RebarTerminationOrientation.Left;
            RebarTerminationOrientation orientacionInicio = volteado ? otra : orientacion;
            RebarTerminationOrientation orientacionFin = (forma == FormaMenor.GrapaS) != volteado ? otra : orientacion;

            // La línea ya viene calculada para que cada gancho abrace su barra
            // (GanchoGrapa.Linea, con el mismo criterio de lados que acá).
            var curvas = new List<Curve> { Line.CreateBound(desde, hasta) };

            // Camino preferido: la forma NATIVA de un segmento con estilo
            // Standard, igual que la grapa del modelo de referencia. Antes se
            // le pasaba a CreateFromCurves una línea suelta con estilo
            // StirrupTie -- un "estribo" de un solo lado, que no es una forma
            // real -- y Revit tenía que sintetizarla, sacando ganchos enormes y
            // deformes (también en columnas de 40x40: no era el tamaño de la
            // sección). Con CreateFromCurvesAndShape la posición la damos
            // nosotros y la geometría la resuelve la definición de la forma.
            using var terminaciones = new BarTerminationsData(doc)
            {
                HookTypeIdAtStart = gancho.Id,
                HookTypeIdAtEnd = gancho.Id,
                TerminationOrientationAtStart = orientacionInicio,
                TerminationOrientationAtEnd = orientacionFin,
            };

            Rebar? grapa = null;
            if (formaGrapa is not null)
            {
                grapa = Rebar.CreateFromCurvesAndShape(
                    doc, formaGrapa, barra, host, XYZ.BasisZ, curvas, terminaciones);
            }

            if (grapa is null)
            {
                grapa = Rebar.CreateFromCurves(
                    doc,
                    // Standard, no StirrupTie: la grapa es una barra recta, no
                    // un estribo -- medido contra el modelo de referencia.
                    RebarStyle.Standard,
                    barra,
                    host,
                    XYZ.BasisZ,
                    curvas,
                    terminaciones,
                    useExistingShapeIfPossible: true,
                    createNewShape: true);
            }

            if (grapa is null)
            {
                motivo =
                    $"Rebar.CreateFromCurves devolvió null con la barra «{barra.Name}» y el gancho «{gancho.Name}»";
                return null;
            }

            MarcaEstribos.Marcar(grapa, host.Id);
            return grapa;
        }
        catch (Exception ex)
        {
            motivo = GeometriaSeccion.Explicar(ex);
            try { doc.Regenerate(); } catch { /* best-effort */ }
            return null;
        }
    }

    /// <summary>
    /// Lo medido en una grapa de prueba, en pies: dónde queda el centro del
    /// gancho del inicio respecto de la punta de la línea pedida -- <c>Axial</c>
    /// a lo largo de la grapa (hacia adentro) y <c>Lateral</c> de costado (hacia
    /// el centro de la sección, hacia donde abre la C) -- y el radio interior
    /// del doblez. Con eso <see cref="GanchoGrapa.Linea"/> calcula la línea que
    /// deja cada gancho donde tiene que ir.
    /// </summary>
    private sealed record GanchoMedido(double Axial, double Lateral, double RadioInterior);

    /// <summary>
    /// Dónde queda REALMENTE el gancho de una grapa respecto de la línea que se
    /// le pide a Revit.
    ///
    /// No se calcula con una fórmula: se MIDE contra Revit (regla del CLAUDE.md,
    /// y el error que se repitió nueve veces con este mismo gancho). Se crea una
    /// grapa de prueba (una C) con la línea cruda, se le leen las curvas CON
    /// ganchos y radios reales -- <c>GetCenterlineCurves(false, false, false, ...)</c> --
    /// y se toma el centro y el radio del arco del gancho. La grapa de prueba se
    /// borra. Qué hacer con eso (que el gancho abrace la barra, #130) ya no es
    /// de acá: lo resuelve <see cref="GanchoGrapa"/>.
    /// </summary>
    private static GanchoMedido? MedirGanchoDeGrapa(
        Document doc, FamilyInstance host, RebarBarType barra, RebarHookType gancho, RebarShape? formaGrapa,
        XYZ desde, XYZ hasta, XYZ centroSeccion, string marcaColumna, ResultadoArmado res,
        out string motivo)
    {
        Rebar? prueba = IntentarGrapa(doc, host, barra, gancho, formaGrapa, FormaMenor.GrapaC, volteado: false,
            desde, hasta, centroSeccion, out motivo);
        if (prueba is null) return null;

        try
        {
            IList<Curve> curvas = prueba.GetCenterlineCurves(
                false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, 0);

            // La grapa es una sola recta con un gancho en cada punta, así que
            // los únicos arcos son los dos ganchos: el del extremo inicial es
            // el que tiene el centro más cerca de "desde".
            Arc? arco = curvas.OfType<Arc>().OrderBy(a => a.Center.DistanceTo(desde)).FirstOrDefault();
            if (arco is null)
            {
                motivo = "la grapa de prueba salió sin ningún arco de gancho";
                return null;
            }

            XYZ direccion = (hasta - desde).Normalize();
            var medido = new GanchoMedido(
                (arco.Center - desde).DotProduct(direccion),
                (arco.Center - desde).DotProduct(HaciaElCentro(desde, direccion, centroSeccion)),
                arco.Radius - barra.BarNominalDiameter / 2);

            res.Detalles.Add(
                $"{marcaColumna}: gancho de grapa medido en Revit -- doblez de {GeometriaSeccion.Metros(2 * medido.RadioInterior) * 1000:0} mm " +
                $"por dentro; su centro queda {GeometriaSeccion.Metros(medido.Axial) * 100:0.0} cm hacia adentro y " +
                $"{GeometriaSeccion.Metros(medido.Lateral) * 100:0.0} cm de costado de la punta pedida.");

            return medido;
        }
        catch (Exception ex)
        {
            motivo = $"no se pudo leer la geometría de la grapa de prueba: {GeometriaSeccion.Explicar(ex)}";
            return null;
        }
        finally
        {
            Descartar(doc, prueba);
        }
    }

    /// <summary>
    /// Versor perpendicular a la grapa, en planta, que apunta hacia el centro de
    /// la sección -- el lado hacia el que gira el gancho.
    /// </summary>
    private static XYZ HaciaElCentro(XYZ desde, XYZ direccion, XYZ centroSeccion)
    {
        XYZ perpendicular = XYZ.BasisZ.CrossProduct(direccion);
        return perpendicular.DotProduct(centroSeccion - desde) >= 0 ? perpendicular : -perpendicular;
    }

    /// <summary>
    /// Ya NO cae al respaldo de <c>Rebar.CreateFromCurves</c> cuando la forma
    /// falla: ese camino dejaba en el modelo barras que Revit después no podía
    /// resolver ("Can't solve Rebar Shape", con Delete Rebar como única salida)
    /// y lazos enormes que se salían del hormigón -- visto en Revit real con una
    /// caja angosta, 10-sep. Si la forma no se puede usar, se reintenta sin
    /// gancho y, si tampoco, no se crea nada.
    /// </summary>
    private static void CrearCaja(
        Document doc, FamilyInstance host, RebarBarType barra, RebarHookType? gancho, RebarShape? formaEstribo,
        HashSet<ElementId> formasRechazadas,
        double x1, double x2, double y1, double y2, double z,
        string marcaColumna, ResultadoArmado res, ref int creadas)
    {
        if (formaEstribo is null)
        {
            res.Avisos.Add(
                $"{marcaColumna}: el documento no tiene una forma de estribo cargada (RebarShape); " +
                "no se generan cajas.");
            return;
        }

        Rebar? caja = CrearCajaDesdeForma(doc, host, barra, formaEstribo, formasRechazadas, gancho, x1, x2, y1, y2, z, marcaColumna, res, out bool falloElGancho);

        // Segundo intento sin gancho -- lo que eligió el usuario para cuando no
        // entra ninguno (10-sep): que la caja exista igual, cerrada pero sin
        // gancho, en vez de no salir. Solo tiene sentido si lo que falló fue
        // justamente el gancho: si la forma no se pudo ni ajustar al rectángulo,
        // sacarle el gancho no cambia nada y solo duplica el aviso.
        if (caja is null && falloElGancho)
            caja = CrearCajaDesdeForma(doc, host, barra, formaEstribo, formasRechazadas, null, x1, x2, y1, y2, z, marcaColumna, res, out _);

        if (caja is null) return; // ya avisado, con el paso que falló y las medidas

        MarcaEstribos.Marcar(caja, host.Id);
        creadas++;
    }

    /// <summary>
    /// Mismo patrón que <c>GeneradorArmado.CrearDesdeForma</c>, pero con los
    /// tres pasos en <c>try</c> separados: Revit contesta "An internal error has
    /// occurred" sin decir dónde, y con un solo <c>try</c> era imposible saber si
    /// el problema era el gancho (que no entra) o <c>ScaleToBox</c> (que no puede
    /// achatar la forma a una caja angosta) -- son causas distintas con arreglos
    /// distintos. El aviso nombra el paso y las medidas reales.
    /// </summary>
    private static Rebar? CrearCajaDesdeForma(
        Document doc, FamilyInstance host, RebarBarType barra, RebarShape forma, HashSet<ElementId> formasRechazadas,
        RebarHookType? gancho, double x1, double x2, double y1, double y2, double z,
        string marcaColumna, ResultadoArmado res, out bool falloElGancho)
    {
        falloElGancho = false;

        // Ya se comprobó en esta corrida que esta forma trae un lado
        // diagonal (ver GeometriaSeccion.TieneLadoDiagonal) -- mismo criterio
        // que el estribo principal, no vale la pena repetir el intento.
        if (formasRechazadas.Contains(forma.Id)) return null;

        double ancho = Math.Abs(x2 - x1);
        double alto = Math.Abs(y2 - y1);
        XYZ origen = new(Math.Min(x1, x2), Math.Min(y1, y2), z);

        string Medidas() =>
            $"caja de {GeometriaSeccion.Metros(ancho) * 100:0.#}×{GeometriaSeccion.Metros(alto) * 100:0.#} cm, " +
            $"barra «{barra.Name}», " +
            (gancho is null
                ? "sin gancho"
                : $"gancho «{gancho.Name}» de {GeometriaSeccion.Metros(LargoDeGancho(barra, gancho) ?? 0) * 100:0.#} cm");

        Rebar? caja;
        try
        {
            caja = Rebar.CreateFromRebarShape(doc, forma, barra, host, origen, XYZ.BasisX, XYZ.BasisY);
        }
        catch (Exception ex)
        {
            res.Avisos.Add($"{marcaColumna}: Revit rechazó crear la forma «{forma.Name}» ({Medidas()}): {ex.Message}");
            return null;
        }

        if (caja is null)
        {
            res.Avisos.Add($"{marcaColumna}: Revit devolvió null al crear la forma «{forma.Name}» ({Medidas()}).");
            return null;
        }

        try
        {
            caja.GetShapeDrivenAccessor().ScaleToBox(origen, new XYZ(ancho, 0, 0), new XYZ(0, alto, 0));
        }
        catch (Exception ex)
        {
            // Sin esto queda en el modelo una caja creada pero sin escalar.
            Descartar(doc, caja);
            res.Avisos.Add(
                $"{marcaColumna}: la {Medidas()} es demasiado angosta -- la barra no se puede doblar en un lazo " +
                $"cerrado de ese alto (diámetro {GeometriaSeccion.Metros(barra.BarNominalDiameter) * 1000:0.#} mm). " +
                "Esa caja no se genera: dibuja el estribo cerrado entre barras más separadas, o usa una barra más delgada. " +
                $"(Revit: {ex.Message})");
            return null;
        }

        if (gancho is not null)
        {
            try
            {
                caja.SetHookTypeId(0, gancho.Id);
                caja.SetHookTypeId(1, gancho.Id);
            }
            catch (Exception ex)
            {
                falloElGancho = true;
                Descartar(doc, caja);
                res.Avisos.Add($"{marcaColumna}: el gancho no entra en la {Medidas()}: {ex.Message}");
                return null;
            }

            try
            {
                caja.SetTerminationOrientation(0, RebarTerminationOrientation.Left);
                caja.SetTerminationOrientation(1, RebarTerminationOrientation.Left);
            }
            catch { /* la forma puede no tener posiciones de gancho orientables -- no bloquea */ }
        }

        // Mismo chequeo que el estribo principal (GeneradorArmado.CrearDesdeForma):
        // BuscarFormaEstribo elige por NOMBRE, no por geometría, y una forma con
        // una cruceta de confinamiento incorporada generaría acá el mismo
        // problema -- medido contra Revit real el 11-sep.
        if (GeometriaSeccion.TieneLadoDiagonal(caja))
        {
            Descartar(doc, caja);

            if (formasRechazadas.Add(forma.Id))
                res.Avisos.Add(
                    $"{marcaColumna}: la forma «{forma.Name}» generó un lado diagonal en la {Medidas()} " +
                    "(¿tiene una cruceta de confinamiento incorporada?); no se genera esta caja.");

            return null;
        }

        return caja;
    }

    private static double? LargoDeGancho(RebarBarType barra, RebarHookType gancho)
    {
        try { return barra.GetHookLength(gancho.Id); }
        catch { return null; }
    }

    private static void Descartar(Document doc, Rebar rebar)
    {
        try { doc.Delete(rebar.Id); }
        catch { /* best-effort: si no se puede borrar, el aviso ya salió */ }
    }
}
