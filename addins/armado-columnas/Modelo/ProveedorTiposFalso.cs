namespace practicacq.Modelo;

using System.Collections.Generic;
using System.Linq;

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
    /// <summary>
    /// Los diámetros salen del cuadro de la empresa (<see cref="CuadroVarillas"/>),
    /// no inventados: así la vista previa ejercita de verdad la elección del
    /// tipo por defecto por diámetro (#3 para el estribo, #4 para la
    /// longitudinal), que es lo que hace la ventana contra el documento real.
    /// El orden es alfabético a propósito, igual que en el proveedor real --
    /// si el default se eligiera por "el primero de la lista" saldría el #2.
    /// </summary>
    public IReadOnlyList<TipoBarra> ListarBarras() => CuadroVarillas.Cuadro
        .Select(v => new TipoBarra($"#{v.Numero} COL", v.DiametroMm)
        {
            // Doblados como los medidos el 8-oct en el modelo de prueba: 4 d y
            // 6 d, salvo la #2 (7,81 d), que así queda fuera de la lista de
            // estribo y ejercita la nota del #64.
            DobladoEstriboMm = (v.Numero == 2 ? 7.81 : 4) * v.DiametroMm,
            DobladoGanchoMm = (v.Numero == 2 ? 7.81 : 6) * v.DiametroMm,
        })
        .OrderBy(b => b.Nombre)
        .ToList();

    public IReadOnlyList<TipoGancho> ListarGanchos() => new List<TipoGancho>
    {
        new("Standard - 180 deg.", EstiloGancho.Estandar, 180),
        new("Standard - 90 deg.", EstiloGancho.Estandar, 90),
        new("Stirrup/Tie - 135 deg.", EstiloGancho.Estribo, 135),
        new("Stirrup/Tie - 90 deg.", EstiloGancho.Estribo, 90),
        new("Stirrup/Tie Seismic - 135 deg.", EstiloGancho.Estribo, 135),
    };
}
