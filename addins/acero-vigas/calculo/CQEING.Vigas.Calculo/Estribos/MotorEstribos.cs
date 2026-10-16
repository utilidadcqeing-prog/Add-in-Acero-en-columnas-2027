using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Seccion;

namespace CQEING.Vigas.Calculo.Estribos;

/// <summary>
/// Los estribos de un pórtico (#90, plan docs/planes/2026-10-16-estribos-vigas-90.md): el cerrado de las esquinas y
/// los adicionales en cada tramo, con su largo y sus kilos, y la sección con las barras que alinean los adicionales
/// (<see cref="AlineadorEstribos"/>). Va aparte de <see cref="MotorVigas"/> porque necesita la sección.
/// </summary>
public static class MotorEstribos
{
    /// <summary>Id del cerrado de las esquinas, el obligatorio.</summary>
    public const string IdCerrado = "E";

    /// <summary>Elemento del aviso cuando el valor no válido es de la configuración del pórtico.</summary>
    public const string General = "general";

    public static ResultadoEstribos Calcular(EntradaEstribos e)
    {
        var avisos = new List<Aviso>();
        if (!e.Seccion.Reglas.PorDiametro.ContainsKey(e.General.Diametro))
        {
            avisos.Add(new Aviso(TipoAviso.DiametroSinReglas, $"#{e.General.Diametro}"));
            return new ResultadoEstribos([], RepartidorFilas.Repartir(e.Seccion), [], avisos);
        }

        // La sección usa el diámetro del pórtico; el de un tramo solo cambia el largo y los kilos de sus estribos.
        EntradaSeccion seccion = e.Seccion with { DiametroEstribo = e.General.Diametro };
        Recubrimientos rec = seccion.Recubrimiento;
        var principal = new EstriboEnSeccion(IdCerrado, TipoEstribo.Cerrado, rec.Lados, seccion.Ancho - rec.Lados, rec.Abajo, seccion.Alto - rec.Arriba);

        bool generalValida = EsValida(e.General, General, avisos);
        IReadOnlyList<Tramo> tramos = DistribuidorEstribos.Tramos(e.Portico);
        List<ConfigEstribos?> propias = tramos.Select(t => e.PorTramo?.GetValueOrDefault(t.Nombre)).ToList();
        IReadOnlyList<(string Id, EstriboAdicional Adicional)> adicionales = Numerar(e.General, propias);
        (ResultadoSeccion corte, IReadOnlyList<EstriboEnSeccion> figuras) = AlineadorEstribos.Alinear(seccion, adicionales, avisos);
        var figuraDe = new Dictionary<EstriboAdicional, EstriboEnSeccion>(ReferenceEqualityComparer.Instance);
        foreach ((string id, EstriboAdicional a) in adicionales)
            if (figuras.FirstOrDefault(f => f.Id == id) is { } f)
                figuraDe[a] = f;

        double estriboSeccion = RepartidorFilas.Diametro(seccion, seccion.DiametroEstribo);
        var estribos = new List<EstriboEnPortico>();
        for (int k = 0; k < tramos.Count; k++)
        {
            Tramo t = tramos[k];
            ConfigEstribos? propia = propias[k];
            ConfigEstribos c = propia ?? e.General;
            if (!(propia is null ? generalValida : EsValida(propia, t.Nombre, avisos)) || ReglaDe(seccion, c, avisos) is not { } regla)
                continue;

            IReadOnlyList<double> posiciones = DistribuidorEstribos.Posiciones(t, c, regla.Diametro);
            double alto = AltoAlEje(seccion, regla);
            int gancho = Gancho(regla, c.Gancho);
            EstriboEnPortico Estribo(string id, TipoEstribo tipo, double largo) =>
                new(id, tipo, t.Nombre, c.Diametro, c.Gancho, posiciones, largo, Kg(posiciones.Count, largo, regla));

            estribos.Add(Estribo(IdCerrado, TipoEstribo.Cerrado, Largo(TipoEstribo.Cerrado, seccion.Ancho - 2 * rec.Lados - regla.Diametro, alto, gancho)));
            // Los adicionales van solo en los tramos de su configuración, con las posiciones del cerrado de ese tramo.
            foreach (EstriboAdicional a in c.Adicionales ?? [])
            {
                if (!figuraDe.TryGetValue(a, out EstriboEnSeccion? f))
                    continue;
                // Por dentro, el cerrado abraza siempre las mismas barras; por el eje, crece con el estribo del tramo.
                double ancho = f.Derecha - f.Izquierda - 2 * estriboSeccion + regla.Diametro;
                estribos.Add(Estribo(f.Id, a.Tipo, Largo(a.Tipo, ancho, alto, gancho)));
            }
        }
        return new ResultadoEstribos(estribos, corte, [principal, .. figuras], avisos);
    }

    /// <summary>
    /// <c>EA1</c>, <c>EA2</c>…: primero los adicionales del pórtico y después los de cada tramo, en el orden de los
    /// tramos. Un tramo que copia la configuración del pórtico (<c>General with { … }</c>) trae los mismos adicionales:
    /// conservan su id y no se repiten.
    /// </summary>
    private static IReadOnlyList<(string Id, EstriboAdicional Adicional)> Numerar(ConfigEstribos general, IEnumerable<ConfigEstribos?> propias)
    {
        var vistos = new HashSet<EstriboAdicional>(ReferenceEqualityComparer.Instance);
        return new[] { general }.Concat(propias.OfType<ConfigEstribos>())
            .SelectMany(c => c.Adicionales ?? [])
            .Where(vistos.Add)
            .Select((a, i) => ($"EA{i + 1}", a))
            .ToList();
    }

    /// <summary>Decisión 10: separaciones mayores que 0 y largo de zona no negativo (0 = sin zona).</summary>
    private static bool EsValida(ConfigEstribos c, string elemento, List<Aviso> avisos)
    {
        if (c.SeparacionZona > 0 && c.SeparacionCentro > 0 && c.LargoZona >= 0)
            return true;
        avisos.Add(new Aviso(TipoAviso.EstriboValorNoValido, elemento));
        return false;
    }

    /// <summary>La regla del diámetro del estribo; si no está en las reglas, avisa una sola vez por diámetro.</summary>
    private static ReglaDiametro? ReglaDe(EntradaSeccion s, ConfigEstribos c, List<Aviso> avisos)
    {
        if (s.Reglas.PorDiametro.TryGetValue(c.Diametro, out ReglaDiametro? regla))
            return regla;
        var aviso = new Aviso(TipoAviso.DiametroSinReglas, $"#{c.Diametro}");
        if (!avisos.Contains(aviso))
            avisos.Add(aviso);
        return null;
    }

    /// <summary>Alto por el eje del estribo: de recubrimiento a recubrimiento, menos un diámetro del estribo.</summary>
    private static double AltoAlEje(EntradaSeccion s, ReglaDiametro estribo) =>
        s.Alto - s.Recubrimiento.Arriba - s.Recubrimiento.Abajo - estribo.Diametro;

    private static int Gancho(ReglaDiametro r, GanchoEstribo g) => g == GanchoEstribo.De135 ? r.Estribo135.B : r.Gancho90.B;

    /// <summary>Decisión 8: el cerrado da la vuelta completa y el abierto es una sola rama; los dos llevan dos ganchos.</summary>
    private static double Largo(TipoEstribo tipo, double ancho, double alto, int gancho) =>
        (tipo == TipoEstribo.Cerrado ? 2 * (ancho + alto) : alto) + 2 * gancho;

    private static double Kg(int cantidad, double largo, ReglaDiametro r) => cantidad * largo / 1000 * r.KgPorMetro;
}
