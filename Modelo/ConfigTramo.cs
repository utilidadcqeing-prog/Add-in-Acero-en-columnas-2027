namespace practicacq.Modelo;

using System.Text.Json.Serialization;

/// <summary>Modo de armado de un tramo. Excluyentes: ver la máquina de estados del panel (L3).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModoEstribo
{
    Uniforme,
    Confinado
}

/// <summary>
/// Lo que el panel de L3 termina de armar para un tramo — Uniforme o Confinado,
/// nunca los dos campos a la vez tienen sentido, pero se guardan los dos
/// separados en vez de con una unión discriminada más elaborada porque esto
/// tiene que poder ir directo a JSON (ver L5) sin un conversor a mano.
///
/// El tipo de barra y de gancho viajan por nombre (<see cref="TipoBarraNombre"/>,
/// <see cref="TipoGanchoNombre"/>), no como <c>RebarBarType</c>/<c>RebarHookType</c>:
/// esos son tipos de la API de Revit, y este POCO tiene que poder existir (y
/// serializarse) sin Revit instalado. El nombre es la misma clave que ya usa
/// <c>TipoBarra.Tipo.Name</c>/<c>TipoGancho.Tipo.Name</c> del lado de la ventana
/// conectada a Revit, así que resolver de uno a otro es una búsqueda por nombre,
/// no una conversión.
/// </summary>
public sealed class ConfigTramo
{
    public ModoEstribo Modo { get; set; } = ModoEstribo.Uniforme;

    public string? TipoBarraNombre { get; set; }
    public string? TipoGanchoNombre { get; set; }

    // ---- Modo Uniforme ----

    /// <summary>Espaciado constante entre estribos, en metros. Solo aplica en modo Uniforme.</summary>
    public double EspaciadoUniforme { get; set; }

    // ---- Modo Confinado ----

    /// <summary>Longitud de confinamiento (L1 de la norma), en metros, en cada extremo del tramo.</summary>
    public double LongitudConfinamiento { get; set; }

    /// <summary>Espaciado en la zona confinada, en metros.</summary>
    public double EspaciadoConfinado { get; set; }

    /// <summary>
    /// Espaciado en la zona no confinada (central), en metros. L3 lo
    /// autocompleta al doble de <see cref="EspaciadoConfinado"/> cuando este
    /// cambia, pero queda editable — por eso es una propiedad con valor propio
    /// y no una de solo lectura calculada.
    /// </summary>
    public double EspaciadoNoConfinado { get; set; }

    /// <summary>El doble del espaciado confinado — el valor que L3 sugiere por defecto.</summary>
    public double EspaciadoNoConfinadoSugerido => EspaciadoConfinado * 2;
}
