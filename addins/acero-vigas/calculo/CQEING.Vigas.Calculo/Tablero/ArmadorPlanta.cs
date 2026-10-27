using System.Globalization;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>
/// Arma la vista en planta del tablero (planta, 27-oct): por nivel, los ejes de la grilla, las columnas y muros que
/// llegan a su cota y el eje de cada pórtico de punta a punta. Junta las viguetas iguales en un sistema ("sistema de
/// viguetas", Carlos): al hacer clic en una se toman todas y llevan el mismo acero.
/// </summary>
public static class ArmadorPlanta
{
    /// <summary>El nivel de los pórticos que no tienen.</summary>
    public const string SinNivel = "Sin nivel";

    /// <summary>Lo que puede pasarse una columna de la cota del nivel y seguir en él, en mm.</summary>
    private const double ToleranciaCota = 50;

    /// <summary>Diferencia de largo máxima entre dos viguetas iguales, en mm (1 cm).</summary>
    private const double ToleranciaLargo = 10;

    /// <summary>Seno del ángulo máximo entre dos viguetas paralelas.</summary>
    private const double ToleranciaAngulo = 0.01;

    /// <summary>
    /// La planta de los pórticos (<paramref name="porticos"/>, con su id de la lista). Se omiten los que no tienen
    /// ubicación en el modelo. Sin <paramref name="modelo"/> no hay ejes ni columnas, y la cota de cada nivel es la menor
    /// cara de arriba de sus pórticos.
    /// </summary>
    public static PlantaVista Armar(IEnumerable<(string Id, PorticoLeido Portico)> porticos, ModeloLeido? modelo)
    {
        Dictionary<string, VigaLeida> vigas = modelo?.Vigas.GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First()) ?? [];
        List<EnPlanta> enPlanta = porticos
            .Where(p => p.Portico.Ubicacion is { Vigas.Count: > 0 })
            .Select(p => EnPlanta.De(p.Id, p.Portico, vigas))
            .ToList();
        List<EjePlanta> ejes = modelo?.Ejes.Select(e => new EjePlanta(e.Nombre, M(e.Desde.X), M(e.Desde.Y), M(e.Hasta.X), M(e.Hasta.Y))).ToList() ?? [];

        var niveles = enPlanta
            .GroupBy(e => e.Portico.Nivel ?? SinNivel)
            .Select(g => (Nombre: g.Key, Cota: CotaDe(g.Key, g.ToList()), Elementos: g.ToList()))
            .OrderBy(n => n.Nombre == SinNivel)
            .ThenBy(n => n.Cota)
            .Select(n => new NivelPlanta(n.Nombre, M(n.Cota), ejes, Columnas(modelo, n.Cota), Elementos(n.Elementos)))
            .ToList();
        return new PlantaVista(niveles);
    }

    /// <summary>
    /// Los ids de los pórticos de cada sistema de viguetas de <paramref name="planta"/>: cada miembro apunta a la lista
    /// de todos, en su orden. Los que no están en un sistema no aparecen.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Grupos(PlantaVista planta)
    {
        var grupos = new Dictionary<string, IReadOnlyList<string>>();
        foreach (NivelPlanta nivel in planta.Niveles)
            foreach (IGrouping<string, ElementoPlanta> g in nivel.Elementos.Where(e => e.Grupo != "").GroupBy(e => e.Grupo))
            {
                List<string> miembros = g.Select(e => e.Portico).ToList();
                foreach (string id in miembros)
                    grupos[id] = miembros;
            }
        return grupos;
    }

    /// <summary>
    /// La cota del nivel, en mm: la del nivel de Revit de sus vigas; sin ella (sin modelo o "Sin nivel"), la menor cara
    /// de arriba de sus pórticos.
    /// </summary>
    private static double CotaDe(string nombre, List<EnPlanta> elementos) =>
        nombre != SinNivel && elementos.Select(e => e.Nivel).FirstOrDefault(n => n is not null) is { } nivel
            ? nivel.Elevacion
            : elementos.Min(e => e.Portico.Ubicacion!.ZArriba);

    /// <summary>
    /// Las columnas y muros cuyo alto alcanza la cota (con <see cref="ToleranciaCota"/>): las que llegan desde abajo y las
    /// que siguen hacia arriba. Una huella repetida (la misma columna en dos tramos de alto) va una sola vez.
    /// </summary>
    private static List<ColumnaPlanta> Columnas(ModeloLeido? modelo, double cota)
    {
        if (modelo is null)
            return [];
        var vistas = new HashSet<string>();
        var columnas = new List<ColumnaPlanta>();
        foreach (ApoyoLeido a in modelo.Apoyos.Where(a => a.Tipo != TipoApoyo.Viga && a.Huella.Count > 0
            && a.ZAbajo - ToleranciaCota <= cota && cota <= a.ZArriba + ToleranciaCota))
        {
            double[][] puntos = a.Huella.Select(p => new[] { M(p.X), M(p.Y) }).ToArray();
            if (vistas.Add(string.Join(";", puntos.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p[0]},{p[1]}")))))
                columnas.Add(new ColumnaPlanta(puntos));
        }
        return columnas;
    }

    /// <summary>Los elementos del nivel, con el sistema de cada vigueta que tiene al menos otra igual.</summary>
    private static List<ElementoPlanta> Elementos(List<EnPlanta> elementos)
    {
        var sistemas = new List<List<EnPlanta>>();
        foreach (EnPlanta e in elementos.Where(e => e.Portico.Elemento == Elemento.Vigueta))
        {
            if (sistemas.FirstOrDefault(s => s[0].IgualA(e)) is { } sistema)
                sistema.Add(e);
            else
                sistemas.Add([e]);
        }
        var grupo = new Dictionary<EnPlanta, string>();
        int n = 0;
        foreach (List<EnPlanta> s in sistemas.Where(s => s.Count > 1))
        {
            string texto = $"{s[0].Clave} · {++n}";
            foreach (EnPlanta e in s)
                grupo[e] = texto;
        }
        return elementos.Select(e => new ElementoPlanta(e.Id, e.Portico.Elemento == Elemento.Vigueta ? "vigueta" : "viga",
            M(e.X1), M(e.Y1), M(e.X2), M(e.Y2), M(e.Portico.Ubicacion!.Ancho), grupo.GetValueOrDefault(e, ""))).ToList();
    }

    /// <summary>mm del modelo a m, con 3 decimales.</summary>
    private static double M(double mm) => Math.Round(mm / 1000, 3);

    /// <summary>
    /// Un pórtico en planta, en mm: sus puntas, su dirección (unitaria), su largo, el nivel de Revit de sus vigas y la
    /// clave de su tipo (el nombre del tipo de Revit o, sin él, la sección ancho×alto en cm).
    /// </summary>
    private sealed class EnPlanta
    {
        public required string Id { get; init; }
        public required PorticoLeido Portico { get; init; }
        public required double X1 { get; init; }
        public required double Y1 { get; init; }
        public required double X2 { get; init; }
        public required double Y2 { get; init; }
        public required double Ux { get; init; }
        public required double Uy { get; init; }
        public required double Largo { get; init; }
        public required NivelLeido? Nivel { get; init; }
        public required string Clave { get; init; }

        public static EnPlanta De(string id, PorticoLeido p, Dictionary<string, VigaLeida> vigas)
        {
            UbicacionPortico u = p.Ubicacion!;
            double largoU = Math.Sqrt(u.Ux * u.Ux + u.Uy * u.Uy);
            (double ux, double uy) = largoU > 0 ? (u.Ux / largoU, u.Uy / largoU) : (1, 0);
            double desde = u.Vigas.Min(v => v.Desde), hasta = u.Vigas.Max(v => v.Hasta);
            List<VigaLeida> leidas = p.Vigas.Select(v => vigas.GetValueOrDefault(v)).OfType<VigaLeida>().ToList();
            string? tipo = leidas.Select(v => v.NombreTipo).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            return new EnPlanta
            {
                Id = id,
                Portico = p,
                X1 = u.OrigenX + ux * desde,
                Y1 = u.OrigenY + uy * desde,
                X2 = u.OrigenX + ux * hasta,
                Y2 = u.OrigenY + uy * hasta,
                Ux = ux,
                Uy = uy,
                Largo = hasta - desde,
                Nivel = leidas.Select(v => v.Nivel).FirstOrDefault(n => n is not null),
                Clave = tipo ?? $"{Math.Round(u.Ancho / 10)}×{Math.Round(u.Alto / 10)}",
            };
        }

        /// <summary>Misma clave, mismo largo (1 cm) y paralelas, sin importar el sentido.</summary>
        public bool IgualA(EnPlanta otra) =>
            Clave == otra.Clave
            && Math.Abs(Largo - otra.Largo) <= ToleranciaLargo
            && Math.Abs(Ux * otra.Uy - Uy * otra.Ux) <= ToleranciaAngulo;
    }
}
