using CQEING.Vigas.Calculo.Estribos;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// El pórtico y la sección de la maqueta: 300 × 500, recubrimiento 40 y estribo #3 (9.5 mm). Por el eje, el cerrado
/// mide 300 − 80 − 9.5 = 210.5 de ancho y 410.5 de alto: perímetro 1242 y, con dos ganchos de 135° (113), 1468.
/// </summary>
public class MotorEstribosTests
{
    private static readonly ConfigEstribos General = new();

    internal static EntradaSeccion Seccion() =>
        new(Ancho: 300, Alto: 500, Recubrimientos.Iguales(40), DiametroEstribo: 3,
            Superior: [new GrupoBarras(5, 5)], Inferior: [new GrupoBarras(4, 5)], Reglas.Generales);

    private static ResultadoEstribos Calcular(ConfigEstribos? general = null, Dictionary<string, ConfigEstribos>? porTramo = null) =>
        MotorEstribos.Calcular(new EntradaEstribos(Datos.PorticoMaqueta(), Seccion(), general ?? General, porTramo));

    private static EstriboEnPortico Cerrado(ResultadoEstribos r, string tramo) => r.Estribos.Single(e => e.Id == "E" && e.Tramo == tramo);

    [Fact]
    public void ElCerradoDeLasEsquinasSaleSoloEnCadaTramo()
    {
        ResultadoEstribos r = Calcular();

        Assert.Equal(["A-B", "B-C", "C-D", "Voladizo D"], r.Estribos.Where(e => e.Id == "E").Select(e => e.Tramo));
        Assert.All(r.Estribos, e => Assert.Equal((TipoEstribo.Cerrado, 3, GanchoEstribo.De135), (e.Tipo, e.Diametro, e.Gancho)));
        Assert.Equal(94, r.Cantidad);
        Assert.Empty(r.Avisos);
    }

    [Fact]
    public void ElLargoDelCerradoEsElPerimetroMasDosGanchos()
    {
        Assert.All(Calcular().Estribos, e => Assert.Equal(1468, e.Largo, 6));
    }

    [Fact]
    public void ConGanchoDe90_UsaLaColaDe6DiametrosQueDibujaRevit()
    {
        ResultadoEstribos r = Calcular(General with { Gancho = GanchoEstribo.De90 });

        // #3: cola de 6 d (57) + arco de 90° por el eje con el doblado de estribo de 38 (π/2 × 23.75 = 37), no el
        // gancho de 90° de las barras (166, con cola de 12 d)
        Assert.Equal(1242 + 2 * 94, Cerrado(r, "A-B").Largo, 6);
    }

    [Fact]
    public void ConGanchoDe180_UsaElGancho180DeLasReglas()
    {
        ResultadoEstribos r = Calcular(General with { Gancho = GanchoEstribo.De180 });

        Assert.Equal(1242 + 2 * 142, Cerrado(r, "A-B").Largo, 6);
        Assert.Equal(GanchoEstribo.De180, Cerrado(r, "A-B").Gancho);
    }

    [Fact]
    public void ElAbiertoLlevaLaFormaElegidaYElCerradoVaEnC()
    {
        var enS = new EstriboAdicional(TipoEstribo.Abierto, [new PuntoBarra(Posicion.Superior, 1, 3), new PuntoBarra(Posicion.Inferior, 1, 3)],
            FormaGrapa.S);

        ResultadoEstribos r = Calcular(General with { Adicionales = [enS] });

        Assert.All(r.Estribos.Where(e => e.Id == "E1"), e => Assert.Equal(FormaGrapa.S, e.Forma));
        Assert.All(r.Estribos.Where(e => e.Id == "E"), e => Assert.Equal(FormaGrapa.C, e.Forma));
    }

    [Fact]
    public void LosKilosSonCantidadPorLargoPorPeso()
    {
        ResultadoEstribos r = Calcular();

        Assert.Equal(28 * 1.468 * 0.560, Cerrado(r, "A-B").Kg, 6);
        Assert.Equal(94 * 1.468 * 0.560, r.Kg, 6);
    }

    [Fact]
    public void EnLaSeccionElCerradoVaDelRecubrimientoHaciaAdentro()
    {
        Assert.Equal([new EstriboEnSeccion("E", TipoEstribo.Cerrado, 40, 260, 40, 460)], Calcular().EnSeccion);
    }

    [Fact]
    public void UnTramoConSuPropiaConfiguracion()
    {
        ResultadoEstribos r = Calcular(porTramo: new() { ["B-C"] = General with { SeparacionCentro = 150 } });

        Assert.Equal([28, 26, 28, 15], r.Estribos.Select(e => e.Posiciones.Count));
    }

    [Fact]
    public void UnTramoConOtroDiametro_SoloCambiaSuLargoYSusKilos()
    {
        ResultadoEstribos r = Calcular(porTramo: new() { ["A-B"] = General with { Diametro = 4 } });

        // #4 (12.7): 2 × (207.3 + 407.3) + 2 × 151.
        EstriboEnPortico ab = Cerrado(r, "A-B");
        Assert.Equal((4, 1531.2), (ab.Diametro, Math.Round(ab.Largo, 6)));
        Assert.Equal(28 * 1.5312 * 0.994, ab.Kg, 6);
        Assert.Equal(1468, Cerrado(r, "B-C").Largo, 6);
        // La sección sigue con #3: la barra de esquina queda a 40 + 9.5 + 7.95.
        Assert.Equal(57.45 + (19 - 7.95) * (1 - 1 / Math.Sqrt(2)), r.Seccion.Inferior.Filas[0][0].X, 6);
    }

    [Fact]
    public void UnaSeparacionEnCero_AvisaYEseTramoQuedaSinEstribos()
    {
        ResultadoEstribos r = Calcular(porTramo: new() { ["B-C"] = General with { SeparacionZona = 0 } });

        Assert.Equal([new Aviso(TipoAviso.EstriboValorNoValido, "B-C")], r.Avisos);
        Assert.DoesNotContain(r.Estribos, e => e.Tramo == "B-C");
        Assert.Equal(3, r.Estribos.Count);
    }

    [Fact]
    public void UnLargoDeZonaNegativoEnGeneral_AvisaConGeneral()
    {
        ResultadoEstribos r = Calcular(General with { LargoZona = -1 }, new() { ["B-C"] = General });

        Assert.Equal([new Aviso(TipoAviso.EstriboValorNoValido, "general")], r.Avisos);
        Assert.Equal(["B-C"], r.Estribos.Select(e => e.Tramo));
    }

    [Fact]
    public void UnDiametroSinReglas_Avisa()
    {
        ResultadoEstribos r = Calcular(General with { Diametro = 9 });

        Assert.Equal([new Aviso(TipoAviso.DiametroSinReglas, "#9")], r.Avisos);
        Assert.Empty(r.Estribos);
    }
}
