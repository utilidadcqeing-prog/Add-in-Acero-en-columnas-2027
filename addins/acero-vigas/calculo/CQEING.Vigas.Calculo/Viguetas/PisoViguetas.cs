using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Viguetas;

/// <summary>
/// Un elemento de armazón del piso, en mm. <paramref name="Creada"/>: la creó «Crear viguetas» (tiene la marca en
/// Comentarios).
/// </summary>
public sealed record ArmazonPiso(string Id, Punto Desde, Punto Hasta, double Ancho, string? NombreTipo, bool Creada)
{
    public bool EsVigueta => ReglaVigueta.Es(NombreTipo, Ancho);

    public Punto Medio => new((Desde.X + Hasta.X) / 2, (Desde.Y + Hasta.Y) / 2);
}

/// <summary>Una columna del piso como su caja en planta, en mm.</summary>
public sealed record ColumnaPiso(string Id, Punto Min, Punto Max);

/// <summary>Lo que se lee de un nivel de Revit para «Crear viguetas» (#948). <paramref name="Cota"/> en mm.</summary>
public sealed record PisoLeido(string Id, string Nombre, double Cota, IReadOnlyList<ArmazonPiso> Armazon, IReadOnlyList<ColumnaPiso> Columnas);

/// <summary>Un nivel en la lista de pisos: cuántos elementos de armazón tiene.</summary>
public sealed record PisoResumen(string Id, string Nombre, double Cota, int Armazon);

/// <summary>Un tipo de armazón del modelo que se puede usar como vigueta; <paramref name="Sugerido"/> si cumple la regla de vigueta.</summary>
public sealed record TipoArmazon(string Id, string Nombre, bool Sugerido);

/// <summary>
/// El piso abierto en la ventana y sus paneles (#947). Las viguetas que ya existen no parten los paneles salvo que se
/// pida (<paramref name="viguetasComoBorde"/>, para las viguetas de borde); las creadas por el add-in nunca.
/// </summary>
public sealed class PisoViguetas(PisoLeido piso, bool viguetasComoBorde)
{
    public PisoLeido Piso { get; } = piso;

    public bool ViguetasComoBorde { get; } = viguetasComoBorde;

    public IReadOnlyList<Panel> Paneles { get; } = DetectorPaneles.Detectar(piso.Armazon
        .Where(a => !a.Creada && (viguetasComoBorde || !a.EsVigueta))
        .Select(a => new BordePanel(a.Id, a.Desde, a.Hasta, a.Ancho)));

    public Panel? Panel(string id) => Paneles.FirstOrDefault(p => p.Id == id);

    /// <summary>Las viguetas que creó el add-in dentro del panel (su punto medio cae en el contorno por ejes).</summary>
    public List<ArmazonPiso> CreadasEn(Panel panel) =>
        Piso.Armazon.Where(a => a.Creada && Geometria.Dentro(panel.Eje, a.Medio)).ToList();
}
