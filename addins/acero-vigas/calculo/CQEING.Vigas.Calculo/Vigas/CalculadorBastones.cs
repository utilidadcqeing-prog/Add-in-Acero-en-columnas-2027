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
            bastones.AddRange(lugares.Select((lugar, i) => Armar($"{prefijo}{i + 1}", capa, lugar, grupo, regla, armado, g)));
        }
        return bastones;
    }

    private static GrupoBarras GrupoDe(ArmadoBastones armado, EntradaVigas e, Posicion capa) => capa == Posicion.Superior
        ? armado.Superior ?? new GrupoBarras(1, e.Superior[0].Diametro)
        : armado.Inferior ?? new GrupoBarras(1, e.Inferior[0].Diametro);

    private static Baston Armar(string id, Posicion capa, Lugar lugar, GrupoBarras grupo, ReglaDiametro regla,
        ArmadoBastones armado, GeometriaPortico g)
    {
        bool manual = armado.Largos?.ContainsKey(id) == true;
        int largo = manual ? armado.Largos![id] : armado.Largo;
        int desde = lugar.Centro - largo / 2, hasta = desde + largo;
        int ganchoInicio = armado.ConGancho && desde < g.InicioBarras ? regla.Gancho90.B : 0;
        int ganchoFin = armado.ConGancho && hasta > g.FinBarras ? regla.Gancho90.B : 0;
        desde = Math.Max(desde, g.InicioBarras);
        hasta = Math.Min(hasta, g.FinBarras);
        int total = hasta - desde + ganchoInicio + ganchoFin;
        return new Baston(id, capa, lugar.Donde, lugar.EnApoyo, lugar.Centro, desde, hasta, ganchoInicio, ganchoFin, total,
            manual, grupo, total / 1000.0 * grupo.Cantidad * regla.KgPorMetro);
    }
}
