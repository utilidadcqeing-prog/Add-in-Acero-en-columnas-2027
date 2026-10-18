using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Nucleo;

namespace CQEING.Vigas.Calculo.Vigas;

/// <summary>
/// Datos de una capa para el motor, en mm. El traslapo y los ganchos son los del diámetro mayor; cada punta del borde
/// lleva el suyo (#84), y <paramref name="TipoInicio"/> / <paramref name="TipoFin"/> dicen cuál es.
/// </summary>
public sealed record ParametrosCapa(Referencia Referencia, int TraslapoMinimo, int GanchoInicio, int GanchoFin, int Modulo, int Varilla,
    int VarillaLarga, TipoGancho TipoInicio = TipoGancho.De90, TipoGancho TipoFin = TipoGancho.De90);

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

        // Manda tener menos varillas de 12 m: una solución sin ninguna le gana a cualquiera con alguna, así que la
        // respuesta es la mejor sin varillas de 12 m de la menor cantidad de traslapos que la tenga. Solo se resuelven
        // las combinaciones cuyas barras pueden salir todas de la varilla normal (tarea 3 del plan del tablero #83).
        for (int cantidad = 0; cantidad <= g.Luces.Count; cantidad++)
        {
            CapaResuelta? sinDoce = null;
            foreach (IReadOnlyList<Luz> luces in Combinaciones(g.Luces, cantidad))
            {
                if (LargoMinimoMayor(g, p, luces) > p.Varilla)
                    continue;
                CapaResuelta? capa = Probar(g, p, luces, largosFijos);
                if (capa is { Solucion.BarrasDe12: 0 } && (sinDoce is null || EsMejor(capa.Solucion, sinDoce.Solucion)))
                    sinDoce = capa;
            }
            if (sinDoce is not null)
                return sinDoce;
        }

        // Ninguna sale sin varillas de 12 m: la búsqueda completa, sin resolver las combinaciones imposibles.
        CapaResuelta? mejor = null;
        int maxima = Math.Max(p.Varilla, p.VarillaLarga);
        for (int cantidad = 0; cantidad <= g.Luces.Count; cantidad++)
        {
            foreach (IReadOnlyList<Luz> luces in Combinaciones(g.Luces, cantidad))
            {
                if (LargoMinimoMayor(g, p, luces) > maxima)
                    continue;
                CapaResuelta? capa = Probar(g, p, luces, largosFijos);
                if (capa is not null && (mejor is null || EsMejor(capa.Solucion, mejor.Solucion)))
                    mejor = capa;
            }
        }
        return mejor;
    }

    /// <summary>
    /// El largo mínimo posible de la barra más larga si se traslapa en <paramref name="luces"/>, sin resolver: cada
    /// traslapo queda dentro de su ranura y mide al menos el mínimo. La primera barra va del gancho al fin del primer
    /// traslapo, las del medio del inicio de un traslapo al fin del siguiente y la última hasta el gancho final.
    /// </summary>
    private static int LargoMinimoMayor(GeometriaPortico g, ParametrosCapa p, IReadOnlyList<Luz> luces)
    {
        int min = p.TraslapoMinimo;
        int inicio = g.InicioBarras - p.GanchoInicio, fin = g.FinBarras + p.GanchoFin;
        if (luces.Count == 0)
            return fin - inicio;
        List<Ranura> ranuras = luces.Select(l => g.Ranura(l, p.Referencia)).ToList();
        int mayor = Math.Max(ranuras[0].ZonaDesde + min - inicio, fin - (ranuras[^1].ZonaHasta - min));
        for (int i = 1; i < ranuras.Count; i++)
            mayor = Math.Max(mayor, ranuras[i].ZonaDesde - ranuras[i - 1].ZonaHasta + 2 * min);
        return mayor;
    }

    private static CapaResuelta? Probar(GeometriaPortico g, ParametrosCapa p, IReadOnlyList<Luz> luces,
        IReadOnlyDictionary<int, int>? largosFijos)
    {
        // Decisión 15 del plan: el traslapo se corre a lo sumo un módulo de su punto ideal, como en el procedimiento
        // de Carlos (solo se mueve lo que exige el redondeo).
        var problema = new ProblemaLinea(g.InicioBarras, g.FinBarras, p.GanchoInicio, p.GanchoFin,
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
