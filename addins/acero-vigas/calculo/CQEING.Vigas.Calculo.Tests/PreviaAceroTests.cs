using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// La vista previa 3D del acero (el botón "3D"). Pórtico a lo largo de +X, viga de 300 × 500 desde Z 3000: un punto del
/// corte (xc, yc) en x queda en el modelo en (x, 150 − xc, 3000 + yc).
/// </summary>
public class PreviaAceroTests
{
    private static readonly UbicacionPortico U = new(1, 0, 0, 0, 3000, 3500, 300, 500, [new("101", 0, 9000)]);

    private static readonly Dictionary<int, DobladoBarra> Doblados = new() { [5] = new(16, 64, 40), [3] = new(10, 40, 40) };

    private static IReadOnlyList<Trazo> Trazos(IReadOnlyList<PiezaLongitudinal>? barras = null, IReadOnlyList<GrupoEstribos>? estribos = null) =>
        PreviaAcero.Trazos(new PlanAcero("P", barras ?? [], [], [], estribos ?? [], []), U, Reglas.Generales, Doblados);

    private static PiezaLongitudinal Barra(TipoGancho inicio = TipoGancho.Sin, TipoGancho fin = TipoGancho.Sin) =>
        new("S1", Posicion.Superior, 5, 60, 440, 1000, 5000, inicio, fin, "101", 4000);

    private static (double Xc, double Yc) Corte(Punto3 p) => (150 - p.Y, p.Z - 3000);

    [Fact]
    public void UnaBarraSinGanchosEsSuParteRecta()
    {
        Trazo t = Assert.Single(Trazos([Barra()]));

        Assert.Equal([new Punto3(1000, 90, 3440), new Punto3(5000, 90, 3440)], t.Puntos);
        Assert.Equal((5, 16.0), (t.Numero, t.Diametro));
    }

    [Fact]
    public void ElGanchoDeArribaDoblaHaciaAbajoConElRadioYLaColaDelTipo()
    {
        // radio 64 / 2 + 16 / 2 = 40; cola = la extensión del gancho propio con B = 278 del cuadro
        double cola = GanchoPropio.Extension(278, 16, 64, 90);

        Trazo t = Assert.Single(Trazos([Barra(inicio: TipoGancho.De90)]));

        Assert.Equal(960, t.Puntos[0].X, 6);
        Assert.Equal(3440 - 40 - cola, t.Puntos[0].Z, 6);
        Assert.Contains(t.Puntos, p => Math.Abs(p.X - 960) < 1e-6 && Math.Abs(p.Z - 3400) < 1e-6);
        Assert.Equal(new Punto3(5000, 90, 3440), t.Puntos[^1]);
    }

    [Fact]
    public void ElGanchoDeAbajoDoblaHaciaArriba()
    {
        var abajo = Barra(fin: TipoGancho.De90) with { Capa = Posicion.Inferior, YCorte = 60 };

        Trazo t = Assert.Single(Trazos([abajo]));

        Assert.Equal(5040, t.Puntos[^1].X, 6);
        Assert.True(t.Puntos[^1].Z > 3060 + 40);
    }

    [Fact]
    public void CadaEstriboDeLaCorridaEsUnLazoConSusColasDentroDelNucleo()
    {
        // eje del estribo: 45 a 255 de lado y 45 a 455 de alto
        var g = new GrupoEstribos("E", "1-2", TipoEstribo.Cerrado, 3, GanchoEstribo.De135, 40, 260, 40, 460, new Corrida(500, 3, 100), "101");

        IReadOnlyList<Trazo> t = Trazos(estribos: [g]);

        Assert.Equal([500.0, 600.0, 700.0], t.Select(x => x.Puntos[0].X));
        Assert.All(t, x => Assert.All(x.Puntos, p => Assert.Equal(x.Puntos[0].X, p.X, 6)));
        List<(double Xc, double Yc)> lazo = t[0].Puntos.Select(Corte).ToList();
        Assert.All(lazo, p => Assert.InRange(p.Xc, 45 - 1e-6, 255 + 1e-6));
        Assert.All(lazo, p => Assert.InRange(p.Yc, 45 - 1e-6, 455 + 1e-6));
        // las dos colas terminan adentro, abajo a la derecha de la esquina de los ganchos
        Assert.All(new[] { lazo[0], lazo[^1] }, p => Assert.True(p.Xc > 70 && p.Yc < 430, $"cola en {p}"));
        Assert.Contains(lazo, p => Math.Abs(p.Xc - 255) < 1e-6 && Math.Abs(p.Yc - 45 - 25) < 1e-6);
    }

    [Fact]
    public void ElAbiertoVaAlLadoDeSusBarrasYLasAbraza()
    {
        // barras en X 200 (a la derecha del centro): la rama va a su izquierda, a un radio (20 + 5) de su eje
        var g = new GrupoEstribos("E1", "1-2", TipoEstribo.Abierto, 3, GanchoEstribo.De135, 200, 200, 40, 460, new Corrida(500, 1, 0), "101",
            new Rama(200, 440, 60, 16));

        List<(double Xc, double Yc)> lazo = Assert.Single(Trazos(estribos: [g])).Puntos.Select(Corte).ToList();

        Assert.Contains(lazo, p => Math.Abs(p.Xc - 175) < 1e-6 && Math.Abs(p.Yc - 440) < 1e-6);
        Assert.Contains(lazo, p => Math.Abs(p.Xc - 175) < 1e-6 && Math.Abs(p.Yc - 60) < 1e-6);
        Assert.Contains(lazo, p => Math.Abs(p.Xc - 200) < 1e-6 && Math.Abs(p.Yc - 465) < 1e-6);
        Assert.Contains(lazo, p => Math.Abs(p.Xc - 200) < 1e-6 && Math.Abs(p.Yc - 35) < 1e-6);
        // las colas salen del lado de las barras, como las deja Revit (séptima prueba)
        Assert.True(lazo[0].Xc > 200 && lazo[^1].Xc > 200);
    }

    [Fact]
    public void SinDobladosDelModeloUsaElDiametroDeLasReglas()
    {
        Trazo t = Assert.Single(PreviaAcero.Trazos(new PlanAcero("P", [Barra()], [], [], [], []), U, Reglas.Generales));

        Assert.Equal(Reglas.Generales.PorDiametro[5].Diametro, t.Diametro);
    }
}
