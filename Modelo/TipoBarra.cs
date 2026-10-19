namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Un tipo de barra del documento con lo mínimo para poder elegirlo bien: su
/// nombre (que es lo que <see cref="ConfigTramo.TipoBarraNombre"/> y compañía
/// guardan como texto) y su diámetro nominal en milímetros.
///
/// El diámetro está acá porque el valor POR DEFECTO de los combos no se puede
/// elegir por nombre: los nombres varían entre modelos ("#3", "#3 COL", "3"…),
/// y "el primero alfabéticamente" -- lo que se hacía hasta el 11-sep -- daba el
/// #10 en el modelo de William. Mismo patrón que <see cref="TipoGancho"/>.
/// </summary>
public sealed record TipoBarra(string Nombre, double DiametroMm)
{
    /// <summary>
    /// El tipo de barra que corresponde por defecto para esa varilla del cuadro
    /// de la empresa (#3 para el estribo, #4 para la longitudinal, confirmado
    /// por William el 11-sep). Si el documento no tiene ninguna de ese
    /// diámetro devuelve null: quien llama decide si cae a la primera de la
    /// lista, pero avisando -- elegir otra barra en silencio es justamente el
    /// problema que esto viene a resolver.
    /// </summary>
    /// <remarks>
    /// Primero el tipo propio del add-in ("#4 CQEING"), si el modelo lo tiene;
    /// si no (el usuario dijo que no a agregarlos), el primero de ese diámetro,
    /// como antes del 19-oct.
    /// </remarks>
    public static TipoBarra? PorDefecto(IEnumerable<TipoBarra> barras, int numeroVarilla)
    {
        if (CuadroVarillas.DiametroDe(numeroVarilla) is not double diametro) return null;

        string propio = CuadroVarillas.NombrePropio(numeroVarilla);
        return barras.FirstOrDefault(b => b.Nombre == propio)
            ?? barras.FirstOrDefault(b => Math.Abs(b.DiametroMm - diametro) < CuadroVarillas.ToleranciaMm);
    }
}
