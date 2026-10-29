using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Vigas;

namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>
/// Los saltos de un punto arrastrado (#84, decisiones 6 a 8 del plan docs/planes/2026-10-17-edicion-vigas-84.md): los
/// lugares donde puede caer y lo que se guarda al soltar ahí. Se arman con aritmética, sin resolver la capa; al soltar,
/// el motor vuelve a validar el cambio. Van de izquierda a derecha, y el que deja el elemento donde está va sin cambios.
/// </summary>
public static class EditorArrastre
{
    /// <summary>A esta distancia del borde (mm), la punta de un bastón se pega a él (decisión 8).</summary>
    public const int Iman = 50;

    /// <summary>De a cuánto (mm) se corre un bastón entero (decisión 8, pregunta 4 en #79).</summary>
    public const int PasoPosicion = 50;

    /// <summary>Vacía si ese punto no se puede arrastrar.</summary>
    public static IReadOnlyList<Salto> Saltos(EntradaVigas e, ResultadoVigas r, string elemento, PuntoArrastre punto)
    {
        var g = new GeometriaPortico(e.Portico, e.RecubrimientoPuntas, e.EstriboEnVoladizo);
        SolucionCapa? capa = new[] { r.Superior, r.Inferior }.FirstOrDefault(c => c?.Barras.Any(b => b.Id == elemento) == true);
        if (capa is not null)
            return DeBarra(e.Reglas, MotorVigas.GeometriaDe(e, capa.Capa, g), capa, elemento, punto);
        Baston? baston = r.Bastones.FirstOrDefault(b => b.Id == elemento);
        return baston is null ? [] : DeBaston(e, g, baston, punto);
    }

    /// <summary>
    /// Una punta de una barra corrida mueve el traslapo que está ahí; el centro, sus dos traslapos con el mismo Δ. La
    /// punta que llega al borde no se arrastra, ni el centro de una barra de borde (decisión 6).
    /// </summary>
    private static IReadOnlyList<Salto> DeBarra(Reglas reglas, GeometriaPortico g, SolucionCapa capa, string id, PuntoArrastre punto)
    {
        int i = capa.Barras.ToList().FindIndex(b => b.Id == id);
        Barra barra = capa.Barras[i];
        Traslapo? izquierdo = i > 0 ? capa.Traslapos[i - 1] : null;
        Traslapo? derecho = i < capa.Traslapos.Count ? capa.Traslapos[i] : null;
        IReadOnlyList<Traslapo> mueve = punto switch
        {
            PuntoArrastre.Izquierda when izquierdo is not null => [izquierdo],
            PuntoArrastre.Derecha when derecho is not null => [derecho],
            PuntoArrastre.Centro when izquierdo is not null && derecho is not null => [izquierdo, derecho],
            _ => [],
        };
        if (mueve.Count == 0)
            return [];

        int modulo = reglas.Modulo;
        int maximo = (g.FinBarras - g.InicioBarras) / modulo;
        var saltos = new List<Salto>();
        for (int k = -maximo; k <= maximo; k++)
        {
            int corre = k * modulo;
            if (!SePuedeCorrer(reglas, g, capa, mueve, corre, out bool enVoladizo, out bool pregunta))
                continue;
            int desde = barra.Desde + (punto == PuntoArrastre.Derecha ? 0 : corre);
            int hasta = barra.Hasta + (punto == PuntoArrastre.Izquierda ? 0 : corre);
            int total = barra.Largo + (hasta - barra.Hasta) - (desde - barra.Desde);
            int x = punto switch { PuntoArrastre.Izquierda => desde, PuntoArrastre.Derecha => hasta, _ => (desde + hasta) / 2 };
            IReadOnlyList<CambioArrastre> cambios = corre == 0 ? []
                : mueve.Select(t => new CambioArrastre("movido", t.Id, t.Desde + corre, EnVoladizo: enVoladizo)).ToList();
            saltos.Add(new Salto(M(x), M(hasta - desde), M(total), Rotulo.De(barra.Grupos, hasta - desde, total), pregunta, cambios));
        }
        return saltos;
    }

    /// <summary>
    /// Si los traslapos <paramref name="mueve"/> se pueden correr <paramref name="corre"/> mm, con las reglas con que el
    /// motor los aplica (<see cref="AplicadorCambios"/>). <paramref name="pregunta"/>: cae en el voladizo y antes no estaba.
    /// </summary>
    private static bool SePuedeCorrer(Reglas reglas, GeometriaPortico g, SolucionCapa capa, IReadOnlyList<Traslapo> mueve, int corre,
        out bool enVoladizo, out bool pregunta)
    {
        enVoladizo = pregunta = false;
        int primero = capa.Traslapos.ToList().IndexOf(mueve[0]);
        // crece la barra de la izquierda del primer traslapo y se acorta la de la derecha del último; la del medio solo
        // se traslada
        Barra izquierda = capa.Barras[primero], derecha = capa.Barras[primero + mueve.Count];
        if (!AplicadorCambios.SaleDeLaVarilla(reglas, izquierda.Largo, izquierda.Largo + corre)
            || !AplicadorCambios.SaleDeLaVarilla(reglas, derecha.Largo, derecha.Largo - corre))
            return false;
        foreach (Traslapo t in mueve)
        {
            MotivoBloqueo donde = AplicadorCambios.DondeQueda(g, t, t.Desde + corre, t.Hasta + corre, enVoladizo: false);
            if (donde == MotivoBloqueo.TraslapoEnVoladizo)
            {
                enVoladizo = true;
                pregunta |= AplicadorCambios.DondeQueda(g, t, t.Desde, t.Hasta, enVoladizo: false) != MotivoBloqueo.TraslapoEnVoladizo;
            }
            else if (donde != MotivoBloqueo.Ninguno)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Una punta de un bastón mueve solo esa punta, y el largo total salta de módulo en módulo; el centro lo corre
    /// entero de 5 cm en 5 cm. Se puede montar sobre otros bastones; solo queda dentro de la viga y mide al menos un
    /// módulo. Cerca del borde se pega (imán) y el total sube al módulo con el sobrante en la otra punta (decisión 8).
    /// </summary>
    private static IReadOnlyList<Salto> DeBaston(EntradaVigas e, GeometriaPortico g, Baston b, PuntoArrastre punto)
    {
        if (punto == PuntoArrastre.Centro)
        {
            // de 5 cm en 5 cm desde donde está, y pegado a cada borde
            int min = g.InicioBarras - b.Desde, max = g.FinBarras - b.Hasta;
            IEnumerable<int> corridas = Enumerable.Range(min / PasoPosicion, max / PasoPosicion - min / PasoPosicion + 1)
                .Select(j => j * PasoPosicion).Append(min).Append(max).Distinct().Order();
            return corridas.Select(c => EnPuntas(e, g, b, b.Desde + c, b.Hasta + c, (b.Desde + b.Hasta) / 2 + c)).ToList();
        }

        int modulo = e.Reglas.Modulo;
        bool izquierda = punto == PuntoArrastre.Izquierda;
        int fija = izquierda ? b.Hasta : b.Desde;
        int ganchoFijo = izquierda ? b.GanchoFin : b.GanchoInicio;
        int borde = izquierda ? g.InicioBarras : g.FinBarras;
        var puntas = new List<(int Desde, int Hasta)> { (b.Desde, b.Hasta) };
        // la punta suelta no lleva gancho: el total salta de módulo en módulo hasta que la punta llega al imán
        for (int total = modulo; ; total += modulo)
        {
            int recto = total - ganchoFijo;
            if (recto < modulo)
                continue;
            int suelta = izquierda ? fija - recto : fija + recto;
            if (izquierda ? suelta <= borde + Iman : suelta >= borde - Iman)
            {
                puntas.Add(Pegada(e, g, b, izquierda));
                break;
            }
            puntas.Add(izquierda ? (suelta, fija) : (fija, suelta));
        }
        return puntas.Distinct().OrderBy(p => izquierda ? p.Desde : p.Hasta)
            .Select(p => EnPuntas(e, g, b, p.Desde, p.Hasta, izquierda ? p.Desde : p.Hasta)).ToList();
    }

    /// <summary>La punta pegada al borde: el total con su gancho sube al módulo y el sobrante va a la otra punta.</summary>
    private static (int Desde, int Hasta) Pegada(EntradaVigas e, GeometriaPortico g, Baston b, bool izquierda)
    {
        int desde = izquierda ? g.InicioBarras : b.Desde, hasta = izquierda ? b.Hasta : g.FinBarras;
        int total = hasta - desde + Ganchos(e, g, b, desde, hasta);
        int modulo = e.Reglas.Modulo, sobra = (total + modulo - 1) / modulo * modulo - total;
        return izquierda ? (desde, Math.Min(g.FinBarras, hasta + sobra)) : (Math.Max(g.InicioBarras, desde - sobra), hasta);
    }

    /// <summary>El salto del bastón con esas puntas; <paramref name="x"/> es dónde queda el punto arrastrado.</summary>
    private static Salto EnPuntas(EntradaVigas e, GeometriaPortico g, Baston b, int desde, int hasta, int x)
    {
        int total = hasta - desde + Ganchos(e, g, b, desde, hasta);
        IReadOnlyList<CambioArrastre> cambios = (desde, hasta) == (b.Desde, b.Hasta) ? [] : [new CambioArrastre("puntas", b.Id, desde, hasta)];
        return new Salto(M(x), M(hasta - desde), M(total), Rotulo.De([b.Grupo], hasta - desde, total), false, cambios);
    }

    /// <summary>Los ganchos del bastón con esas puntas: solo los lleva una punta que llega al borde, como en el motor.</summary>
    private static int Ganchos(EntradaVigas e, GeometriaPortico g, Baston b, int desde, int hasta)
    {
        ReglaDiametro regla = e.Reglas.PorDiametro[b.Grupo.Diametro];
        return e.Reglas.LargoGanchoRedondeado(regla, CalculadorBastones.GanchoDe(e, b.Id + ".inicio", desde <= g.InicioBarras))
            + e.Reglas.LargoGanchoRedondeado(regla, CalculadorBastones.GanchoDe(e, b.Id + ".fin", hasta >= g.FinBarras));
    }

    private static double M(int mm) => mm / 1000.0;
}
