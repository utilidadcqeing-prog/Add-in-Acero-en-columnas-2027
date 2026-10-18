using System.Diagnostics;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Nucleo;
using CQEING.Vigas.Calculo.Vigas;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// El resolvedor de capas tiene que ser rápido con pórticos largos y dar exactamente lo mismo que la búsqueda completa
/// (tarea 3 del plan del tablero #83).
/// </summary>
public class ResolvedorCapaRapidoTests
{
    private static Portico Luces(int luces, int largo, int voladizo = 0) =>
        new(Enumerable.Range(0, luces + 1).Select(i => Datos.Columna(((char)('A' + i)).ToString(), i * largo)).ToList(), VoladizoFinal: voladizo);

    private static long Milisegundos(Portico p)
    {
        var e = new EntradaVigas(p, TipoViga.Aerea, [new GrupoBarras(3, 5)], [new GrupoBarras(3, 5)], Reglas.Generales);
        var reloj = Stopwatch.StartNew();
        MotorVigas.Calcular(e);
        return reloj.ElapsedMilliseconds;
    }

    [Fact]
    public void OchoLucesDeCincoMetrosSeResuelvenRapido()
    {
        Assert.InRange(Milisegundos(Luces(8, 5000)), 0, 3000);
    }

    [Fact]
    public void DiezLucesSeResuelvenRapido()
    {
        Assert.InRange(Milisegundos(Luces(10, 5000)), 0, 5000);
    }

    public static TheoryData<int, int, int, Referencia, int, int> Casos()
    {
        var casos = new TheoryData<int, int, int, Referencia, int, int>();
        int[] largos = [3000, 4400, 5000, 6500, 8000];
        foreach (int luces in new[] { 1, 2, 3, 4 })
            foreach (int largo in largos)
                foreach (int voladizo in largo <= 5000 ? new[] { 0, 1500 } : [0])
                    foreach (Referencia referencia in Enum.GetValues<Referencia>())
                        foreach (int varilla in new[] { 6000, 12000 })
                            casos.Add(luces, largo, voladizo, referencia, varilla, 5);
        foreach (Referencia referencia in Enum.GetValues<Referencia>())
        {
            casos.Add(5, 5000, 1500, referencia, 6000, 5);
            casos.Add(3, 5000, 0, referencia, 6000, 8);
            casos.Add(3, 8000, 0, referencia, 12000, 8);
        }
        return casos;
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public void DaLoMismoQueLaBusquedaCompleta(int luces, int largo, int voladizo, Referencia referencia, int varilla, int diametro)
    {
        ReglaDiametro regla = Reglas.Generales.PorDiametro[diametro];
        var g = new GeometriaPortico(Luces(luces, largo, voladizo), 50);
        var p = new ParametrosCapa(referencia, regla.Traslapo!.Value, regla.Gancho90.B, 500, varilla, 12000);

        CapaResuelta? esperada = BusquedaCompleta(g, p);
        CapaResuelta? obtenida = ResolvedorCapa.Resolver(g, p);

        Assert.Equal(Firma(esperada), Firma(obtenida));
    }

    private static string Firma(CapaResuelta? c) => c is null ? "sin solución"
        : string.Join(" ", c.Solucion.Barras.Select(b => $"[{b.Desde},{b.Hasta}:{b.Largo}]"))
            + " | " + string.Join(" ", c.Solucion.Traslapos.Select(t => $"({t.Desde},{t.Hasta})"))
            + " | " + string.Join(",", c.Luces.Select(l => l.Nombre));

    /// <summary>El algoritmo de antes de la tarea 3: todas las combinaciones, hasta una cantidad sin varillas de 12 m.</summary>
    private static CapaResuelta? BusquedaCompleta(GeometriaPortico g, ParametrosCapa p)
    {
        CapaResuelta? mejor = null;
        for (int cantidad = 0; cantidad <= g.Luces.Count; cantidad++)
        {
            foreach (IReadOnlyList<Luz> luces in Combinaciones(g.Luces, cantidad))
            {
                var problema = new ProblemaLinea(g.InicioBarras, g.FinBarras, p.Gancho, p.Gancho,
                    luces.Select(l => g.Ranura(l, p.Referencia) with { DesvioMaximo = p.Modulo }).ToList(),
                    p.TraslapoMinimo, p.Modulo, p.Varilla, Math.Max(p.Varilla, p.VarillaLarga));
                SolucionLinea? s = ResolvedorLinea.Resolver(problema);
                if (s is null)
                    continue;
                var capa = new CapaResuelta(s, luces);
                if (mejor is null || (s.BarrasDe12, s.Traslapos.Count, s.Acero, s.DesvioTotal)
                        .CompareTo((mejor.Solucion.BarrasDe12, mejor.Solucion.Traslapos.Count, mejor.Solucion.Acero, mejor.Solucion.DesvioTotal)) < 0)
                    mejor = capa;
            }
            if (mejor is { Solucion.BarrasDe12: 0 })
                break;
        }
        return mejor;
    }

    private static IEnumerable<IReadOnlyList<Luz>> Combinaciones(IReadOnlyList<Luz> luces, int cantidad, int desde = 0)
    {
        if (cantidad == 0)
        {
            yield return [];
            yield break;
        }
        for (int i = desde; i <= luces.Count - cantidad; i++)
            foreach (IReadOnlyList<Luz> resto in Combinaciones(luces, cantidad - 1, i + 1))
                yield return [luces[i], .. resto];
    }
}
