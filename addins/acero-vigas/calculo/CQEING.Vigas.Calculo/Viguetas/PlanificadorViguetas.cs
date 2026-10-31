using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Viguetas;

/// <summary>Hacia dónde corren las viguetas: paralelas al eje X o al eje Y del proyecto.</summary>
public enum Direccion
{
    X,
    Y,
}

/// <summary>
/// Una vigueta del panel. <paramref name="Cota"/> es su eje en la dirección del reparto; <paramref name="Desde"/> y
/// <paramref name="Hasta"/>, sus caras libres a lo largo (largo y tipo); <paramref name="EjeDesde"/> y
/// <paramref name="EjeHasta"/>, hasta los ejes de las vigas (la línea que se crea en Revit). Todo en mm.
/// <paramref name="Tipo"/> es 0 si está borrada.
/// </summary>
public sealed record Vigueta(string Id, double Cota, double Desde, double Hasta, double EjeDesde, double EjeHasta, int Tipo, bool Borrada)
{
    public double Largo => Hasta - Desde;
}

/// <summary>Viguetas iguales (mismo largo libre, al cm) van en el mismo tipo; el 1 es el más largo.</summary>
public sealed record TipoVigueta(int Numero, double Largo, int Cantidad);

/// <summary>Lo que se propone para un panel (#950). <paramref name="Libre"/> es el ancho libre repartido, en mm.</summary>
public sealed record PropuestaViguetas(string Panel, Direccion Direccion, double Ancho, double Libre, Reparto Reparto,
    IReadOnlyList<Vigueta> Viguetas, IReadOnlyList<TipoVigueta> Tipos)
{
    public IEnumerable<Vigueta> Vivas => Viguetas.Where(v => !v.Borrada);

    /// <summary>El punto en planta de una vigueta: <paramref name="largo"/> a lo largo de ella, a su cota.</summary>
    public Punto Punto(double cota, double largo) => Direccion == Direccion.X ? new(largo, cota) : new(cota, largo);
}

/// <summary>
/// Reparte las viguetas de un panel con la fórmula del Excel (<see cref="RepartoViguetas"/>) sobre el ancho libre del
/// panel en la dirección del reparto, y recorta cada una al contorno del panel.
/// </summary>
public static class PlanificadorViguetas
{
    public static PropuestaViguetas? Planificar(Panel panel, Direccion direccion, double ejeAEje, double ancho, IReadOnlySet<string> borradas)
    {
        // Viguetas paralelas a X se reparten a lo largo de Y, y al revés.
        bool repartoEnY = direccion == Direccion.X;
        double Cota(Punto p) => repartoEnY ? p.Y : p.X;
        double desde = panel.Libre.Min(Cota), hasta = panel.Libre.Max(Cota);
        if (RepartoViguetas.Calcular(hasta - desde, ejeAEje, ancho) is not Reparto reparto)
            return null;

        var viguetas = new List<Vigueta>();
        for (int i = 0; i < reparto.Cantidad; i++)
        {
            double cota = desde + reparto.LibreExtremos + ancho / 2 + i * ejeAEje;
            List<(double Desde, double Hasta)> ejes = Geometria.Cortes(panel.Eje, repartoEnY, cota);
            List<(double Desde, double Hasta)> tramos = Geometria.Cortes(panel.Libre, repartoEnY, cota);
            for (int k = 0; k < tramos.Count; k++)
            {
                (double d, double h) = tramos[k];
                double medio = (d + h) / 2;
                (double ed, double eh) = ejes.FirstOrDefault(e => e.Desde <= medio && medio <= e.Hasta, (Desde: d, Hasta: h));
                string id = tramos.Count == 1 ? $"{i + 1}" : $"{i + 1}.{k + 1}";
                viguetas.Add(new Vigueta(id, cota, d, h, ed, eh, 0, borradas.Contains(id)));
            }
        }

        List<IGrouping<double, Vigueta>> grupos = viguetas.Where(v => !v.Borrada)
            .GroupBy(v => Math.Round(v.Largo / 10)).OrderByDescending(g => g.Key).ToList();
        var numero = grupos.Select((g, i) => (g.Key, i + 1)).ToDictionary(x => x.Key, x => x.Item2);
        List<Vigueta> conTipo = viguetas.Select(v => v.Borrada ? v : v with { Tipo = numero[Math.Round(v.Largo / 10)] }).ToList();
        List<TipoVigueta> tipos = grupos.Select((g, i) => new TipoVigueta(i + 1, g.Key * 10, g.Count())).ToList();
        return new PropuestaViguetas(panel.Id, direccion, ancho, hasta - desde, reparto, conTipo, tipos);
    }
}
