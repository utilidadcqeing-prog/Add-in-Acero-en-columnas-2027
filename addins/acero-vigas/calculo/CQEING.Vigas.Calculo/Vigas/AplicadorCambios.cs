using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Vigas;

/// <summary>
/// Aplica los cambios manuales de una capa sobre su solución automática (decisiones 7 y 8 del plan). Lo que no se
/// puede aplicar se ignora y deja un aviso <see cref="TipoAviso.CambioNoAplicado"/> con su motivo.
/// </summary>
internal static class AplicadorCambios
{
    public static SolucionCapa Aplicar(CapaCalculada capa, GeometriaPortico g, CambiosManuales? cambios, List<Aviso> avisos)
    {
        SolucionCapa solucion = AplicarBarras(capa, g, cambios?.Barras, avisos);
        return AplicarTraslapos(capa, g, solucion, cambios?.Traslapos, avisos);
    }

    /// <summary>
    /// Subir / Bajar: fija el largo de esas barras y vuelve a resolver la capa con las mismas luces de traslapo, para
    /// que los ids no cambien.
    /// </summary>
    private static SolucionCapa AplicarBarras(CapaCalculada capa, GeometriaPortico g, IReadOnlyDictionary<string, int>? barras,
        List<Aviso> avisos)
    {
        var automatica = capa.Convertir();
        var fijos = new Dictionary<int, int>();
        foreach (var (id, largo) in DeEstaCapa(capa, barras))
        {
            int indice = automatica.Barras.ToList().FindIndex(b => b.Id == id);
            MotivoBloqueo motivo = indice < 0 ? MotivoBloqueo.NoExiste
                : SaleDeLaVarilla(capa, automatica.Barras[indice].Largo, largo) ? MotivoBloqueo.Ninguno
                : MotivoBloqueo.NoSaleDeLaVarilla;
            if (motivo == MotivoBloqueo.Ninguno)
                fijos[indice] = largo;
            else
                avisos.Add(new Aviso(TipoAviso.CambioNoAplicado, id, Largo: largo, Motivo: motivo));
        }
        if (fijos.Count == 0)
            return automatica;

        CapaResuelta? resuelta = ResolvedorCapa.Resolver(g, capa.Parametros, capa.Resuelta.Luces, fijos);
        if (resuelta is null)
        {
            foreach (var (indice, largo) in fijos)
                avisos.Add(new Aviso(TipoAviso.CambioNoAplicado, automatica.Barras[indice].Id, Largo: largo, Motivo: MotivoBloqueo.SinSolucion));
            return automatica;
        }
        return capa.Convertir(resuelta, fijos.Keys.ToHashSet());
    }

    /// <summary>
    /// Traslapo editado: crece hasta el largo escrito, redondeado hacia arriba al módulo; la barra de la derecha
    /// empieza antes y crece lo mismo.
    /// </summary>
    private static SolucionCapa AplicarTraslapos(CapaCalculada capa, GeometriaPortico g, SolucionCapa solucion,
        IReadOnlyDictionary<string, int>? traslapos, List<Aviso> avisos)
    {
        var barras = solucion.Barras.ToList();
        var lista = solucion.Traslapos.ToList();
        int modulo = capa.Parametros.Modulo;
        foreach (var (id, escrito) in DeEstaCapa(capa, traslapos).OrderBy(c => lista.FindIndex(t => t.Id == c.Key)))
        {
            int i = lista.FindIndex(t => t.Id == id);
            if (i < 0)
            {
                avisos.Add(new Aviso(TipoAviso.CambioNoAplicado, id, Largo: escrito, Motivo: MotivoBloqueo.NoExiste));
                continue;
            }
            Traslapo t = lista[i];
            if (escrito < t.Minimo)
            {
                avisos.Add(new Aviso(TipoAviso.CambioNoAplicado, id, Largo: escrito, Motivo: MotivoBloqueo.TraslapoBajoElMinimo));
                continue;
            }
            int crece = escrito <= t.Largo ? 0 : (escrito - t.Largo + modulo - 1) / modulo * modulo;
            if (crece == 0)
                continue;

            Barra derecha = barras[i + 1];
            int desde = t.Desde - crece;
            Luz luz = g.Luces.First(l => l.Nombre == t.Luz);
            MotivoBloqueo motivo = !SaleDeLaVarilla(capa, derecha.Largo, derecha.Largo + crece) ? MotivoBloqueo.NoSaleDeLaVarilla
                : desde < luz.CaraIzquierda + GeometriaPortico.DistanciaACara ? MotivoBloqueo.EntraAlNudo
                : MotivoBloqueo.Ninguno;
            if (motivo != MotivoBloqueo.Ninguno)
            {
                avisos.Add(new Aviso(TipoAviso.CambioNoAplicado, id, Largo: escrito, Motivo: motivo));
                continue;
            }
            int largo = derecha.Largo + crece;
            barras[i + 1] = derecha with { Desde = desde, Largo = largo, Varilla = capa.VarillaPara(largo), Kg = capa.KgDe(largo) };
            lista[i] = t with { Desde = desde, Manual = true };
        }
        return solucion with { Barras = barras, Traslapos = lista };
    }

    /// <summary>
    /// Una barra que salía de la varilla normal no puede pasar de ella a mano; una que ya usaba la larga, no puede
    /// pasar de la larga.
    /// </summary>
    private static bool SaleDeLaVarilla(CapaCalculada capa, int largoActual, int largoNuevo) =>
        largoNuevo <= capa.VarillaPara(largoActual);

    private static IEnumerable<KeyValuePair<string, int>> DeEstaCapa(CapaCalculada capa, IReadOnlyDictionary<string, int>? cambios) =>
        cambios?.Where(c => c.Key.StartsWith(capa.Prefijo, StringComparison.Ordinal)) ?? [];
}
