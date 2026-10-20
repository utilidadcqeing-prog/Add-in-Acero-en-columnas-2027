namespace practicacq.Modelo;

/// <summary>
/// Cómo se lee la cantidad de barras por cara en el panel. El número de
/// «N. barras X / Y» es el total de las DOS caras enfrentadas y se reparte
/// mitad y mitad (<see cref="ConfigLongitudinal.Anillo"/>): "4 (2 + 2)".
/// </summary>
public static class TextoBarras
{
    public static string PorCara(int total) => $"{total} ({total / 2} + {total / 2})";
}
