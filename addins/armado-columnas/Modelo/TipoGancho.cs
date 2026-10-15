namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A qué barras se les puede poner un gancho. En Revit un <c>RebarHookType</c>
/// solo se aplica a barras de su mismo estilo, así que ofrecer los dos estilos
/// mezclados en un combo es ofrecer ganchos que van a fallar: el error aparece
/// recién al generar, cuando <c>Rebar.CreateFromCurves</c> devuelve null.
///
/// El enum vive acá y no es <c>RebarStyle</c> a propósito: <c>Modelo/</c> y
/// <c>Vistas/</c> no referencian la API de Revit (ver <see cref="IProveedorTipos"/>).
/// La traducción la hace el proveedor real.
/// </summary>
public enum EstiloGancho
{
    /// <summary>Estribos y tirantes (<c>RebarStyle.StirrupTie</c>).</summary>
    Estribo,

    /// <summary>Barras longitudinales (<c>RebarStyle.Standard</c>).</summary>
    Estandar,
}

/// <summary>
/// Un tipo de gancho del documento, con lo mínimo para poder elegirlo bien:
/// a qué estilo de barra aplica y con cuántos grados dobla.
/// </summary>
public sealed record TipoGancho(string Nombre, EstiloGancho Estilo, double AnguloGrados)
{
    /// <summary>
    /// El gancho que corresponde por defecto para ese estilo: el del ángulo
    /// pedido si está (135° para el estribo, 90° para el anclaje en la
    /// zapata, confirmado por William en la reunión del 10-sep), y si no,
    /// cualquiera del estilo correcto antes que ninguno.
    /// </summary>
    public static TipoGancho? PorDefecto(IEnumerable<TipoGancho> ganchos, EstiloGancho estilo, double anguloGrados)
    {
        List<TipoGancho> delEstilo = ganchos.Where(g => g.Estilo == estilo).ToList();

        return delEstilo.FirstOrDefault(g => Math.Abs(g.AnguloGrados - anguloGrados) < 1)
            ?? delEstilo.FirstOrDefault();
    }
}
