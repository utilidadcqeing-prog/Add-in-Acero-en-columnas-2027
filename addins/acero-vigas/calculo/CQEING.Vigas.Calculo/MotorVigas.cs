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
            return new ResultadoVigas(null, null, capaDeLaCara, null, null, Cara.Inicio, Cara.Inicio, avisos, []);

        var g = new GeometriaPortico(entrada.Portico, entrada.RecubrimientoPuntas, entrada.EstriboEnVoladizo);
        GeometriaPortico gCentro = GeometriaDe(entrada, capaDelCentro, g), gCara = GeometriaDe(entrada, capaDeLaCara, g);
        CapaCalculada? centro = Resolver(entrada, gCentro, capaDelCentro, Cara.Inicio, avisos);
        CapaCalculada? inicio = Resolver(entrada, gCara, capaDeLaCara, Cara.Inicio, avisos);
        CapaCalculada? final = Resolver(entrada, gCara, capaDeLaCara, Cara.Final, avisos);
        SolucionCapa? solucionInicio = inicio?.Convertir();
        SolucionCapa? solucionFinal = final?.Convertir();

        Cara masEconomica = solucionFinal is not null && (solucionInicio is null || solucionFinal.Kg < solucionInicio.Kg)
            ? Cara.Final : Cara.Inicio;
        Cara usada = entrada.CaraElegida ?? masEconomica;
        Cara otra = usada == Cara.Inicio ? Cara.Final : Cara.Inicio;

        SolucionCapa? capaCentro = centro is null ? null : AplicadorCambios.Aplicar(centro, gCentro, entrada.Cambios, avisos);
        CapaCalculada? usadaCalculada = usada == Cara.Inicio ? inicio : final;
        SolucionCapa? capaCara = usadaCalculada is null ? null : AplicadorCambios.Aplicar(usadaCalculada, gCara, entrada.Cambios, avisos);

        int varilla = entrada.Reglas.Varilla;
        if (capaCentro is not null)
            avisos.AddRange(Avisos.De(capaCentro, otraCara: null, otra, varilla));
        if (capaCara is not null)
            avisos.AddRange(Avisos.De(capaCara, usada == Cara.Inicio ? solucionFinal : solucionInicio, otra, varilla));

        IReadOnlyList<Baston> bastones = CalculadorBastones.Calcular(entrada, g, avisos);
        return capaDeLaCara == Posicion.Inferior
            ? new ResultadoVigas(capaCentro, capaCara, capaDeLaCara, solucionInicio, solucionFinal, usada, masEconomica, avisos, bastones)
            : new ResultadoVigas(capaCara, capaCentro, capaDeLaCara, solucionInicio, solucionFinal, usada, masEconomica, avisos, bastones);
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

    /// <summary>
    /// Cómo quedaría el pórtico si el bastón <paramref name="id"/> se escribe con <paramref name="largo"/> (mm, sin
    /// redondear). "Usar el largo general" no pasa por aquí: quita el id de <see cref="ArmadoBastones.Largos"/>.
    /// </summary>
    public static Previsualizacion PrevisualizarBaston(EntradaVigas entrada, string id, int largo)
    {
        if (Calcular(entrada).Bastones.All(b => b.Id != id))
            return new Previsualizacion(false, MotivoBloqueo.NoExiste, null);
        if (largo <= 0)
            return new Previsualizacion(false, MotivoBloqueo.LargoNoValido, null);
        ArmadoBastones armado = entrada.Bastones ?? new ArmadoBastones();
        if (CalculadorBastones.LargoDe(armado, id) == largo)
            return new Previsualizacion(false, MotivoBloqueo.SinCambio, null);
        ResultadoVigas r = Calcular(entrada with { Bastones = armado with { Largos = Con(armado.Largos, id, largo) } });
        return new Previsualizacion(true, MotivoBloqueo.Ninguno, r);
    }

    private static Previsualizacion Probar(EntradaVigas conCambio, string id)
    {
        ResultadoVigas r = Calcular(conCambio);
        Aviso? bloqueo = r.Avisos.FirstOrDefault(a => a.Tipo == TipoAviso.CambioNoAplicado && a.Elemento == id);
        return bloqueo is null ? new Previsualizacion(true, MotivoBloqueo.Ninguno, r) : new Previsualizacion(false, bloqueo.Motivo, r);
    }

    /// <summary>
    /// El gancho de una punta del borde de las corridas (<c>"S.inicio"</c>…): 90° si no se cambió. Una corrida no puede
    /// quedar sin gancho en el borde: se avisa una vez y queda el de 90° (decisión 5 del plan de #84). El núcleo deja
    /// el largo total en múltiplo del módulo con el gancho que sea.
    /// </summary>
    private static TipoGancho GanchoCorrida(CambiosManuales? cambios, string clave, List<Aviso> avisos)
    {
        if (cambios?.Ganchos?.TryGetValue(clave, out TipoGancho tipo) != true)
            return TipoGancho.De90;
        if (tipo != TipoGancho.Sin)
            return tipo;
        var aviso = new Aviso(TipoAviso.CambioNoAplicado, clave, Motivo: MotivoBloqueo.GanchoNoValido);
        if (!avisos.Contains(aviso))
            avisos.Add(aviso);
        return TipoGancho.De90;
    }

    /// <summary>
    /// Dónde terminan las barras corridas de <paramref name="capa"/>. Las de abajo terminan antes, la mitad del diámetro de
    /// arriba más la mitad del de abajo: la pata de su gancho queda pegada por dentro a la de arriba (ganchos anidados, como
    /// se arma en obra). Con las dos capas hasta el mismo borde las patas quedaban una encima de otra (#85, novena prueba
    /// en Revit; Juan: opción 1). El núcleo deja el largo en múltiplo del módulo igual que siempre.
    /// </summary>
    public static GeometriaPortico GeometriaDe(EntradaVigas e, Posicion capa, GeometriaPortico comun)
    {
        if (capa == Posicion.Superior)
            return comun;
        double Mayor(IReadOnlyList<GrupoBarras> grupos) => e.Reglas.PorDiametro[grupos.Max(gr => gr.Diametro)].Diametro;
        int anidado = (int)Math.Round((Mayor(e.Superior) + Mayor(e.Inferior)) / 2);
        return new GeometriaPortico(e.Portico, e.RecubrimientoPuntas + anidado, e.EstriboEnVoladizo);
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
        string prefijo = capa == Posicion.Superior ? "S" : "I";
        TipoGancho inicio = GanchoCorrida(e.Cambios, prefijo + ".inicio", avisos), fin = GanchoCorrida(e.Cambios, prefijo + ".fin", avisos);
        var parametros = new ParametrosCapa(GeometriaPortico.ReferenciaDe(e.Tipo, capa, cara), regla.Traslapo!.Value,
            Reglas.LargoGancho(regla, inicio), Reglas.LargoGancho(regla, fin), e.Reglas.Modulo, e.Reglas.Varilla, e.Reglas.VarillaLarga,
            inicio, fin);

        CapaResuelta? resuelta = ResolvedorCapa.Resolver(g, parametros);
        if (resuelta is not null)
            return new CapaCalculada(e, capa, grupos, diametro, parametros, resuelta);
        var aviso = new Aviso(TipoAviso.SinSolucion, capa.ToString());
        if (!avisos.Contains(aviso))
            avisos.Add(aviso);
        return null;
    }
}
