namespace practicacq.Modelo;

/// <summary>
/// Misma idea que <see cref="IProveedorContexto"/> pero para los tipos de barra
/// y de gancho: la ventana (L2/L3) los necesita para llenar los combos ("nada se
/// teclea a mano"), y no puede leerlos directo de
/// <c>RebarBarType</c>/<c>RebarHookType</c> porque eso es API de Revit.
///
/// El lado real (J2, junto con <c>LectorContexto</c>) implementa esto leyendo
/// <c>FilteredElementCollector(doc).OfClass(typeof(RebarBarType))</c> /
/// <c>RebarHookType</c> y devolviendo el <c>.Name</c> de cada uno — el mismo
/// nombre que <see cref="ConfigTramo.TipoBarraNombre"/> y
/// <see cref="ConfigLongitudinal.TipoBarraEsquinasNombre"/> guardan como texto.
/// </summary>
public interface IProveedorTipos
{
    /// <summary>
    /// Las barras con su diámetro, no solo el nombre: el valor por defecto de
    /// los combos se elige por diámetro contra el cuadro de la empresa, porque
    /// los nombres cambian de modelo a modelo. Ver <see cref="TipoBarra"/>.
    /// </summary>
    IReadOnlyList<TipoBarra> ListarBarras();

    /// <summary>
    /// Los ganchos con su estilo y su ángulo, no solo el nombre: la ventana
    /// necesita esos dos datos para no ofrecer ganchos de estribo donde va una
    /// barra longitudinal (y al revés), y para elegir el que corresponde por
    /// defecto. Ver <see cref="TipoGancho"/>.
    /// </summary>
    IReadOnlyList<TipoGancho> ListarGanchos();

    /// <summary>
    /// Los tipos de empalme del modelo, por nombre -- para el combo "Tipo de
    /// empalme" de la longitudinal (issue #41). No incluye los propios del
    /// add-in ("CQ Traslapo #N"): esos son la opción por defecto, que la
    /// ventana ofrece aparte. Puede venir vacía.
    /// </summary>
    IReadOnlyList<string> ListarEmpalmes();
}
