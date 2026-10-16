using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Vigas;

namespace CQEING.Vigas.Calculo;

/// <summary>
/// Punto de entrada del motor (#80): recibe un pórtico con su armado y sus reglas y devuelve lo que pinta el
/// tablero. No usa Revit. Plan: docs/planes/2026-10-16-motor-calculo-vigas-80.md.
/// </summary>
public static class MotorVigas
{
    public static ResultadoVigas Calcular(EntradaVigas entrada)
    {
        Posicion capaDeLaCara = entrada.Tipo == TipoViga.Aerea ? Posicion.Inferior : Posicion.Superior;
        Posicion capaDelCentro = capaDeLaCara == Posicion.Inferior ? Posicion.Superior : Posicion.Inferior;

        List<Aviso> avisos = DiametrosSinTraslapo(entrada).Select(d => new Aviso(TipoAviso.SinTraslapoMinimo, $"#{d}")).ToList();
        if (avisos.Count > 0)
            return new ResultadoVigas(null, null, capaDeLaCara, null, null, Cara.Inicio, Cara.Inicio, avisos);

        var g = new GeometriaPortico(entrada.Portico, entrada.RecubrimientoPuntas);
        CapaCalculada? centro = Resolver(entrada, g, capaDelCentro, Cara.Inicio, avisos);
        CapaCalculada? inicio = Resolver(entrada, g, capaDeLaCara, Cara.Inicio, avisos);
        CapaCalculada? final = Resolver(entrada, g, capaDeLaCara, Cara.Final, avisos);
        SolucionCapa? solucionInicio = inicio?.Convertir();
        SolucionCapa? solucionFinal = final?.Convertir();

        Cara masEconomica = solucionFinal is not null && (solucionInicio is null || solucionFinal.Kg < solucionInicio.Kg)
            ? Cara.Final : Cara.Inicio;
        Cara usada = entrada.CaraElegida ?? masEconomica;
        Cara otra = usada == Cara.Inicio ? Cara.Final : Cara.Inicio;

        SolucionCapa? capaCentro = centro is null ? null : AplicadorCambios.Aplicar(centro, g, entrada.Cambios, avisos);
        CapaCalculada? usadaCalculada = usada == Cara.Inicio ? inicio : final;
        SolucionCapa? capaCara = usadaCalculada is null ? null : AplicadorCambios.Aplicar(usadaCalculada, g, entrada.Cambios, avisos);

        int varilla = entrada.Reglas.Varilla;
        if (capaCentro is not null)
            avisos.AddRange(Avisos.De(capaCentro, otraCara: null, otra, varilla));
        if (capaCara is not null)
            avisos.AddRange(Avisos.De(capaCara, usada == Cara.Inicio ? solucionFinal : solucionInicio, otra, varilla));

        return capaDeLaCara == Posicion.Inferior
            ? new ResultadoVigas(capaCentro, capaCara, capaDeLaCara, solucionInicio, solucionFinal, usada, masEconomica, avisos)
            : new ResultadoVigas(capaCara, capaCentro, capaDeLaCara, solucionInicio, solucionFinal, usada, masEconomica, avisos);
    }

    /// <summary>Cómo quedaría el pórtico si la barra <paramref name="id"/> pasa a <paramref name="nuevoLargo"/> (Subir / Bajar).</summary>
    public static Previsualizacion PrevisualizarBarra(EntradaVigas entrada, string id, int nuevoLargo)
    {
        Barra? barra = Calcular(entrada).Barras.FirstOrDefault(b => b.Id == id);
        if (barra is null)
            return new Previsualizacion(false, MotivoBloqueo.NoExiste, null);
        if (barra.Largo == nuevoLargo)
            return new Previsualizacion(false, MotivoBloqueo.SinCambio, null);
        var cambios = entrada.Cambios ?? new CambiosManuales();
        return Probar(entrada with { Cambios = cambios with { Barras = Con(cambios.Barras, id, nuevoLargo) } }, id);
    }

    /// <summary>Cómo quedaría el pórtico si el traslapo <paramref name="id"/> se escribe con <paramref name="largo"/>.</summary>
    public static Previsualizacion PrevisualizarTraslapo(EntradaVigas entrada, string id, int largo)
    {
        Traslapo? traslapo = Calcular(entrada).Traslapos.FirstOrDefault(t => t.Id == id);
        if (traslapo is null)
            return new Previsualizacion(false, MotivoBloqueo.NoExiste, null);
        if (largo < traslapo.Minimo)
            return new Previsualizacion(false, MotivoBloqueo.TraslapoBajoElMinimo, null);
        if (largo <= traslapo.Largo)
            return new Previsualizacion(false, MotivoBloqueo.SinCambio, null);
        var cambios = entrada.Cambios ?? new CambiosManuales();
        return Probar(entrada with { Cambios = cambios with { Traslapos = Con(cambios.Traslapos, id, largo) } }, id);
    }

    private static Previsualizacion Probar(EntradaVigas conCambio, string id)
    {
        ResultadoVigas r = Calcular(conCambio);
        Aviso? bloqueo = r.Avisos.FirstOrDefault(a => a.Tipo == TipoAviso.CambioNoAplicado && a.Elemento == id);
        return bloqueo is null ? new Previsualizacion(true, MotivoBloqueo.Ninguno, r) : new Previsualizacion(false, bloqueo.Motivo, r);
    }

    private static Dictionary<string, int> Con(IReadOnlyDictionary<string, int>? actuales, string id, int valor) =>
        new(actuales ?? new Dictionary<string, int>()) { [id] = valor };

    private static IEnumerable<int> DiametrosSinTraslapo(EntradaVigas e) =>
        e.Superior.Concat(e.Inferior).Select(gr => gr.Diametro).Distinct().Order()
            .Where(d => !e.Reglas.PorDiametro.TryGetValue(d, out ReglaDiametro? r) || r.Traslapo is null);

    private static CapaCalculada? Resolver(EntradaVigas e, GeometriaPortico g, Posicion capa, Cara cara, List<Aviso> avisos)
    {
        IReadOnlyList<GrupoBarras> grupos = capa == Posicion.Superior ? e.Superior : e.Inferior;
        int diametro = grupos.Max(gr => gr.Diametro);
        ReglaDiametro regla = e.Reglas.PorDiametro[diametro];
        var parametros = new ParametrosCapa(GeometriaPortico.ReferenciaDe(e.Tipo, capa, cara), regla.Traslapo!.Value,
            regla.Gancho90, e.Reglas.Modulo, e.Reglas.Varilla, e.Reglas.VarillaLarga);

        CapaResuelta? resuelta = ResolvedorCapa.Resolver(g, parametros);
        if (resuelta is not null)
            return new CapaCalculada(e, capa, grupos, diametro, parametros, resuelta);
        var aviso = new Aviso(TipoAviso.SinSolucion, capa.ToString());
        if (!avisos.Contains(aviso))
            avisos.Add(aviso);
        return null;
    }
}
