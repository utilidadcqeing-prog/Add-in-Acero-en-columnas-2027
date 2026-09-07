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
