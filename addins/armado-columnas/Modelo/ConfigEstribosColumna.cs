namespace practicacq.Modelo;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Los estribos de TODA una columna, como los pide la interfaz web (#209):
/// una sola config para los tramos libres, el espaciado de los nudos y, si
/// hace falta, algún tramo distinto. Reemplaza el ir tramo por tramo de la
/// ventana vieja (decisión 4 de docs/planes/2026-10-29-interfaz-web-columnas.md).
///
/// Los generadores no lo conocen: <see cref="ATramos"/> lo traduce a la
/// <c>List&lt;ConfigTramo?&gt;</c> de siempre (<see cref="ColumnaPlan.Tramos"/>).
/// </summary>
public sealed class ConfigEstribosColumna
{
    /// <summary>La config de los tramos a la vista, de viga a viga: uniforme o confinada.</summary>
    public ConfigTramo TramosLibres { get; set; } = new();

    /// <summary>
    /// Cada cuánto va un estribo en los nudos, en metros. Solo cuenta con
    /// <see cref="TramosLibres"/> en confinado: en uniforme, los nudos llevan el
    /// mismo espaciado que la columna (decisión 5).
    /// </summary>
    public double NudoEspaciado { get; set; }

    /// <summary>Barra de los nudos. <c>null</c> = la de <see cref="TramosLibres"/>.</summary>
    public string? NudoBarraNombre { get; set; }

    /// <summary>Los tramos que el usuario marcó «Este tramo distinto», por índice de tramo.</summary>
    public Dictionary<int, ConfigTramo> Excepciones { get; set; } = new();

    /// <summary>
    /// Desde la config de un solo tramo (la ventana vieja, o una configuración
    /// guardada con la 1.4.1): los nudos arrancan con el espaciado del centro
    /// si es confinada, nunca con el confinado (decisión 5).
    /// </summary>
    public static ConfigEstribosColumna Desde(ConfigTramo tramo) => new()
    {
        TramosLibres = Copiar(tramo),
        NudoEspaciado = tramo.Modo == ModoEstribo.Confinado ? tramo.EspaciadoNoConfinado : tramo.EspaciadoUniforme,
    };

    /// <summary>
    /// Al revés de <see cref="ATramos"/>: desde lo que se generó tramo por tramo
    /// (<see cref="ColumnaGenerada.Tramos"/>, o la ventana vieja). Los tramos
    /// libres toman el primer tramo libre, los nudos el espaciado del primer
    /// nudo, y lo que no coincide con eso queda como excepción. Así
    /// <c>DesdeTramos(c, t).ATramos(c)</c> devuelve <paramref name="tramos"/>
    /// en todo tramo que no sea <c>null</c> ni el de la zapata.
    /// </summary>
    public static ConfigEstribosColumna DesdeTramos(ColumnaContexto contexto, IReadOnlyList<ConfigTramo?> tramos, ConfigTramo porDefecto)
    {
        ConfigTramo? DelTipo(TipoTramo tipo) => contexto.Tramos
            .Where(t => t.Tipo == tipo && t.Indice < tramos.Count)
            .Select(t => tramos[t.Indice])
            .FirstOrDefault(c => c is not null);

        ConfigEstribosColumna estribos = Desde(DelTipo(TipoTramo.Libre) ?? tramos.FirstOrDefault(c => c is not null) ?? porDefecto);
        if (estribos.TramosLibres.Modo == ModoEstribo.Confinado && DelTipo(TipoTramo.DentroDeViga) is { } nudo)
            estribos.NudoEspaciado = nudo.EspaciadoUniforme;

        List<ConfigTramo?> traducidos = estribos.ATramos(contexto);
        foreach (Tramo tramo in contexto.Tramos)
        {
            if (tramo.Tipo == TipoTramo.ContraZapata || tramo.Indice >= tramos.Count) continue;
            if (tramos[tramo.Indice] is ConfigTramo real && !ComparadorConfig.Igual(real, traducidos[tramo.Indice]))
                estribos.Excepciones[tramo.Indice] = Copiar(real);
        }
        return estribos;
    }

    public ConfigEstribosColumna Copiar() => new()
    {
        TramosLibres = Copiar(TramosLibres),
        NudoEspaciado = NudoEspaciado,
        NudoBarraNombre = NudoBarraNombre,
        Excepciones = Excepciones.ToDictionary(e => e.Key, e => Copiar(e.Value)),
    };

    /// <summary>
    /// Un <see cref="ConfigTramo"/> por tramo de <paramref name="contexto"/>,
    /// con el mismo índice, nunca <c>null</c>. Los nudos y el tramo contra
    /// zapata salen siempre uniformes (#58); el contra zapata no queda
    /// <c>null</c> porque <c>null</c> es «no se genera» (decisión 6), y el
    /// generador igual se lo salta. Las excepciones con un índice que la
    /// columna no tiene se ignoran.
    /// </summary>
    public List<ConfigTramo?> ATramos(ColumnaContexto contexto)
    {
        double nudoCada = TramosLibres.Modo == ModoEstribo.Uniforme ? TramosLibres.EspaciadoUniforme : NudoEspaciado;

        var resultado = new List<ConfigTramo?>(contexto.Tramos.Count);
        foreach (Tramo tramo in contexto.Tramos)
        {
            Excepciones.TryGetValue(tramo.Indice, out ConfigTramo? excepcion);

            ConfigTramo config = tramo.Tipo switch
            {
                TipoTramo.Libre => Copiar(excepcion ?? TramosLibres),
                TipoTramo.DentroDeViga when excepcion is not null => EnUniforme(excepcion, excepcion.EspaciadoUniforme),
                _ => EnUniforme(TramosLibres, nudoCada, NudoBarraNombre),
            };
            resultado.Add(config);
        }
        return resultado;
    }

    private static ConfigTramo EnUniforme(ConfigTramo origen, double cada, string? barra = null)
    {
        ConfigTramo copia = Copiar(origen);
        copia.Modo = ModoEstribo.Uniforme;
        copia.EspaciadoUniforme = cada;
        if (barra is not null) copia.TipoBarraNombre = barra;
        return copia;
    }

    private static ConfigTramo Copiar(ConfigTramo c) => new()
    {
        Modo = c.Modo,
        TipoBarraNombre = c.TipoBarraNombre,
        TipoGanchoNombre = c.TipoGanchoNombre,
        EspaciadoUniforme = c.EspaciadoUniforme,
        LongitudConfinamiento = c.LongitudConfinamiento,
        EspaciadoConfinado = c.EspaciadoConfinado,
        EspaciadoNoConfinado = c.EspaciadoNoConfinado,
    };
}
