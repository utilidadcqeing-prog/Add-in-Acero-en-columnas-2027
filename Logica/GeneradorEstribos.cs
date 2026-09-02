namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// Crea el acero de estribos de una columna con espaciado uniforme.
///
/// Es la traducción a la API del flujo "Estribos en columna basica.dyn":
/// leer el recubrimiento lateral, quedarse con la cara inferior de la columna,
/// meter su perímetro hacia adentro ese recubrimiento, subirlo la separación de
/// extremo y crear un único estribo con ganchos de 135° que se propaga hacia
/// arriba con espaciado máximo.
/// </summary>
public static class GeneradorEstribos
{
    private const double Tolerancia = 1e-6;

    public static ResultadoEstribos Generar(Document doc, OpcionesEstribos op)
    {
        var res = new ResultadoEstribos();

        if (op.TipoBarra is null)
        {
            res.Avisos.Add("No se eligió tipo de barra.");
            return res;
        }

        if (op.TipoGancho is null)
        {
            res.Avisos.Add("No se eligió tipo de gancho.");
            return res;
        }

        RebarHookType gancho = op.TipoGancho;

        res.Eliminados = BorrarAnteriores(doc, op.Columnas);

        foreach (FamilyInstance columna in op.Columnas)
        {
            try
            {
                GenerarEnColumna(doc, columna, op, op.TipoBarra, gancho, res);
            }
            catch (Exception ex)
            {
                // Una columna mal modelada no debe tumbar el resto del lote.
                res.Omitidas++;
                res.Avisos.Add($"Columna {columna.Id}: {Explicar(ex)}");
            }
        }

        return res;
    }

    private static void GenerarEnColumna(
        Document doc,
        FamilyInstance columna,
        OpcionesEstribos op,
        RebarBarType barra,
        RebarHookType gancho,
        ResultadoEstribos res)
    {
        if (RebarHostData.GetRebarHostData(columna) is null)
            throw new InvalidOperationException("no admite armadura (host no válido para rebar).");

        // 1. Recubrimiento lateral. Se corrige si no es el de norma y luego se LEE,
        //    porque el desfase del estribo sale de ese valor, no de un número impuesto.
        double recubrimiento = ResolverRecubrimiento(doc, columna, op, res);

        // 2. Sólido de la columna.
        Solid solido = ObtenerSolido(columna)
            ?? throw new InvalidOperationException("no se pudo leer su geometría.");

        // 3. Caras cuya normal es paralela a Z: la tapa inferior y la superior.
        var carasHorizontales = solido.Faces
            .OfType<PlanarFace>()
            .Where(f => Math.Abs(f.FaceNormal.Normalize().DotProduct(XYZ.BasisZ)) > 1 - 1e-4)
            .ToList();

        if (carasHorizontales.Count < 2)
            throw new InvalidOperationException(
                "no tiene tapa superior e inferior horizontales (¿columna inclinada o recortada?).");

        PlanarFace caraInferior = carasHorizontales.OrderBy(f => f.Origin.Z).First();
        PlanarFace caraSuperior = carasHorizontales.OrderByDescending(f => f.Origin.Z).First();

        // La altura sale de la geometría real y no del parámetro "Longitud del sistema",
        // que no existe o miente cuando la columna está mal modelada.
        double altura = caraSuperior.Origin.Z - caraInferior.Origin.Z;

        // 4. Perímetro exterior de la cara inferior, metido hacia adentro el recubrimiento.
        CurveLoop perimetro = LoopExterior(caraInferior);
        CurveLoop contorno = OffsetHaciaAdentro(perimetro, recubrimiento);

        // 4b. La barra tiene que caber doblada. Revit, si no cabe, solo dice
        //     "no se puede resolver forma de armadura", que no orienta a nadie.
        double ladoMenor = contorno.Min(c => c.Length);
        double doblez = barra.StirrupTieBendDiameter;

        if (ladoMenor <= doblez)
            throw new InvalidOperationException(
                $"la barra {barra.Name} es demasiado gruesa para esta sección: " +
                $"su doblez de estribo mide {Metros(doblez) * 100:0.#} cm y el lado más corto " +
                $"del estribo queda en {Metros(ladoMenor) * 100:0.#} cm. Usa una barra más delgada.");

        // 5. Subirlo la separación de extremo (los 5 cm de norma).
        var subir = Transform.CreateTranslation(XYZ.BasisZ * op.SeparacionExtremos);
        IList<Curve> curvas = contorno.Select(c => c.CreateTransformed(subir)).ToList();

        // 6. Un solo estribo cerrado, con gancho de 135° en ambos extremos.
        //    El vector normal es el de propagación: hacia arriba.
        using var terminaciones = new BarTerminationsData(doc)
        {
            HookTypeIdAtStart = gancho.Id,
            HookTypeIdAtEnd = gancho.Id,
            // Left, no Right: con Right los ganchos salen hacia afuera del estribo y
            // quedan fuera del concreto. Tienen que voltear hacia adentro.
            TerminationOrientationAtStart = RebarTerminationOrientation.Left,
            TerminationOrientationAtEnd = RebarTerminationOrientation.Left
        };

        Rebar estribo = Rebar.CreateFromCurves(
            doc,
            RebarStyle.StirrupTie,
            barra,
            columna,
            XYZ.BasisZ,
            curvas,
            terminaciones,
            useExistingShapeIfPossible: true,
            createNewShape: true);

        // 7. Propagación hacia arriba: longitud de columna menos la separación de
        //    ambos extremos. Queda como UN elemento con cantidad N, no como barras sueltas.
        double longitudReparto = altura - 2 * op.SeparacionExtremos;
        if (longitudReparto <= op.Espaciado)
            throw new InvalidOperationException(
                $"la altura libre ({Metros(longitudReparto):0.00} m) no alcanza para " +
                $"un espaciado de {Metros(op.Espaciado):0.00} m.");

        estribo.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(
            op.Espaciado,
            longitudReparto,
            barsOnNormalSide: true,
            includeFirstBar: true,
            includeLastBar: true);

        MarcaEstribos.Marcar(estribo, columna.Id);

        res.Creados++;
        Reportar(columna, estribo, barra, gancho, res);
    }

    /// <summary>
    /// Quita los estribos que este mismo add-in creó antes en esas columnas, para que
    /// regenerar reemplace en vez de apilar un juego sobre otro. Solo toca lo que
    /// lleva nuestra marca: la armadura que alguien puso a mano se respeta.
    /// </summary>
    private static int BorrarAnteriores(Document doc, List<FamilyInstance> columnas)
    {
        var objetivo = columnas.Select(c => c.Id).ToHashSet();

        List<ElementId> viejos = new FilteredElementCollector(doc)
            .OfClass(typeof(Rebar))
            .WherePasses(MarcaEstribos.Filtro())
            .Where(r => MarcaEstribos.LeerColumna(r) is ElementId id && objetivo.Contains(id))
            .Select(r => r.Id)
            .ToList();

        if (viejos.Count > 0)
            doc.Delete(viejos);

        return viejos.Count;
    }

    /// <summary>
    /// Deja constancia de lo que quedó creado, y avisa si la pata del gancho se sale
    /// de lo que la propia plantilla calcularía por defecto. Eso último no es un
    /// error del add-in: es el RebarHookType mal configurado, y hay que corregirlo
    /// en la plantilla, no aquí.
    /// </summary>
    private static void Reportar(
        FamilyInstance columna,
        Rebar estribo,
        RebarBarType barra,
        RebarHookType gancho,
        ResultadoEstribos res)
    {
        // Todo leído del elemento ya creado, no de lo que le pedimos: si Revit ajustó
        // algo, queremos ver lo que quedó, no lo que pretendíamos.
        RebarShapeDrivenAccessor acceso = estribo.GetShapeDrivenAccessor();

        int posiciones = estribo.NumberOfBarPositions;
        double reparto = acceso.ArrayLength;
        double separacion = posiciones > 1 ? reparto / (posiciones - 1) : 0;

        res.Detalles.Add(
            $"Columna {columna.Id}: {posiciones} estribos cada " +
            $"{Metros(separacion) * 100:0.#} cm sobre {Metros(reparto):0.00} m " +
            $"({estribo.LayoutRule}, tope {Metros(estribo.MaxSpacing) * 100:0.#} cm).");

        ValidarGancho(barra, gancho, res);
    }

    /// <summary>
    /// Contrasta la pata del gancho contra el cuadro de la empresa. No contra lo que
    /// Revit calcularía: para una #3 Revit propone 4,8 cm y el cuadro pide 13,1, y el
    /// que manda es el cuadro.
    /// </summary>
    private static void ValidarGancho(RebarBarType barra, RebarHookType gancho, ResultadoEstribos res)
    {
        EstandarCQ.Varilla? varilla = EstandarCQ.Buscar(barra);

        if (varilla is null)
        {
            res.Avisos.Add(
                $"La barra «{barra.Name}» no está en el cuadro de la empresa " +
                "(diámetro fuera de los normalizados). No se pudo verificar el gancho.");
            return;
        }

        double real = barra.GetHookLength(gancho.Id);
        double esperado = EstandarCQ.APies(varilla.GanchoEstribo);

        // Medio centímetro de margen: el cuadro viene redondeado a milímetros.
        if (System.Math.Abs(real - esperado) > EstandarCQ.APies(0.005))
            res.Avisos.Add(
                $"El gancho «{gancho.Name}» mide {Metros(real) * 100:0.#} cm con la barra " +
                $"{barra.Name}, y el cuadro de la empresa pide " +
                $"{varilla.GanchoEstribo * 100:0.#} cm para la #{varilla.Numero}.");
    }

    /// <summary>
    /// Deja la columna con el recubrimiento lateral de norma si hace falta y
    /// devuelve el valor que quedó, en pies.
    /// </summary>
    private static double ResolverRecubrimiento(
        Document doc, FamilyInstance columna, OpcionesEstribos op, ResultadoEstribos res)
    {
        Parameter p = columna.get_Parameter(BuiltInParameter.CLEAR_COVER_OTHER)
            ?? throw new InvalidOperationException(
                "no tiene el parámetro 'Recubrimiento de armadura - Otras caras'.");

        var actual = doc.GetElement(p.AsElementId()) as RebarCoverType;

        if (op.CorregirRecubrimiento &&
            (actual is null || Math.Abs(actual.CoverDistance - op.RecubrimientoObjetivo) > Tolerancia))
        {
            RebarCoverType destino = ObtenerTipoRecubrimiento(doc, op.RecubrimientoObjetivo);
            p.Set(destino.Id);
            res.RecubrimientosCorregidos++;
            return destino.CoverDistance;
        }

        if (actual is null)
            throw new InvalidOperationException("no tiene un tipo de recubrimiento asignado.");

        return actual.CoverDistance;
    }

    private static RebarCoverType ObtenerTipoRecubrimiento(Document doc, double distancia)
    {
        RebarCoverType? existente = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarCoverType))
            .Cast<RebarCoverType>()
            .FirstOrDefault(t => Math.Abs(t.CoverDistance - distancia) < Tolerancia);

        if (existente is not null) return existente;

        string nombre = $"CQ {Metros(distancia) * 100:0} cm";
        return RebarCoverType.Create(doc, nombre, distancia);
    }

    /// <summary>
    /// Revit contesta "An internal error has occurred" cuando la armadura le sale
    /// geométricamente inválida, sin decir por qué. Casi siempre es que el gancho
    /// no cabe: o la pata es muy larga para la sección, o la barra muy gruesa.
    /// </summary>
    private static string Explicar(Exception ex)
    {
        if (ex.Message.Contains("internal error", StringComparison.OrdinalIgnoreCase))
            return "Revit rechazó la armadura sin dar motivo. Suele ser el gancho: " +
                   "prueba otro tipo de 135°, o una barra más delgada.";

        return ex.Message;
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
    /// Mete el contorno hacia adentro. Prueba los dos signos y se queda con el que
    /// encoge, así no importa cómo venga orientado el loop.
    /// </summary>
    private static CurveLoop OffsetHaciaAdentro(CurveLoop loop, double distancia)
    {
        CurveLoop a = CurveLoop.CreateViaOffset(loop, distancia, XYZ.BasisZ);
        CurveLoop b = CurveLoop.CreateViaOffset(loop, -distancia, XYZ.BasisZ);
        return Normalizar(Longitud(a) < Longitud(b) ? a : b);
    }

    /// <summary>
    /// Deja el contorno siempre en sentido antihorario visto desde +Z.
    ///
    /// Importa porque la orientación del gancho (Left/Right) se interpreta respecto
    /// al sentido de recorrido de la curva. Sin normalizar, la misma configuración
    /// mete los ganchos hacia adentro en una columna y hacia afuera en la siguiente,
    /// según cómo Revit haya devuelto la cara.
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
            // Loop no planar o degenerado: lo dejamos como está y que falle más
            // adelante con un mensaje que sí diga algo.
        }

        return loop;
    }

    private static double Longitud(CurveLoop loop) => loop.Sum(c => c.Length);

    private static double Metros(double pies) =>
        UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Meters);
}
