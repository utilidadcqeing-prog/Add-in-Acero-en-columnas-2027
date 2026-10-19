using System.Collections.Generic;
using System.Linq;

namespace CQEING_CuadroCantidades.Core.Application.Services;

/// <summary>
/// Contorno del lote en planta: uno o varios polígonos cerrados (las líneas de propiedad del modelo). Un punto está
/// dentro del lote si cae dentro de alguno. Coordenadas del proyecto en pies, sin la Z.
/// </summary>
public sealed class LotContour
{
    private readonly IReadOnlyList<IReadOnlyList<(double X, double Y)>> _polygons;

    public LotContour(IReadOnlyList<IReadOnlyList<(double X, double Y)>> polygons)
    {
        _polygons = polygons.Where(polygon => polygon.Count >= 3).ToList();
    }

    /// <summary>
    /// Indica que no hay ningún polígono válido.
    /// </summary>
    public bool IsEmpty => _polygons.Count == 0;

    /// <summary>
    /// Indica si el punto cae dentro de alguno de los polígonos.
    /// </summary>
    public bool Contains(double x, double y) => _polygons.Any(polygon => IsInside(polygon, x, y));

    // Regla par-impar: un rayo horizontal desde el punto cruza un número impar de lados si el punto está dentro
    private static bool IsInside(IReadOnlyList<(double X, double Y)> polygon, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (xi, yi) = polygon[i];
            var (xj, yj) = polygon[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }

        return inside;
    }
}
