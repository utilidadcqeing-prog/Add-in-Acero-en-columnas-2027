namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// J3: genera el acero de estribos real a partir de un <see cref="PlanArmado"/>
/// -- Uniforme y Confinado en un solo generador, consumiendo <see cref="ConfigTramo"/>
/// tal como lo terminó de armar L3 en la ventana.
///
/// Reemplaza a <c>GeneradorEstribos.cs</c> (el prototipo del 1-sep, nunca
/// enganchado a ningún botón real -- ver #1/#2/#5): ese solo sabía espaciado
/// uniforme sobre un único elemento por columna. Este trabaja por TRAMO, no
/// por columna ni por elemento, porque un mismo <c>FamilyInstance</c> puede
/// contener más de un tramo (una columna que atraviesa una viga a mitad de
/// su propio cuerpo, ver <c>LectorContexto.ArmarTramos</c>) y una columna
/// lógica puede repartirse en varios elementos apilados (#28) -- por eso cada
/// tramo hostea su Rebar en su propio <see cref="Tramo.IdElementoHost"/> y
/// no en un elemento representativo de la columna entera.
/// </summary>
public static class GeneradorArmado
{
    private const double Tolerancia = 1e-6;

    /// <summary>
    /// 5 cm de norma entre el arranque/remate del estribo y la cara del
    /// tramo -- issue #5: "se respetan en los cuatro bordes de cada tramo".
    /// Se miden al BORDE del estribo, no a su eje (Luciano, 8-oct) -- mismo
    /// criterio que el recubrimiento, que va a la cara exterior de la barra
    /// (#1): quien arma el tramo le suma medio diámetro.
    /// No es un dato que la ventana pida hoy, así que va fijo acá, igual que
    /// el cuadro de <see cref="EstandarCQ"/>.
    /// </summary>
    private const double SeparacionExtremosMetros = RepartoEstribos.SeparacionExtremosMetros;

    /// <summary>
    /// TEMPORAL -- traza de diagnóstico (8-oct): deja en un archivo de texto
    /// las cotas reales, medidas sobre el Rebar ya creado, del primer y el
    /// último estribo de cada grupo, para comprobar contra Revit los 5 cm de
    /// los extremos sin depender de una cota puesta a mano. Sacar una vez
    /// comprobado.
    /// </summary>
#pragma warning disable CS0649 // en Release nadie lo asigna: el registro es solo de Debug
    private static System.Text.StringBuilder? _traza;
#pragma warning restore CS0649

    public static ResultadoArmado Generar(Document doc, OpcionesArmado opciones)
    {
        var res = new ResultadoArmado();
        PlanArmado plan = opciones.Plan;

        // #145: regenerar reemplaza, no apila. Va antes de crear nada, y en la
        // misma transacción: si la generación falla, el acero anterior vuelve.
        res.Eliminados = BorrarAnteriores(doc, plan, opciones.BorrarArmadoAjeno);

#if DEBUG
        // Solo en Debug: el paquete publicado (Release) no deja registros.
        _traza = new System.Text.StringBuilder();
#endif

        Dictionary<string, RebarBarType> barrasPorNombre = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
            .GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First());

        Dictionary<string, RebarHookType> ganchosPorNombre = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First());

        // #64: forma y gancho PROPIOS del add-in, con las medidas del cuadro,
        // en cualquier modelo. Si no se pudieron crear, la forma del modelo
        // (GeometriaSeccion.BuscarFormaEstribo) y el gancho configurado en el
        // tramo quedan de respaldo; y sin forma, cada CrearGrupo cae solo al
        // camino de siempre (CreateFromCurves). Se resuelve una sola vez acá.
        var propios = new TiposPropios(doc);
        TiposPropios.Pieza<RebarShape> formaPropia = propios.FormaEstribo();
        RebarShape? formaEstribo = formaPropia.Tipo ?? GeometriaSeccion.BuscarFormaEstribo(doc);
        if (formaPropia.Aviso is not null)
            res.Avisos.Add($"Estribos: {formaPropia.Aviso}; se usa la forma de estribo del modelo.");

        TiposPropios.Pieza<RebarHookType> ganchoPropio = propios.GanchoEstribo();
        if (ganchoPropio.Aviso is not null)
            res.Avisos.Add($"Estribos: {ganchoPropio.Aviso}; se usa el gancho configurado en cada tramo.");

        if (VarillasPropias.CompletarPeso(doc) is string avisoPeso)
            res.Avisos.Add($"Varillas: {avisoPeso}.");

        var cacheHost = new Dictionary<ElementId, (CurveLoop ContornoCrudo, double ZBase)>();
        var ganchosYaAvisados = new HashSet<(ElementId Barra, ElementId Gancho)>();

        // Qué formas de estribo (RebarShape) ya se comprobó que generan un
        // lado diagonal (ver GeometriaSeccion.TieneLadoDiagonal) -- una sola
        // vez por corrida: si "formaEstribo" resulta mala, no tiene sentido
        // crear+escalar+medir+borrar en cada uno de los estribos de todas las
        // columnas, ni repetir el mismo aviso decenas de veces.
        var formasRechazadas = new HashSet<ElementId>();

        foreach (ColumnaPlan columnaPlan in plan.Columnas)
        {
            GenerarColumna(doc, columnaPlan, barrasPorNombre, ganchosPorNombre, formaEstribo, propios, ganchoPropio.Tipo,
                cacheHost, ganchosYaAvisados, formasRechazadas, res);
        }

#if DEBUG
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "practicacq-estribos.log"), _traza?.ToString() ?? "");
        }
        catch { /* best-effort */ }
        _traza = null;
#endif

        return res;
    }

    private static void GenerarColumna(
        Document doc,
        ColumnaPlan columnaPlan,
        Dictionary<string, RebarBarType> barrasPorNombre,
        Dictionary<string, RebarHookType> ganchosPorNombre,
        RebarShape? formaEstribo,
        TiposPropios propios,
        RebarHookType? ganchoPropio,
        Dictionary<ElementId, (CurveLoop ContornoCrudo, double ZBase)> cacheHost,
        HashSet<(ElementId Barra, ElementId Gancho)> ganchosYaAvisados,
        HashSet<ElementId> formasRechazadas,
        ResultadoArmado res)
    {
        ColumnaContexto? contexto = columnaPlan.Contexto;
        if (contexto is null)
        {
            res.Avisos.Add($"Columna {columnaPlan.Id}: sin contexto en el plan; se omite.");
            return;
        }

        double recubrimientoPies = EstandarCQ.APies(columnaPlan.Recubrimiento ?? contexto.Recubrimiento);
        if (recubrimientoPies <= Tolerancia)
        {
            res.Avisos.Add(
                $"{contexto.Marca}: no tiene recubrimiento lateral asignado en el modelo. " +
                "Asígnale un recubrimiento en Revit y vuelve a generar -- acá no se impone ninguno.");
            res.TramosOmitidos += columnaPlan.Tramos.Count(t => t is not null);
            return;
        }

        // Todos los estribos de la columna salen con la misma sección: la zona
        // común de la pila. Sin esto, en una pieza más ancha (el dado de
        // cimentación) el estribo salía más grande que en el resto y dejaba
        // las barras longitudinales -- que ya van con la zona común -- sueltas
        // adentro (visto por Luciano el 8-oct).
        (double XMin, double XMax, double YMin, double YMax)? zonaComun =
            GeometriaSeccion.ZonaComun(doc, contexto, cacheHost);
        var tramosConSeccionMayor = new List<int>();

        // Los estribos menores de la columna: arman el paquete de cada
        // posición de estribo, y el reparto tiene que dejarle lugar.
        List<EstriboMenor> piezas = columnaPlan.Longitudinal?.MenoresParaGenerar() ?? new List<EstriboMenor>();

        int n = Math.Min(contexto.Tramos.Count, columnaPlan.Tramos.Count);
        for (int i = 0; i < n; i++)
        {
            // null: este tramo no estaba aceptado en la ventana -- no es un
            // problema que reportar, es simplemente "no pedido en esta corrida".
            if (columnaPlan.Tramos[i] is not ConfigTramo cfg) continue;

            Tramo tramo = contexto.Tramos[i];

            // El tramo contra zapata no lleva estribos (Luciano, 20-oct); el
            // acero longitudinal sí pasa por él.
            if (tramo.Tipo == TipoTramo.ContraZapata)
            {
                res.Avisos.Add($"{contexto.Marca} tramo {tramo.Indice}: es el tramo contra zapata; ahí no se generan estribos.");
                continue;
            }

            try
            {
                if (!GenerarTramo(
                        doc, contexto.Marca, tramo, cfg, barrasPorNombre, ganchosPorNombre, formaEstribo, propios, ganchoPropio,
                        piezas, recubrimientoPies, zonaComun, tramosConSeccionMayor, cacheHost, ganchosYaAvisados, formasRechazadas, res))
                {
                    res.TramosOmitidos++;
                }
            }
            catch (Exception ex)
            {
                res.TramosOmitidos++;
                res.Avisos.Add($"{contexto.Marca} tramo {tramo.Indice}: {GeometriaSeccion.Explicar(ex)}");

                // Probado sobre Revit real: después de que Rebar.CreateFromCurves
                // falla una vez (devuelve null) en una transacción, las llamadas
                // siguientes -- incluso para geometría sin ninguna relación con la
                // que falló -- empiezan a fallar igual. Regenerar acá intenta
                // "limpiar" ese estado antes de seguir con el próximo tramo/barra.
                try { doc.Regenerate(); } catch { /* best-effort */ }
            }
        }

        if (tramosConSeccionMayor.Count > 0 && zonaComun is { } zona)
            res.Avisos.Add(
                $"{contexto.Marca}: {tramosConSeccionMayor.Count} tramo(s) están en una pieza más ancha que el resto de la " +
                $"columna; sus estribos se armaron con la sección común " +
                $"({GeometriaSeccion.Metros(zona.XMax - zona.XMin) * 100:0.#}×{GeometriaSeccion.Metros(zona.YMax - zona.YMin) * 100:0.#} cm) " +
                "para que todos queden iguales y alineados.");
    }

    /// <summary>
    /// Un tramo, un <see cref="ConfigTramo"/>. Devuelve false (con aviso, sin
    /// tirar excepción) para los motivos "esperables" -- host borrado, tipo de
    /// barra/gancho que ya no existe -- y deja que una excepción real
    /// de geometría se propague para que <see cref="GenerarColumna"/> la
    /// registre con <see cref="GeometriaSeccion.Explicar"/> sin tumbar el resto del lote.
    /// </summary>
    private static bool GenerarTramo(
        Document doc,
        string marcaColumna,
        Tramo tramo,
        ConfigTramo cfg,
        Dictionary<string, RebarBarType> barrasPorNombre,
        Dictionary<string, RebarHookType> ganchosPorNombre,
        RebarShape? formaEstribo,
        TiposPropios propios,
        RebarHookType? ganchoPropio,
        IReadOnlyList<EstriboMenor> piezas,
        double recubrimientoPies,
        (double XMin, double XMax, double YMin, double YMax)? zonaComun,
        List<int> tramosConSeccionMayor,
        Dictionary<ElementId, (CurveLoop ContornoCrudo, double ZBase)> cacheHost,
        HashSet<(ElementId Barra, ElementId Gancho)> ganchosYaAvisados,
        HashSet<ElementId> formasRechazadas,
        ResultadoArmado res)
    {
        // Un tramo dentro del canto de la viga (nudo) genera estribos como
        // cualquier otro: hasta el 11-sep se salteaba con un "return true" sin
        // avisar, y quedaba un hueco de armadura justo en el nudo. William lo
        // cerró en la reunión de ese día -- "obliga que en los nudos sea
        // uniforme, no se permita meter confinamiento en el nudo" (issue #58,
        // que contesta el #55). La parte de "uniforme" la garantiza la ventana,
        // que deshabilita el modo confinado con un nudo activo.
        //
        // Las grapas y cajas (estribos menores, J4) siguen yendo solo en los
        // tramos libres: eso no cambia acá.

        // tramo.Indice ya es el mismo número 0-indexado que muestra la
        // ventana ("Tramo 0", "Tramo 1"...); sumarle 1 acá desalineaba el
        // aviso con lo que el usuario tenía aceptado en pantalla.
        string etiquetaTramo = $"{marcaColumna} tramo {tramo.Indice}";

        if (tramo.IdElementoHost is null || !long.TryParse(tramo.IdElementoHost, out long idHost))
        {
            res.Avisos.Add($"{etiquetaTramo}: sin elemento de Revit asociado; se omite.");
            return false;
        }

        if (doc.GetElement(new ElementId(idHost)) is not FamilyInstance host)
        {
            res.Avisos.Add($"{etiquetaTramo}: el elemento que lo hosteaba ya no existe en el modelo; se omite.");
            return false;
        }

        if (string.IsNullOrEmpty(cfg.TipoBarraNombre) || !barrasPorNombre.TryGetValue(cfg.TipoBarraNombre, out RebarBarType? barra))
        {
            res.Avisos.Add($"{etiquetaTramo}: el tipo de barra «{cfg.TipoBarraNombre}» no existe en el documento; se omite.");
            return false;
        }

        // #64: al generar se vuelve a evaluar la barra (el plan puede venir de
        // una configuración guardada o de otro modelo).
        (AptitudEstribo aptitud, string? motivoAptitud) = CuadroVarillas.EvaluarParaEstribo(EstandarCQ.ComoTipoBarra(barra));
        if (aptitud == AptitudEstribo.NoApta)
        {
            res.Avisos.Add($"{etiquetaTramo}: la barra no es apta para estribo según el cuadro ({motivoAptitud}); se omite.");
            return false;
        }

        // El gancho propio, ajustado a la extensión del cuadro para esta
        // barra; el configurado en el tramo queda solo de respaldo.
        RebarHookType? gancho = ganchoPropio;
        if (gancho is not null)
        {
            if (EstandarCQ.Buscar(barra)?.GanchoEstribo135 is { } cuadro && propios.AjustarExtension(barra, gancho, cuadro) is string avisoAjuste)
                res.Avisos.Add($"{etiquetaTramo}: {avisoAjuste}");
        }
        else if (string.IsNullOrEmpty(cfg.TipoGanchoNombre) || !ganchosPorNombre.TryGetValue(cfg.TipoGanchoNombre, out gancho))
        {
            res.Avisos.Add($"{etiquetaTramo}: el tipo de gancho «{cfg.TipoGanchoNombre}» no existe en el documento; se omite.");
            return false;
        }
        else
        {
            AvisarSiGanchoNoCumpleNorma(barra, gancho, ganchosYaAvisados, res);
        }

        if (RebarHostData.GetRebarHostData(host) is null)
            throw new InvalidOperationException($"el elemento {host.Id} no admite armadura (host no válido para rebar).");

        (CurveLoop contornoCrudo, double zBase) = GeometriaSeccion.ObtenerContornoHost(host, cacheHost);

        // Si esta pieza es más ancha que la zona común de la columna, el
        // estribo se arma con la zona común y no con el contorno propio. Si
        // coincide (lo normal, y siempre en una columna de una sola sección)
        // se deja el contorno real del elemento, sin tocar nada.
        if (zonaComun is { } zona)
        {
            const double margen = 0.003; // ~1 mm, en pies
            (double hXMin, double hXMax, double hYMin, double hYMax) = GeometriaSeccion.Huella(contornoCrudo);
            if (hXMin < zona.XMin - margen || hXMax > zona.XMax + margen ||
                hYMin < zona.YMin - margen || hYMax > zona.YMax + margen)
            {
                contornoCrudo = GeometriaSeccion.Rectangulo(zona.XMin, zona.XMax, zona.YMin, zona.YMax, zBase);
                tramosConSeccionMayor.Add(tramo.Indice);
            }
        }

        // Recubrimiento a la cara EXTERIOR de la barra, no a su centro (#1):
        // la curva que le pasamos a Rebar.CreateFromCurves es el eje de la
        // barra, así que hay que meter el contorno medio diámetro más que el
        // recubrimiento puro para que el borde de la barra quede justo ahí.
        CurveLoop contorno = GeometriaSeccion.OffsetHaciaAdentro(contornoCrudo, recubrimientoPies + barra.BarNominalDiameter / 2);

        double ladoMenor = contorno.Min(c => c.Length);
        double doblez = barra.StirrupTieBendDiameter;
        if (ladoMenor <= doblez)
            throw new InvalidOperationException(
                $"la barra {barra.Name} es demasiado gruesa para esta sección: su doblez de estribo mide " +
                $"{GeometriaSeccion.Metros(doblez) * 100:0.#} cm y el lado más corto del estribo queda en {GeometriaSeccion.Metros(ladoMenor) * 100:0.#} cm.");

        // Este chequeo ("gancho > mitad del lado más largo") se agregó el
        // 4-sep basado en fallas reales de Rebar.CreateFromCurves -- pero esa
        // sesión todavía tenía el bug del corte a la mitad del lado (curvas
        // colineales sin ángulo real, ver CrearGrupo), que por sí solo ya
        // hacía fallar la creación sin importar el gancho. No hay evidencia
        // de que este límite sea real una vez sacado ese bug -- se saca
        // también, y que sea Rebar.CreateFromCurves (ya con diagnóstico
        // claro si devuelve null) el que decida si la geometría entra o no,
        // en vez de cortar acá con una heurística sin confirmar.

        // Paquete de la posición de estribo: el principal en el centro y los
        // estribos menores pegados encima y debajo (sección 2 del diseño del
        // 20-oct). Los 5 cm de los extremos van al BORDE del paquete, y el
        // reparto es del punto medio de un paquete al del siguiente; el eje
        // del principal queda corrido DesplazamientoPrincipal de ese punto.
        // Sin menores (o en un nudo, donde no van) el paquete es solo el
        // principal y esto da lo de siempre: 5 cm más medio diámetro.
        var claveTramo = (tramo.IdElementoHost!, tramo.Indice);
        bool conMenores = tramo.Tipo != TipoTramo.DentroDeViga && piezas.Count > 0;
        PaqueteEstribos.Paquete paquete = PaqueteEstribos.Calcular(
            conMenores ? piezas : Array.Empty<EstriboMenor>(),
            EstandarCQ.DiametroMm(barra),
            p => EstandarCQ.DiametroMm(GeneradorLongitudinal.BarraDePieza(p, barra, barrasPorNombre, out _)));
        if (conMenores) res.PaquetePorTramo[claveTramo] = paquete;

        _traza?.AppendLine(
            $"{etiquetaTramo} ({tramo.Tipo}): Z {tramo.ZInferior:0.####} -> {tramo.ZSuperior:0.####} m; barra «{barra.Name}» " +
            $"Ø {GeometriaSeccion.Metros(barra.BarNominalDiameter) * 1000:0.#} mm");
        // Misma cuenta que la vista previa del panel (RepartoEstribos).
        (double zInfMetros, double zSupMetros) = RepartoEstribos.Limites(tramo.ZInferior, tramo.ZSuperior, paquete);
        double zInf = EstandarCQ.APies(zInfMetros);
        double zSup = EstandarCQ.APies(zSupMetros);
        double altura = zSup - zInf;

        if (altura <= Tolerancia)
        {
            res.Avisos.Add(
                $"{etiquetaTramo}: no deja altura libre para los {SeparacionExtremosMetros * 100:0} cm de extremo en ambos bordes; se omite.");
            return false;
        }

        using var terminaciones = new BarTerminationsData(doc)
        {
            HookTypeIdAtStart = gancho.Id,
            HookTypeIdAtEnd = gancho.Id,
            TerminationOrientationAtStart = RebarTerminationOrientation.Left,
            TerminationOrientationAtEnd = RebarTerminationOrientation.Left
        };

        if (cfg.Modo == ModoEstribo.Uniforme)
        {
            double espaciado = EstandarCQ.APies(cfg.EspaciadoUniforme);
            CrearGrupo(doc, host, contorno, zBase, zInf, altura, barra, formaEstribo, formasRechazadas, terminaciones, espaciado, etiquetaTramo, "uniforme", res, claveTramo);
            return true;
        }

        // Confinado: tres grupos -- abajo confinado, centro no confinado, arriba
        // confinado -- cada uno su propio Rebar (#5). Un estribo justo en cada
        // uno de los 4 bordes (zInf, zInf+L1, zSup-L1, zSup): el de en medio no
        // repite el de sus vecinos (includeFirstBar/includeLastBar se turnan).
        double l1 = EstandarCQ.APies(cfg.LongitudConfinamiento);
        double espaciadoConf = EstandarCQ.APies(cfg.EspaciadoConfinado);
        double espaciadoNoConf = EstandarCQ.APies(cfg.EspaciadoNoConfinado);

        if (l1 * 2 >= altura - Tolerancia)
        {
            // No queda zona central: todo el tramo confinado en un solo grupo.
            res.Avisos.Add(
                $"{etiquetaTramo}: la longitud de confinamiento ×2 no deja zona central; " +
                "se armó confinado de punta a punta.");
            CrearGrupo(doc, host, contorno, zBase, zInf, altura, barra, formaEstribo, formasRechazadas, terminaciones, espaciadoConf, etiquetaTramo, "confinado (sin zona central)", res, claveTramo);
            return true;
        }

        double l2 = altura - 2 * l1;

        CrearGrupo(doc, host, contorno, zBase, zInf, l1, barra, formaEstribo, formasRechazadas, terminaciones, espaciadoConf,
            etiquetaTramo, "confinado inferior", res, claveTramo, incluirUltimo: false);

        CrearGrupo(doc, host, contorno, zBase, zInf + l1, l2, barra, formaEstribo, formasRechazadas, terminaciones, espaciadoNoConf,
            etiquetaTramo, "zona central", res, claveTramo, incluirUltimo: false);

        CrearGrupo(doc, host, contorno, zBase, zInf + l1 + l2, l1, barra, formaEstribo, formasRechazadas, terminaciones, espaciadoConf,
            etiquetaTramo, "confinado superior", res, claveTramo);

        return true;
    }

    /// <summary>
    /// Un solo <c>Rebar</c> con espaciado máximo, propagado hacia arriba
    /// desde <paramref name="zInicio"/> a lo largo de <paramref name="longitud"/>.
    /// <paramref name="incluirUltimo"/> en false evita duplicar el estribo que
    /// el grupo vecino ya pone en el borde compartido (ver los tres llamados
    /// del caso Confinado).
    /// </summary>
    private static void CrearGrupo(
        Document doc,
        FamilyInstance host,
        CurveLoop contorno,
        double zBaseContorno,
        double zInicio,
        double longitud,
        RebarBarType barra,
        RebarShape? formaEstribo,
        HashSet<ElementId> formasRechazadas,
        BarTerminationsData terminaciones,
        double espaciado,
        string etiquetaTramo,
        string etiquetaGrupo,
        ResultadoArmado res,
        (string IdElementoHost, int IndiceTramo) claveTramo,
        bool incluirUltimo = true)
    {
        if (longitud <= Tolerancia || espaciado <= Tolerancia) return;

        var mover = Transform.CreateTranslation(XYZ.BasisZ * (zInicio - zBaseContorno));
        IList<Curve> curvasTrasladadas = contorno.Select(c => c.CreateTransformed(mover)).ToList();

        // Camino preferido (ver GeometriaSeccion.BuscarFormaEstribo): la
        // familia RebarShape resuelve sus propias esquinas/ganchos, sin el
        // quiebre a mitad de lado ni su dobladura visible.
        Rebar? estribo = formaEstribo is not null
            ? CrearDesdeForma(doc, host, curvasTrasladadas, barra, formaEstribo, formasRechazadas, terminaciones, res, etiquetaTramo)
            : null;

        // Sin forma cargada en el documento, o si CrearDesdeForma no pudo
        // (sección incompatible con esa forma, por ejemplo) -- camino de
        // siempre, con el quiebre a mitad de lado (ver ConQuiebreEnMedioDeLado).
        // Mejor un estribo con la dobladura conocida que ninguno.
        IList<Curve>? curvasFallback = null;
        if (estribo is null)
        {
            curvasFallback = GeometriaSeccion.ConQuiebreEnMedioDeLado(curvasTrasladadas);
            estribo = Rebar.CreateFromCurves(
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

        // Rebar.CreateFromCurves puede devolver null en vez de tirar
        // excepción cuando Revit no logra armar la forma con estas curvas/
        // gancho/barra (probado sobre Revit real -- sin este chequeo,
        // GetShapeDrivenAccessor() de la línea siguiente revienta con un
        // NullReferenceException que no dice nada útil).
        if (estribo is null)
        {
            IList<Curve> curvasDiagnostico = curvasFallback ?? curvasTrasladadas;
            string ladosCm = string.Join(", ", curvasDiagnostico.Select(c => $"{GeometriaSeccion.Metros(c.Length) * 100:0.#}cm"));
            double hookLength = 0;
            try { hookLength = barra.GetHookLength(terminaciones.HookTypeIdAtStart); } catch { /* diagnóstico best-effort */ }

            throw new InvalidOperationException(
                $"Revit no pudo crear el estribo con esta geometría (Rebar.CreateFromCurves devolvió null) " +
                $"con la barra «{barra.Name}». Diagnóstico: {curvasDiagnostico.Count} curvas [{ladosCm}], " +
                $"diámetro barra {GeometriaSeccion.Metros(barra.BarNominalDiameter) * 1000:0.#}mm, " +
                $"doblez estribo {GeometriaSeccion.Metros(barra.StirrupTieBendDiameter) * 100:0.#}cm, " +
                $"gancho {GeometriaSeccion.Metros(hookLength) * 100:0.#}cm, recubrimiento+radio ya restado del contorno.");
        }

        estribo.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(
            espaciado,
            longitud,
            barsOnNormalSide: true,
            includeFirstBar: true,
            includeLastBar: incluirUltimo);

        MarcaEstribos.Marcar(estribo, host.Id);

        // Sin regenerar, NumberOfBarPositions todavía devuelve la cuenta de
        // ANTES de aplicar el reparto: uno menos de los que quedan de verdad.
        // Medido contra Revit real el 8-oct (17 estribos reales, 16 leídos).
        // Con la cuenta corta salía mal el detalle que se le muestra al
        // usuario y, peor, las posiciones que se le pasan a los estribos
        // menores (RegistrarPosiciones): 16 a 20,3 cm en vez de 17 a 19 cm.
        doc.Regenerate();

        RebarShapeDrivenAccessor acceso = estribo.GetShapeDrivenAccessor();
        int posiciones = estribo.NumberOfBarPositions;
        double reparto = acceso.ArrayLength;
        double separacionReal = posiciones > 1 ? reparto / (posiciones - 1) : 0;

        // La vista previa del panel cuenta los estribos sin Revit
        // (RepartoEstribos); si Revit puso otra cantidad, la vista previa
        // mintió y hay que corregir la cuenta. No cambia lo que se genera.
        // Va a Avisos (no a Detalles) porque Detalles no se muestra en la ventana.
        int previstos = RepartoEstribos.CantidadEnGrupo(longitud, espaciado, incluirUltimo);
        if (previstos != estribo.Quantity)
            res.Avisos.Add(
                $"{etiquetaTramo} ({etiquetaGrupo}): la vista previa calculó {previstos} estribos y Revit puso {estribo.Quantity} " +
                $"(posiciones {posiciones}).");

        res.GruposCreados++;
        res.Detalles.Add(
            $"{etiquetaTramo} ({etiquetaGrupo}): {posiciones} estribos cada " +
            $"{GeometriaSeccion.Metros(separacionReal) * 100:0.#} cm sobre {GeometriaSeccion.Metros(reparto):0.00} m.");

        RegistrarPosiciones(posiciones, zInicio, separacionReal, incluirUltimo, claveTramo, res);

        if (_traza is not null)
        {
            try
            {
                doc.Regenerate();

                // Extensión real en Z de un estribo del grupo: el eje (sin
                // ganchos) y el estribo completo (con ganchos y radios, que no
                // siempre es plano -- el solape del cierre lo desplaza).
                (double Min, double Max) RangoZ(int indice, bool completo)
                {
                    Transform lugar = acceso.GetBarPositionTransform(indice);
                    List<double> zs = estribo
                        .GetCenterlineCurves(false, !completo, !completo, MultiplanarOption.IncludeAllMultiplanarCurves, indice)
                        .SelectMany(c => c.Tessellate())
                        .Select(q => lugar.OfPoint(q).Z)
                        .ToList();
                    return (zs.Min(), zs.Max());
                }

                _traza.AppendLine(
                    $"    {etiquetaGrupo}: posiciones {estribo.NumberOfBarPositions}, cantidad {estribo.Quantity}, " +
                    $"largo del reparto {GeometriaSeccion.Metros(acceso.ArrayLength):0.####} m, espaciado máximo " +
                    $"{GeometriaSeccion.Metros(estribo.MaxSpacing) * 100:0.##} cm, incluye primero: {estribo.IncludeFirstBar}, " +
                    $"último: {estribo.IncludeLastBar}; pedido: arranque Z {GeometriaSeccion.Metros(zInicio):0.####}, " +
                    $"largo {GeometriaSeccion.Metros(longitud):0.####} m, espaciado {GeometriaSeccion.Metros(espaciado) * 100:0.##} cm");

                foreach (int indice in new[] { 0, posiciones - 2, posiciones - 1 }.Where(i => i >= 0).Distinct())
                {
                    (double ejeMin, double ejeMax) = RangoZ(indice, completo: false);
                    (double todoMin, double todoMax) = RangoZ(indice, completo: true);
                    _traza.AppendLine(
                        $"    {etiquetaGrupo}, estribo {indice + 1} de {posiciones} (existe: {estribo.DoesBarExistAtPosition(indice)}): eje Z {GeometriaSeccion.Metros(ejeMin):0.####} a " +
                        $"{GeometriaSeccion.Metros(ejeMax):0.####}; con ganchos Z {GeometriaSeccion.Metros(todoMin):0.####} a " +
                        $"{GeometriaSeccion.Metros(todoMax):0.####} m");
                }
            }
            catch (Exception ex)
            {
                _traza.AppendLine($"    {etiquetaGrupo}: no se pudo medir ({ex.GetType().Name}: {ex.Message})");
            }
        }
    }

    /// <summary>
    /// Crea el estribo a partir de la familia <see cref="RebarShape"/> del
    /// documento (ver <see cref="GeometriaSeccion.BuscarFormaEstribo"/>) en
    /// vez de curvas armadas a mano -- mismo patrón que
    /// <c>ShapeBasedStirrupService.cs</c> del otro add-in de referencia del
    /// equipo: <c>CreateFromRebarShape</c> + <c>ScaleToBox</c> para ajustarla
    /// al tamaño real de la sección, y el gancho se fija DESPUÉS de crear
    /// (<c>SetHookTypeId</c>/<c>SetHookOrientation</c> por posición, 0 y 1 --
    /// esta sobrecarga no acepta <see cref="BarTerminationsData"/>).
    /// Cualquier falla acá (forma incompatible con esta sección puntual,
    /// etc.) devuelve <c>null</c> en vez de tirar excepción -- quien llama
    /// cae al camino de <c>CreateFromCurves</c> de siempre, no vale la pena
    /// tirar la columna abajo por esto cuando ya existe un camino que funciona.
    /// </summary>
    /// <summary>~0,03 mm: por debajo, el eje ya está donde se pidió.</summary>
    private const double ToleranciaEjePies = 1e-4;

    /// <summary>
    /// Dónde quedó cada lado del eje del estribo en planta: la X de los dos
    /// lados verticales y la Y de los dos horizontales, leídos sin ganchos ni
    /// radios. No sirve la caja que encierra esas curvas: en la esquina de los
    /// ganchos Revit alarga los dos tramos que llegan ahí (~7 mm con #3,
    /// medido el 21-oct en la columna 387983) y la caja salía más grande que
    /// el estribo. null si no se pudo leer.
    /// </summary>
    private static (double X1, double X2, double Y1, double Y2)? LadosDelEje(Rebar estribo)
    {
        try
        {
            List<Line> rectas = estribo
                .GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0)
                .OfType<Line>().ToList();
            List<double> xs = rectas.Where(l => Math.Abs(l.Direction.X) < 1e-6).Select(l => l.GetEndPoint(0).X).ToList();
            List<double> ys = rectas.Where(l => Math.Abs(l.Direction.Y) < 1e-6).Select(l => l.GetEndPoint(0).Y).ToList();
            if (xs.Count < 2 || ys.Count < 2) return null;
            return (xs.Min(), xs.Max(), ys.Min(), ys.Max());
        }
        catch
        {
            return null;
        }
    }

    private static Rebar? CrearDesdeForma(
        Document doc, FamilyInstance host, IList<Curve> curvasTrasladadas,
        RebarBarType barra, RebarShape forma, HashSet<ElementId> formasRechazadas,
        BarTerminationsData terminaciones, ResultadoArmado res, string etiquetaTramo)
    {
        // Ya se comprobó en esta corrida que esta forma trae un lado
        // diagonal (ver GeometriaSeccion.TieneLadoDiagonal) -- no vale la
        // pena repetir crear+escalar+medir+borrar para cada estribo, cae
        // directo al camino de respaldo.
        if (formasRechazadas.Contains(forma.Id)) return null;

        try
        {
            (double xMin, double xMax, double yMin, double yMax) = GeometriaSeccion.Huella(curvasTrasladadas);
            double z = curvasTrasladadas[0].GetEndPoint(0).Z;
            XYZ origen = new(xMin, yMin, z);
            XYZ vectorX = new(xMax - xMin, 0, 0);
            XYZ vectorY = new(0, yMax - yMin, 0);

            Rebar? estribo = Rebar.CreateFromRebarShape(doc, forma, barra, host, origen, XYZ.BasisX, XYZ.BasisY);
            if (estribo is null) return null;

            estribo.GetShapeDrivenAccessor().ScaleToBox(origen, vectorX, vectorY);

            // La caja que se pide es el EJE de la barra, pero la forma propia
            // "CQ Estribo" se acota por FUERA del doblez: la caja terminaba
            // siendo la cara exterior y el estribo quedaba medio diámetro más
            // adentro que el recubrimiento (4,48 cm en vez de 4 con #3; medido
            // el 21-oct, docs/planes/2026-10-21-seccion-como-en-revit.md). Se
            // mide dónde quedó cada lado y se vuelve a escalar con la caja
            // corregida lado por lado, hasta que coincida (así sirve igual para
            // una forma del modelo acotada por el eje).
            (double X1, double X2, double Y1, double Y2) caja = (xMin, xMax, yMin, yMax);
            for (int intento = 0; intento < 3 && LadosDelEje(estribo) is { } lados; intento++)
            {
                double dx1 = xMin - lados.X1, dx2 = xMax - lados.X2, dy1 = yMin - lados.Y1, dy2 = yMax - lados.Y2;
                if (new[] { dx1, dx2, dy1, dy2 }.All(d => Math.Abs(d) <= ToleranciaEjePies)) break;

                caja = (caja.X1 + dx1, caja.X2 + dx2, caja.Y1 + dy1, caja.Y2 + dy2);
                estribo.GetShapeDrivenAccessor().ScaleToBox(
                    new XYZ(caja.X1, caja.Y1, z), new XYZ(caja.X2 - caja.X1, 0, 0), new XYZ(0, caja.Y2 - caja.Y1, 0));
            }

            ElementId idGancho = terminaciones.HookTypeIdAtStart;
            if (idGancho != ElementId.InvalidElementId)
            {
                try { estribo.SetHookTypeId(0, idGancho); estribo.SetHookTypeId(1, idGancho); }
                catch { /* la forma puede no tener posiciones de gancho -- no bloquea */ }

                try { estribo.SetTerminationOrientation(0, RebarTerminationOrientation.Left); estribo.SetTerminationOrientation(1, RebarTerminationOrientation.Left); }
                catch { /* idem */ }
            }

            // BuscarFormaEstribo elige por NOMBRE, no por geometría -- una
            // forma cuyo nombre hace match pero en realidad trae una cruceta
            // de confinamiento incorporada (patas diagonales) se aceptaría
            // sin darse cuenta. Medido contra Revit real el 11-sep: la forma
            // "T1" del modelo de William generó justo esto.
            if (GeometriaSeccion.TieneLadoDiagonal(estribo))
            {
                try { doc.Delete(estribo.Id); } catch { /* best-effort */ }

                if (formasRechazadas.Add(forma.Id))
                    res.Avisos.Add(
                        $"{etiquetaTramo}: la forma «{forma.Name}» generó un lado diagonal en vez de un " +
                        "rectángulo simple (¿tiene una cruceta de confinamiento incorporada?); se usa el " +
                        "camino de respaldo para el resto de esta corrida.");

                return null;
            }

            return estribo;
        }
        catch (Exception ex)
        {
            res.Avisos.Add(
                $"{etiquetaTramo}: no se pudo usar la forma «{forma.Name}» para el estribo ({ex.Message}); " +
                "se usa el camino de respaldo.");
            return null;
        }
    }

    /// <summary>
    /// Guarda la cota Z de cada estribo de este grupo en
    /// <see cref="ResultadoArmado.PosicionesEstribosPorTramo"/> -- issue #32:
    /// GeneradorLongitudinal (J4) los lee de acá para poner los estribos
    /// menores exactamente ahí, en vez de calcular sus propias posiciones.
    ///
    /// Calculado a mano (zInicio + i * separacionReal, propagando en +Z como
    /// dice el propio <c>SetLayoutAsMaximumSpacing(..., barsOnNormalSide: true,
    /// includeFirstBar: true, ...)</c>) y NO leído de vuelta con
    /// <c>Rebar.GetCenterlineCurves</c> -- probado sobre Revit real: esa
    /// llamada tira <c>NullReferenceException</c> justo después de fijar el
    /// layout, porque Revit todavía no recalculó la geometría del Rebar en
    /// esta transacción (hace falta un <c>Document.Regenerate()</c> de por
    /// medio). La cuenta a mano evita depender de eso.
    /// </summary>
    private static void RegistrarPosiciones(
        int posiciones, double zInicio, double separacionReal, bool incluirUltimo,
        (string IdElementoHost, int IndiceTramo) claveTramo, ResultadoArmado res)
    {
        if (!res.PosicionesEstribosPorTramo.TryGetValue(claveTramo, out List<double>? zetas))
        {
            zetas = new List<double>();
            res.PosicionesEstribosPorTramo[claveTramo] = zetas;
        }

        // NumberOfBarPositions cuenta también la última posición aunque el
        // grupo no tenga estribo ahí (includeLastBar: false).
        zetas.AddRange(PaqueteEstribos.PosicionesDelGrupo(posiciones, zInicio, separacionReal, incluirUltimo));
    }

    /// <summary>
    /// #145: antes de generar, quita el acero que este add-in generó en una
    /// corrida anterior (el que lleva <see cref="MarcaEstribos"/>), para que
    /// regenerar reemplace en vez de apilar un juego sobre otro. Solo en las
    /// columnas de esta corrida (las que tienen algún tramo con ✓):
    /// <list type="bullet">
    /// <item>las longitudinales, en toda la columna: se vuelven a armar de
    /// punta a punta aunque se marque un solo tramo;</item>
    /// <item>los estribos, cajas y grapas, solo en los tramos con ✓: los de
    /// los tramos sin ✓ no se regeneran, así que se conservan (Luciano,
    /// 22-oct).</item>
    /// </list>
    /// Con <paramref name="tambienAjeno"/> (la casilla "Borrar TODO el armado
    /// existente…", pedido del 9-sep) se borra además el acero que no generó
    /// el add-in, en toda la columna. Se conserva aunque #61 pedía sacarla:
    /// Luciano, 22-oct.
    /// </summary>
    private static int BorrarAnteriores(Document doc, PlanArmado plan, bool tambienAjeno)
    {
        var hostsColumna = new HashSet<ElementId>();
        var hostsMarcados = new HashSet<ElementId>();
        foreach (ColumnaPlan columna in plan.Columnas)
        {
            if (columna.Contexto is null || columna.Tramos.All(t => t is null)) continue;
            for (int i = 0; i < columna.Contexto.Tramos.Count; i++)
            {
                if (!long.TryParse(columna.Contexto.Tramos[i].IdElementoHost, out long v)) continue;
                var host = new ElementId(v);
                hostsColumna.Add(host);
                if (i < columna.Tramos.Count && columna.Tramos[i] is not null) hostsMarcados.Add(host);
            }
        }

        if (hostsColumna.Count == 0) return 0;

        HashSet<ElementId> propios = new FilteredElementCollector(doc)
            .OfClass(typeof(Rebar))
            .WherePasses(MarcaEstribos.Filtro())
            .ToElementIds()
            .ToHashSet();

        List<ElementId> viejos = new FilteredElementCollector(doc)
            .OfClass(typeof(Rebar))
            .Cast<Rebar>()
            .Where(r => hostsColumna.Contains(r.GetHostId()))
            .Where(r => propios.Contains(r.Id)
                ? !EsTransversal(r) || hostsMarcados.Contains(r.GetHostId())
                : tambienAjeno)
            .Select(r => r.Id)
            .ToList();

        if (viejos.Count > 0)
            doc.Delete(viejos);

        return viejos.Count;
    }

    /// <summary>
    /// Estribo, caja o grapa: su plano es horizontal (normal vertical), porque
    /// se crean con normal Z o con BasisX/BasisY. Las longitudinales se crean
    /// con una normal horizontal (<see cref="BarraContinua"/>). Una barra que
    /// no es de forma (a mano, libre) cuenta como longitudinal: se borra en
    /// toda la columna.
    /// </summary>
    private static bool EsTransversal(Rebar r) =>
        r.IsRebarShapeDriven() && Math.Abs(r.GetShapeDrivenAccessor().Normal.Z) > 0.9;

    /// <summary>
    /// Solo cuando se cayó al gancho del modelo (el propio no se pudo crear):
    /// contrasta su extensión recta contra la E del cuadro, una sola vez por
    /// combinación (barra, gancho). Se compara la extensión y no el largo del
    /// gancho porque Revit mide ese largo de otra manera (#3 a 135°: 8,47 cm
    /// contra 11,3 cm del cuadro) -- #64.
    /// </summary>
    private static void AvisarSiGanchoNoCumpleNorma(
        RebarBarType barra, RebarHookType gancho,
        HashSet<(ElementId, ElementId)> yaAvisados, ResultadoArmado res)
    {
        if (!yaAvisados.Add((barra.Id, gancho.Id))) return;

        CuadroVarillas.Varilla? varilla = EstandarCQ.Buscar(barra);
        if (varilla is null)
        {
            res.Avisos.Add(
                $"La barra «{barra.Name}» no está en el cuadro de la empresa (diámetro fuera de los normalizados). " +
                "No se pudo verificar el gancho.");
            return;
        }

        if (varilla.GanchoEstribo135 is not { } cuadro) return;

        double real = gancho.GetHookExtensionLength(barra);
        if (Math.Abs(real - EstandarCQ.APies(cuadro.Extension)) > EstandarCQ.APies(0.005))
            res.Avisos.Add(
                $"El gancho del modelo «{gancho.Name}» deja una cola de {GeometriaSeccion.Metros(real) * 100:0.#} cm con la barra {barra.Name}, " +
                $"y el cuadro de la empresa pide {cuadro.Extension * 100:0.#} cm para la #{varilla.Numero}.");
    }

}
