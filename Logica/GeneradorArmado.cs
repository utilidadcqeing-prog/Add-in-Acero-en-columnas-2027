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
    /// No es un dato que la ventana pida hoy, así que va fijo acá, igual que
    /// el cuadro de <see cref="EstandarCQ"/>.
    /// </summary>
    private const double SeparacionExtremosMetros = 0.05;

    public static ResultadoArmado Generar(Document doc, OpcionesArmado opciones)
    {
        var res = new ResultadoArmado();
        PlanArmado plan = opciones.Plan;

        if (opciones.BorrarAnteriores)
            res.Eliminados = BorrarAnteriores(doc, plan);

        Dictionary<string, RebarBarType> barrasPorNombre = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
            .GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First());

        Dictionary<string, RebarHookType> ganchosPorNombre = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First());

        // Ver GeometriaSeccion.BuscarFormaEstribo: si el documento tiene una
        // forma de estribo cerrado cargada, se usa esa (evita de raíz la
        // dobladura de ConQuiebreEnMedioDeLado); si no, cada CrearGrupo cae
        // solo al camino de siempre (CreateFromCurves). Se resuelve una sola
        // vez acá, no en cada llamada.
        RebarShape? formaEstribo = GeometriaSeccion.BuscarFormaEstribo(doc);

        var cacheHost = new Dictionary<ElementId, (CurveLoop ContornoCrudo, double ZBase)>();
        var ganchosYaAvisados = new HashSet<(ElementId Barra, ElementId Gancho)>();

        foreach (ColumnaPlan columnaPlan in plan.Columnas)
        {
            GenerarColumna(doc, columnaPlan, barrasPorNombre, ganchosPorNombre, formaEstribo, cacheHost, ganchosYaAvisados, res);
        }

        return res;
    }

    private static void GenerarColumna(
        Document doc,
        ColumnaPlan columnaPlan,
        Dictionary<string, RebarBarType> barrasPorNombre,
        Dictionary<string, RebarHookType> ganchosPorNombre,
        RebarShape? formaEstribo,
        Dictionary<ElementId, (CurveLoop ContornoCrudo, double ZBase)> cacheHost,
        HashSet<(ElementId Barra, ElementId Gancho)> ganchosYaAvisados,
        ResultadoArmado res)
    {
        ColumnaContexto? contexto = columnaPlan.Contexto;
        if (contexto is null)
        {
            res.Avisos.Add($"Columna {columnaPlan.Id}: sin contexto en el plan; se omite.");
            return;
        }

        double recubrimientoPies = EstandarCQ.APies(contexto.Recubrimiento);
        if (recubrimientoPies <= Tolerancia)
        {
            res.Avisos.Add(
                $"{contexto.Marca}: no tiene recubrimiento lateral asignado en el modelo. " +
                "Asígnale un recubrimiento en Revit y vuelve a generar -- acá no se impone ninguno.");
            res.TramosOmitidos += columnaPlan.Tramos.Count(t => t is not null);
            return;
        }

        int n = Math.Min(contexto.Tramos.Count, columnaPlan.Tramos.Count);
        for (int i = 0; i < n; i++)
        {
            // null: este tramo no estaba aceptado en la ventana -- no es un
            // problema que reportar, es simplemente "no pedido en esta corrida".
            if (columnaPlan.Tramos[i] is not ConfigTramo cfg) continue;

            Tramo tramo = contexto.Tramos[i];

            try
            {
                if (!GenerarTramo(
                        doc, contexto.Marca, tramo, cfg, barrasPorNombre, ganchosPorNombre, formaEstribo,
                        recubrimientoPies, cacheHost, ganchosYaAvisados, res))
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
        double recubrimientoPies,
        Dictionary<ElementId, (CurveLoop ContornoCrudo, double ZBase)> cacheHost,
        HashSet<(ElementId Barra, ElementId Gancho)> ganchosYaAvisados,
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

        if (string.IsNullOrEmpty(cfg.TipoGanchoNombre) || !ganchosPorNombre.TryGetValue(cfg.TipoGanchoNombre, out RebarHookType? gancho))
        {
            res.Avisos.Add($"{etiquetaTramo}: el tipo de gancho «{cfg.TipoGanchoNombre}» no existe en el documento; se omite.");
            return false;
        }

        if (RebarHostData.GetRebarHostData(host) is null)
            throw new InvalidOperationException($"el elemento {host.Id} no admite armadura (host no válido para rebar).");

        (CurveLoop contornoCrudo, double zBase) = GeometriaSeccion.ObtenerContornoHost(host, cacheHost);

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

        AvisarSiGanchoNoCumpleNorma(barra, gancho, ganchosYaAvisados, res);

        double separacionExtremos = EstandarCQ.APies(SeparacionExtremosMetros);
        double zInf = EstandarCQ.APies(tramo.ZInferior) + separacionExtremos;
        double zSup = EstandarCQ.APies(tramo.ZSuperior) - separacionExtremos;
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

        var claveTramo = (tramo.IdElementoHost!, tramo.Indice);

        if (cfg.Modo == ModoEstribo.Uniforme)
        {
            double espaciado = EstandarCQ.APies(cfg.EspaciadoUniforme);
            CrearGrupo(doc, host, contorno, zBase, zInf, altura, barra, formaEstribo, terminaciones, espaciado, etiquetaTramo, "uniforme", res, claveTramo);
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
            CrearGrupo(doc, host, contorno, zBase, zInf, altura, barra, formaEstribo, terminaciones, espaciadoConf, etiquetaTramo, "confinado (sin zona central)", res, claveTramo);
            return true;
        }

        double l2 = altura - 2 * l1;

        CrearGrupo(doc, host, contorno, zBase, zInf, l1, barra, formaEstribo, terminaciones, espaciadoConf,
            etiquetaTramo, "confinado inferior", res, claveTramo, incluirUltimo: false);

        CrearGrupo(doc, host, contorno, zBase, zInf + l1, l2, barra, formaEstribo, terminaciones, espaciadoNoConf,
            etiquetaTramo, "zona central", res, claveTramo, incluirUltimo: false);

        CrearGrupo(doc, host, contorno, zBase, zInf + l1 + l2, l1, barra, formaEstribo, terminaciones, espaciadoConf,
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
            ? CrearDesdeForma(doc, host, curvasTrasladadas, barra, formaEstribo, terminaciones, res, etiquetaTramo)
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

        RebarShapeDrivenAccessor acceso = estribo.GetShapeDrivenAccessor();
        int posiciones = estribo.NumberOfBarPositions;
        double reparto = acceso.ArrayLength;
        double separacionReal = posiciones > 1 ? reparto / (posiciones - 1) : 0;

        res.GruposCreados++;
        res.Detalles.Add(
            $"{etiquetaTramo} ({etiquetaGrupo}): {posiciones} estribos cada " +
            $"{GeometriaSeccion.Metros(separacionReal) * 100:0.#} cm sobre {GeometriaSeccion.Metros(reparto):0.00} m.");

        RegistrarPosiciones(posiciones, zInicio, separacionReal, claveTramo, res);
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
    private static Rebar? CrearDesdeForma(
        Document doc, FamilyInstance host, IList<Curve> curvasTrasladadas,
        RebarBarType barra, RebarShape forma, BarTerminationsData terminaciones,
        ResultadoArmado res, string etiquetaTramo)
    {
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

            ElementId idGancho = terminaciones.HookTypeIdAtStart;
            if (idGancho != ElementId.InvalidElementId)
            {
                try { estribo.SetHookTypeId(0, idGancho); estribo.SetHookTypeId(1, idGancho); }
                catch { /* la forma puede no tener posiciones de gancho -- no bloquea */ }

                try { estribo.SetTerminationOrientation(0, RebarTerminationOrientation.Left); estribo.SetTerminationOrientation(1, RebarTerminationOrientation.Left); }
                catch { /* idem */ }
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
        int posiciones, double zInicio, double separacionReal,
        (string IdElementoHost, int IndiceTramo) claveTramo, ResultadoArmado res)
    {
        if (!res.PosicionesEstribosPorTramo.TryGetValue(claveTramo, out List<double>? zetas))
        {
            zetas = new List<double>();
            res.PosicionesEstribosPorTramo[claveTramo] = zetas;
        }

        for (int i = 0; i < posiciones; i++)
            zetas.Add(zInicio + i * separacionReal);
    }

    /// <summary>
    /// Quita TODO el acero de refuerzo (<see cref="Rebar"/>) ya hosteado en
    /// los elementos que el plan de hoy va a tocar -- sin importar si lo
    /// generó este add-in o si lo modeló alguien a mano (pedido explícito
    /// del usuario, 9-sep: antes solo se borraba lo marcado por el add-in
    /// vía <see cref="MarcaEstribos"/>; con la casilla tildada ahora se borra
    /// cualquier armado existente en esas columnas, para no dejar una barra
    /// vieja convivendo con la nueva). Solo alcanza a los host que aparecen
    /// como <see cref="Tramo.IdElementoHost"/> en el plan -- no toca columnas
    /// fuera de esta corrida. Ver el checkbox en la ventana
    /// ("Borrar TODO el armado existente en estas columnas") -- es una
    /// operación destructiva a propósito, el usuario la tilda explícitamente
    /// cada vez que la quiere.
    /// </summary>
    private static int BorrarAnteriores(Document doc, PlanArmado plan)
    {
        var idsHost = plan.Columnas
            .SelectMany(c => c.Contexto?.Tramos ?? new List<Tramo>())
            .Select(t => t.IdElementoHost)
            .Where(id => id is not null)
            .Select(id => long.TryParse(id, out long v) ? new ElementId(v) : null)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToHashSet();

        if (idsHost.Count == 0) return 0;

        List<ElementId> viejos = new FilteredElementCollector(doc)
            .OfClass(typeof(Rebar))
            .Cast<Rebar>()
            .Where(r => idsHost.Contains(r.GetHostId()))
            .Select(r => r.Id)
            .ToList();

        if (viejos.Count > 0)
            doc.Delete(viejos);

        return viejos.Count;
    }

    /// <summary>
    /// Contrasta la pata del gancho contra el cuadro de la empresa, una sola
    /// vez por combinación (barra, gancho) aunque se repita en varios tramos
    /// o columnas -- si no, la misma advertencia sale veinte veces en un lote.
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

        double real = barra.GetHookLength(gancho.Id);
        double esperado = EstandarCQ.APies(varilla.GanchoEstribo);

        if (Math.Abs(real - esperado) > EstandarCQ.APies(0.005))
            res.Avisos.Add(
                $"El gancho «{gancho.Name}» mide {GeometriaSeccion.Metros(real) * 100:0.#} cm con la barra {barra.Name}, " +
                $"y el cuadro de la empresa pide {varilla.GanchoEstribo * 100:0.#} cm para la #{varilla.Numero}.");
    }

}
