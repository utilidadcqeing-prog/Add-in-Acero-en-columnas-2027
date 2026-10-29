using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Vigas;

/// <summary>
/// Pone los bastones de un pórtico (#89, decisiones 2 a 6 del plan docs/planes/2026-10-16-bastones-vigas-89.md):
/// en todos los apoyos, centrados en el eje, y en el centro de la luz libre de cada luz. En la viga aérea los de los
/// apoyos van arriba; en la de cimentación, abajo. No se redondean: si una punta se pasa del pórtico, se corta ahí y
/// es la única que puede llevar gancho.
/// </summary>
internal static class CalculadorBastones
{
    private sealed record Lugar(string Donde, bool EnApoyo, int Centro);

    public static IReadOnlyList<Baston> Calcular(EntradaVigas e, GeometriaPortico g, List<Aviso> avisos)
    {
        ArmadoBastones armado = e.Bastones ?? new ArmadoBastones();
        if (armado.Largo <= 0)
        {
            avisos.Add(new Aviso(TipoAviso.BastonLargoNoValido, "general", armado.Largo));
            return [];
        }
        Posicion capaDeLosApoyos = e.Tipo == TipoViga.Aerea ? Posicion.Superior : Posicion.Inferior;
        IReadOnlyList<Lugar> apoyos = e.Portico.Apoyos.Select(a => new Lugar(a.Eje, true, a.PosicionEje)).ToList();
        IReadOnlyList<Lugar> luces = g.Luces.Select(l => new Lugar(l.Nombre, false, (l.CaraIzquierda + l.CaraDerecha) / 2)).ToList();

        var bastones = new List<Baston>();
        foreach (Posicion capa in new[] { Posicion.Superior, Posicion.Inferior })
        {
            GrupoBarras grupo = GrupoDe(armado, e, capa);
            if (grupo.Cantidad <= 0)
                continue;
            if (!e.Reglas.PorDiametro.TryGetValue(grupo.Diametro, out ReglaDiametro? regla))
            {
                avisos.Add(new Aviso(TipoAviso.DiametroSinReglas, $"#{grupo.Diametro}"));
                continue;
            }
            string prefijo = capa == Posicion.Superior ? "BS" : "BI";
            IEnumerable<Lugar> lugares = (capa == capaDeLosApoyos ? apoyos : luces).OrderBy(l => l.Centro);
            bastones.AddRange(lugares.Select((lugar, i) => Armar($"{prefijo}{i + 1}", capa, lugar, grupo, regla, e, armado, g, avisos)));
        }
        // un gancho o unas puntas guardados para un bastón que ya no está (#84)
        foreach (string clave in e.Cambios?.Ganchos?.Keys ?? [])
        {
            if (clave.StartsWith('B') && bastones.All(b => b.Id != clave.Split('.')[0]))
                avisos.Add(new Aviso(TipoAviso.CambioNoAplicado, clave, Motivo: MotivoBloqueo.NoExiste));
        }
        foreach (string id in armado.Puntas?.Keys ?? [])
        {
            if (bastones.All(b => b.Id != id))
                avisos.Add(new Aviso(TipoAviso.CambioNoAplicado, id, Motivo: MotivoBloqueo.NoExiste));
        }
        return bastones;
    }

    /// <summary>El largo con que se arma el bastón <paramref name="id"/>: el escrito a mano si es válido, o el general.</summary>
    public static int LargoDe(ArmadoBastones armado, string id) =>
        armado.Largos?.TryGetValue(id, out int largo) == true && largo > 0 ? largo : armado.Largo;

    /// <summary>
    /// El gancho de una punta de bastón (#84): en el borde, el elegido con los botones o el general del paso 2; fuera
    /// del borde, ninguno.
    /// </summary>
    public static TipoGancho GanchoDe(EntradaVigas e, string clave, bool enBorde)
    {
        if (!enBorde)
            return TipoGancho.Sin;
        return e.Cambios?.Ganchos?.TryGetValue(clave, out TipoGancho tipo) == true ? tipo
            : (e.Bastones ?? new ArmadoBastones()).ConGancho ? TipoGancho.De90 : TipoGancho.Sin;
    }

    private static GrupoBarras GrupoDe(ArmadoBastones armado, EntradaVigas e, Posicion capa) => capa == Posicion.Superior
        ? armado.Superior ?? new GrupoBarras(1, e.Superior[0].Diametro)
        : armado.Inferior ?? new GrupoBarras(1, e.Inferior[0].Diametro);

    private static Baston Armar(string id, Posicion capa, Lugar lugar, GrupoBarras grupo, ReglaDiametro regla,
        EntradaVigas e, ArmadoBastones armado, GeometriaPortico g, List<Aviso> avisos)
    {
        int? escrito = armado.Largos?.TryGetValue(id, out int valor) == true ? valor : null;
        if (escrito <= 0)
            avisos.Add(new Aviso(TipoAviso.CambioNoAplicado, id, escrito.Value, Motivo: MotivoBloqueo.LargoNoValido));
        // arrastrado, deja de estar centrado (decisión 8 del plan de #84)
        PuntasBaston? puntas = armado.Puntas?.GetValueOrDefault(id);
        if (puntas is not null && puntas.Hasta <= puntas.Desde)
        {
            avisos.Add(new Aviso(TipoAviso.CambioNoAplicado, id, puntas.Hasta - puntas.Desde, Motivo: MotivoBloqueo.LargoNoValido));
            puntas = null;
        }
        bool manual = escrito > 0 || puntas is not null;
        int largo = LargoDe(armado, id);
        int desde = puntas?.Desde ?? lugar.Centro - largo / 2, hasta = puntas?.Hasta ?? desde + largo;
        // una punta arrastrada se pega justo en el borde, y desde ahí también lleva gancho
        bool bordeInicio = desde <= g.InicioBarras, bordeFin = hasta >= g.FinBarras;
        TipoGancho tipoInicio = Punta(id + ".inicio", bordeInicio), tipoFin = Punta(id + ".fin", bordeFin);
        int ganchoInicio = e.Reglas.LargoGanchoRedondeado(regla, tipoInicio), ganchoFin = e.Reglas.LargoGanchoRedondeado(regla, tipoFin);
        desde = Math.Max(desde, g.InicioBarras);
        hasta = Math.Min(hasta, g.FinBarras);
        int total = hasta - desde + ganchoInicio + ganchoFin;
        // con gancho, el total sube al múltiplo y el sobrante va a la otra punta (decisión 9 del plan de #84, Carlos 17-oct:
        // "1.95… va a aumentar cinco para afuera"), venga de los botones o del "con gancho" general (Juan, 29-oct: quedaban
        // de 1.31); el escrito sin gancho no se redondea
        if ((bordeInicio && tipoInicio != TipoGancho.Sin) || (bordeFin && tipoFin != TipoGancho.Sin))
        {
            int modulo = e.Reglas.Modulo, sobra = (total + modulo - 1) / modulo * modulo - total;
            if (bordeInicio && !bordeFin)
                hasta = Math.Min(g.FinBarras, hasta + sobra);
            else if (bordeFin && !bordeInicio)
                desde = Math.Max(g.InicioBarras, desde - sobra);
            total = hasta - desde + ganchoInicio + ganchoFin;
        }
        return new Baston(id, capa, lugar.Donde, lugar.EnApoyo, lugar.Centro, desde, hasta, ganchoInicio, ganchoFin, total,
            manual, grupo, total / 1000.0 * grupo.Cantidad * regla.KgPorMetro, tipoInicio, tipoFin, bordeInicio, bordeFin);

        // un gancho elegido en una punta que no llega al borde se ignora con aviso; "Sin" no avisa: igual no lleva
        TipoGancho Punta(string clave, bool enBorde)
        {
            if (!enBorde && e.Cambios?.Ganchos?.TryGetValue(clave, out TipoGancho elegido) == true && elegido != TipoGancho.Sin)
                avisos.Add(new Aviso(TipoAviso.CambioNoAplicado, clave, Motivo: MotivoBloqueo.NoLlegaAlBorde));
            return GanchoDe(e, clave, enBorde);
        }
    }
}
