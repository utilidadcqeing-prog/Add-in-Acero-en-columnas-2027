using System.Text.Json;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Tablero;
using Xunit.Abstractions;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>La vista en planta del tablero y los sistemas de viguetas iguales (planta, 27-oct).</summary>
public sealed class ArmadorPlantaTests(ITestOutputHelper salida)
{
    private static readonly NivelLeido Nivel2 = new("Nivel 2", 3500);
    private static readonly NivelLeido Nivel3 = new("Nivel 3", 7000);

    /// <summary>Una vigueta de <paramref name="largo"/> mm en y, desde (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private static PorticoLeido Vigueta(string viga, double x, double y, double largo, double uy = 1) =>
        ArmadorVistaTests.Portico1() with
        {
            Nombre = "Vigueta " + viga,
            Elemento = Elemento.Vigueta,
            Vigas = [viga],
            Ubicacion = new UbicacionPortico(0, uy, x, y, 3150, 3500, 100, 350, [new(viga, 0, (int)largo)]),
        };

    private static VigaLeida VigaDe(string id, NivelLeido nivel, string? tipo = null) =>
        new(id, new Punto(0, 0), new Punto(1, 0), nivel.Elevacion - 350, nivel.Elevacion, 100, nivel, NombreTipo: tipo);

    private static ApoyoLeido Columna(string id, double x, double y, double zAbajo, double zArriba) =>
        new(id, TipoApoyo.Columna, [new(x - 200, y - 200), new(x + 200, y - 200), new(x + 200, y + 200), new(x - 200, y + 200)], zAbajo, zArriba);

    /// <summary>
    /// Nivel 2: la viga del eje 1, tres viguetas iguales de 5 m (una dibujada al revés) y una de 4 m. Nivel 3: la viga.
    /// Más una sin nivel y una sin ubicación en el modelo.
    /// </summary>
    private static (List<(string, PorticoLeido)> Porticos, ModeloLeido Modelo) Datos()
    {
        PorticoLeido viga = PlanificadorAceroTests.Portico();
        var porticos = new List<(string, PorticoLeido)>
        {
            ("Pórtico 1 · Nivel 3", viga with { Nivel = "Nivel 3", Vigas = ["301"], Ubicacion = viga.Ubicacion! with { ZArriba = 7000 } }),
            ("Pórtico 1 · Nivel 2", viga),
            ("Vigueta v1", Vigueta("v1", 2000, 0, 5000)),
            ("Vigueta v2", Vigueta("v2", 2700, 5000, 5000, uy: -1)),
            ("Vigueta v3", Vigueta("v3", 3400, 0, 5004)),
            ("Vigueta v4", Vigueta("v4", 4100, 0, 4000)),
            ("Aérea", viga with { Nivel = null, Vigas = ["901"], Ubicacion = viga.Ubicacion! with { ZArriba = 1000 } }),
            ("Sin ubicación", viga with { Ubicacion = null }),
        };
        var modelo = new ModeloLeido(
            [new EjeLeido("1", new Punto(-1000, 0), new Punto(15000, 0)), new EjeLeido("A", new Punto(0, -1000), new Punto(0, 6000))],
            [VigaDe("101", Nivel2), VigaDe("301", Nivel3), VigaDe("v1", Nivel2, "VT_10X35"), VigaDe("v2", Nivel2, "VT_10X35"),
                VigaDe("v3", Nivel2, "VT_10X35"), VigaDe("v4", Nivel2, "VT_10X35")],
            [Columna("c1", 0, 0, 0, 3500), Columna("c2", 0, 0, 3500, 7000), Columna("c3", 14000, 0, 3500, 7000), Columna("c4", 7000, 0, 0, 3000)]);
        return (porticos, modelo);
    }

    private static PlantaVista Armar()
    {
        (List<(string, PorticoLeido)> porticos, ModeloLeido modelo) = Datos();
        return ArmadorPlanta.Armar(porticos, modelo);
    }

    [Fact]
    public void LosNivelesVanPorCotaYLosSinNivelAlFinal()
    {
        PlantaVista p = Armar();

        Assert.Equal([("Nivel 2", 3.5), ("Nivel 3", 7.0), ("Sin nivel", 1.0)], p.Niveles.Select(n => (n.Nombre, n.Elevacion)));
        Assert.DoesNotContain(p.Niveles.SelectMany(n => n.Elementos), e => e.Portico == "Sin ubicación");
    }

    [Fact]
    public void CadaPorticoEsSuEjeDePuntaAPunta()
    {
        NivelPlanta n2 = Armar().Niveles[0];

        ElementoPlanta viga = n2.Elementos[0];
        Assert.Equal(("Pórtico 1 · Nivel 2", "viga", -0.2, 0.0, 14.0, 0.0, 0.3), (viga.Portico, viga.Tipo, viga.X1, viga.Y1, viga.X2, viga.Y2, viga.Ancho));
        ElementoPlanta alReves = n2.Elementos.Single(e => e.Portico == "Vigueta v2");
        Assert.Equal(("vigueta", 2.7, 5.0, 2.7, 0.0, 0.1), (alReves.Tipo, alReves.X1, alReves.Y1, alReves.X2, alReves.Y2, alReves.Ancho));
    }

    /// <summary>Tres viguetas iguales (una al revés, otra 4 mm más larga) son un sistema; la de 4 m queda sola.</summary>
    [Fact]
    public void LasViguetasIgualesSonUnSistema()
    {
        PlantaVista p = Armar();

        Assert.Equal(["", "VT_10X35 · 1", "VT_10X35 · 1", "VT_10X35 · 1", ""], p.Niveles[0].Elementos.Select(e => e.Grupo));
        IReadOnlyDictionary<string, IReadOnlyList<string>> grupos = ArmadorPlanta.Grupos(p);
        Assert.Equal(["Vigueta v1", "Vigueta v2", "Vigueta v3"], grupos["Vigueta v2"]);
        Assert.False(grupos.ContainsKey("Vigueta v4"));
        Assert.False(grupos.ContainsKey("Pórtico 1 · Nivel 2"));
    }

    /// <summary>Sin nombre de tipo, las iguales se reconocen por su sección en cm.</summary>
    [Fact]
    public void SinModeloElSistemaSeNombraPorLaSeccion()
    {
        PlantaVista p = ArmadorPlanta.Armar(Datos().Porticos, null);

        Assert.Equal("10×35 · 1", p.Niveles[0].Elementos[1].Grupo);
        Assert.Empty(p.Niveles[0].Ejes);
        Assert.Empty(p.Niveles[0].Columnas);
        // sin el nivel de Revit, la cota es la menor cara de arriba de sus pórticos
        Assert.Equal(3.5, p.Niveles[0].Elevacion);
    }

    [Fact]
    public void LasColumnasSonLasQueLleganALaCotaDelNivel()
    {
        PlantaVista p = Armar();

        // Nivel 2: c1 llega desde abajo y c2 sigue arriba con la misma huella (va una vez); c3 arranca ahí; c4 no llega
        Assert.Equal(2, p.Niveles[0].Columnas.Count);
        Assert.Equal([-0.2, -0.2], p.Niveles[0].Columnas[0].Puntos[0]);
        Assert.Equal(2, p.Niveles[1].Columnas.Count);
        Assert.Equal(["1", "A"], p.Niveles[0].Ejes.Select(e => e.Nombre));
        Assert.Equal((0.0, -1.0, 0.0, 6.0), (p.Niveles[1].Ejes[1].X1, p.Niveles[1].Ejes[1].Y1, p.Niveles[1].Ejes[1].X2, p.Niveles[1].Ejes[1].Y2));
    }

    [Fact]
    public void LaPlantaViajaEnElContratoDeLaPagina()
    {
        string json = JsonSerializer.Serialize(Armar(), ProtocoloTablero.Json);
        salida.WriteLine(json);

        JsonElement n2 = JsonDocument.Parse(json).RootElement.GetProperty("niveles")[0];
        Assert.Equal("Nivel 2", n2.GetProperty("nombre").GetString());
        Assert.Equal(-0.2, n2.GetProperty("columnas")[0].GetProperty("puntos")[0][0].GetDouble());
        JsonElement vigueta = n2.GetProperty("elementos")[1];
        Assert.Equal(("Vigueta v1", "vigueta", "VT_10X35 · 1", 5.0), (vigueta.GetProperty("portico").GetString(),
            vigueta.GetProperty("tipo").GetString(), vigueta.GetProperty("grupo").GetString(), vigueta.GetProperty("y2").GetDouble()));
    }
}
