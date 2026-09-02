namespace practicacq.Modelo;

using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

public class OpcionesEstribos
{
    public List<FamilyInstance> Columnas { get; set; } = new();
    public RebarBarType? TipoBarra { get; set; }

    /// <summary>Gancho de los dos extremos del estribo. Normalmente uno de 135°.</summary>
    public RebarHookType? TipoGancho { get; set; }

    /// <summary>Separación entre estribos, en pies (unidades internas).</summary>
    public double Espaciado { get; set; }

    /// <summary>
    /// Recubrimiento que debe tener la cara lateral de la columna, en pies.
    /// Por norma de la empresa son 4 cm.
    /// </summary>
    public double RecubrimientoObjetivo { get; set; }

    /// <summary>
    /// Si la columna trae otro recubrimiento, se lo corrige antes de medir.
    /// El desfase del estribo siempre se lee del parámetro, nunca se impone.
    /// </summary>
    public bool CorregirRecubrimiento { get; set; } = true;

    /// <summary>Separación fija entre el arranque del estribo y la cara del elemento, en pies.</summary>
    public double SeparacionExtremos { get; set; }
}

public class ResultadoEstribos
{
    public int Eliminados { get; set; }
    public int Creados { get; set; }
    public int Omitidas { get; set; }
    public int RecubrimientosCorregidos { get; set; }
    public List<string> Avisos { get; } = new();

    /// <summary>Lo que quedó creado, para no tener que ir a buscarlo a Propiedades.</summary>
    public List<string> Detalles { get; } = new();
}
