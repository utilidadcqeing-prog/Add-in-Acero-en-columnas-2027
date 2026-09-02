namespace practicacq.Modelo;

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

/// <summary>
/// Envoltorio de un RebarBarType para mostrarlo en el ComboBox, y así no tener
/// que escribir el nombre de la barra a mano como tocaba en Dynamo.
/// </summary>
public class TipoBarra
{
    public TipoBarra(RebarBarType tipo)
    {
        Tipo = tipo;
        Diametro = tipo.BarNominalDiameter;

        // Los tipos métricos ya se llaman "6 mm"; no hay que repetirles el calibre.
        // Los imperiales ("#4") sí lo agradecen.
        Etiqueta = tipo.Name.Contains("mm", StringComparison.OrdinalIgnoreCase)
            ? tipo.Name
            : $"{tipo.Name}  ·  {Milimetros(Diametro):0.#} mm";

        EsDeColumna = LlevaSufijo(tipo.Name, "COL");
        Grupo = EsDeColumna ? "Columnas" : "Otros";
    }

    /// <summary>
    /// La empresa nombra un tipo por elemento: "#3 COL", "#3 VIGA", "#3 CIM".
    /// Los de columna van arriba porque esta herramienta es de columnas.
    /// </summary>
    public bool EsDeColumna { get; }

    public string Grupo { get; }

    /// <summary>
    /// Compara el sufijo como palabra completa. "#3 COL" cuenta;
    /// "#3 COLUMNETA" no, aunque empiece igual.
    /// </summary>
    private static bool LlevaSufijo(string nombre, string sufijo)
    {
        foreach (string parte in nombre.Split(' ', '-', '.'))
        {
            if (parte.Equals(sufijo, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public RebarBarType Tipo { get; }
    public string Etiqueta { get; }

    /// <summary>
    /// Diámetro nominal en pies. Se ordena por aquí y no por nombre, porque
    /// alfabéticamente "#10" queda antes que "#3" y el usuario termina con la
    /// barra más gruesa preseleccionada.
    /// </summary>
    public double Diametro { get; }

    private static double Milimetros(double pies) =>
        UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Millimeters);

    public override string ToString() => Etiqueta;
}
