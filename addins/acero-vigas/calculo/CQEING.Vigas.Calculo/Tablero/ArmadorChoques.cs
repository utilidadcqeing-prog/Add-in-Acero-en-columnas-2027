using System.Globalization;
using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>
/// Los choques del pórtico para el alzado del tablero (paso 3 del plan docs/planes/2026-10-22-choques-entre-barras-vigas.md):
/// los de <see cref="DetectorChoques"/> sobre los trazos de la vista previa 3D, con el texto y la pista de la decisión 4.
/// </summary>
public static class ArmadorChoques
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <param name="doblados">Los del modelo, para marcar lo mismo que la vista previa 3D; null = los de las reglas.</param>
    public static IReadOnlyList<ChoqueVista> Armar(PlanAcero plan, UbicacionPortico u, Reglas r, IReadOnlyDictionary<int, DobladoBarra>? doblados = null) =>
        Detectar(plan, u, r, doblados).Select(c => Vista(c, u)).ToList();

    /// <summary>Los choques del plan en el orden del alzado, de izquierda a derecha: el índice de cada uno es el de la vista.</summary>
    public static IReadOnlyList<Choque> Detectar(PlanAcero plan, UbicacionPortico u, Reglas r, IReadOnlyDictionary<int, DobladoBarra>? doblados) =>
        DetectorChoques.Detectar(PreviaAcero.Trazos(plan, u, r, doblados)).OrderBy(c => MarcoPortico.EnPortico(u, c.Punto)).ToList();

    /// <summary>
    /// El punto en el alzado. Va sobre la pieza longitudinal (decisión 3): la barra en la que se mete el estribo, o la
    /// que se mete (el bastón contra una barra, o una de dos barras).
    /// </summary>
    public static ChoqueVista Vista(Choque c, UbicacionPortico u)
    {
        string mm = c.SeMeteMm.ToString("0.0", Inv);
        string veces = c.Veces > 1 ? $" · ×{c.Veces} estribos" : "";
        string texto = $"{Nombre(c.A, true)} (#{c.A.Numero}) se mete {mm} mm en {Nombre(c.B, false)} (#{c.B.Numero}){veces}";
        (string elemento, string pista) = c.Tipo switch
        {
            TipoChoque.EstriboConBarra => (c.B.Pieza, c.A.Tipo == TipoTrazo.Grapa
                ? $"Prueba la grapa en C o en S, otro gancho o mover {Nombre(c.B, false)}"
                : $"Prueba otro gancho o mover {Nombre(c.B, false)}"),
            TipoChoque.EntreEstribos => (c.A.Pieza, "Prueba otro gancho en alguno de los dos"),
            _ => (c.A.Pieza, $"Mueve o acorta {Nombre(c.A, false)}"),
        };
        return new ChoqueVista(Math.Round(MarcoPortico.EnPortico(u, c.Punto) / 1000, 4), elemento, texto, pista, c.Veces, Math.Round(c.SeMeteMm, 1));
    }

    /// <summary>"La grapa E1 de A-B", "el bastón BS1", "la barra S2"…</summary>
    private static string Nombre(PiezaChocada p, bool alInicio)
    {
        string nombre = p.Tipo switch
        {
            TipoTrazo.Grapa => $"la grapa {p.Pieza}{DelTramo(p.Tramo)}",
            TipoTrazo.Estribo => $"el estribo {p.Pieza}{DelTramo(p.Tramo)}",
            TipoTrazo.Baston => $"el bastón {p.Pieza}",
            _ => $"la barra {p.Pieza}",
        };
        return alInicio ? char.ToUpperInvariant(nombre[0]) + nombre[1..] : nombre;
    }

    /// <summary>" de A-B" o " del voladizo D".</summary>
    private static string DelTramo(string? tramo) => tramo switch
    {
        null or "" => "",
        _ when tramo.StartsWith("Voladizo", StringComparison.Ordinal) => " del v" + tramo[1..],
        _ => " de " + tramo,
    };
}
