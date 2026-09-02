namespace practicacq.Modelo;

using System;
using Autodesk.Revit.DB.Structure;

/// <summary>
/// Envoltorio de un RebarHookType para el ComboBox. El proyecto suele traer varios
/// ganchos de 135°, y no todos sirven para estribo: elegir el primero que aparezca
/// deja patas de 13 cm que cruzan el estribo entero.
/// </summary>
public class TipoGancho
{
    public TipoGancho(RebarHookType tipo)
    {
        Tipo = tipo;
        Grados = tipo.HookAngle * 180 / Math.PI;
        EsDe135 = Math.Abs(Grados - 135) < 0.5;
        EsDeEstribo = EsDe135 && tipo.Style == RebarStyle.StirrupTie;
        Etiqueta = $"{tipo.Name}  ·  {Grados:0}°";

        Grupo = EsDeEstribo ? "Para estribo"
              : EsDe135     ? "Otros de 135°"
              :               "Otros ángulos";
    }

    public string Grupo { get; }

    public RebarHookType Tipo { get; }
    public string Etiqueta { get; }
    public double Grados { get; }

    /// <summary>Los de 135° van primero en la lista: son los de estribo.</summary>
    public bool EsDe135 { get; }

    /// <summary>
    /// De 135° y además declarado con Style = StirrupTie.
    ///
    /// El ángulo por sí solo no basta: "Estándar - 135°." también es de 135° pero su
    /// Style es Standard, o sea para barra longitudinal, y Revit lo rechaza dentro de
    /// un estribo con un error interno que no explica nada. Style es además
    /// independiente del idioma, así que no se confunde con los duplicados ES/EN de
    /// la plantilla.
    /// </summary>
    public bool EsDeEstribo { get; }

    public override string ToString() => Etiqueta;
}
