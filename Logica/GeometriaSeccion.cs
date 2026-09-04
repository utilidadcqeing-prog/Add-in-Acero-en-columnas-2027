namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

/// <summary>
/// Geometría de sección compartida entre los generadores de armado real:
/// estribos (<see cref="GeneradorArmado"/>) y longitudinal + estribos
/// menores (<see cref="GeneradorLongitudinal"/>). Los dos necesitan lo
/// mismo -- leer el sólido de un elemento host, sacarle el contorno
/// exterior de su cara inferior, ofsetearlo hacia adentro -- así que vive
/// acá aparte en vez de duplicarse en los dos archivos.
/// </summary>
internal static class GeometriaSeccion
{
    private const double Tolerancia = 1e-6;

    /// <summary>
    /// El contorno exterior del elemento host (sin offset) y la cota Z de su
    /// cara inferior -- se cachea porque varios tramos de la pila pueden
    /// compartir el mismo host (una viga que cruza a mitad de un elemento
    /// parte ese elemento en dos tramos, ver <c>LectorContexto.ArmarTramos</c>).
    /// </summary>
    public static (CurveLoop ContornoCrudo, double ZBase) ObtenerContornoHost(
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
        CurveLoop contorno = LoopExterior(caraInferior);
        ValidarRectangular(contorno, host.Id);

        var resultado = (contorno, caraInferior.Origin.Z);
        cache[host.Id] = resultado;
        return resultado;
    }

    /// <summary>
    /// Rectángulo en planta que envuelve al contorno: (xMin, xMax, yMin, yMax).
    /// Asume sección rectangular, igual que el resto del carril (ver #11).
    /// </summary>
    public static (double XMin, double XMax, double YMin, double YMax) Huella(CurveLoop loop)
    {
        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;

        foreach (Curve c in loop)
        {
            foreach (XYZ p in new[] { c.GetEndPoint(0), c.GetEndPoint(1) })
            {
                xMin = Math.Min(xMin, p.X);
                xMax = Math.Max(xMax, p.X);
                yMin = Math.Min(yMin, p.Y);
                yMax = Math.Max(yMax, p.Y);
            }
        }

        return (xMin, xMax, yMin, yMax);
    }

    public static Solid? ObtenerSolido(Element el)
    {
        var opts = new Options { DetailLevel = ViewDetailLevel.Fine };
        return SolidoMayor(el.get_Geometry(opts));
    }

    public static Solid? SolidoMayor(GeometryElement? ge)
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
    public static CurveLoop LoopExterior(PlanarFace cara) =>
        cara.GetEdgesAsCurveLoops().OrderByDescending(Longitud).First();

    /// <summary>
    /// Todo el carril (offset del contorno, esquinas de #4, cara X/cara Y de
    /// J4) asume una sección rectangular -- ver #11. Nada de esto tiene
    /// sentido geométrico para una columna circular o de más de 4 lados
    /// (Revit devolvería <c>Arc</c> en vez de <c>Line</c>, o más de 4 curvas),
    /// así que se corta acá con un mensaje claro en vez de generar acero mal
    /// puesto en silencio.
    /// </summary>
    private static void ValidarRectangular(CurveLoop contorno, ElementId hostId)
    {
        List<Curve> curvas = contorno.ToList();

        if (curvas.Any(c => c is not Line))
            throw new InvalidOperationException(
                $"el elemento {hostId} no tiene una sección rectangular (borde curvo) -- no soportado por ahora (#11).");

        if (curvas.Count != 4)
            throw new InvalidOperationException(
                $"el elemento {hostId} tiene una sección de {curvas.Count} lados, no rectangular -- no soportado por ahora (#11).");
    }

    /// <summary>
    /// Mete el contorno hacia adentro. Prueba los dos signos y se queda con el
    /// que encoge, así no importa cómo venga orientado el loop.
    /// </summary>
    public static CurveLoop OffsetHaciaAdentro(CurveLoop loop, double distancia)
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
    public static CurveLoop Normalizar(CurveLoop loop)
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

    public static double Longitud(CurveLoop loop) => loop.Sum(c => c.Length);

    /// <summary>
    /// Parte la primera curva de un loop cerrado por su punto medio, así el
    /// arranque/cierre del <c>IList&lt;Curve&gt;</c> que recibe
    /// <c>Rebar.CreateFromCurves</c> cae en la mitad de un lado recto, no en
    /// una esquina.
    ///
    /// Revit no arma el fillet de 90° en el vértice donde el loop se cierra
    /// cuando ahí también van los ganchos de 135° -- el gancho se come el
    /// doblez que debería atrapar la barra longitudinal de esa esquina,
    /// dejándola sin amarrar (documentado en el informe técnico de
    /// referencia de otro intento del mismo problema -- ver issue de
    /// seguimiento). Partir un lado por la mitad deja los 4 vértices reales
    /// con continuidad entre dos curvas normales, así que sí reciben el
    /// fillet; los ganchos quedan sobre el tramo recto en su lugar, que es
    /// justo la solución que Autodesk documenta para esto sin cambiar de API.
    /// </summary>
    public static IList<Curve> ConEmpalmeEnMedioDeLado(IList<Curve> curvas)
    {
        if (curvas.Count == 0) return curvas;

        Curve primera = curvas[0];
        XYZ inicio = primera.GetEndPoint(0);
        XYZ fin = primera.GetEndPoint(1);
        XYZ medio = (inicio + fin) / 2;

        var resultado = new List<Curve> { Line.CreateBound(inicio, medio), Line.CreateBound(medio, fin) };
        resultado.AddRange(curvas.Skip(1));
        return resultado;
    }

    public static double Metros(double pies) =>
        UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Meters);

    /// <summary>
    /// Revit contesta "An internal error has occurred" cuando la armadura le
    /// sale geométricamente inválida, sin decir por qué. Casi siempre es que
    /// el gancho no cabe: o la pata es muy larga para la sección, o la barra
    /// muy gruesa.
    /// </summary>
    public static string Explicar(Exception ex)
    {
        if (ex.Message.Contains("internal error", StringComparison.OrdinalIgnoreCase))
            return "Revit rechazó la armadura sin dar motivo. Suele ser el gancho: " +
                   "prueba otro tipo de 135°, o una barra más delgada.";

        // Diagnóstico temporal: un NullReferenceException (u otra excepción
        // genérica de .NET) siempre da el mismo mensaje sin importar dónde
        // ocurrió -- sin esto es imposible saber qué línea falló. Se agregan
        // los primeros frames del stack trace (el .pdb ya viaja junto al
        // .dll en deploy.ps1, así que trae archivo+línea). Sacar una vez que
        // se identifique y corrija la causa real.
        string[] frames = (ex.StackTrace ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string resumenStack = frames.Length > 0 ? " || " + string.Join(" <- ", frames.Take(3)) : "";
        return ex.Message + resumenStack;
    }
}
