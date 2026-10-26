using CQEING.Vigas.Calculo.Estribos;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// La sección de la maqueta (300 × 500, recubrimiento 40, estribo #3) con arriba 5 #5 (x 60.686, 103.725, 150, 196.275
/// y 239.314) y abajo 4 #5 (x 60.686, 119.15, 180.85 y 239.314): las de esquina se apoyan en la curva del estribo y
/// entran 3.236 (21-oct). Entre dos #5 deben quedar 15.9 + 25 = 40.9 entre centros.
/// </summary>
public class AlineadorEstribosTests
{
    private static readonly ConfigEstribos General = new();

    private static PuntoBarra S(int numero, int fila = 1) => new(Posicion.Superior, fila, numero);
    private static PuntoBarra I(int numero, int fila = 1) => new(Posicion.Inferior, fila, numero);
    private static EstriboAdicional Abierto(params PuntoBarra[] b) => new(TipoEstribo.Abierto, b);
    private static EstriboAdicional Cerrado(params PuntoBarra[] b) => new(TipoEstribo.Cerrado, b);

    private static EntradaSeccion Seccion(int arriba = 5, GrupoBarras? abajo = null) =>
        MotorEstribosTests.Seccion() with { Superior = [new GrupoBarras(arriba, 5)], Inferior = [abajo ?? new GrupoBarras(4, 5)] };

    private static ResultadoEstribos Calcular(EntradaSeccion seccion, params EstriboAdicional[] adicionales) =>
        MotorEstribos.Calcular(new EntradaEstribos(Datos.PorticoMaqueta(), seccion, General with { Adicionales = adicionales }));

    private static ResultadoEstribos Calcular(params EstriboAdicional[] adicionales) => Calcular(Seccion(), adicionales);

    /// <summary>La x de las barras de la fila 1, redondeada a 3 decimales.</summary>
    private static double[] Xs(ResultadoEstribos r, Posicion cara) =>
        (cara == Posicion.Superior ? r.Seccion.Superior : r.Seccion.Inferior).Filas[0].Select(b => Math.Round(b.X, 3)).ToArray();

    private static EstriboEnSeccion Figura(ResultadoEstribos r, string id)
    {
        EstriboEnSeccion f = r.EnSeccion.Single(e => e.Id == id);
        return f with { Izquierda = Math.Round(f.Izquierda, 3), Derecha = Math.Round(f.Derecha, 3) };
    }

    private static readonly double[] ArribaSinMover = [60.686, 103.725, 150, 196.275, 239.314];
    private static readonly double[] AbajoSinMover = [60.686, 119.15, 180.85, 239.314];

    private static void NoSeAgrega(ResultadoEstribos r, TipoAviso tipo, string id = "E1")
    {
        Assert.Contains(new Aviso(tipo, id), r.Avisos);
        Assert.DoesNotContain(r.Estribos, e => e.Id == id);
        Assert.DoesNotContain(r.EnSeccion, e => e.Id == id);
    }

    [Fact]
    public void AbiertoMandaLaDeArriba_LaDeAbajoSeMueve()
    {
        ResultadoEstribos r = Calcular(Abierto(S(2), I(2)));

        Assert.Equal([60.686, 103.725, 180.85, 239.314], Xs(r, Posicion.Inferior));
        Assert.Equal(ArribaSinMover, Xs(r, Posicion.Superior));
        Assert.Equal(new EstriboEnSeccion("E1", TipoEstribo.Abierto, 103.725, 103.725, 40, 460), Figura(r, "E1"));
        EstriboEnPortico ab = r.Estribos.Single(e => e.Id == "E1" && e.Tramo == "A-B");
        Assert.Equal((TipoEstribo.Abierto, 28, 588.92), (ab.Tipo, ab.Posiciones.Count, Math.Round(ab.Largo, 2)));
        Assert.Equal(28 * 0.588919 * 0.560, ab.Kg, 3);
        Assert.Equal(4, r.Estribos.Count(e => e.Id == "E1"));
        Assert.Empty(r.Avisos);
    }

    [Fact]
    public void CerradoEnCadaVerticalMandaLaPrimera()
    {
        ResultadoEstribos r = Calcular(Cerrado(S(2), S(4), I(2), I(3)));

        Assert.Equal([60.686, 103.725, 196.275, 239.314], Xs(r, Posicion.Inferior));
        Assert.Equal(ArribaSinMover, Xs(r, Posicion.Superior));
        Assert.Equal(new EstriboEnSeccion("E1", TipoEstribo.Cerrado, 86.275, 213.725, 40, 460), Figura(r, "E1"));
        // Por el eje 117.95 × 410.5, como lo cuenta Revit: lados menos un radio (23.75) en cada punta, tres esquinas y dos ganchos de 135°.
        Assert.Equal(1204.74, r.Estribos.First(e => e.Id == "E1").Largo, 2);
    }

    [Fact]
    public void AbiertoSiSeEligePrimeroLaDeAbajo_MandaLaDeAbajo()
    {
        ResultadoEstribos r = Calcular(Seccion(arriba: 3), Abierto(I(2), S(2)));

        Assert.Equal([60.686, 119.15, 239.314], Xs(r, Posicion.Superior));
        Assert.Equal(AbajoSinMover, Xs(r, Posicion.Inferior));
    }

    [Fact]
    public void CerradoAlternado_SiNiCorriendoLaVecinaCabe_NoSePuedeAlinear()
    {
        ResultadoEstribos r = Calcular(Cerrado(I(2), S(2), S(4), I(3)));

        NoSeAgrega(r, TipoAviso.NoSePuedeAlinear);
        Assert.Equal(ArribaSinMover, Xs(r, Posicion.Superior));
        Assert.Equal(AbajoSinMover, Xs(r, Posicion.Inferior));
    }

    [Fact]
    public void SiLaVecinaNoDa_SeCorreParaRespetarLaDistancia()
    {
        ResultadoEstribos r = Calcular(Abierto(S(3), I(3)));

        Assert.Equal([60.686, 105.343, 150, 239.314], Xs(r, Posicion.Inferior));
        Assert.Empty(r.Avisos);
    }

    [Fact]
    public void CerradoConLasVecinasCorridas_ElCasoDeLaCaptura()
    {
        ResultadoEstribos r = Calcular(Cerrado(S(2), S(3), I(2), I(3)));

        Assert.Equal([60.686, 103.725, 150, 239.314], Xs(r, Posicion.Inferior));
        Assert.Equal(ArribaSinMover, Xs(r, Posicion.Superior));
        Assert.Empty(r.Avisos);
    }

    [Fact]
    public void UnAbiertoSobreUnaRamaDelCerrado_EsElObligatorio()
    {
        NoSeAgrega(Calcular(Abierto(S(1), I(1))), TipoAviso.IgualAlCerrado);
    }

    [Fact]
    public void UnCerradoConLasCuatroEsquinas_EsElObligatorio()
    {
        NoSeAgrega(Calcular(Cerrado(S(1), I(1), S(5), I(4))), TipoAviso.IgualAlCerrado);
    }

    [Fact]
    public void ElAdicionalVaSoloEnSuTramo()
    {
        var porTramo = new Dictionary<string, ConfigEstribos> { ["A-B"] = General with { Adicionales = [Abierto(S(2), I(2))] } };

        ResultadoEstribos r = MotorEstribos.Calcular(new EntradaEstribos(Datos.PorticoMaqueta(), Seccion(), General, porTramo));

        EstriboEnPortico ea1 = Assert.Single(r.Estribos, e => e.Id == "E1");
        Assert.Equal(("A-B", 28), (ea1.Tramo, ea1.Posiciones.Count));
        Assert.Equal([60.686, 103.725, 180.85, 239.314], Xs(r, Posicion.Inferior));
    }

    [Fact]
    public void UnTramoQueCopiaLaGeneral_NoRepiteSusAdicionales()
    {
        ConfigEstribos general = General with { Adicionales = [Abierto(S(2), I(2))] };
        var porTramo = new Dictionary<string, ConfigEstribos> { ["B-C"] = general with { SeparacionCentro = 150 } };

        ResultadoEstribos r = MotorEstribos.Calcular(new EntradaEstribos(Datos.PorticoMaqueta(), Seccion(), general, porTramo));

        Assert.Equal(4, r.Estribos.Count(e => e.Id == "E1"));
        Assert.DoesNotContain(r.Estribos, e => e.Id == "E2");
        Assert.Equal(26, r.Estribos.Single(e => e.Id == "E1" && e.Tramo == "B-C").Posiciones.Count);
    }

    /// <summary>
    /// Abajo 8 #5: la fila 2 lleva 3 (57.45, 150 y 242.55, a 73.35 + 3.236 de la cara: apoya en la esquina, que bajó). Como en la maqueta, las sueltas solo se
    /// reparten entre dos fijas: en la punta de la fila 2 no se corren y basta con que quede la distancia libre.
    /// </summary>
    [Fact]
    public void EnLaFila2_LasSueltasDeLaPuntaNoSeCorren()
    {
        ResultadoEstribos r = Calcular(Seccion(abajo: new GrupoBarras(8, 5)), Abierto(S(4), I(1, fila: 2)));

        Assert.Empty(r.Avisos);
        // La fila conserva el orden del reparto (la 1 sigue siendo la misma barra), aunque ahora quede a la derecha de la 2.
        Assert.Equal([196.275, 150, 242.55], r.Seccion.Inferior.Filas[1].Select(b => Math.Round(b.X, 3)));
        // La rama va pegada a las barras que abraza: de la de la fila 2 (76.586 - 7.95 - 9.5) a la de arriba.
        EstriboEnSeccion f = Figura(r, "E1");
        Assert.Equal(new EstriboEnSeccion("E1", TipoEstribo.Abierto, 196.275, 196.275, 59.136, 460), f with { Abajo = Math.Round(f.Abajo, 3) });
    }

    [Fact]
    public void UnAdicionalDeUnTramoSinEstribos_NoMueveBarras()
    {
        var porTramo = new Dictionary<string, ConfigEstribos>
        {
            ["B-C"] = General with { SeparacionZona = 0, Adicionales = [Abierto(S(2), I(2))] },
        };

        ResultadoEstribos r = MotorEstribos.Calcular(new EntradaEstribos(Datos.PorticoMaqueta(), Seccion(), General, porTramo));

        Assert.Equal([new Aviso(TipoAviso.EstriboValorNoValido, "B-C")], r.Avisos);
        Assert.Equal(AbajoSinMover, Xs(r, Posicion.Inferior));
        Assert.DoesNotContain(r.EnSeccion, e => e.Id == "E1");
    }

    [Fact]
    public void SinLasBarrasQuePide_EstriboIncompleto()
    {
        NoSeAgrega(Calcular(Abierto(S(2), S(3))), TipoAviso.EstriboIncompleto);
    }

    [Fact]
    public void LaMismaBarraDosVeces_EstriboIncompleto()
    {
        NoSeAgrega(Calcular(Abierto(S(2), S(2))), TipoAviso.EstriboIncompleto);
    }

    [Fact]
    public void UnaBarraQueNoExiste_BarraNoExiste()
    {
        NoSeAgrega(Calcular(Abierto(S(2), I(9))), TipoAviso.BarraNoExiste);
        NoSeAgrega(Calcular(Abierto(S(2), I(1, fila: 2))), TipoAviso.BarraNoExiste);
    }

    [Fact]
    public void NoSeMueveUnaEsquina()
    {
        ResultadoEstribos r = Calcular(Abierto(S(2), I(1)));

        NoSeAgrega(r, TipoAviso.MueveUnaEsquina);
        Assert.Equal(60.686, Xs(r, Posicion.Inferior)[0]);
    }

    [Fact]
    public void SiNoQuedaLaDistanciaLibre_NoSePuedeAlinear()
    {
        ResultadoEstribos r = Calcular(Seccion(abajo: new GrupoBarras(3, 8)), Abierto(S(2), I(2)));

        NoSeAgrega(r, TipoAviso.NoSePuedeAlinear);
        Assert.Equal([64.045, 150, 235.955], Xs(r, Posicion.Inferior));
    }

    [Fact]
    public void DosAdicionalesQueMuevenLaMismaBarra_ElSegundoNoSePuede()
    {
        ResultadoEstribos r = Calcular(Abierto(S(2), I(2)), Abierto(S(4), I(2)));

        NoSeAgrega(r, TipoAviso.NoSePuedeAlinear, "E2");
        Assert.Contains(r.EnSeccion, e => e.Id == "E1");
        Assert.Equal([60.686, 103.725, 180.85, 239.314], Xs(r, Posicion.Inferior));
    }

    [Fact]
    public void ConLaSeccionConError_NoHayAdicionalesPeroSiElCerrado()
    {
        ResultadoEstribos r = Calcular(Seccion(abajo: new GrupoBarras(1, 5)), Abierto(S(2), I(1)));

        NoSeAgrega(r, TipoAviso.NoSePuedeAlinear);
        Assert.Equal(4, r.Estribos.Count(e => e.Id == "E"));
        Assert.Contains(r.EnSeccion, e => e.Id == "E");
    }

    [Fact]
    public void AbiertoEnUnaRamaDeOtroAdicional_NoSeAgrega()
    {
        // Hallazgo E14 (Revit, 2026-10-19): un cerrado en las barras 1 y 2 y un abierto en la 2 abrazaban la misma barra.
        ResultadoEstribos r = Calcular(Cerrado(S(1), S(2), I(1), I(2)), Abierto(S(2), I(2)));

        NoSeAgrega(r, TipoAviso.RepiteOtroAdicional, "E2");
        Assert.Contains(r.EnSeccion, e => e.Id == "E1");
    }

    [Fact]
    public void CerradoIgualAOtroAdicional_NoSeAgrega()
    {
        ResultadoEstribos r = Calcular(Cerrado(S(2), S(3), I(2), I(3)), Cerrado(I(3), S(2), S(3), I(2)));

        NoSeAgrega(r, TipoAviso.RepiteOtroAdicional, "E2");
    }

    [Fact]
    public void AbiertoEnOtraBarra_SiSeAgrega()
    {
        ResultadoEstribos r = Calcular(Cerrado(S(1), S(2), I(1), I(2)), Abierto(S(3), I(3)));

        Assert.Contains(r.EnSeccion, e => e.Id == "E2");
        Assert.DoesNotContain(r.Avisos, a => a.Tipo == TipoAviso.RepiteOtroAdicional);
    }
}
