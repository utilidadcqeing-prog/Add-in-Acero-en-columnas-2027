using CQEING.Vigas.Calculo.Nucleo;

namespace CQEING.Vigas.Calculo.Vigas;

/// <summary>Datos de una capa para el motor, en mm. El traslapo y el gancho son los del diámetro mayor.</summary>
public sealed record ParametrosCapa(Referencia Referencia, int TraslapoMinimo, int Gancho, int Modulo, int Varilla, int VarillaLarga);

/// <summary>Solución de una capa y las luces donde quedó cada traslapo (una por traslapo, en orden).</summary>
public sealed record CapaResuelta(SolucionLinea Solucion, IReadOnlyList<Luz> Luces);

/// <summary>
/// Decide en qué luces traslapa una capa: prueba sin traslapos, con uno, con dos… (a lo sumo uno por luz) y se
/// queda con la mejor según la decisión 2 del plan: menos barras de 12 m, menos traslapos, menos acero y menos
/// desvío. Los voladizos no son luces, así que ahí nunca hay traslapo.
/// </summary>
public static class ResolvedorCapa
{
    /// <param name="lucesFijas">Si viene, solo se prueban esas luces (para recalcular sin que cambien los ids).</param>
    /// <param name="largosFijos">Cambios manuales: índice de barra → largo total.</param>
    public static CapaResuelta? Resolver(GeometriaPortico g, ParametrosCapa p,
        IReadOnlyList<Luz>? lucesFijas = null, IReadOnlyDictionary<int, int>? largosFijos = null)
    {
        if (lucesFijas is not null)
            return Probar(g, p, lucesFijas, largosFijos);

        CapaResuelta? mejor = null;
        for (int cantidad = 0; cantidad <= g.Luces.Count; cantidad++)
        {
            foreach (IReadOnlyList<Luz> luces in Combinaciones(g.Luces, cantidad))
            {
                CapaResuelta? capa = Probar(g, p, luces, largosFijos);
                if (capa is not null && (mejor is null || EsMejor(capa.Solucion, mejor.Solucion)))
                    mejor = capa;
            }
            // Con más traslapos no se puede mejorar una solución que ya no usa varillas de 12 m.
            if (mejor is { Solucion.BarrasDe12: 0 })
                break;
        }
        return mejor;
    }

    private static CapaResuelta? Probar(GeometriaPortico g, ParametrosCapa p, IReadOnlyList<Luz> luces,
        IReadOnlyDictionary<int, int>? largosFijos)
    {
        // Decisión 15 del plan: el traslapo se corre a lo sumo un módulo de su punto ideal, como en el procedimiento
        // de Carlos (solo se mueve lo que exige el redondeo).
        var problema = new ProblemaLinea(g.InicioBarras, g.FinBarras, p.Gancho, p.Gancho,
            luces.Select(l => g.Ranura(l, p.Referencia) with { DesvioMaximo = p.Modulo }).ToList(),
            p.TraslapoMinimo, p.Modulo, p.Varilla, Math.Max(p.Varilla, p.VarillaLarga), largosFijos);
        SolucionLinea? s = ResolvedorLinea.Resolver(problema);
        return s is null ? null : new CapaResuelta(s, luces);
    }

    private static bool EsMejor(SolucionLinea a, SolucionLinea b) =>
        (a.BarrasDe12, a.Traslapos.Count, a.Acero, a.DesvioTotal).CompareTo((b.BarrasDe12, b.Traslapos.Count, b.Acero, b.DesvioTotal)) < 0;

    /// <summary>Todas las formas de elegir <paramref name="cantidad"/> luces, conservando el orden.</summary>
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
