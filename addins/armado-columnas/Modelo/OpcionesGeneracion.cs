namespace practicacq.Modelo;

using System.Collections.Generic;
using Autodesk.Revit.DB;

public class OpcionesGeneracion
{
    public List<Wall> Muros { get; set; } = new();
    public FamilySymbol? TipoColumneta { get; set; }
    public FamilySymbol? TipoVigueta { get; set; }

    public bool GenerarColumnetas { get; set; } = true;
    public bool GenerarViguetasSuperiores { get; set; } = true;
    public bool GenerarViguetasInferiores { get; set; } = true;

    /// <summary>Separación máxima entre columnetas, en pies (unidades internas).</summary>
    public double SeparacionMaxima { get; set; }

    public bool AplicarDesfaseVertical { get; set; }

    /// <summary>Desfase de la vigueta superior hacia abajo desde el tope del muro, en pies.</summary>
    public double DesfaseVertical { get; set; }
}

public class ResultadoGeneracion
{
    public int Eliminados { get; set; }
    public int Columnetas { get; set; }
    public int ColumnetasOmitidas { get; set; }
    public int ViguetasSuperiores { get; set; }
    public int ViguetasInferiores { get; set; }
    public List<string> Avisos { get; } = new();
}
