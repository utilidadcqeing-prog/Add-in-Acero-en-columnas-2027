using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Nucleo;

namespace CQEING.Vigas.Calculo.Vigas;

/// <summary>
/// Una capa ya resuelta por el núcleo, con lo necesario para convertirla a lo que pinta el tablero: sus grupos de
/// barras, el diámetro que manda (el mayor) y los parámetros con que se resolvió.
/// </summary>
internal sealed record CapaCalculada(
    EntradaVigas Entrada,
    Posicion Capa,
    IReadOnlyList<GrupoBarras> Grupos,
    int Diametro,
    ParametrosCapa Parametros,
    CapaResuelta Resuelta)
{
    public string Prefijo => Capa == Posicion.Superior ? "S" : "I";

    public double KgPorMetro => Grupos.Sum(g => g.Cantidad * Entrada.Reglas.PorDiametro[g.Diametro].KgPorMetro);

    public double KgDe(int largo) => largo / 1000.0 * KgPorMetro;

    /// <summary>Varilla de la que sale una barra de ese largo.</summary>
    public int VarillaPara(int largo) => largo > Entrada.Reglas.Varilla ? Entrada.Reglas.VarillaLarga : Entrada.Reglas.Varilla;

    /// <summary>Convierte <paramref name="resuelta"/> (por defecto la automática); <paramref name="manuales"/> son índices de barra.</summary>
    public SolucionCapa Convertir(CapaResuelta? resuelta = null, IReadOnlySet<int>? manuales = null)
    {
        resuelta ??= Resuelta;
        Portico portico = Entrada.Portico;
        Apoyo primero = portico.Apoyos[0], ultimo = portico.Apoyos[^1];
        IReadOnlyList<BarraLinea> lineas = resuelta.Solucion.Barras;

        var barras = lineas.Select((b, i) => new Barra(
            Id: $"{Prefijo}{i + 1}",
            Capa: Capa,
            Desde: b.Desde,
            Hasta: b.Hasta,
            GanchoInicio: i == 0 ? Parametros.GanchoInicio : 0,
            GanchoFin: i == lineas.Count - 1 ? Parametros.GanchoFin : 0,
            Largo: b.Largo,
            Varilla: VarillaPara(b.Largo),
            Manual: manuales?.Contains(i) ?? false,
            LlegaAlVoladizo: (portico.VoladizoInicio > 0 && b.Desde < primero.CaraIzquierda)
                || (portico.VoladizoFinal > 0 && b.Hasta > ultimo.CaraDerecha),
            Grupos: Grupos,
            Kg: KgDe(b.Largo),
            TipoInicio: i == 0 ? Parametros.TipoInicio : TipoGancho.Sin,
            TipoFin: i == lineas.Count - 1 ? Parametros.TipoFin : TipoGancho.Sin)).ToList();

        var traslapos = resuelta.Solucion.Traslapos.Select((t, i) => new Traslapo(
            Id: $"{barras[i].Id}–{barras[i + 1].Id}",
            BarraIzquierda: barras[i].Id,
            BarraDerecha: barras[i + 1].Id,
            Capa: Capa,
            Luz: resuelta.Luces[i].Nombre,
            Desde: t.Desde,
            Hasta: t.Hasta,
            Minimo: Parametros.TraslapoMinimo,
            Diametro: Diametro,
            Desvio: t.Desvio,
            Manual: false)).ToList();

        return new SolucionCapa(Capa, Parametros.Referencia, barras, traslapos);
    }
}
