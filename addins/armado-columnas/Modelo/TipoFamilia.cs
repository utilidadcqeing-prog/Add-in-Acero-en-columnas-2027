namespace practicacq.Modelo;

using Autodesk.Revit.DB;

/// <summary>
/// Envoltorio de un FamilySymbol para mostrarlo en los ComboBox
/// con el formato "Familia - Tipo".
/// </summary>
public class TipoFamilia
{
    public TipoFamilia(FamilySymbol simbolo)
    {
        Simbolo = simbolo;
        Etiqueta = $"{simbolo.FamilyName} - {simbolo.Name}";
    }

    public FamilySymbol Simbolo { get; }
    public string Etiqueta { get; }

    public override string ToString() => Etiqueta;
}
