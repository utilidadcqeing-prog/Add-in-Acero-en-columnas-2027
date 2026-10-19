namespace practicacq.Modelo;

using System;
using System.Globalization;

/// <summary>
/// El nombre de los tipos de empalme propios del add-in ("CQ Traslapo #4 · 0,90 m"),
/// sin Revit para poder probarlo (ver <c>Logica/TiposEmpalme</c>, que los crea).
///
/// El nombre identifica al tipo, y el tipo fija UN largo de solape: dos
/// traslapos que difieren en milímetros no pueden compartir nombre, porque
/// ajustar el tipo para uno le cambiaría el largo al otro. Pasa seguido: el
/// traslapo de más arriba absorbe los milímetros sueltos de la altura real
/// (revisión final del 17-oct). Por eso el largo va al centímetro cuando es
/// redondo y al milímetro cuando no.
/// </summary>
public static class NombreEmpalme
{
    public const string PrefijoPropio = "CQ Traslapo #";

    private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-CO");

    public static string Propio(int numeroVarilla, double largoMetros)
    {
        int milimetros = (int)Math.Round(largoMetros * 1000);
        string largo = (milimetros / 1000.0).ToString(milimetros % 10 == 0 ? "0.00" : "0.000", Espanol);
        return $"{PrefijoPropio}{numeroVarilla} · {largo} m";
    }
}
