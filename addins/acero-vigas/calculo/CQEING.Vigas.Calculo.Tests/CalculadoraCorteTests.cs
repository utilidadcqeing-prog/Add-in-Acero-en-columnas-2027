using CQEING.Vigas.Calculo.Estribos;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Seccion;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// Dónde puede ir el corte 1-1, su vista tipo y qué bastones corta o proyecta (#91, decisiones 2 a 5 del plan
/// docs/planes/2026-10-17-corte-vigas-91.md). Los números salen del motor, que ya está probado aparte.
/// </summary>
public class CalculadoraCorteTests
{
    private static readonly ArmadoBastones ConBastones = new(new GrupoBarras(1, 5), new GrupoBarras(1, 5));

    private static ResultadoVigas Vigas(Portico p, ArmadoBastones? bastones = null) =>
        MotorVigas.Calcular(new EntradaVigas(p, TipoViga.Aerea, [new GrupoBarras(3, 5)], [new GrupoBarras(3, 5)], Reglas.Generales,
            Bastones: bastones ?? ConBastones));

    private static ResultadoEstribos Estribos(Portico p) =>
        MotorEstribos.Calcular(new EntradaEstribos(p, MotorEstribosTests.Seccion(), new ConfigEstribos()));

    private static CorteEnPunto En(OpcionesCorte o, Portico? portico = null)
    {
        Portico p = portico ?? Datos.PorticoMaqueta();
        return CalculadoraCorte.En(p, Vigas(p), Estribos(p), o);
    }

    private static IReadOnlyList<Intervalo> Permitidos() => CalculadoraCorte.Permitidos(Datos.PorticoMaqueta(), Vigas(Datos.PorticoMaqueta()));

    [Fact]
    public void LosPermitidosNoTocanTraslaposNiApoyos()
    {
        Portico p = Datos.PorticoMaqueta();
        ResultadoVigas rv = Vigas(p);
        IReadOnlyList<Intervalo> permitidos = CalculadoraCorte.Permitidos(p, rv);
        const int m = CalculadoraCorte.MargenMm;

        Assert.NotEmpty(permitidos);
        foreach (Intervalo i in permitidos)
        {
            Assert.All(rv.ZonasTraslapo, z => Assert.True(i.Hasta <= z.Desde - m + 1e-6 || i.Desde >= z.Hasta + m - 1e-6));
            Assert.All(p.Apoyos, a => Assert.True(i.Hasta <= a.CaraIzquierda - m + 1e-6 || i.Desde >= a.CaraDerecha + m - 1e-6));
            Assert.Contains(DistribuidorEstribos.Tramos(p), t => t.CaraIzquierda <= i.Desde && i.Hasta <= t.CaraDerecha);
        }
        Assert.All(permitidos.Zip(permitidos.Skip(1)), par => Assert.True(par.First.Hasta < par.Second.Desde));
    }

    [Fact]
    public void UnPuntoPermitidoNoSeMueve()
    {
        Assert.All(Permitidos(), i => Assert.Equal((i.Desde + i.Hasta) / 2, CalculadoraCorte.Ajustar((i.Desde + i.Hasta) / 2, Permitidos())));
    }

    [Fact]
    public void UnPuntoEnUnTraslapoSaltaAlBordeMasCercano()
    {
        (int desde, int hasta) = Vigas(Datos.PorticoMaqueta()).ZonasTraslapo[0];

        Assert.Equal(desde - CalculadoraCorte.MargenMm, CalculadoraCorte.Ajustar(desde + 0.3 * (hasta - desde), Permitidos()), 6);
        Assert.Equal(hasta + CalculadoraCorte.MargenMm, CalculadoraCorte.Ajustar(desde + 0.7 * (hasta - desde), Permitidos()), 6);
    }

    [Fact]
    public void EnUnEmpateGanaLaDerecha()
    {
        Assert.Equal(2000, CalculadoraCorte.Ajustar(1500, [new Intervalo(0, 1000), new Intervalo(2000, 3000)]));
    }

    [Fact]
    public void UnPuntoEnUnaColumnaSaltaALaCara()
    {
        // La columna B va de 4200 a 4600: 4300 está más cerca de su cara izquierda.
        Assert.Equal(4200 - CalculadoraCorte.MargenMm, CalculadoraCorte.Ajustar(4300, Permitidos()));
    }

    [Fact]
    public void UnCorteFueraDelPorticoSeAjusta()
    {
        CorteEnPunto c = En(new OpcionesCorte(X: 50_000));

        Assert.True(c.Ajustado);
        Assert.False(c.EsTipo);
        Assert.Equal(c.Permitidos[^1].Hasta, c.X);
    }

    [Fact]
    public void LaVistaTipoEsLaSeccionQueMasSeRepite()
    {
        // Dos luces de 7.60 m con bastones de 2 m solo arriba: la mayor parte del largo no tiene bastón.
        var p = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 8000), Datos.Columna("C", 16000)]);
        var arriba = new ArmadoBastones(new GrupoBarras(1, 5));

        CorteEnPunto c = CalculadoraCorte.En(p, Vigas(p, arriba), Estribos(p), new OpcionesCorte());

        Assert.True(c.EsTipo);
        Assert.False(c.Ajustado);
        Assert.Empty(c.Cortados);
        Assert.Contains(c.Permitidos, i => i.Desde <= c.X && c.X <= i.Hasta);
        Assert.All(p.Apoyos, a => Assert.True(Math.Abs(c.X - a.PosicionEje) > 1000));
        Assert.Equal(0, c.X % 10);
    }

    [Fact]
    public void LosBastonesQuePasanSeCortanYLosCercanosSeProyectan()
    {
        Baston b = Vigas(Datos.PorticoMaqueta()).Bastones.First(x => x.Capa == Posicion.Inferior);

        CorteEnPunto antes = En(new OpcionesCorte(X: b.Desde - 50));
        CorteEnPunto alOtroLado = En(new OpcionesCorte(X: b.Desde - 50, Mirando: MiraCorte.Izquierda));
        CorteEnPunto adentro = En(new OpcionesCorte(X: b.Desde + 50));

        Assert.False(antes.Ajustado);
        Assert.DoesNotContain(antes.Cortados, x => x.Id == b.Id);
        Assert.Contains(antes.Proyectados, x => x.Id == b.Id);
        Assert.DoesNotContain(alOtroLado.Cortados.Concat(alOtroLado.Proyectados), x => x.Id == b.Id);
        Assert.False(adentro.Ajustado);
        Assert.Contains(adentro.Cortados, x => x.Id == b.Id);
        Assert.DoesNotContain(adentro.Proyectados, x => x.Id == b.Id);
    }

    [Fact]
    public void ProfundidadNegativaNoProyecta()
    {
        Baston b = Vigas(Datos.PorticoMaqueta()).Bastones.First(x => x.Capa == Posicion.Inferior);

        Assert.Empty(En(new OpcionesCorte(X: b.Desde - 50, Profundidad: -100)).Proyectados);
        Assert.Empty(En(new OpcionesCorte(X: b.Desde - 50, Profundidad: 0)).Proyectados);
    }

    [Fact]
    public void ElCorteSabeSuTramo()
    {
        Assert.Equal("A-B", En(new OpcionesCorte(X: 2200)).Tramo);
        Assert.Equal("Voladizo D", En(new OpcionesCorte(X: 13000)).Tramo);
    }

    [Fact]
    public void SinPuntosPermitidosAvisaYNoSeMueve()
    {
        // Luz libre de 15 mm: con los márgenes no queda ningún punto.
        var p = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 415)]);
        var rv = new ResultadoVigas(null, null, Posicion.Inferior, null, null, Cara.Inicio, Cara.Inicio, [], []);
        var re = new ResultadoEstribos([], null!, [], []);

        CorteEnPunto c = CalculadoraCorte.En(p, rv, re, new OpcionesCorte(X: 300));

        Assert.True(c.SinLugar);
        Assert.False(c.Ajustado);
        Assert.Equal(300, c.X);
        Assert.Empty(c.Permitidos);
    }
}
