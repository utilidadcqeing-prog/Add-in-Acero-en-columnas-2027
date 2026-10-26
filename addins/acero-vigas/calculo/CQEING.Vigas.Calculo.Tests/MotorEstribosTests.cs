using CQEING.Vigas.Calculo.Estribos;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// El pórtico y la sección de la maqueta: 300 × 500, recubrimiento 40 y estribo #3 (9.5 mm). Por el eje, el cerrado
/// mide 300 − 80 − 9.5 = 210.5 de ancho y 410.5 de alto. Como lo cuenta Revit, con radio de doblado 23.75 por el eje:
/// lados de 163 y 363, tres esquinas de 37.31 y dos ganchos de 135° (arco de 55.96 y cola de 57), 1389.84.
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

    /// <summary>
    /// La Vigueta T1 del plano de referencia (decisión 8 del plan de #938): 10 × 35, recubrimiento 3, 1 #4 arriba y abajo,
    /// y en vez del cerrado una grapa en S #3 que abraza las dos barras.
    /// </summary>
    internal static EntradaSeccion VigaT1() =>
        new(Ancho: 100, Alto: 350, Recubrimientos.Iguales(30), DiametroEstribo: 3,
            Superior: [new GrupoBarras(1, 4)], Inferior: [new GrupoBarras(1, 4)], Reglas.Generales, PermiteUnaBarra: true);

    private static ResultadoEstribos ConGrapa(ConfigEstribos? general = null) =>
        MotorEstribos.Calcular(new EntradaEstribos(Datos.PorticoMaqueta(), VigaT1(), general ?? General, Principal: TipoEstribo.Abierto));

    [Fact]
    public void EnUnaVigueta_ElEstriboEsUnaGrapaEnS()
    {
        ResultadoEstribos r = ConGrapa();

        Assert.NotEmpty(r.Estribos);
        Assert.All(r.Estribos.Where(e => e.Id == "E"), e => Assert.Equal((TipoEstribo.Abierto, FormaGrapa.S), (e.Tipo, e.Forma)));
    }

    [Fact]
    public void LaGrapaDeLaViguetaPuedeIrEnC()
    {
        ResultadoEstribos r = ConGrapa(General with { Forma = FormaGrapa.C });

        Assert.All(r.Estribos.Where(e => e.Id == "E"), e => Assert.Equal((TipoEstribo.Abierto, FormaGrapa.C), (e.Tipo, e.Forma)));
        // en C la rama es vertical: 233 entre dobleces, más dos ganchos de 135° (112.96); medido en Revit, 458.9
        Assert.All(r.Estribos.Where(e => e.Id == "E"), e => Assert.Equal(458.92, e.Largo, 2));
    }

    [Fact]
    public void LaGrapaVaPorElCentro_DeRecubrimientoARecubrimiento()
    {
        EstriboEnSeccion e = ConGrapa().EnSeccion.Single(f => f.Id == "E");

        Assert.Equal((TipoEstribo.Abierto, 50.0, 50.0, 30.0, 320.0), (e.Tipo, e.Izquierda, e.Derecha, e.Abajo, e.Arriba));
    }

    [Fact]
    public void LaGrapaEsUnaRamaConDosGanchos()
    {
        // En S la rama es la tangente entre los dos dobleces, que quedan a 233 y con los centros a un lado y al otro de
        // ella: √(233² − (2 · 23.75)²) = 228.11, más dos ganchos de 135° del #3 (112.96). Medido en Revit, 454.03.
        Assert.All(ConGrapa().Estribos.Where(e => e.Id == "E"), e => Assert.Equal(454.03, e.Largo, 2));
    }

    /// <summary>La viga 388501 (25 × 35, recubrimiento 4, estribo #3) del modelo de prueba, medida en Revit el 26-oct.</summary>
    private static ResultadoEstribos Viga25x35(GanchoEstribo gancho, params EstriboAdicional[] adicionales) =>
        MotorEstribos.Calcular(new EntradaEstribos(Datos.PorticoMaqueta(),
            new EntradaSeccion(Ancho: 250, Alto: 350, Recubrimientos.Iguales(40), DiametroEstribo: 3,
                Superior: [new GrupoBarras(3, 5)], Inferior: [new GrupoBarras(3, 5)], Reglas.Generales),
            General with { Gancho = gancho, Adicionales = adicionales }));

    [Theory]
    [InlineData(GanchoEstribo.De90, 952.53)]
    [InlineData(GanchoEstribo.De135, 989.84)]
    [InlineData(GanchoEstribo.De180, 1043.14)]
    public void ElCerradoMideLoMismoQueEnRevit(GanchoEstribo gancho, double revit)
    {
        Assert.Equal(revit, Cerrado(Viga25x35(gancho), "A-B").Largo, 2);
    }

    [Fact]
    public void LaGrapaEnCMideLoMismoQueEnRevit()
    {
        var grapa = new EstriboAdicional(TipoEstribo.Abierto, [new PuntoBarra(Posicion.Superior, 1, 2), new PuntoBarra(Posicion.Inferior, 1, 2)]);

        Assert.Equal(401.61, Viga25x35(GanchoEstribo.De90, grapa).Estribos.First(e => e.Id == "E1").Largo, 2);
    }

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
        Assert.All(Calcular().Estribos, e => Assert.Equal(1389.84, e.Largo, 2));
    }

    [Fact]
    public void ConGanchoDe90_UsaLaColaDe6DiametrosQueDibujaRevit()
    {
        ResultadoEstribos r = Calcular(General with { Gancho = GanchoEstribo.De90 });

        // #3: cola de 6 d (57) + arco de 90° por el eje con el doblado de estribo de 38 (π/2 × 23.75 = 37), no el
        // gancho de 90° de las barras (166, con cola de 12 d)
        Assert.Equal(1352.53, Cerrado(r, "A-B").Largo, 2);
    }

    [Fact]
    public void ConGanchoDe180_UsaElGancho180DeLasReglas()
    {
        ResultadoEstribos r = Calcular(General with { Gancho = GanchoEstribo.De180 });

        Assert.Equal(1443.14, Cerrado(r, "A-B").Largo, 2);
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

        Assert.Equal(28 * 1.389838 * 0.560, Cerrado(r, "A-B").Kg, 4);
        Assert.Equal(94 * 1.389838 * 0.560, r.Kg, 4);
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
        Assert.Equal((4, 1426.58), (ab.Diametro, Math.Round(ab.Largo, 2)));
        Assert.Equal(28 * 1.426579 * 0.994, ab.Kg, 3);
        Assert.Equal(1389.84, Cerrado(r, "B-C").Largo, 2);
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
