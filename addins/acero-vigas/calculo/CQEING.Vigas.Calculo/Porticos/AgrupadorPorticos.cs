using System.Globalization;
using System.Text;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Porticos;

/// <summary>
/// Agrupa las vigas leídas del modelo en pórticos listos para el motor (#81): vigas del mismo nivel sobre una misma
/// línea, seguidas o con el hueco tapado por un apoyo. Reglas: decisión 4 del plan
/// docs/planes/2026-10-16-leer-portico-revit-81.md, ajustadas con el modelo real de prueba.
/// </summary>
public static class AgrupadorPorticos
{
    /// <summary>Distancia para considerar dos líneas (o una línea y un eje) la misma, en mm.</summary>
    private const double ToleranciaLinea = 50;

    /// <summary>Hueco máximo entre dos vigas seguidas, en mm.</summary>
    private const double ToleranciaContacto = 10;

    /// <summary>Seno del ángulo máximo entre dos direcciones paralelas.</summary>
    private const double ToleranciaAngulo = 0.01;

    /// <summary>Solape mínimo en alto entre una viga y su apoyo, en mm: tocarse en una cara no es apoyar.</summary>
    private const double SolapeMinimoEnAlto = 1;

    public static ResultadoLectura Agrupar(ModeloLeido modelo)
    {
        var porticos = new List<PorticoLeido>();
        var avisos = new List<AvisoLectura>();
        int sinEje = 0;

        foreach (Linea linea in Lineas(modelo.Vigas))
        {
            List<Soporte> soportes = Soportes(linea, modelo);
            List<List<Tramo>> cadenas = Cadenas(linea, soportes);
            EjeLeido? ejePropio = modelo.Ejes.FirstOrDefault(e => linea.EsColineal(e.Desde, e.Hasta));

            for (int i = 0; i < cadenas.Count; i++)
            {
                List<Tramo> cadena = cadenas[i];
                double desde = cadena.Min(t => t.Desde), hasta = cadena.Max(t => t.Hasta);
                List<Soporte> propios = SinArmazonDeVoladizos(Unir(soportes.Where(s => s.Hasta >= desde - ToleranciaContacto && s.Desde <= hasta + ToleranciaContacto)
                    .Where(s => s.Tipo != TipoApoyo.Viga || TerminaSobre(cadena, s, soportes))), modelo);
                if (propios.Count == 0)
                {
                    avisos.Add(new AvisoLectura(TipoAvisoLectura.SinApoyos, string.Join(", ", cadena.Select(t => t.Viga.Id))));
                    continue;
                }

                EjeLeido? eje = ejePropio ?? EjeQuePasaPorLosApoyos(linea, modelo.Ejes, propios);
                string nombre = eje is null
                    ? $"Pórtico sin eje {++sinEje}"
                    : $"Pórtico {eje.Nombre}{(cadenas.Count > 1 ? (char)('a' + i) : "")}";

                porticos.Add(Armar(linea, modelo.Ejes, cadena, propios, nombre, eje?.Nombre, desde, hasta, avisos));
            }
        }
        return new ResultadoLectura(porticos, avisos);
    }

    /// <summary>
    /// Viga corrida del eje (al borde de la columna, excéntrica): la rejilla paralela más cercana que pasa por dentro
    /// de todas las columnas y muros del pórtico, o null (decisión 2 del plan docs/planes/2026-10-19-ajustes-tablero-vigas-100.md).
    /// </summary>
    private static EjeLeido? EjeQuePasaPorLosApoyos(Linea linea, IReadOnlyList<EjeLeido> ejes, List<Soporte> soportes)
    {
        List<Soporte> firmes = soportes.Where(s => s.Tipo != TipoApoyo.Viga).ToList();
        if (firmes.Count == 0)
            return null;
        return ejes.Where(e => linea.EsParalela(e.Hasta.X - e.Desde.X, e.Hasta.Y - e.Desde.Y))
            .Select(e => (Eje: e, Distancia: linea.Normal(e.Desde) - linea.Desplazamiento))
            .Where(c => firmes.All(s => c.Distancia >= s.NormalDesde && c.Distancia <= s.NormalHasta))
            .OrderBy(c => Math.Abs(c.Distancia))
            .Select(c => c.Eje)
            .FirstOrDefault();
    }

    private static PorticoLeido Armar(Linea linea, IReadOnlyList<EjeLeido> ejes, List<Tramo> cadena, List<Soporte> soportes,
        string nombre, string? eje, double desde, double hasta, List<AvisoLectura> avisos)
    {
        var nombres = new List<string>();
        var posiciones = new List<double>();
        foreach (Soporte s in soportes)
        {
            // El eje de un apoyo es el de la grilla que lo cruza; si no hay, su centro y su id.
            var cruce = ejes.Select(e => (e.Nombre, T: linea.Cruce(e.Desde, e.Hasta)))
                .Where(c => c.T is not null && c.T >= s.Desde - ToleranciaLinea && c.T <= s.Hasta + ToleranciaLinea)
                .OrderBy(c => Math.Abs(c.T!.Value - (s.Desde + s.Hasta) / 2))
                .FirstOrDefault();
            nombres.Add(cruce.Nombre ?? $"#{s.Id}");
            posiciones.Add(cruce.T ?? (s.Desde + s.Hasta) / 2);
        }

        double origen = posiciones[0];
        int Mm(double t) => (int)Math.Round(t - origen, MidpointRounding.AwayFromZero);

        var apoyos = soportes.Select((s, i) => new Apoyo(nombres[i], Mm(posiciones[i]), Mm(s.Desde), Mm(s.Hasta))).ToList();
        int voladizoInicio = soportes[0].Desde - desde > ToleranciaContacto ? Mm(soportes[0].Desde) - Mm(desde) : 0;
        int voladizoFinal = hasta - soportes[^1].Hasta > ToleranciaContacto ? Mm(hasta) - Mm(soportes[^1].Hasta) : 0;

        var tramos = new List<TramoLeido>();
        if (voladizoInicio > 0)
            tramos.Add(Tramo($"Voladizo {nombres[0]}", desde, soportes[0].Desde, true));
        for (int i = 0; i + 1 < soportes.Count; i++)
            tramos.Add(Tramo($"Viga {nombres[i]}-{nombres[i + 1]}", soportes[i].Hasta, soportes[i + 1].Desde, false));
        if (voladizoFinal > 0)
            tramos.Add(Tramo($"Voladizo {nombres[^1]}", soportes[^1].Hasta, hasta, true));

        // El mismo eje se repite en cada nivel ("Pórtico A" de Piso 1, Piso 2 y Cubierta): los avisos dicen el nivel.
        NivelLeido? nivel = cadena[0].Viga.Nivel;
        string elemento = nivel is null ? nombre : $"{nombre} · {nivel.Nombre}";
        if (eje is null)
            avisos.Add(new AvisoLectura(TipoAvisoLectura.SinEje, elemento));
        if (tramos.Select(t => (t.Ancho, t.Alto)).Distinct().Count() > 1)
            avisos.Add(new AvisoLectura(TipoAvisoLectura.CambioDeSeccion, elemento));
        if (nivel is null)
            avisos.Add(new AvisoLectura(TipoAvisoLectura.SinNivel, elemento));

        return new PorticoLeido(nombre, eje, nivel?.Nombre, TipoDe(nivel),
            new Portico(apoyos, voladizoInicio, voladizoFinal),
            soportes.Select(s => s.Tipo).ToList(), tramos, cadena.Select(t => t.Viga.Id).ToList(),
            soportes.Select(s => Math.Round(s.NormalHasta - s.NormalDesde)).ToList());

        TramoLeido Tramo(string nombreTramo, double a, double b, bool voladizo)
        {
            // La sección es la de la viga que pasa por el medio del tramo.
            double medio = (a + b) / 2;
            VigaLeida viga = cadena.OrderBy(t => medio < t.Desde ? t.Desde - medio : medio > t.Hasta ? medio - t.Hasta : 0).First().Viga;
            // Al milímetro: la medida en la geometría de Revit trae ruido de decimales (Hallazgos del plan).
            return new TramoLeido(nombreTramo, Mm(a), Mm(b), voladizo, Math.Round(viga.Ancho), Math.Round(viga.Alto));
        }
    }

    /// <summary>Cimentación si la cota del nivel es ≤ 0 o su nombre dice cimentación o fundación (decisión 4).</summary>
    private static TipoViga TipoDe(NivelLeido? nivel)
    {
        if (nivel is null)
            return TipoViga.Aerea;
        string nombre = SinTildes(nivel.Nombre).ToLowerInvariant();
        return nivel.Elevacion <= SolapeMinimoEnAlto || nombre.Contains("cimenta") || nombre.Contains("fundac")
            ? TipoViga.Cimentacion : TipoViga.Aerea;
    }

    private static string SinTildes(string texto) =>
        new(texto.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());

    /// <summary>Una viga sobre la línea, medida a lo largo de ella (Desde &lt; Hasta).</summary>
    private sealed record Tramo(VigaLeida Viga, double Desde, double Hasta);

    /// <summary>
    /// Un apoyo sobre la línea: el rango de su huella medido a lo largo de ella (Desde–Hasta) y de lado a lado
    /// (NormalDesde–NormalHasta, con signo desde la línea).
    /// </summary>
    private sealed record Soporte(string Id, TipoApoyo Tipo, double Desde, double Hasta, double NormalDesde, double NormalHasta);

    /// <summary>Vigas del mismo nivel sobre una misma línea, con la línea en dirección +X o +Y.</summary>
    private sealed class Linea
    {
        public Linea(VigaLeida primera)
        {
            double dx = primera.Hasta.X - primera.Desde.X, dy = primera.Hasta.Y - primera.Desde.Y;
            double largo = Math.Sqrt(dx * dx + dy * dy);
            (Ux, Uy) = (dx / largo, dy / largo);
            if (Ux < -ToleranciaAngulo || (Math.Abs(Ux) <= ToleranciaAngulo && Uy < 0))
                (Ux, Uy) = (-Ux, -Uy);
            Desplazamiento = Normal(primera.Desde);
            ZArriba = primera.ZArriba;
        }

        public double Ux { get; }
        public double Uy { get; }
        public double Desplazamiento { get; }
        public double ZArriba { get; }
        public List<Tramo> Tramos { get; } = [];
        public double ZAbajo => Tramos.Min(t => t.Viga.ZAbajo);
        public double ZTope => Tramos.Max(t => t.Viga.ZArriba);

        /// <summary>Distancia a lo largo de la línea.</summary>
        public double A(Punto p) => p.X * Ux + p.Y * Uy;

        /// <summary>Distancia con signo a la línea, sin descontar su desplazamiento.</summary>
        public double Normal(Punto p) => -p.X * Uy + p.Y * Ux;

        public bool EsParalela(double dx, double dy)
        {
            double largo = Math.Sqrt(dx * dx + dy * dy);
            return largo > 0 && Math.Abs(Ux * dy - Uy * dx) / largo <= ToleranciaAngulo;
        }

        public bool Acepta(VigaLeida v) =>
            EsParalela(v.Hasta.X - v.Desde.X, v.Hasta.Y - v.Desde.Y)
            && Math.Abs(Normal(v.Desde) - Desplazamiento) <= ToleranciaLinea
            && Math.Abs(Normal(v.Hasta) - Desplazamiento) <= ToleranciaLinea
            && Math.Abs(v.ZArriba - ZArriba) <= ToleranciaLinea;

        public bool EsColineal(Punto a, Punto b) =>
            EsParalela(b.X - a.X, b.Y - a.Y) && Math.Abs(Normal(a) - Desplazamiento) <= ToleranciaLinea;

        /// <summary>Dónde cruza la línea a la recta a–b (distancia a lo largo de la línea), o null si son paralelas.</summary>
        public double? Cruce(Punto a, Punto b)
        {
            double gx = b.X - a.X, gy = b.Y - a.Y;
            double denominador = Ux * gy - Uy * gx;
            if (EsParalela(gx, gy) || Math.Abs(denominador) < 1e-12)
                return null;
            // Punto de la línea en t: (−Uy·d + t·Ux, Ux·d + t·Uy), con d el desplazamiento.
            double ox = -Uy * Desplazamiento - a.X, oy = Ux * Desplazamiento - a.Y;
            return -(ox * gy - oy * gx) / denominador;
        }

        /// <summary>Si la huella cruza la línea en planta, el rango que ocupa a lo largo de ella y de lado a lado.</summary>
        public (double Desde, double Hasta, double NormalDesde, double NormalHasta)? Cruza(IReadOnlyList<Punto> huella)
        {
            double minimo = huella.Min(p => Normal(p) - Desplazamiento), maximo = huella.Max(p => Normal(p) - Desplazamiento);
            if (minimo > SolapeMinimoEnAlto || maximo < -SolapeMinimoEnAlto)
                return null;
            return (huella.Min(A), huella.Max(A), minimo, maximo);
        }

        public bool SeSolapaEnAlto(double zAbajo, double zArriba) =>
            Math.Min(zArriba, ZTope) - Math.Max(zAbajo, ZAbajo) > SolapeMinimoEnAlto;
    }

    private static List<Linea> Lineas(IReadOnlyList<VigaLeida> vigas)
    {
        var lineas = new List<Linea>();
        foreach (VigaLeida v in vigas)
        {
            Linea linea = lineas.FirstOrDefault(l => l.Acepta(v)) ?? Agregar(new Linea(v));
            double a = linea.A(v.Desde), b = linea.A(v.Hasta);
            linea.Tramos.Add(new Tramo(v, Math.Min(a, b), Math.Max(a, b)));
        }
        foreach (Linea l in lineas)
            l.Tramos.Sort((x, y) => x.Desde.CompareTo(y.Desde));
        return lineas;

        Linea Agregar(Linea nueva)
        {
            lineas.Add(nueva);
            return nueva;
        }
    }

    /// <summary>
    /// Columnas y muros de carga que cruzan la línea en planta y se solapan con ella en alto; los muros que van a lo
    /// largo no cuentan. Las vigas que la cruzan quedan como candidatas a viga cargadora.
    /// </summary>
    private static List<Soporte> Soportes(Linea linea, ModeloLeido modelo)
    {
        var soportes = new List<Soporte>();
        foreach (ApoyoLeido a in modelo.Apoyos)
        {
            if (!linea.SeSolapaEnAlto(a.ZAbajo, a.ZArriba))
                continue;
            if (a.Direccion is Punto d && linea.EsParalela(d.X, d.Y))
                continue;
            if (linea.Cruza(a.Huella) is var (desde, hasta, normalDesde, normalHasta))
                soportes.Add(new Soporte(a.Id, a.Tipo, desde, hasta, normalDesde, normalHasta));
        }
        foreach (VigaLeida v in modelo.Vigas)
        {
            if (linea.Tramos.Any(t => t.Viga == v) || linea.EsParalela(v.Hasta.X - v.Desde.X, v.Hasta.Y - v.Desde.Y)
                || !linea.SeSolapaEnAlto(v.ZAbajo, v.ZArriba))
                continue;
            if (linea.Cruza(Huella(v)) is var (desde, hasta, normalDesde, normalHasta))
                soportes.Add(new Soporte(v.Id, TipoApoyo.Viga, desde, hasta, normalDesde, normalHasta));
        }
        return soportes;
    }

    /// <summary>Contorno en planta de una viga, a partir de su eje y su ancho.</summary>
    private static IReadOnlyList<Punto> Huella(VigaLeida v)
    {
        double dx = v.Hasta.X - v.Desde.X, dy = v.Hasta.Y - v.Desde.Y;
        double largo = Math.Sqrt(dx * dx + dy * dy);
        double nx = -dy / largo * v.Ancho / 2, ny = dx / largo * v.Ancho / 2;
        return [new(v.Desde.X + nx, v.Desde.Y + ny), new(v.Hasta.X + nx, v.Hasta.Y + ny),
                new(v.Hasta.X - nx, v.Hasta.Y - ny), new(v.Desde.X - nx, v.Desde.Y - ny)];
    }

    /// <summary>Una viga que cruza es apoyo solo donde una viga del pórtico termina sobre ella sin columna ni muro.</summary>
    private static bool TerminaSobre(List<Tramo> cadena, Soporte viga, List<Soporte> soportes) =>
        cadena.SelectMany(t => new[] { t.Desde, t.Hasta })
            .Any(punta => Contiene(viga, punta) && !soportes.Any(s => s.Tipo != TipoApoyo.Viga && Contiene(s, punta)));

    /// <summary>
    /// Lo que pasa de la primera / última columna o muro es voladizo: una viga que cruza ahí es armazón, no apoyo,
    /// salvo que ella misma descanse en una columna o muro (cargadora). Decisión 3 del plan
    /// docs/planes/2026-10-19-ajustes-tablero-vigas-100.md.
    /// </summary>
    private static List<Soporte> SinArmazonDeVoladizos(List<Soporte> soportes, ModeloLeido modelo)
    {
        int primero = soportes.FindIndex(s => s.Tipo != TipoApoyo.Viga), ultimo = soportes.FindLastIndex(s => s.Tipo != TipoApoyo.Viga);
        return soportes.Where((s, i) => s.Tipo != TipoApoyo.Viga || (primero >= 0 && i > primero && i < ultimo)
            || modelo.Vigas.FirstOrDefault(v => v.Id == s.Id) is VigaLeida v && modelo.Apoyos.Any(a => Descansa(v, a))).ToList();
    }

    /// <summary>La viga descansa en el apoyo si se pisan en planta y el apoyo llega a la altura de la viga.</summary>
    private static bool Descansa(VigaLeida v, ApoyoLeido a)
    {
        if (a.ZArriba < v.ZAbajo - ToleranciaContacto || a.ZAbajo > v.ZArriba)
            return false;
        double dx = v.Hasta.X - v.Desde.X, dy = v.Hasta.Y - v.Desde.Y;
        double largo = Math.Sqrt(dx * dx + dy * dy), ux = dx / largo, uy = dy / largo;
        double A(Punto p) => (p.X - v.Desde.X) * ux + (p.Y - v.Desde.Y) * uy;
        double N(Punto p) => -(p.X - v.Desde.X) * uy + (p.Y - v.Desde.Y) * ux;
        return a.Huella.Max(A) >= -ToleranciaContacto && a.Huella.Min(A) <= largo + ToleranciaContacto
            && a.Huella.Max(N) >= -v.Ancho / 2 - ToleranciaContacto && a.Huella.Min(N) <= v.Ancho / 2 + ToleranciaContacto;
    }

    private static bool Contiene(Soporte s, double t) => t >= s.Desde - ToleranciaContacto && t <= s.Hasta + ToleranciaContacto;

    /// <summary>Parte la línea donde hay un hueco que ningún apoyo tapa: cada parte es un pórtico.</summary>
    private static List<List<Tramo>> Cadenas(Linea linea, List<Soporte> soportes)
    {
        var cadenas = new List<List<Tramo>>();
        List<Tramo>? actual = null;
        double fin = double.MinValue;
        foreach (Tramo t in linea.Tramos)
        {
            bool sigue = actual is not null && (t.Desde - fin <= ToleranciaContacto
                || soportes.Any(s => s.Desde <= fin + ToleranciaContacto && s.Hasta >= t.Desde - ToleranciaContacto));
            if (!sigue)
                cadenas.Add(actual = []);
            actual!.Add(t);
            fin = Math.Max(fin, t.Hasta);
        }
        return cadenas;
    }

    /// <summary>Ordena los apoyos a lo largo de la línea y junta los que se pisan (por ejemplo, columna y muro).</summary>
    private static List<Soporte> Unir(IEnumerable<Soporte> soportes)
    {
        var unidos = new List<Soporte>();
        foreach (Soporte s in soportes.OrderBy(s => s.Desde))
        {
            if (unidos.Count > 0 && s.Desde <= unidos[^1].Hasta)
                unidos[^1] = unidos[^1] with
                {
                    Hasta = Math.Max(unidos[^1].Hasta, s.Hasta),
                    NormalDesde = Math.Min(unidos[^1].NormalDesde, s.NormalDesde),
                    NormalHasta = Math.Max(unidos[^1].NormalHasta, s.NormalHasta),
                };
            else
                unidos.Add(s);
        }
        return unidos;
    }
}
