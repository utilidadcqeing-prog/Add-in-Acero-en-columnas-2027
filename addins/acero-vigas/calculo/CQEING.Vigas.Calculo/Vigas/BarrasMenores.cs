using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Nucleo;

namespace CQEING.Vigas.Calculo.Vigas;

/// <summary>
/// Las barras de los números menores de una capa (Santiago, 29-oct; plan docs/planes/2026-10-29-traslapo-por-diametro.md).
/// La capa se resuelve con el mayor, como la pinta el tablero. Cada número menor se resuelve después con su traslapo
/// mínimo y su gancho, con un traslapo por cada uno del mayor: en su misma luz y lo más cerca posible de él (a lo sumo un
/// módulo), así también sigue los traslapos corridos o alargados a mano. Su largo queda en múltiplo del módulo.
/// </summary>
internal static class BarrasMenores
{
    /// <summary>
    /// La <paramref name="solucion"/> (ya con los cambios manuales) con las medidas de los números menores en cada barra
    /// y los kg de cada número con su largo. Sin números menores, tal cual.
    /// </summary>
    public static SolucionCapa Agregar(CapaCalculada capa, GeometriaPortico g, SolucionCapa solucion)
    {
        List<int> menores = capa.Grupos.Select(gr => gr.Diametro).Where(d => d != capa.Diametro).Distinct().Order().ToList();
        if (menores.Count == 0)
            return solucion;

        Dictionary<int, IReadOnlyList<MedidaBarra>?> porDiametro = menores.ToDictionary(d => d, d => Resolver(capa, g, solucion, d));
        var barras = solucion.Barras.Select((b, i) =>
        {
            Barra conMenores = b with { Menores = menores.Select(d => porDiametro[d]?[i] ?? ConSuGancho(capa, b, d)).ToList() };
            return conMenores with { Kg = Kg(capa, conMenores) };
        }).ToList();
        return solucion with { Barras = barras };
    }

    /// <summary>Null si el número no tiene solución en esas luces (no debería pasar: su traslapo es más corto).</summary>
    private static IReadOnlyList<MedidaBarra>? Resolver(CapaCalculada capa, GeometriaPortico g, SolucionCapa solucion, int diametro)
    {
        Reglas reglas = capa.Entrada.Reglas;
        ReglaDiametro regla = reglas.PorDiametro[diametro];
        ParametrosCapa p = capa.Parametros;
        int ganchoInicio = reglas.LargoGanchoRedondeado(regla, p.TipoInicio), ganchoFin = reglas.LargoGanchoRedondeado(regla, p.TipoFin);
        List<Ranura> ranuras = solucion.Traslapos.Select(t => Junto(g, t, p.Referencia, p.Modulo)).ToList();
        var problema = new ProblemaLinea(g.InicioBarras, g.FinBarras, ganchoInicio, ganchoFin, ranuras, regla.Traslapo!.Value,
            p.Modulo, p.Varilla, Math.Max(p.Varilla, p.VarillaLarga));
        // si a un módulo del traslapo del mayor no sale, en cualquier punto de su luz
        SolucionLinea? s = ResolvedorLinea.Resolver(problema)
            ?? ResolvedorLinea.Resolver(problema with { Ranuras = ranuras.Select(r => r with { DesvioMaximo = int.MaxValue }).ToList() });
        if (s is null)
            return null;

        int ultima = s.Barras.Count - 1;
        return s.Barras.Select((b, i) => new MedidaBarra(diametro, b.Desde, b.Hasta, i == 0 ? ganchoInicio : 0,
            i == ultima ? ganchoFin : 0, b.Largo, reglas.VarillaPara(b.Largo))).ToList();
    }

    /// <summary>
    /// Dónde puede ir el traslapo del menor: en la luz del traslapo <paramref name="t"/> del mayor (o donde quedó, si se
    /// corrió al voladizo), con el punto ideal en el del mayor.
    /// </summary>
    private static Ranura Junto(GeometriaPortico g, Traslapo t, Referencia referencia, int modulo)
    {
        Ranura luz = g.Ranura(g.Luces.First(l => l.Nombre == t.Luz), referencia);
        int ideal = referencia switch
        {
            Referencia.Inicio => t.Desde,
            Referencia.Fin => t.Hasta,
            _ => (t.Desde + t.Hasta) / 2,
        };
        return new Ranura(Math.Min(luz.ZonaDesde, t.Desde), Math.Max(luz.ZonaHasta, t.Hasta), ideal, referencia, modulo);
    }

    /// <summary>Si el menor no se resuelve: la parte recta del mayor con su gancho (como se dibujaba antes del 29-oct).</summary>
    private static MedidaBarra ConSuGancho(CapaCalculada capa, Barra b, int diametro)
    {
        Reglas reglas = capa.Entrada.Reglas;
        ReglaDiametro regla = reglas.PorDiametro[diametro];
        int ganchoInicio = reglas.LargoGanchoRedondeado(regla, b.TipoInicio), ganchoFin = reglas.LargoGanchoRedondeado(regla, b.TipoFin);
        int largo = b.LargoRecto + ganchoInicio + ganchoFin;
        return new MedidaBarra(diametro, b.Desde, b.Hasta, ganchoInicio, ganchoFin, largo, reglas.VarillaPara(largo));
    }

    private static double Kg(CapaCalculada capa, Barra b) =>
        capa.Grupos.Sum(gr => b.Medida(gr.Diametro).Largo / 1000.0 * gr.Cantidad * capa.Entrada.Reglas.PorDiametro[gr.Diametro].KgPorMetro);
}
