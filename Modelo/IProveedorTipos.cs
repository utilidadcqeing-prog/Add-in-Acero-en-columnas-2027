namespace practicacq.Modelo;

/// <summary>
/// Misma idea que <see cref="IProveedorContexto"/> pero para los nombres de
/// tipo de barra y de gancho: la ventana (L2/L3) los necesita para llenar los
/// combos ("nada se teclea a mano"), y no puede leerlos directo de
/// <c>RebarBarType</c>/<c>RebarHookType</c> porque eso es API de Revit.
///
/// El lado real (J2, junto con <c>LectorContexto</c>) implementa esto leyendo
/// <c>FilteredElementCollector(doc).OfClass(typeof(RebarBarType))</c> /
/// <c>RebarHookType</c> y devolviendo <c>.Name</c> de cada uno — el mismo
/// nombre que <see cref="ConfigTramo.TipoBarraNombre"/> y
/// <see cref="ConfigLongitudinal.TipoBarraEsquinasNombre"/> guardan como texto.
/// </summary>
public interface IProveedorTipos
{
    IReadOnlyList<string> ListarTiposBarra();
    IReadOnlyList<string> ListarTiposGancho();
}

/// <summary>
/// Nombres fijos para desarrollar y probar L2/L3 sin Revit — los mismos
/// sufijos que ya usa <c>TipoBarra</c> ("#N COL") y ángulos de gancho de
/// estribo que ya usa <c>TipoGancho</c>, para que se vea igual que en la
/// ventana conectada de verdad.
/// </summary>
public sealed class ProveedorTiposFalso : IProveedorTipos
{
    public IReadOnlyList<string> ListarTiposBarra() => new[]
    {
        "#3 COL", "#4 COL", "#5 COL", "#6 COL", "#3 VIGA", "#4 VIGA",
    };

    public IReadOnlyList<string> ListarTiposGancho() => new[]
    {
        "Estribo 135°", "Estándar 135°", "Estándar 90°",
    };
}
