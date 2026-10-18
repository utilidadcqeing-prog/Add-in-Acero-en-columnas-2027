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
        solucion = AplicarTraslapos(capa, g, solucion, cambios?.Traslapos, avisos);
        return AplicarMovidos(capa, g, solucion, cambios?.Movidos, avisos);
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
    /// Traslapo arrastrado (decisiones 6 y 7 del plan de #84): se corre entero, de módulo en módulo, para que sus dos
    /// barras sigan en largo cerrado; la de la izquierda crece lo que se corre y la de la derecha se acorta lo mismo.
    /// </summary>
    private static SolucionCapa AplicarMovidos(CapaCalculada capa, GeometriaPortico g, SolucionCapa solucion,
        IReadOnlyDictionary<string, TraslapoMovido>? movidos, List<Aviso> avisos)
    {
        var barras = solucion.Barras.ToList();
        var lista = solucion.Traslapos.ToList();
        int modulo = capa.Parametros.Modulo;
        // si se corren dos traslapos seguidos (el centro de una barra), la barra del medio primero se acorta y después
        // crece: a la derecha se aplican de izquierda a derecha, y a la izquierda al revés
        var orden = DeEstaCapa(capa, movidos)
            .Select(c => (c.Key, c.Value, Indice: lista.FindIndex(t => t.Id == c.Key)))
            .OrderBy(c => c.Indice < 0 || c.Value.Desde >= lista[c.Indice].Desde ? 0 : 1)
            .ThenBy(c => c.Indice < 0 || c.Value.Desde >= lista[c.Indice].Desde ? c.Indice : -c.Indice);
        foreach (var (id, movido, i) in orden)
        {
            if (i < 0)
            {
                avisos.Add(new Aviso(TipoAviso.CambioNoAplicado, id, Largo: movido.Desde, Motivo: MotivoBloqueo.NoExiste));
                continue;
            }
            Traslapo t = lista[i];
            int corre = (int)Math.Round((movido.Desde - t.Desde) / (double)modulo, MidpointRounding.AwayFromZero) * modulo;
            if (corre == 0)
                continue;

            Barra izquierda = barras[i], derecha = barras[i + 1];
            int desde = t.Desde + corre, hasta = t.Hasta + corre;
            int largoIzquierda = izquierda.Largo + corre, largoDerecha = derecha.Largo - corre;
            MotivoBloqueo motivo = !SaleDeLaVarilla(capa, izquierda.Largo, largoIzquierda) || !SaleDeLaVarilla(capa, derecha.Largo, largoDerecha)
                ? MotivoBloqueo.NoSaleDeLaVarilla
                : DondeQueda(g, t, desde, hasta, movido.EnVoladizo);
            if (motivo != MotivoBloqueo.Ninguno)
            {
                avisos.Add(new Aviso(TipoAviso.CambioNoAplicado, id, Largo: movido.Desde, Motivo: motivo));
                continue;
            }
            barras[i] = izquierda with
            {
                Hasta = hasta, Largo = largoIzquierda, Varilla = capa.VarillaPara(largoIzquierda), Kg = capa.KgDe(largoIzquierda),
                Manual = true, LlegaAlVoladizo = capa.LlegaAlVoladizo(izquierda.Desde, hasta),
            };
            barras[i + 1] = derecha with
            {
                Desde = desde, Largo = largoDerecha, Varilla = capa.VarillaPara(largoDerecha), Kg = capa.KgDe(largoDerecha),
                Manual = true, LlegaAlVoladizo = capa.LlegaAlVoladizo(desde, derecha.Hasta),
            };
            lista[i] = t with { Desde = desde, Hasta = hasta, Manual = true };
        }
        return solucion with { Barras = barras, Traslapos = lista };
    }

    /// <summary>
    /// Un traslapo corrido no cambia de luz y queda a 5 cm de sus caras. Solo el de la primera o la última luz puede
    /// pasar al voladizo que toca, sin salirse por la punta, y si el usuario lo confirmó.
    /// </summary>
    internal static MotivoBloqueo DondeQueda(GeometriaPortico g, Traslapo t, int desde, int hasta, bool enVoladizo)
    {
        const int cara = GeometriaPortico.DistanciaACara;
        Luz luz = g.Luces.First(l => l.Nombre == t.Luz);
        if (desde >= luz.CaraIzquierda + cara && hasta <= luz.CaraDerecha - cara)
            return MotivoBloqueo.Ninguno;

        Portico portico = g.Portico;
        bool alVoladizo =
            (luz == g.Luces[0] && portico.VoladizoInicio > 0 && desde > g.InicioBarras && hasta <= portico.Apoyos[0].CaraIzquierda - cara)
            || (luz == g.Luces[^1] && portico.VoladizoFinal > 0 && desde >= portico.Apoyos[^1].CaraDerecha + cara && hasta < g.FinBarras);
        return !alVoladizo ? MotivoBloqueo.EntraAlNudo
            : enVoladizo ? MotivoBloqueo.Ninguno
            : MotivoBloqueo.TraslapoEnVoladizo;
    }

    /// <summary>
    /// Una barra que salía de la varilla normal no puede pasar de ella a mano; una que ya usaba la larga, no puede
    /// pasar de la larga.
    /// </summary>
    internal static bool SaleDeLaVarilla(Reglas reglas, int largoActual, int largoNuevo) =>
        largoNuevo <= reglas.VarillaPara(largoActual);

    private static bool SaleDeLaVarilla(CapaCalculada capa, int largoActual, int largoNuevo) =>
        SaleDeLaVarilla(capa.Entrada.Reglas, largoActual, largoNuevo);

    private static IEnumerable<KeyValuePair<string, T>> DeEstaCapa<T>(CapaCalculada capa, IReadOnlyDictionary<string, T>? cambios) =>
        cambios?.Where(c => c.Key.StartsWith(capa.Prefijo, StringComparison.Ordinal)) ?? [];
}
