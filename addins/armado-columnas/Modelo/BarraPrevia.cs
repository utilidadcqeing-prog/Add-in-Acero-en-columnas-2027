namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Qué pieza del armado es una barra de la vista previa 3D (#123).</summary>
public enum ClasePieza
{
    Longitudinal,

    /// <summary>El estribo principal, el que rodea toda la sección.</summary>
    Estribo,

    /// <summary>Grapas en C y en S, y estribos cerrados (cajas).</summary>
    EstriboMenor,
}

/// <summary>Qué parte de una barra es un pedazo: lo que filtran «solo ganchos» y «solo traslapos».</summary>
public enum ParteBarra
{
    Recto,

    /// <summary>Doblez y cola de anclaje.</summary>
    Gancho,

    /// <summary>Donde dos piezas de la misma posición se pisan.</summary>
    Traslapo,
}

/// <summary>A: las posiciones pares del anillo; B: las impares (traslapan en tramos distintos).</summary>
public enum GrupoTraslapo { A, B }

public enum ZonaEstribo
{
    /// <summary>Las puntas del tramo con el espaciado confinado.</summary>
    Confinada,

    /// <summary>La zona central, o todo el tramo si es uniforme.</summary>
    NoConfinada,
}

/// <summary>Un punto del modelo, en metros.</summary>
public readonly record struct Punto3(double X, double Y, double Z);

/// <summary>Un pedazo de barra: una polilínea por su eje, en metros.</summary>
public sealed record PedazoBarra(IReadOnlyList<Punto3> Puntos, ParteBarra Parte);

/// <summary>
/// Una barra de la vista previa 3D, ya clasificada para los filtros
/// (docs/planes/2026-10-21-vista-previa-3d-columnas-123.md, paso 3). Sin
/// Revit: la arma <c>Logica/ClasificadorPrevia3D</c> con lo que dejó la
/// simulación y la filtra <see cref="FiltroPrevia3D"/>.
/// </summary>
/// <param name="Grupo">Solo longitudinales.</param>
/// <param name="Posicion">Solo longitudinales.</param>
/// <param name="Zona">Solo estribos y estribos menores.</param>
public sealed record BarraPrevia(
    ClasePieza Clase, double DiametroMm, IReadOnlyList<PedazoBarra> Pedazos,
    GrupoTraslapo? Grupo = null, PosicionBarra? Posicion = null, ZonaEstribo? Zona = null);

/// <summary>Operaciones de polilínea que usan el clasificador y el filtro.</summary>
public static class Polilinea
{
    /// <summary>
    /// Parte una polilínea según la altura: lo que cae dentro de
    /// [<paramref name="zDesde"/>, <paramref name="zHasta"/>] y lo que queda
    /// afuera, cada pedazo continuo por separado, cortando los tramos que
    /// cruzan un borde.
    /// </summary>
    public static (List<List<Punto3>> Dentro, List<List<Punto3>> Fuera) PartirPorAltura(
        IReadOnlyList<Punto3> puntos, double zDesde, double zHasta)
    {
        var dentro = new List<List<Punto3>>();
        var fuera = new List<List<Punto3>>();
        if (puntos.Count == 0) return (dentro, fuera);

        bool Adentro(Punto3 p) => p.Z >= zDesde && p.Z <= zHasta;

        List<Punto3> actual = new() { puntos[0] };
        bool actualDentro = Adentro(puntos[0]);

        for (int i = 1; i < puntos.Count; i++)
        {
            Punto3 a = puntos[i - 1], b = puntos[i];

            // Los bordes que cruza este tramo, en orden desde a.
            var cortes = new List<double>();
            foreach (double z in new[] { zDesde, zHasta })
            {
                if ((a.Z - z) * (b.Z - z) < 0) cortes.Add((z - a.Z) / (b.Z - a.Z));
            }
            cortes.Sort();

            foreach (double t in cortes)
            {
                var corte = new Punto3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
                actual.Add(corte);
                (actualDentro ? dentro : fuera).Add(actual);
                actual = new List<Punto3> { corte };
                actualDentro = !actualDentro;
            }

            actual.Add(b);
        }

        (actualDentro ? dentro : fuera).Add(actual);
        return (dentro.Where(p => p.Count > 1).ToList(), fuera.Where(p => p.Count > 1).ToList());
    }

    public static double ZMin(IEnumerable<Punto3> puntos) => puntos.Min(p => p.Z);

    public static double ZMax(IEnumerable<Punto3> puntos) => puntos.Max(p => p.Z);
}
