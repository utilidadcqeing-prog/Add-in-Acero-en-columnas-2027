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
///    era falsa. Los traslapos solo se generan donde la longitud comercial de
///    la barra (<see cref="ConfigLongitudinal.LongitudComercialBarra"/>, 6 o
///    12 m) obliga a cortar, y solo a la mitad de un nivel, alternando niveles
///    pares/impares entre las posiciones de la sección. Qué barra cae en cada
///    grupo lo decide su lugar en el ANILLO del perímetro (ver
///    <see cref="GenerarColumna"/>, lista <c>anillo</c>): "uno sí, uno no"
///    dando la vuelta, de modo que dos barras VECINAS nunca traslapan a la
///    misma altura -- regla del equipo, confirmada por Luciano el 11-sep (ver
///    docs/planes/2026-09-11-alternancia-de-traslapos-por-anillo.md). El
///    solape y el corrimiento lateral los pone Revit según el tipo de empalme
///    (<see cref="TiposEmpalme"/>): por defecto uno propio del add-in con el
///    solape del cuadro de la empresa.
///  - Las barras longitudinales llevan el gancho de
///    <see cref="ConfigLongitudinal.TipoGanchoLongitudinalNombre"/> (90°
///    estándar) en las DOS puntas de la columna -- abajo el anclaje en la
///    zapata (issue #37) y arriba el remate (issue #59) -- y en ninguna junta
///    intermedia. Los dos miran hacia el centro de la sección (issue #43).
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

        // Mismo criterio que GeneradorArmado.Generar: una sola vez por
        // corrida, no por cada caja -- ver GeometriaSeccion.TieneLadoDiagonal.
        var formasRechazadas = new HashSet<ElementId>();

        // Un solo resolvedor por corrida: cachea los tipos de empalme propios
        // del add-in para no crearlos ni reconfigurarlos una vez por varilla.
        var tiposEmpalme = new TiposEmpalme(doc);

        var cacheHost = new Dictionary<ElementId, (CurveLoop ContornoCrudo, double ZBase)>();

        // TEMPORAL -- ver BarraContinua.Traza. Queda en un archivo de texto
        // para poder leerlo sin copiar nada a mano desde Revit.
        // Solo en Debug: el paquete publicado (Release) no deja registros.
        var traza = new System.Text.StringBuilder();
#if DEBUG
        BarraContinua.Traza = linea => traza.AppendLine(linea);
#endif

        foreach (ColumnaPlan columnaPlan in opciones.Plan.Columnas)
        {
            try
            {
                GenerarColumna(doc, columnaPlan, barrasPorNombre, ganchosPorNombre, formaEstribo, formasRechazadas, tiposEmpalme, cacheHost, res);
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
        TiposEmpalme tiposEmpalme,
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

        double insetEsquinas = recubrimientoPies + diametroEstribo + barraEsquinas.BarNominalDiameter / 2;

        int columnas = Math.Max(0, cfg.BarrasPorCaraX / 2);
        int filas = Math.Max(0, cfg.BarrasPorCaraY / 2);

        double insetX = barraCaraX is not null ? recubrimientoPies + diametroEstribo + barraCaraX.BarNominalDiameter / 2 : 0;
        double insetY = barraCaraY is not null ? recubrimientoPies + diametroEstribo + barraCaraY.BarNominalDiameter / 2 : 0;

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
                $"recubrimiento + la barra de esquina «{barraEsquinas.Name}»; se omite el acero longitudinal de esta columna.");
            return;
        }

        if (barraCaraX is not null && (xMax - xMin) - 2 * insetX <= Tolerancia)
            res.Avisos.Add(
                $"{contexto.Marca}: la zona común de la pila ({zonaComun}) es demasiado chica para la barra de cara X " +
                $"«{barraCaraX.Name}»; sus posiciones pueden quedar mal.");

        if (barraCaraY is not null && (yMax - yMin) - 2 * insetY <= Tolerancia)
            res.Avisos.Add(
                $"{contexto.Marca}: la zona común de la pila ({zonaComun}) es demasiado chica para la barra de cara Y " +
                $"«{barraCaraY.Name}»; sus posiciones pueden quedar mal.");

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
            $"longitud comercial {cfg.LongitudComercialBarra:0.#} m");

        // El longitudinal entra a anclarse hasta 7.5cm de la cara inferior de
        // la zapata (#26). Ya no hace falta hostear ese pedazo en la zapata:
        // la varilla puede salirse de su host (medido el 7-oct).
        double zInicioTotal = contexto.ZInferiorLongitudinal;

        // "Niveles" para Fase 5 (#7): un candidato de traslapo por tramo
        // LIBRE/CONTRA ZAPATA de TODA la columna (no solo los aceptados en
        // esta corrida) -- el número de nivel (para el intercalado par/impar)
        // es una propiedad estable de la columna real, no debería cambiar
        // según qué tramos se tildaron hoy.
        List<(int Ordinal, double ZInf, double ZSup)> niveles = contexto.Tramos
            .Where(t => t.Tipo != TipoTramo.DentroDeViga)
            .Select((t, idx) => (Ordinal: idx + 1, t.ZInferior, t.ZSuperior))
            .ToList();

        double zFinTotal = contexto.ZSuperior;

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
        var anillo = new List<(RebarBarType Barra, double X, double Y, XYZ Normal)>();

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
        //    positivo va a lo largo de una cara y no se sale por la punta. En
        //    la esquina superior derecha los dos lados positivos se salen:
        //    ahí BarraContinua corre la varilla entera un diámetro para el
        //    otro lado (ver BarraContinua.Alinear y los límites de abajo).
        XYZ NormalDeEsquina(double y) => y < yEsqArriba - Tolerancia ? XYZ.BasisY : XYZ.BasisX;

        void AgregarEsquina(double x, double y) =>
            anillo.Add((barraEsquinas, x, y, NormalDeEsquina(y)));

        // Intermedias de las caras inferior/superior: varían en X.
        void AgregarDeCaraX(int c, double y)
        {
            if (barraCaraX is null) return;
            double x = PosicionIntermedia(xEsqIzq, xEsqDer, columnas, c);
            anillo.Add((barraCaraX, x, y, XYZ.BasisX));
        }

        // Intermedias de las caras izquierda/derecha: varían en Y.
        void AgregarDeCaraY(int f, double x)
        {
            if (barraCaraY is null) return;
            double y = PosicionIntermedia(yEsqAbajo, yEsqArriba, filas, f);
            anillo.Add((barraCaraY, x, y, XYZ.BasisY));
        }

        AgregarEsquina(xEsqIzq, yEsqAbajo);
        for (int c = 0; c < columnas; c++) AgregarDeCaraX(c, referencia.YMin + insetX);
        AgregarEsquina(xEsqDer, yEsqAbajo);
        for (int f = 0; f < filas; f++) AgregarDeCaraY(f, referencia.XMax - insetY);
        AgregarEsquina(xEsqDer, yEsqArriba);
        for (int c = columnas - 1; c >= 0; c--) AgregarDeCaraX(c, referencia.YMax - insetX);
        AgregarEsquina(xEsqIzq, yEsqArriba);
        for (int f = filas - 1; f >= 0; f--) AgregarDeCaraY(f, referencia.XMin + insetY);

        // En un anillo CERRADO la alternancia estricta solo existe si la
        // cantidad de posiciones es par. Con el modelo de hoy siempre lo es
        // (4 + 2*columnas + 2*filas), pero no se deja implícito.
        if (anillo.Count % 2 != 0)
            res.Avisos.Add(
                $"{contexto.Marca}: la sección tiene {anillo.Count} barras longitudinales (cantidad impar), " +
                "así que dos barras vecinas van a traslapar a la misma altura -- con un número impar " +
                "alrededor del perímetro no hay forma de alternarlas todas.");

        // Las alturas de traslapo dependen solo del grupo par/impar: se
        // calculan (y avisan) una vez por grupo, no una vez por varilla.
        var alturasPorGrupo = new Dictionary<bool, List<double>>();

        // Los avisos de empalme son los mismos para todas las varillas de un
        // mismo tipo de barra: uno por columna y motivo, no uno por varilla.
        var avisosDeBarras = new HashSet<string>();

        int piezas = 0;
        for (int i = 0; i < anillo.Count; i++)
        {
            (RebarBarType barraDeLaPosicion, double x, double y, XYZ normal) = anillo[i];

            bool esParGrupo = i % 2 == 0;
            if (!alturasPorGrupo.TryGetValue(esParGrupo, out List<double>? alturas))
            {
                alturas = PlanificarAlturas(
                    esParGrupo, niveles, zInicioTotal, zFinTotal, cfg.LongitudComercialBarra, contexto.Marca, res);
                alturasPorGrupo[esParGrupo] = alturas;
            }

            // El tipo de empalme solo se resuelve (y, si es propio, se crea)
            // cuando esta varilla de verdad lleva algún traslapo.
            ElementId? tipoEmpalme = null;
            if (alturas.Count > 0)
            {
                TiposEmpalme.Resultado tipo = tiposEmpalme.Resolver(barraDeLaPosicion, cfg.TipoEmpalmeNombre);
                tipoEmpalme = tipo.Tipo;
                if (tipo.Aviso is not null) avisosDeBarras.Add(tipo.Aviso);
            }

            (RebarTerminationOrientation orientacionAbajo, RebarTerminationOrientation orientacionArriba) =
                OrientacionesHaciaAdentro(referencia, x, y, normal);

            // De esquina a esquina de la armadura; se abre hasta la propia
            // barra por si su inset es menor que el de las esquinas.
            var limites = new BarraContinua.Limites(
                Math.Min(xEsqIzq, x), Math.Max(xEsqDer, x), Math.Min(yEsqAbajo, y), Math.Max(yEsqArriba, y));

            BarraContinua.Resultado creada = BarraContinua.Crear(doc, new BarraContinua.Pedido(
                barraDeLaPosicion, x, y, normal,
                EstandarCQ.APies(zInicioTotal), EstandarCQ.APies(zFinTotal),
                alturas.Select(EstandarCQ.APies).ToList(), pila,
                ganchoLongitudinal, ganchoLongitudinal, orientacionAbajo, orientacionArriba,
                tipoEmpalme, limites));

            piezas += creada.Piezas;
            if (creada.Aviso is not null) avisosDeBarras.Add(creada.Aviso);
        }

        foreach (string aviso in avisosDeBarras)
            res.Avisos.Add($"{contexto.Marca}: {aviso}");

        if (piezas > 0)
            res.Detalles.Add(
                $"{contexto.Marca}: {anillo.Count} barra(s) longitudinal(es) de punta a punta, {piezas} pieza(s) con sus traslapos.");

        GenerarMenores(
            doc, columnaPlan, contexto, cfg, barrasPorNombre, ganchosPorNombre, formaEstribo, formasRechazadas, cacheHost,
            columnas, filas, insetEsquinas, insetX, insetY, referencia, res);
    }

    /// <summary>
    /// Grapas y cajas en cada tramo LIBRE aceptado (no dentro de una viga), en
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
        HashSet<ElementId> formasRechazadas,
        Dictionary<ElementId, (CurveLoop, double)> cacheHost,
        int columnas,
        int filas,
        double insetEsquinas,
        double insetX,
        double insetY,
        FootprintHost referencia,
        ResultadoArmado res)
    {
        if (cfg.GrapasFila.Count == 0 && cfg.GrapasColumna.Count == 0 && cfg.Cajas.Count == 0) return;

        barrasPorNombre.TryGetValue(cfg.TipoBarraCaraXNombre ?? "", out RebarBarType? barraCaraX);
        barrasPorNombre.TryGetValue(cfg.TipoBarraCaraYNombre ?? "", out RebarBarType? barraCaraY);
        // Solo de RESPALDO: grapas y cajas se hacen con la barra del ESTRIBO
        // del tramo (ver más abajo). Esta barra longitudinal era la que se usaba
        // hasta el 10-sep y queda para el caso de que el tramo no tenga un tipo
        // de barra de estribo válido.
        RebarBarType? barraMenoresRespaldo = barraCaraX ?? barraCaraY;

        if (barraCaraX is null && cfg.GrapasColumna.Count > 0)
            res.Avisos.Add($"{contexto.Marca}: sin tipo de barra de cara X válido, no se generan grapas de columna.");
        if (barraCaraY is null && cfg.GrapasFila.Count > 0)
            res.Avisos.Add($"{contexto.Marca}: sin tipo de barra de cara Y válido, no se generan grapas de fila.");

        int creadas = 0;

        // Prioridad caja-sobre-gancho: si una fila/columna con grapa tildada
        // también queda cubierta por una caja (rango fusionado), gana la
        // caja y la grapa no se genera ahí -- confirmado por Luciano, mismo
        // criterio que aplica VentanaEstribosMenores en la previsualización.
        bool Tapada(EjeCaja eje, int indice) =>
            cfg.Cajas.Any(c => c.Eje == eje && indice >= c.Desde && indice <= c.Hasta);

        List<int> grapasFila = cfg.GrapasFila
            .Where(f => f < filas && !Tapada(EjeCaja.Fila, f))
            .ToList();
        List<int> grapasColumna = cfg.GrapasColumna
            .Where(c => c < columnas && !Tapada(EjeCaja.Columna, c))
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

            // Grapas y cajas se ubican contra la zona común de la columna, la
            // misma que usan las barras longitudinales que conectan -- no
            // contra la huella de este elemento, que puede ser más ancho.
            (double xMin, double xMax, double yMin, double yMax) =
                (referencia.XMin, referencia.XMax, referencia.YMin, referencia.YMax);

            // La geometría en planta de grapas y cajas es la MISMA en todas las
            // alturas de estribo de este tramo, así que el gancho se resuelve
            // una sola vez acá afuera. Antes se reintentaba (y se fallaba) una
            // vez por altura: una sola caja imposible daba 17 avisos idénticos.
            // Gancho propio de los menores para las CAJAS (lazo cerrado, estilo
            // de estribo, como el principal).
            RebarHookType ganchoMenores = GanchoAMedidaParaMenores(doc, gancho) ?? gancho;

            // Grapas y cajas son armadura TRANSVERSAL: van con el tipo de barra
            // del ESTRIBO del tramo (típicamente #3), no con el de la barra
            // longitudinal de cara (#5, #6...), que es lo que se usaba hasta el
            // 10-sep. Toda la geometría del gancho -- radio de doblado y
            // extensión recta -- es proporcional al diámetro, así que una grapa
            // hecha con la barra longitudinal salía con un gancho del doble de
            // tamaño que la del modelo de la empresa, que es #3. Decidido con
            // el usuario, 10-sep.
            RebarBarType? barraEstriboTramo = null;
            if (!string.IsNullOrEmpty(cfgTramo.TipoBarraNombre))
                barrasPorNombre.TryGetValue(cfgTramo.TipoBarraNombre, out barraEstriboTramo);

            RebarBarType? barraMenores = barraEstriboTramo ?? barraMenoresRespaldo;
            if (barraMenores is null)
            {
                res.Avisos.Add(
                    $"{contexto.Marca}: el tramo {tramo.Indice + 1} no tiene ningún tipo de barra válido " +
                    "para grapas ni cajas; se omiten sus estribos menores.");
                continue;
            }

            if (barraEstriboTramo is null)
                res.Avisos.Add(
                    $"{contexto.Marca}: el tramo {tramo.Indice + 1} no tiene un tipo de barra de estribo válido; " +
                    $"sus grapas y cajas se generan con «{barraMenores.Name}», la barra longitudinal de cara.");

            RebarBarType? barraCaja = barraMenores;

            // Las GRAPAS llevan otro gancho: 180° con estilo Standard, medido
            // contra el modelo de referencia de la empresa. Los 180° dan la
            // vuelta completa alrededor de la longitudinal -- por eso la agarran
            // "por detrás", cosa que un gancho de tirante de 135° no hace.
            // El largo del gancho NO se toca: un gancho de 180° tiene un arco de
            // semicírculo obligatorio que, con el diámetro interior mínimo de
            // 4db, ya mide solo él ~7,5 cm en una barra #3 -- más el tramo recto
            // mínimo de 6,5 cm da un piso real de ~14 cm. Intentar fijarlo más
            // corto (5,4 cm por proporción, después 6,5 cm por el mínimo
            // normativo) hizo que Revit contestara "Can't solve Rebar Shape" en
            // todas las grapas. El gancho de la empresa mide 12,7 cm, o sea que
            // ya está en ese piso: no hay margen para achicarlo.
            RebarHookType? ganchoDe180 = GeometriaSeccion.BuscarGanchoGrapa(doc);
            RebarHookType ganchoGrapa = ganchoDe180 ?? ganchoMenores;
            if (ganchoDe180 is null)
                res.Avisos.Add(
                    $"{contexto.Marca}: el documento no tiene ningún gancho de 180° con estilo Standard; " +
                    $"las grapas salen con «{ganchoGrapa.Name}», que no abraza la barra longitudinal igual.");

            RebarHookType? ganchoGrapaFila = barraCaraY is null ? null : ganchoGrapa;
            RebarHookType? ganchoGrapaColumna = barraCaraX is null ? null : ganchoGrapa;

            // Dónde queda REALMENTE el gancho: se mide una sola vez por tramo
            // contra Revit, en vez de calcular el radio de doblado con una
            // fórmula (ver MedirCorreccionDeGrapa). La corrección es la misma
            // para todas las grapas del tramo: comparten barra y gancho.
            (double Axial, double Lateral) correccion = (0, 0);
            XYZ? pruebaDesde = null, pruebaHasta = null;
            double zPrueba = posicionesZ[0];
            var centroPrueba = new XYZ((xMin + xMax) / 2, (yMin + yMax) / 2, zPrueba);

            if (barraCaraY is not null && grapasFila.Count > 0)
            {
                double yPrueba = PosicionIntermedia(yMin + insetEsquinas, yMax - insetEsquinas, filas, grapasFila[0]);
                pruebaDesde = new XYZ(xMin + insetY, yPrueba, zPrueba);
                pruebaHasta = new XYZ(xMax - insetY, yPrueba, zPrueba);
            }
            else if (barraCaraX is not null && grapasColumna.Count > 0)
            {
                double xPrueba = PosicionIntermedia(xMin + insetEsquinas, xMax - insetEsquinas, columnas, grapasColumna[0]);
                pruebaDesde = new XYZ(xPrueba, yMin + insetX, zPrueba);
                pruebaHasta = new XYZ(xPrueba, yMax - insetX, zPrueba);
            }

            if (pruebaDesde is not null && pruebaHasta is not null)
                correccion = MedirCorreccionDeGrapa(
                    doc, host, barraMenores, ganchoGrapa, pruebaDesde, pruebaHasta,
                    centroPrueba, contexto.Marca, res) ?? (0, 0);

            // La caja abarca su rango sobre su propio eje y cruza de extremo a
            // extremo en el perpendicular (a la altura de las barras de esquina).
            var cajas = new List<(double X1, double X2, double Y1, double Y2, RebarHookType? Gancho)>();
            if (barraCaja is not null)
            {
                foreach (CajaMenor caja in cfg.Cajas)
                {
                    int posiciones = caja.Eje == EjeCaja.Fila ? filas : columnas;
                    if (caja.Hasta >= posiciones) continue;

                    double x1, x2, y1, y2;
                    if (caja.Eje == EjeCaja.Fila)
                    {
                        x1 = xMin + insetEsquinas;
                        x2 = xMax - insetEsquinas;
                        y1 = PosicionIntermedia(yMin + insetEsquinas, yMax - insetEsquinas, filas, caja.Desde);
                        y2 = PosicionIntermedia(yMin + insetEsquinas, yMax - insetEsquinas, filas, caja.Hasta);
                    }
                    else
                    {
                        x1 = PosicionIntermedia(xMin + insetEsquinas, xMax - insetEsquinas, columnas, caja.Desde);
                        x2 = PosicionIntermedia(xMin + insetEsquinas, xMax - insetEsquinas, columnas, caja.Hasta);
                        y1 = yMin + insetEsquinas;
                        y2 = yMax - insetEsquinas;
                    }

                    // El gancho de la caja cierra sobre su lado corto: ahí es
                    // donde tiene que entrar. Se prefiere el gancho a medida y,
                    // si ni ese entra, el más chico del modelo que sí.
                    double corto = Math.Min(Math.Abs(x2 - x1), Math.Abs(y2 - y1));
                    RebarHookType? ganchoDeEstaCaja = LargoDeGancho(barraCaja, ganchoMenores) <= corto
                        ? ganchoMenores
                        : GanchoQueEntra(doc, barraCaja, gancho, corto);

                    cajas.Add((x1, x2, y1, y2, ganchoDeEstaCaja));
                }
            }

            foreach (double z in posicionesZ)
            {
                var centro = new XYZ((xMin + xMax) / 2, (yMin + yMax) / 2, z);

                // Los extremos apuntan al EJE de la barra longitudinal que la
                // grapa tiene que enganchar, así que usan el inset de ESA barra
                // de cara (insetX/insetY, los mismos con los que se colocaron
                // las longitudinales), no el de la propia grapa. La corrección
                // medida se aplica encima, dentro de IntentarGrapa.
                if (barraCaraY is not null && ganchoGrapaFila is not null)
                {
                    foreach (int fila in grapasFila)
                    {
                        double y = PosicionIntermedia(yMin + insetEsquinas, yMax - insetEsquinas, filas, fila);
                        CrearGrapa(doc, host, barraMenores, ganchoGrapaFila, gancho,
                            new XYZ(xMin + insetY, y, z), new XYZ(xMax - insetY, y, z),
                            centro, correccion, contexto.Marca, res, ref creadas);
                    }
                }

                if (barraCaraX is not null && ganchoGrapaColumna is not null)
                {
                    foreach (int columna in grapasColumna)
                    {
                        double x = PosicionIntermedia(xMin + insetEsquinas, xMax - insetEsquinas, columnas, columna);
                        CrearGrapa(doc, host, barraMenores, ganchoGrapaColumna, gancho,
                            new XYZ(x, yMin + insetX, z), new XYZ(x, yMax - insetX, z),
                            centro, correccion, contexto.Marca, res, ref creadas);
                    }
                }

                // Si una caja falla, su geometría es idéntica en todas las
                // alturas de este tramo, así que no tiene sentido reintentarla:
                // se saca de la lista y el aviso sale una sola vez, no una por
                // altura de estribo.
                for (int ic = cajas.Count - 1; ic >= 0; ic--)
                {
                    (double x1, double x2, double y1, double y2, RebarHookType? ganchoCaja) = cajas[ic];
                    int antes = creadas;
                    CrearCaja(doc, host, barraCaja!, ganchoCaja, formaEstribo, formasRechazadas, x1, x2, y1, y2, z, contexto.Marca, res, ref creadas);
                    if (creadas == antes) cajas.RemoveAt(ic);
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
    /// Las alturas (en metros) donde traslapa una varilla de un grupo par o
    /// impar: solo donde la longitud comercial de la barra
    /// (<paramref name="longitudComercialMetros"/>) obliga a cortar, y solo a
    /// la mitad de un "nivel" (issue #7), alternando niveles pares/impares
    /// según <paramref name="esParGrupo"/> para que dos barras vecinas nunca
    /// traslapen a la misma altura.
    ///
    /// Ya no hay cortes obligados por límite de columna: una misma varilla
    /// puede recorrer toda la pila (issue #41, medido contra Revit real el
    /// 7-oct). No depende del tipo de barra ni de su posición X/Y, por eso
    /// <see cref="GenerarColumna"/> lo calcula una vez por grupo.
    /// </summary>
    private static List<double> PlanificarAlturas(
        bool esParGrupo,
        List<(int Ordinal, double ZInf, double ZSup)> niveles,
        double zInicioTotalMetros,
        double zFinTotalMetros,
        double longitudComercialMetros,
        string marcaColumna,
        ResultadoArmado res)
    {
        List<double> candidatos = niveles
            .Where(n => (n.Ordinal % 2 == 0) == esParGrupo)
            .Select(n => RedondearArriba10Cm((n.ZInf + n.ZSup) / 2))
            .Where(z => z > zInicioTotalMetros + Tolerancia && z < zFinTotalMetros - Tolerancia)
            .OrderBy(z => z)
            .ToList();

        return PlanificarSplicesReales(
            candidatos, zInicioTotalMetros, zFinTotalMetros, longitudComercialMetros, marcaColumna, res);
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
            // Lo que queda hasta el remate ya entra en una barra comercial:
            // no hace falta ningún traslapo más. Sin este corte se traslapaba
            // siempre al menos una vez, aunque la varilla entera cupiera en
            // una sola barra (visto el 8-oct: 7,9 m con barra de 12 m).
            if (zFin - ultimoCorte <= longitudComercialMetros + Tolerancia) break;

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
        XYZ desde, XYZ hasta, XYZ centroSeccion, (double Axial, double Lateral) correccion,
        string marcaColumna, ResultadoArmado res, ref int creadas)
    {
        if (IntentarGrapa(doc, host, barra, gancho, desde, hasta, centroSeccion, correccion, out string motivo) is not null)
        {
            creadas++;
            return;
        }

        if (ganchoRespaldo is not null && ganchoRespaldo.Id != gancho.Id &&
            IntentarGrapa(doc, host, barra, ganchoRespaldo, desde, hasta, centroSeccion, correccion, out _) is not null)
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

    private static Rebar? IntentarGrapa(
        Document doc, FamilyInstance host, RebarBarType barra, RebarHookType gancho,
        XYZ desde, XYZ hasta, XYZ centroSeccion, (double Axial, double Lateral) correccion, out string motivo)
    {
        motivo = "";
        try
        {
            // Los puntos que entran son los EJES de las dos barras
            // longitudinales que la grapa engancha. La corrección medida los
            // corre para que esos ejes queden en el MEDIO del gancho y no en la
            // punta del tramo recto -- ver MedirCorreccionDeGrapa.
            (desde, hasta) = AplicarCorreccion(desde, hasta, centroSeccion, correccion);

            Line linea = Line.CreateBound(desde, hasta);

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
            bool haciaElCentro = Math.Abs(gancho.HookAngle - Math.PI) > 1e-3;
            bool izquierdaMiraAlCentro = haciaLaIzquierda.DotProduct(centroSeccion - desde) > 0;
            RebarTerminationOrientation orientacion =
                izquierdaMiraAlCentro == haciaElCentro
                    ? RebarTerminationOrientation.Left
                    : RebarTerminationOrientation.Right;

            var curvas = new List<Curve> { linea };

            // Camino preferido: la forma NATIVA de un segmento con estilo
            // Standard, igual que la grapa del modelo de referencia. Antes se
            // le pasaba a CreateFromCurves una línea suelta con estilo
            // StirrupTie -- un "estribo" de un solo lado, que no es una forma
            // real -- y Revit tenía que sintetizarla, sacando ganchos enormes y
            // deformes (también en columnas de 40x40: no era el tamaño de la
            // sección). Con CreateFromCurvesAndShape la posición la damos
            // nosotros y la geometría la resuelve la definición de la forma.
            RebarShape? formaGrapa = GeometriaSeccion.BuscarFormaGrapa(doc);

            using var terminaciones = new BarTerminationsData(doc)
            {
                HookTypeIdAtStart = gancho.Id,
                HookTypeIdAtEnd = gancho.Id,
                TerminationOrientationAtStart = orientacion,
                TerminationOrientationAtEnd = orientacion,
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
    /// Cuánto hay que correr la línea de la grapa para que la barra
    /// longitudinal quede en el MEDIO del gancho -- el centro del semicírculo
    /// de 180° -- y no en la punta del tramo recto, que es el punto de
    /// TANGENCIA del doblez. Hasta el 10-sep la línea terminaba justo en el eje
    /// de la longitudinal, así que el gancho enroscaba al costado de la barra en
    /// vez de abrazarla (lo que el usuario dibujó como "se pasa").
    ///
    /// No se calcula con una fórmula: se MIDE contra Revit (regla del CLAUDE.md,
    /// y el error que se repitió nueve veces con este mismo gancho). Se crea una
    /// grapa de prueba con la línea cruda, se le leen las curvas CON ganchos y
    /// radios reales -- <c>GetCenterlineCurves(false, false, false, ...)</c>,
    /// justo lo que el diagnóstico nunca había pedido -- y se toma el centro del
    /// arco del gancho. La grapa de prueba se borra.
    ///
    /// El resultado va en el marco local de la grapa: <c>Axial</c> a lo largo de
    /// la barra y <c>Lateral</c> hacia el centro de la sección, que es hacia
    /// donde gira el gancho. Así el mismo par sirve para las cuatro
    /// orientaciones de grapa de la sección.
    /// </summary>
    private static (double Axial, double Lateral)? MedirCorreccionDeGrapa(
        Document doc, FamilyInstance host, RebarBarType barra, RebarHookType gancho,
        XYZ desde, XYZ hasta, XYZ centroSeccion, string marcaColumna, ResultadoArmado res)
    {
        Rebar? prueba = IntentarGrapa(doc, host, barra, gancho, desde, hasta, centroSeccion, (0, 0), out string motivo);
        if (prueba is null)
        {
            res.Avisos.Add(
                $"{marcaColumna}: no se pudo medir dónde queda el gancho de las grapas ({motivo}); " +
                "se generan sin corregir y el gancho puede no abrazar la barra longitudinal.");
            return null;
        }

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
                res.Avisos.Add(
                    $"{marcaColumna}: la grapa de prueba salió sin ningún arco de gancho, así que no se " +
                    "puede medir dónde queda; se generan sin corregir.");
                return null;
            }

            XYZ direccion = (hasta - desde).Normalize();
            XYZ haciaElCentro = HaciaElCentro(desde, direccion, centroSeccion);
            XYZ delta = desde - arco.Center;

            (double axial, double lateral) = (delta.DotProduct(direccion), delta.DotProduct(haciaElCentro));

            res.Detalles.Add(
                $"{marcaColumna}: gancho de grapa medido en Revit -- radio {GeometriaSeccion.Metros(arco.Radius) * 100:0.0} cm; " +
                $"la línea se corrige {GeometriaSeccion.Metros(axial) * 100:0.0} cm a lo largo y " +
                $"{GeometriaSeccion.Metros(lateral) * 100:0.0} cm hacia el centro para que la longitudinal " +
                "quede en el medio del gancho.");

            return (axial, lateral);
        }
        catch (Exception ex)
        {
            res.Avisos.Add(
                $"{marcaColumna}: no se pudo leer la geometría del gancho de la grapa " +
                $"({GeometriaSeccion.Explicar(ex)}); se generan sin corregir.");
            return null;
        }
        finally
        {
            Descartar(doc, prueba);
        }
    }

    /// <summary>
    /// Aplica la corrección medida: los dos extremos se corren simétricamente a
    /// lo largo de la barra y la línea entera se corre de costado hacia el
    /// centro. El corrimiento lateral es uno solo para toda la línea porque los
    /// dos ganchos giran para el mismo lado (misma
    /// <see cref="RebarTerminationOrientation"/>, ver <c>IntentarGrapa</c>).
    /// </summary>
    private static (XYZ Desde, XYZ Hasta) AplicarCorreccion(
        XYZ desde, XYZ hasta, XYZ centroSeccion, (double Axial, double Lateral) correccion)
    {
        if (Math.Abs(correccion.Axial) < Tolerancia && Math.Abs(correccion.Lateral) < Tolerancia)
            return (desde, hasta);

        XYZ direccion = (hasta - desde).Normalize();
        XYZ lateral = HaciaElCentro(desde, direccion, centroSeccion) * correccion.Lateral;

        // La corrección axial acorta (o alarga) los DOS extremos. En una sección
        // muy chica podría dejar la grapa de largo cero o invertida, y ahí
        // Line.CreateBound tira excepción: se prefiere la grapa sin corregir.
        if ((hasta - desde).GetLength() - 2 * Math.Abs(correccion.Axial) <= Tolerancia)
            return (desde + lateral, hasta + lateral);

        return (desde + direccion * correccion.Axial + lateral,
                hasta - direccion * correccion.Axial + lateral);
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
                "Esa caja no se genera: elegí casillas que no sean contiguas, o una barra más delgada. " +
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

    /// <summary>
    /// El gancho más chico del documento que entre en
    /// <paramref name="espacioDisponible"/> (pies), entre los del MISMO ángulo
    /// que <paramref name="ganchoDelTramo"/>. Los menores heredaban el gancho
    /// del estribo principal, dimensionado para el estribo grande, y salían
    /// desproporcionados (o directamente no entraban en una caja angosta).
    ///
    /// El largo sale de <c>RebarBarType.GetHookLength</c>, la API nativa para
    /// esto -- no de un cálculo propio de la geometría del gancho.
    ///
    /// El filtro por ángulo es deliberado: sin él, "el más chico" podría bajar
    /// un gancho sísmico de 135° a uno de 90°, que es un cambio estructural y
    /// no estético. Si no hay ninguno del mismo ángulo que entre, devuelve null
    /// y quien llama decide (hoy: crear sin gancho).
    /// </summary>
    private static RebarHookType? GanchoQueEntra(
        Document doc, RebarBarType barra, RebarHookType ganchoDelTramo, double espacioDisponible)
    {
        double anguloDelTramo = ganchoDelTramo.HookAngle;

        return new FilteredElementCollector(doc)
            .OfClass(typeof(RebarHookType))
            .Cast<RebarHookType>()
            .Where(h => Math.Abs(h.HookAngle - anguloDelTramo) < 1e-6)
            .Select(h => (Gancho: h, Largo: LargoDeGancho(barra, h)))
            .Where(x => x.Largo is not null && x.Largo.Value <= espacioDisponible)
            .OrderBy(x => x.Largo!.Value)
            .Select(x => x.Gancho)
            .FirstOrDefault();
    }

    /// <summary>
    /// Largo del tramo recto del gancho de los menores, como multiplo del
    /// diametro de la barra. 6 diametros es el gancho sismico estandar; los
    /// ganchos que traen los modelos suelen estar dimensionados para el
    /// estribo principal y en una grapa se ven enormes.
    /// PENDIENTE de confirmar el valor con Carlos (normativa).
    /// </summary>
    private const double MultiplicadorGanchoMenores = 6;

    private const string NombreGanchoMenores = "CQEING - gancho de menores";

    private static double? LargoDeGancho(RebarBarType barra, RebarHookType gancho)
    {
        try { return barra.GetHookLength(gancho.Id); }
        catch { return null; }
    }

    /// <summary>
    /// Un tipo de gancho a medida para las CAJAS, en vez de reusar el del
    /// estribo principal (que sale desproporcionado) o depender de que el
    /// modelo tenga cargado uno chico.
    ///
    /// Se DUPLICA el gancho del tramo en vez de crear uno de cero con
    /// <c>RebarHookType.Create</c>: un <c>RebarHookType</c> solo se puede
    /// aplicar a barras del estilo que declara (<c>RebarHookType.Style</c>), y
    /// uno creado de cero salía con otro estilo y Revit rechazaba la armadura
    /// entera -- visto en Revit real, 10-sep. Duplicando, el estilo (y todo lo
    /// demás) ya viene bien del gancho que el tramo usa hoy sin problemas.
    ///
    /// Se crea una sola vez por documento: después se encuentra por nombre,
    /// validando además el estilo para no reusar uno inservible.
    /// </summary>
    private static RebarHookType? GanchoAMedidaParaMenores(Document doc, RebarHookType ganchoDelTramo)
    {
        RebarHookType? yaExiste = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarHookType))
            .Cast<RebarHookType>()
            .FirstOrDefault(h => h.Name == NombreGanchoMenores && h.Style == ganchoDelTramo.Style);

        if (yaExiste is not null) return yaExiste;

        try
        {
            if (ganchoDelTramo.Duplicate(NombreGanchoMenores) is not RebarHookType nuevo) return null;

            nuevo.StraightLineMultiplier = MultiplicadorGanchoMenores;
            return nuevo;
        }
        catch
        {
            return null;
        }
    }

    private static void Descartar(Document doc, Rebar rebar)
    {
        try { doc.Delete(rebar.Id); }
        catch { /* best-effort: si no se puede borrar, el aviso ya salió */ }
    }
}
