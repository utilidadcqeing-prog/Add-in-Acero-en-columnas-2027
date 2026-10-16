using CQEING.Vigas.Calculo.Estribos;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// El pórtico de la maqueta con estribo #3 (9.5 mm): el primero queda a 50 + 4.75 = 54.75 mm de la cara.
/// Luces libres: A-B de 200 a 4200, B-C de 4600 a 7700 y C-D de 8100 a 12100; voladizo D de 12500 a 14000.
/// </summary>
public class DistribuidorEstribosTests
{
    private const double Estribo3 = 9.5;
    private static readonly ConfigEstribos General = new();

    private static Tramo TramoDe(Portico p, string nombre) => DistribuidorEstribos.Tramos(p).Single(t => t.Nombre == nombre);

    private static IReadOnlyList<double> Posiciones(string nombre, ConfigEstribos? c = null, Portico? p = null) =>
        DistribuidorEstribos.Posiciones(TramoDe(p ?? Datos.PorticoMaqueta(), nombre), c ?? General, Estribo3);

    /// <summary>Que <paramref name="posiciones"/> vaya de <paramref name="desde"/> a <paramref name="hasta"/>, cada <paramref name="paso"/>.</summary>
    private static void VanCada(IReadOnlyList<double> posiciones, double desde, double hasta, double paso)
    {
        Assert.Equal(desde, posiciones[0], 6);
        Assert.Equal(hasta, posiciones[^1], 6);
        for (int i = 1; i < posiciones.Count; i++)
            Assert.Equal(paso, posiciones[i] - posiciones[i - 1], 6);
    }

    [Fact]
    public void LosTramosDelPorticoSonSusLucesYSuVoladizo()
    {
        IReadOnlyList<Tramo> tramos = DistribuidorEstribos.Tramos(Datos.PorticoMaqueta());

        Assert.Equal(["A-B", "B-C", "C-D", "Voladizo D"], tramos.Select(t => t.Nombre));
        Assert.Equal(new Tramo("Voladizo D", 12500, 14000, true), tramos[^1]);
        Assert.All(tramos.Take(3), t => Assert.False(t.EsVoladizo));
    }

    [Fact]
    public void UnVoladizoAlInicio_EsElPrimerTramo()
    {
        Portico p = Datos.PorticoMaqueta() with { VoladizoInicio = 1000 };

        Assert.Equal(new Tramo("Voladizo A", -1200, -200, true), DistribuidorEstribos.Tramos(p)[0]);
    }

    [Fact]
    public void EnLaLuzAB_OchoPorZonaYDoceEnElCentro()
    {
        IReadOnlyList<double> p = Posiciones("A-B");

        Assert.Equal(28, p.Count);
        VanCada(p.Take(8).ToList(), 254.75, 954.75, 100);
        VanCada(p.Skip(7).Take(14).ToList(), 954.75, 3445.25, 2490.5 / 13);
        VanCada(p.Skip(20).ToList(), 3445.25, 4145.25, 100);
    }

    [Fact]
    public void EnLaLuzBC_SieteEnElCentro()
    {
        IReadOnlyList<double> p = Posiciones("B-C");

        Assert.Equal(23, p.Count);
        VanCada(p.Skip(7).Take(9).ToList(), 5354.75, 6945.25, 1590.5 / 8);
    }

    [Fact]
    public void ElVoladizoVaTodoConLaSeparacionDeColumnas()
    {
        IReadOnlyList<double> p = Posiciones("Voladizo D");

        Assert.Equal(15, p.Count);
        VanCada(p, 12554.75, 13945.25, 1390.5 / 14);
    }

    [Fact]
    public void UnaLuzCortaVaTodaConLaSeparacionDeColumnas()
    {
        var portico = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 1900)]);

        IReadOnlyList<double> p = Posiciones("A-B", p: portico);

        Assert.Equal(15, p.Count);
        VanCada(p, 254.75, 1645.25, 1390.5 / 14);
    }

    [Fact]
    public void UnaLuzIgualADosZonas_NoEsCorta()
    {
        var portico = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 2000)]);

        IReadOnlyList<double> p = Posiciones("A-B", p: portico);

        Assert.Equal(16, p.Count);
        VanCada(p.Take(8).ToList(), 254.75, 954.75, 100);
        Assert.Equal(1045.25 - 954.75, p[8] - p[7], 6);
        VanCada(p.Skip(8).ToList(), 1045.25, 1745.25, 100);
    }

    [Fact]
    public void SinZona_TodaLaLuzVaConLaSeparacionDelCentro()
    {
        IReadOnlyList<double> p = Posiciones("A-B", General with { LargoZona = 0 });

        Assert.Equal(21, p.Count);
        VanCada(p, 254.75, 4145.25, 3890.5 / 20);
    }

    [Fact]
    public void UnaLuzMenorQueDosPrimeros_UnSoloEstriboAlCentro()
    {
        var portico = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 500)]);

        Assert.Equal([250.0], Posiciones("A-B", p: portico));
    }
}
