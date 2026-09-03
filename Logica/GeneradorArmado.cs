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

        var cacheHost = new Dictionary<ElementId, (CurveLoop ContornoCrudo, double ZBase)>();
        var ganchosYaAvisados = new HashSet<(ElementId Barra, ElementId Gancho)>();

        foreach (ColumnaPlan columnaPlan in plan.Columnas)
        {
            GenerarColumna(doc, columnaPlan, barrasPorNombre, ganchosPorNombre, cacheHost, ganchosYaAvisados, res);
        }

        return res;
    }

    private static void GenerarColumna(
        Document doc,
        ColumnaPlan columnaPlan,
        Dictionary<string, RebarBarType> barrasPorNombre,
        Dictionary<string, RebarHookType> ganchosPorNombre,
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
                "Asignale un recubrimiento en Revit y volvé a generar -- acá no se impone ninguno.");
            res.TramosOmitidos += columnaPlan.Tramos.Count(t => t is not null);
            return;
        }

        int n = Math.Min(contexto.Tramos.Count, columnaPlan.Tramos.Count);
        for (int i = 0; i < n; i++)
        {
            // null: este tramo no estaba tildado en la ventana -- no es un
            // problema que reportar, es simplemente "no pedido en esta corrida".
            if (columnaPlan.Tramos[i] is not ConfigTramo cfg) continue;

            Tramo tramo = contexto.Tramos[i];

            try
            {
                if (!GenerarTramo(
                        doc, contexto.Marca, tramo, cfg, barrasPorNombre, ganchosPorNombre,
                        recubrimientoPies, cacheHost, ganchosYaAvisados, res))
                {
                    res.TramosOmitidos++;
                }
            }
            catch (Exception ex)
            {
                res.TramosOmitidos++;
                res.Avisos.Add($"{contexto.Marca} tramo {i + 1}: {Explicar(ex)}");
            }
        }
    }

    /// <summary>
    /// Un tramo, un <see cref="ConfigTramo"/>. Devuelve false (con aviso, sin
    /// tirar excepción) para los motivos "esperables" -- viga, host borrado,
    /// tipo de barra/gancho que ya no existe -- y deja que una excepción real
    /// de geometría se propague para que <see cref="GenerarColumna"/> la
    /// registre con <see cref="Explicar"/> sin tumbar el resto del lote.
    /// </summary>
    private static bool GenerarTramo(
        Document doc,
        string marcaColumna,
        Tramo tramo,
        ConfigTramo cfg,
        Dictionary<string, RebarBarType> barrasPorNombre,
        Dictionary<string, RebarHookType> ganchosPorNombre,
        double recubrimientoPies,
        Dictionary<ElementId, (CurveLoop ContornoCrudo, double ZBase)> cacheHost,
        HashSet<(ElementId Barra, ElementId Gancho)> ganchosYaAvisados,
        ResultadoArmado res)
    {
        // Dentro del canto de una viga no hay confinamiento (ver TipoTramo):
        // no es un error, es el tramo haciendo exactamente lo que dice ser.
        if (tramo.Tipo == TipoTramo.DentroDeViga) return true;

        string etiquetaTramo = $"{marcaColumna} tramo {tramo.Indice + 1}";

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

        (CurveLoop contornoCrudo, double zBase) = ObtenerContornoHost(host, cacheHost);

        // Recubrimiento a la cara EXTERIOR de la barra, no a su centro (#1):
        // la curva que le pasamos a Rebar.CreateFromCurves es el eje de la
        // barra, así que hay que meter el contorno medio diámetro más que el
        // recubrimiento puro para que el borde de la barra quede justo ahí.
        CurveLoop contorno = OffsetHaciaAdentro(contornoCrudo, recubrimientoPies + barra.BarNominalDiameter / 2);

        double ladoMenor = contorno.Min(c => c.Length);
        double doblez = barra.StirrupTieBendDiameter;
        if (ladoMenor <= doblez)
            throw new InvalidOperationException(
                $"la barra {barra.Name} es demasiado gruesa para esta sección: su doblez de estribo mide " +
                $"{Metros(doblez) * 100:0.#} cm y el lado más corto del estribo queda en {Metros(ladoMenor) * 100:0.#} cm.");

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

        if (cfg.Modo == ModoEstribo.Uniforme)
        {
            double espaciado = EstandarCQ.APies(cfg.EspaciadoUniforme);
            CrearGrupo(doc, host, contorno, zBase, zInf, altura, barra, terminaciones, espaciado, etiquetaTramo, "uniforme", res);
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
            CrearGrupo(doc, host, contorno, zBase, zInf, altura, barra, terminaciones, espaciadoConf, etiquetaTramo, "confinado (sin zona central)", res);
            return true;
        }

        double l2 = altura - 2 * l1;

        CrearGrupo(doc, host, contorno, zBase, zInf, l1, barra, terminaciones, espaciadoConf,
            etiquetaTramo, "confinado inferior", res, incluirUltimo: false);

        CrearGrupo(doc, host, contorno, zBase, zInf + l1, l2, barra, terminaciones, espaciadoNoConf,
            etiquetaTramo, "zona central", res, incluirUltimo: false);

        CrearGrupo(doc, host, contorno, zBase, zInf + l1 + l2, l1, barra, terminaciones, espaciadoConf,
            etiquetaTramo, "confinado superior", res);

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
        BarTerminationsData terminaciones,
        double espaciado,
        string etiquetaTramo,
        string etiquetaGrupo,
        ResultadoArmado res,
        bool incluirUltimo = true)
    {
        if (longitud <= Tolerancia || espaciado <= Tolerancia) return;

        var mover = Transform.CreateTranslation(XYZ.BasisZ * (zInicio - zBaseContorno));
        IList<Curve> curvas = contorno.Select(c => c.CreateTransformed(mover)).ToList();

        Rebar estribo = Rebar.CreateFromCurves(
            doc,
            RebarStyle.StirrupTie,
            barra,
            host,
            XYZ.BasisZ,
            curvas,
            terminaciones,
            useExistingShapeIfPossible: true,
            createNewShape: true);

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
            $"{Metros(separacionReal) * 100:0.#} cm sobre {Metros(reparto):0.00} m.");
    }

    /// <summary>
    /// Quita los estribos que ESTE add-in creó antes en los elementos que el
    /// plan de hoy va a tocar. Solo alcanza a los host que aparecen como
    /// <see cref="Tramo.IdElementoHost"/> en el plan -- no toca columnas fuera
    /// de esta corrida ni armadura puesta a mano (issue #2).
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
            .WherePasses(MarcaEstribos.Filtro())
            .Where(r => MarcaEstribos.LeerColumna(r) is ElementId id && idsHost.Contains(id))
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

        EstandarCQ.Varilla? varilla = EstandarCQ.Buscar(barra);
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
                $"El gancho «{gancho.Name}» mide {Metros(real) * 100:0.#} cm con la barra {barra.Name}, " +
                $"y el cuadro de la empresa pide {varilla.GanchoEstribo * 100:0.#} cm para la #{varilla.Numero}.");
    }

    /// <summary>
    /// El contorno exterior del elemento host, sin offset -- se cachea porque
    /// varios tramos de la pila pueden compartir el mismo host (una viga que
    /// cruza a mitad de un elemento parte ese elemento en dos tramos, ver
    /// <c>LectorContexto.ArmarTramos</c>).
    /// </summary>
    private static (CurveLoop ContornoCrudo, double ZBase) ObtenerContornoHost(
        FamilyInstance host, Dictionary<ElementId, (CurveLoop, double)> cache)
    {
        if (cache.TryGetValue(host.Id, out var cacheado)) return cacheado;

        Solid solido = ObtenerSolido(host)
            ?? throw new InvalidOperationException($"no se pudo leer la geometría del elemento {host.Id}.");

        var carasHorizontales = solido.Faces
            .OfType<PlanarFace>()
            .Where(f => Math.Abs(f.FaceNormal.Normalize().DotProduct(XYZ.BasisZ)) > 1 - 1e-4)
            .ToList();

        if (carasHorizontales.Count < 2)
            throw new InvalidOperationException(
                $"el elemento {host.Id} no tiene tapa superior e inferior horizontales (¿inclinado o recortado?).");

        PlanarFace caraInferior = carasHorizontales.OrderBy(f => f.Origin.Z).First();
        var resultado = (LoopExterior(caraInferior), caraInferior.Origin.Z);
        cache[host.Id] = resultado;
        return resultado;
    }

    private static Solid? ObtenerSolido(Element el)
    {
        var opts = new Options { DetailLevel = ViewDetailLevel.Fine };
        return SolidoMayor(el.get_Geometry(opts));
    }

    private static Solid? SolidoMayor(GeometryElement? ge)
    {
        if (ge is null) return null;

        Solid? mejor = null;
        foreach (GeometryObject go in ge)
        {
            Solid? candidato = go switch
            {
                Solid s when s.Volume > Tolerancia => s,
                GeometryInstance gi => SolidoMayor(gi.GetInstanceGeometry()),
                _ => null
            };

            if (candidato is not null && (mejor is null || candidato.Volume > mejor.Volume))
                mejor = candidato;
        }
        return mejor;
    }

    /// <summary>El loop más largo de la cara es el contorno exterior; los demás son huecos.</summary>
    private static CurveLoop LoopExterior(PlanarFace cara) =>
        cara.GetEdgesAsCurveLoops().OrderByDescending(Longitud).First();

    /// <summary>
    /// Mete el contorno hacia adentro. Prueba los dos signos y se queda con el
    /// que encoge, así no importa cómo venga orientado el loop.
    /// </summary>
    private static CurveLoop OffsetHaciaAdentro(CurveLoop loop, double distancia)
    {
        CurveLoop a = CurveLoop.CreateViaOffset(loop, distancia, XYZ.BasisZ);
        CurveLoop b = CurveLoop.CreateViaOffset(loop, -distancia, XYZ.BasisZ);
        return Normalizar(Longitud(a) < Longitud(b) ? a : b);
    }

    /// <summary>
    /// Deja el contorno siempre en sentido antihorario visto desde +Z -- la
    /// orientación del gancho (Left/Right) se interpreta respecto al sentido
    /// de recorrido de la curva, y sin normalizar la misma configuración mete
    /// los ganchos hacia adentro en una columna y hacia afuera en la siguiente.
    /// </summary>
    private static CurveLoop Normalizar(CurveLoop loop)
    {
        try
        {
            if (!loop.IsCounterclockwise(XYZ.BasisZ))
                loop.Flip();
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Loop no planar o degenerado: se deja como está y que falle más
            // adelante con un mensaje que sí diga algo.
        }

        return loop;
    }

    private static double Longitud(CurveLoop loop) => loop.Sum(c => c.Length);

    /// <summary>
    /// Revit contesta "An internal error has occurred" cuando la armadura le
    /// sale geométricamente inválida, sin decir por qué. Casi siempre es que
    /// el gancho no cabe: o la pata es muy larga para la sección, o la barra
    /// muy gruesa.
    /// </summary>
    private static string Explicar(Exception ex)
    {
        if (ex.Message.Contains("internal error", StringComparison.OrdinalIgnoreCase))
            return "Revit rechazó la armadura sin dar motivo. Suele ser el gancho: " +
                   "prueba otro tipo de 135°, o una barra más delgada.";

        return ex.Message;
    }

    private static double Metros(double pies) =>
        UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Meters);
}
