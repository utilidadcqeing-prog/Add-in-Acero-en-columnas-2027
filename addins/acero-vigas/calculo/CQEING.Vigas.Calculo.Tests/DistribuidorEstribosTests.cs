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
        Assert.Equal(new Tramo("Voladizo D", 12500, 14000, TipoTramo.VoladizoFinal), tramos[^1]);
        Assert.All(tramos.Take(3), t => Assert.Equal(TipoTramo.Luz, t.Tipo));
    }

    [Fact]
    public void UnVoladizoAlInicio_EsElPrimerTramo()
    {
        Portico p = Datos.PorticoMaqueta() with { VoladizoInicio = 1000 };

        Assert.Equal(new Tramo("Voladizo A", -1200, -200, TipoTramo.VoladizoInicial), DistribuidorEstribos.Tramos(p)[0]);
    }

    [Fact]
    public void EnLaLuzAB_OchoPorZonaYDoceEnElCentro()
    {
        IReadOnlyList<double> p = Posiciones("A-B");

        Assert.Equal(28, p.Count);
        VanCada(p.Take(8).ToList(), 254.75, 954.75, 100);
        VanCada(p.Skip(7).Take(13).ToList(), 954.75, 3354.75, 200);
        Assert.Equal(90.5, p[20] - p[19], 6); // lo que sobra: más corto, nunca más largo
        VanCada(p.Skip(20).ToList(), 3445.25, 4145.25, 100);
    }

    [Fact]
    public void EnLaLuzBC_SieteEnElCentro()
    {
        IReadOnlyList<double> p = Posiciones("B-C");

        Assert.Equal(23, p.Count);
        VanCada(p.Skip(7).Take(8).ToList(), 5354.75, 6754.75, 200);
        Assert.Equal(190.5, p[15] - p[14], 6);
    }

    /// <summary>
    /// Carlos (21-oct, min 15:14): "en voladizo no existe el confinamiento y el no confinamiento… siempre va a existir
    /// el confinamiento". Todo el voladizo va con la separación de la zona, de la columna a 5 cm de la punta.
    /// </summary>
    [Fact]
    public void ElVoladizoVaTodoConLaSeparacionDeLaZona()
    {
        IReadOnlyList<double> p = Posiciones("Voladizo D");

        Assert.Equal(15, p.Count);
        VanCada(p.Take(14).ToList(), 12554.75, 13854.75, 100);
        Assert.Equal(13945.25, p[^1], 6);
    }

    [Fact]
    public void UnVoladizoAlInicio_TambienVaTodoConLaSeparacionDeLaZona()
    {
        Portico portico = Datos.PorticoMaqueta() with { VoladizoInicio = 1500 };

        IReadOnlyList<double> p = Posiciones("Voladizo A", p: portico);

        Assert.Equal(15, p.Count);
        VanCada(p.Skip(1).ToList(), -1554.75, -254.75, 100); // de la columna hacia la punta
        Assert.Equal(-1645.25, p[0], 6);
    }

    /// <summary>Como la luz corta (min 1:22:43): si no alcanza para la zona, todo el voladizo va como zona.</summary>
    [Fact]
    public void UnVoladizoMasCortoQueLaZona_VaTodoComoZona()
    {
        Portico portico = Datos.PorticoMaqueta() with { VoladizoFinal = 700 };

        IReadOnlyList<double> p = Posiciones("Voladizo D", p: portico);

        Assert.Equal(7, p.Count);
        VanCada(p.Take(6).ToList(), 12554.75, 13054.75, 100);
        Assert.Equal(13145.25, p[^1], 6);
    }

    [Fact]
    public void UnVoladizoIgualALaZona_VaTodoComoZona()
    {
        Portico portico = Datos.PorticoMaqueta() with { VoladizoFinal = 800 };

        IReadOnlyList<double> p = Posiciones("Voladizo D", p: portico);

        Assert.Equal(8, p.Count);
        VanCada(p.Take(7).ToList(), 12554.75, 13154.75, 100);
        Assert.Equal(13245.25, p[^1], 6);
    }

    [Fact]
    public void UnaLuzCortaVaTodaConLaSeparacionDeColumnas()
    {
        var portico = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 1900)]);

        IReadOnlyList<double> p = Posiciones("A-B", p: portico);

        Assert.Equal(15, p.Count);
        VanCada(p.Take(14).ToList(), 254.75, 1554.75, 100);
        Assert.Equal(1645.25, p[^1], 6);
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
        VanCada(p.Take(20).ToList(), 254.75, 4054.75, 200);
        Assert.Equal(4145.25, p[^1], 6);
    }

    /// <summary>Si sobra muy poco no quedan dos estribos casi juntos: los dos últimos espacios se igualan, sin pasar de 200.</summary>
    [Fact]
    public void SiSobraMuyPoco_LosDosUltimosEspaciosSeIgualan()
    {
        IReadOnlyList<double> p = DistribuidorEstribos.Posiciones(new Tramo("A-B", 0, 2119.5, TipoTramo.Luz), General with { LargoZona = 0 }, Estribo3);

        Assert.Equal(12, p.Count);
        VanCada(p.Take(10).ToList(), 54.75, 1854.75, 200);
        Assert.Equal(105, p[10] - p[9], 6);
        Assert.Equal(105, p[11] - p[10], 6);
    }

    [Fact]
    public void UnaLuzMenorQueDosPrimeros_UnSoloEstriboAlCentro()
    {
        var portico = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 500)]);

        Assert.Equal([250.0], Posiciones("A-B", p: portico));
    }

    private static IReadOnlyList<ZonaTramo> ZonasCompletas(string nombre, ConfigEstribos? c = null, Portico? p = null) =>
        DistribuidorEstribos.Zonas(TramoDe(p ?? Datos.PorticoMaqueta(), nombre), c ?? General, Estribo3);

    /// <summary>Solo dónde va cada zona; lo de su rótulo se prueba aparte.</summary>
    private static IReadOnlyList<ZonaTramo> Zonas(string nombre, ConfigEstribos? c = null, Portico? p = null) =>
        ZonasCompletas(nombre, c, p).Select(z => new ZonaTramo(z.Desde, z.Hasta, z.Junta)).ToList();

    /// <summary>
    /// El rótulo de cada zona (#100): la zona se queda con el estribo que comparte con el centro; la L va de la cara de
    /// la columna al último estribo de la zona (A-B: caras en 200 y 4200).
    /// </summary>
    [Fact]
    public void CadaZonaCuentaSusEstribosYSuLargoDesdeLaCara()
    {
        IReadOnlyList<ZonaTramo> z = ZonasCompletas("A-B");

        Assert.Equal((8, 8), (z[0].Cantidad, z[2].Cantidad));
        Assert.Equal((100, 100), (z[0].Separacion, z[2].Separacion));
        Assert.Equal(754.75, z[0].Largo!.Value, 6);
        Assert.Equal(754.75, z[2].Largo!.Value, 6);
        Assert.Null(z[1].Largo);
        Assert.Equal(General.SeparacionCentro, z[1].Separacion);
        Assert.Equal(Posiciones("A-B").Count, z.Sum(x => x.Cantidad));
    }

    /// <summary>Las zonas que pinta el alzado (#83): las de columna a la separación exacta y el centro, en mm.</summary>
    [Fact]
    public void LaLuzTieneSuZonaEnCadaColumnaYElCentro()
    {
        Assert.Equal([new ZonaTramo(254.75, 954.75, true), new ZonaTramo(954.75, 3445.25, false), new ZonaTramo(3445.25, 4145.25, true)],
            Zonas("A-B"));
    }

    [Fact]
    public void ElVoladizoEsUnaSolaZona()
    {
        Assert.Equal([new ZonaTramo(12554.75, 13945.25, true)], Zonas("Voladizo D"));
    }

    [Fact]
    public void UnaLuzCortaEsUnaSolaZona()
    {
        var portico = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 1900)]);

        Assert.Equal([new ZonaTramo(254.75, 1645.25, true)], Zonas("A-B", p: portico));
    }

    [Fact]
    public void SinZonaTodoEsCentro()
    {
        Assert.Equal([new ZonaTramo(254.75, 4145.25, false)], Zonas("A-B", General with { LargoZona = 0 }));
    }
}
