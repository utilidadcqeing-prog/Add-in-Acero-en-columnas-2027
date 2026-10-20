namespace practicacq.Modelo;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

/// <summary>
/// El lado Revit del cuadro de la empresa: buscar la varilla que corresponde a
/// un <see cref="RebarBarType"/> del documento, y pasar de metros a pies.
///
/// La tabla en sí vive en <see cref="CuadroVarillas"/>, sin Revit, porque la
/// ventana también la necesita (para elegir el tipo de barra por defecto por
/// diámetro) y <c>Modelo/</c>/<c>Vistas/</c> tienen que seguir compilando sin la
/// API de Revit -- ver <c>tools/PreviewUI</c>, que excluye este archivo.
///
/// Se compara contra el cuadro y no contra lo que Revit calcularía por su
/// cuenta: para una #3, Revit propone 4,8 cm de gancho de estribo y el cuadro
/// pide 13,1 cm. El que manda es el cuadro.
/// </summary>
public static class EstandarCQ
{
    /// <summary>
    /// Busca la varilla del cuadro que corresponde a ese tipo de barra, o null si el
    /// diámetro no es ninguno de los normalizados.
    /// </summary>
    /// <remarks>
    /// Un tipo propio ("#4 CQEING") se resuelve por nombre; cualquier otro,
    /// por diámetro como siempre, para no romper los modelos que siguen
    /// usando las barras de su plantilla.
    /// </remarks>
    public static CuadroVarillas.Varilla? Buscar(RebarBarType tipo) =>
        CuadroVarillas.PorNombrePropio(tipo.Name) ?? CuadroVarillas.PorDiametro(DiametroMm(tipo));

    /// <summary>El diámetro nominal del tipo de barra, en milímetros.</summary>
    public static double DiametroMm(RebarBarType tipo) =>
        UnitUtils.ConvertFromInternalUnits(tipo.BarNominalDiameter, UnitTypeId.Millimeters);

    /// <summary>
    /// El tipo de barra como lo ve <c>Modelo/</c>: nombre, diámetro y los dos
    /// doblados que pide el filtro de barra de estribo (#64). Lo usan el
    /// proveedor de la ventana y los generadores, para evaluar igual.
    /// </summary>
    public static TipoBarra ComoTipoBarra(RebarBarType tipo) => new(tipo.Name, DiametroMm(tipo))
    {
        DobladoEstriboMm = UnitUtils.ConvertFromInternalUnits(tipo.StirrupTieBendDiameter, UnitTypeId.Millimeters),
        DobladoGanchoMm = UnitUtils.ConvertFromInternalUnits(tipo.StandardHookBendDiameter, UnitTypeId.Millimeters),
    };

    /// <summary>Los valores del cuadro vienen en metros; la API trabaja en pies.</summary>
    public static double APies(double metros) =>
        UnitUtils.ConvertToInternalUnits(metros, UnitTypeId.Meters);
}
