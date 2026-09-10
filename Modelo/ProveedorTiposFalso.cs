namespace practicacq.Modelo;

using System.Collections.Generic;

/// <summary>
/// Mismo papel que <see cref="ProveedorContextoFalso"/> pero para los tipos de
/// barra y de gancho: datos inventados para poder abrir la ventana sin Revit
/// (ver <c>tools/PreviewUI</c>). Los nombres imitan los del modelo real de la
/// empresa -- conviven nombres imperiales ("#3 COL") con los de gancho tal cual
/// los escribe Revit, porque la ventana los guarda como TEXTO y ese texto es lo
/// que después se busca en el documento.
/// </summary>
public sealed class ProveedorTiposFalso : IProveedorTipos
{
    public IReadOnlyList<string> ListarTiposBarra() => new List<string>
    {
        "#2 COL",
        "#3 COL",
        "#4 COL",
        "#5 COL",
        "#6 COL",
        "#7 COL",
        "#8 COL",
    };

    public IReadOnlyList<TipoGancho> ListarGanchos() => new List<TipoGancho>
    {
        new("Standard - 180 deg.", EstiloGancho.Estandar, 180),
        new("Standard - 90 deg.", EstiloGancho.Estandar, 90),
        new("Stirrup/Tie - 135 deg.", EstiloGancho.Estribo, 135),
        new("Stirrup/Tie - 90 deg.", EstiloGancho.Estribo, 90),
        new("Stirrup/Tie Seismic - 135 deg.", EstiloGancho.Estribo, 135),
    };
}
