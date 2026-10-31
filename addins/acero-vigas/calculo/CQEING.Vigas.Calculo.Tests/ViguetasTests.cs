using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Viguetas;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>«Crear viguetas» (#947): reparto del Excel, paneles del piso y viguetas de un panel.</summary>
public class ViguetasTests
{
    [Theory]
    // Los cinco casos del Excel de Carlos y de la reunión del 24-oct (espacio libre, cantidad, libre en extremos).
    [InlineData(2000, 2, 550)]
    [InlineData(3570, 4, 535)]
    [InlineData(2840, 3, 570)]
    [InlineData(2660, 3, 480)]
    [InlineData(3120, 4, 310)]
    public void ElRepartoDaLoMismoQueElExcel(double libre, int cantidad, double extremos)
    {
        Reparto r = RepartoViguetas.Calcular(libre, 800, 100)!;

        Assert.Equal(cantidad, r.Cantidad);
        Assert.Equal(extremos, r.LibreExtremos, 6);
        Assert.Equal(700, r.LibreMaximo, 6);
    }

    [Theory]
    [InlineData(0, 800, 100)]
    [InlineData(2000, 0, 100)]
    [InlineData(2000, 800, 800)]
    public void ConDatosQueNoSirvenNoHayReparto(double libre, double eje, double ancho) =>
        Assert.Null(RepartoViguetas.Calcular(libre, eje, ancho));

    /// <summary>Dos paneles de 4,00 × 3,00 m entre ejes, con vigas de 30 cm.</summary>
    private static List<BordePanel> Grilla() =>
    [
        new("abajo", new(0, 0), new(8000, 0), 300),
        new("arriba", new(0, 3000), new(8000, 3000), 300),
        new("A", new(0, 0), new(0, 3000), 300),
        new("B", new(4000, 0), new(4000, 3000), 300),
        new("C", new(8000, 0), new(8000, 3000), 300),
    ];

    [Fact]
    public void UnaGrillaDeDosPorUnoDaDosPanelesConSuContornoLibre()
    {
        List<Panel> paneles = DetectorPaneles.Detectar(Grilla());

        Assert.Equal(2, paneles.Count);
        Panel izquierdo = paneles.Single(p => Geometria.Centro(p.Libre).X < 4000);
        Assert.Equal(3700 * 2700, izquierdo.AreaLibre, 0);
        Assert.Equal(150, izquierdo.Libre.Min(q => q.X), 3);
        Assert.Equal(3850, izquierdo.Libre.Max(q => q.X), 3);
        Assert.Equal(4, izquierdo.Eje.Count);
    }

    [Fact]
    public void UnVoladizoNoCierraNingunPanel()
    {
        List<BordePanel> bordes = Grilla();
        bordes.Add(new("voladizo", new(8000, 0), new(9500, 0), 300));

        Assert.Equal(2, DetectorPaneles.Detectar(bordes).Count);
    }

    [Fact]
    public void UnaVigaQueTerminaEnLaCaraDeLaOtraIgualCierraElPanel()
    {
        List<BordePanel> bordes = Grilla();
        bordes[3] = new("B", new(4000, 150), new(4000, 2850), 300);

        Assert.Equal(2, DetectorPaneles.Detectar(bordes).Count);
    }

    [Fact]
    public void LasViguetasExistentesSoloPartenElPanelSiSePide()
    {
        List<ArmazonPiso> armazon = Grilla().Select(b => new ArmazonPiso(b.Id, b.Desde, b.Hasta, b.Ancho, "V30X40", false)).ToList();
        armazon.Add(new("vt", new(2000, 0), new(2000, 3000), 100, "VT_10X35", false));
        armazon.Add(new("creada", new(6000, 0), new(6000, 3000), 100, "VT_10X35", true));
        var piso = new PisoLeido("1", "N+3.70", 3700, armazon, []);

        var soloVigas = new PisoViguetas(piso, viguetasComoBorde: false);
        Assert.Equal(2, soloVigas.Paneles.Count);
        Assert.Single(soloVigas.CreadasEn(soloVigas.Paneles.Single(p => Geometria.Centro(p.Libre).X > 4000)));
        // La creada por el add-in nunca parte el panel; la vigueta del modelo sí, si se pide.
        Assert.Equal(3, new PisoViguetas(piso, viguetasComoBorde: true).Paneles.Count);
    }

    /// <summary>Panel libre de 3,57 × 2,00 m (los de la maqueta) entre vigas de 30 cm.</summary>
    private static Panel PanelMaqueta()
    {
        List<BordePanel> bordes =
        [
            new("abajo", new(0, 0), new(3870, 0), 300),
            new("arriba", new(0, 2300), new(3870, 2300), 300),
            new("izq", new(0, 0), new(0, 2300), 300),
            new("der", new(3870, 0), new(3870, 2300), 300),
        ];
        return DetectorPaneles.Detectar(bordes).Single();
    }

    [Fact]
    public void LasViguetasEnXSeRepartenEnLosDosMetrosYVanDeEjeAEje()
    {
        PropuestaViguetas p = PlanificadorViguetas.Planificar(PanelMaqueta(), Direccion.X, 800, 100, new HashSet<string>())!;

        Assert.Equal(2000, p.Libre, 3);
        Assert.Equal(2, p.Reparto.Cantidad);
        Assert.Equal(550, p.Reparto.LibreExtremos, 3);
        Assert.Equal([750.0, 1550.0], p.Viguetas.Select(v => Math.Round(v.Cota, 3)));
        Vigueta primera = p.Viguetas[0];
        Assert.Equal(3570, primera.Largo, 3);
        Assert.Equal(0, primera.EjeDesde, 3);
        Assert.Equal(3870, primera.EjeHasta, 3);
        Assert.Equal(new TipoVigueta(1, 3570, 2), Assert.Single(p.Tipos));
    }

    [Fact]
    public void EnYSonCuatroYUnaBorradaNoCuentaEnLosTipos()
    {
        PropuestaViguetas p = PlanificadorViguetas.Planificar(PanelMaqueta(), Direccion.Y, 800, 100, new HashSet<string> { "2" })!;

        Assert.Equal(4, p.Reparto.Cantidad);
        Assert.True(p.Viguetas.Single(v => v.Id == "2").Borrada);
        Assert.Equal(3, p.Vivas.Count());
        Assert.Equal(3, Assert.Single(p.Tipos).Cantidad);
    }

    [Fact]
    public void EnUnPanelEnLLasViguetasQueCruzanElHuecoSePartenEnDos()
    {
        // Una L: 6 × 4 m menos la esquina de arriba a la derecha (3 × 2 m), por ejes.
        List<BordePanel> bordes =
        [
            new("1", new(0, 0), new(6000, 0), 200),
            new("2", new(6000, 0), new(6000, 2000), 200),
            new("3", new(6000, 2000), new(3000, 2000), 200),
            new("4", new(3000, 2000), new(3000, 4000), 200),
            new("5", new(3000, 4000), new(0, 4000), 200),
            new("6", new(0, 4000), new(0, 0), 200),
        ];
        Panel l = DetectorPaneles.Detectar(bordes).Single();

        PropuestaViguetas p = PlanificadorViguetas.Planificar(l, Direccion.Y, 800, 100, new HashSet<string>())!;

        Assert.All(p.Viguetas, v => Assert.InRange(v.Largo, 1000, 3900));
        Assert.Contains(p.Viguetas, v => v.Cota > 3100 && v.Largo < 2000);
        Assert.True(p.Tipos.Count >= 2);
    }

    [Fact]
    public void ElMensajeDeCrearTraeElPedidoYElTipo()
    {
        MensajeViguetas m = ProtocoloViguetas.Leer(
            """{"tipo":"crear","panel":"P2","direccion":"Y","ejeAEje":800,"ancho":100,"borradas":["1","3"],"tipoVigueta":"123"}""");

        CrearViguetas c = Assert.IsType<CrearViguetas>(m);
        Assert.Equal("123", c.Tipo);
        Assert.Equal(new[] { "1", "3" }, c.Propuesta.Borradas);
        Assert.Equal(Direccion.Y, c.Propuesta.Direccion);
        Assert.IsType<DesconocidoViguetas>(ProtocoloViguetas.Leer("{roto"));
    }
}
