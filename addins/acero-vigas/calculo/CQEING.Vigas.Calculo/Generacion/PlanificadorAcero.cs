using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Seccion;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Generacion;

/// <summary>
/// Convierte el cálculo del pórtico (<see cref="CalculoPortico"/>, el mismo del tablero) en lo que se crea en Revit (#85).
/// Decisiones del plan docs/planes/2026-10-19-generar-acero-vigas-85.md:
/// <list type="bullet">
/// <item>1: cada barra calculada sale una vez por cada barra de su capa en el corte, en todas las filas;</item>
/// <item>2: el traslapo va al lado: la segunda, cuarta… barra de cada capa se corre un diámetro hacia el centro;</item>
/// <item>3: el host es la viga del punto medio de la parte recta, o la más cercana.</item>
/// </list>
/// </summary>
public static class PlanificadorAcero
{
    /// <summary>Null si el pórtico no se puede generar (como dice el tablero) o no se sabe dónde está en el modelo.</summary>
    public static PlanAcero? Planificar(string idPortico, PorticoLeido p, OpcionesTablero o, Reglas r)
    {
        if (p.Ubicacion is not { } u || CalculoPortico.Calcular(p, o, r) is not { SePuedeGenerar: true } c)
            return null;

        var barras = new List<PiezaLongitudinal>();
        foreach (SolucionCapa capa in new[] { c.Vigas.Superior!, c.Vigas.Inferior! })
        {
            CaraSeccion cara = capa.Capa == Posicion.Superior ? c.Estribos.Seccion.Superior : c.Estribos.Seccion.Inferior;
            List<BarraEnSeccion> corte = cara.Filas.SelectMany(f => f).ToList();
            List<Barra> enOrden = capa.Barras.OrderBy(b => b.Desde).ToList();
            for (int i = 0; i < enOrden.Count; i++)
            {
                Barra b = enOrden[i];
                bool alLado = i % 2 == 1;
                barras.AddRange(corte.Select(s => new PiezaLongitudinal(b.Id, b.Capa, s.Diametro,
                    alLado ? AlLado(s, u.Ancho, r) : s.X, s.Y, b.Desde, b.Hasta, b.TipoInicio, b.TipoFin, Host(u, b.Desde, b.Hasta), b.Largo)));
            }
        }

        var bastones = new List<PiezaLongitudinal>();
        var sinLugar = new List<string>();
        foreach (Baston b in c.Vigas.Bastones)
        {
            UbicacionBastones enCorte = UbicadorBastones.Ubicar(c.Seccion, b.Capa, [b.Grupo]);
            if (enCorte.Error != ErrorCara.Ninguno || enCorte.Barras.Count == 0)
            {
                sinLugar.Add(b.Id);
                continue;
            }
            bastones.AddRange(enCorte.Barras.Select(s => new PiezaLongitudinal(b.Id, b.Capa, s.Barra.Diametro, s.Barra.X, s.Barra.Y,
                b.Desde, b.Hasta, b.TipoInicio, b.TipoFin, Host(u, b.Desde, b.Hasta), b.Largo)));
        }

        List<GrupoEstribos> estribos = Estribos(c.Estribos, u);
        List<int> diametros = barras.Concat(bastones).Select(x => x.Diametro).Concat(estribos.Select(e => e.Diametro)).Distinct().Order().ToList();
        return new PlanAcero(idPortico, barras, bastones, sinLugar, estribos, diametros);
    }

    /// <summary>Una corrida por cada tramo y estribo, con las caras del corte (las mismas en todo el pórtico).</summary>
    private static List<GrupoEstribos> Estribos(ResultadoEstribos re, UbicacionPortico u)
    {
        var grupos = new List<GrupoEstribos>();
        foreach (EstriboEnPortico e in re.Estribos)
        {
            if (re.EnSeccion.FirstOrDefault(s => s.Id == e.Id) is not { } caras)
                continue;
            grupos.AddRange(CorridasEstribos.Partir(e.Posiciones).Select(c => new GrupoEstribos(e.Id, e.Tramo, e.Tipo, e.Diametro, e.Gancho,
                caras.Izquierda, caras.Derecha, caras.Abajo, caras.Arriba, c,
                MarcoPortico.VigaEn(u, c.Primero + (c.Cantidad - 1) * c.Separacion / 2) ?? throw new InvalidOperationException("El pórtico no tiene vigas."))));
        }
        return grupos;
    }

    /// <summary>Un diámetro hacia el centro del corte; la barra que está justo en el centro, hacia +X.</summary>
    private static double AlLado(BarraEnSeccion s, double ancho, Reglas r)
    {
        double d = r.PorDiametro[s.Diametro].Diametro;
        return s.X > ancho / 2 ? s.X - d : s.X + d;
    }

    private static string Host(UbicacionPortico u, int desde, int hasta) =>
        MarcoPortico.VigaEn(u, (desde + hasta) / 2.0) ?? throw new InvalidOperationException("El pórtico no tiene vigas.");
}
