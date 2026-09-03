namespace practicacq.Modelo;

using System.Collections.Generic;

/// <summary>
/// Lo que L2 arma para pedirle a J3 que genere el armado real en Revit: el
/// PlanArmado tal como quedó configurado en la ventana (mismo objeto que
/// arma <c>BotonExportar_Click</c>, ver <c>ConstruirPlanArmado</c>) más si
/// hay que borrar antes lo que este add-in ya había generado en esas
/// columnas.
///
/// Vive en Modelo/ y no tiene una sola línea de Autodesk.Revit: la ventana
/// lo arma sin abrir Revit, y quién lo ejecuta (<c>ComandoEstribos</c>) es
/// quien sabe traducirlo a la API real (ver <c>Logica/GeneradorArmado</c>).
/// </summary>
public sealed class OpcionesArmado
{
    public PlanArmado Plan { get; set; } = new();

    /// <summary>
    /// Issue #2: por defecto NO se borra nada -- los estribos nuevos conviven
    /// con lo que ya había, decisión del equipo del 2-sep. Tildar esto borra,
    /// antes de generar, los estribos que este mismo add-in había creado
    /// antes en los elementos de estas columnas (no toca lo modelado a mano).
    /// </summary>
    public bool BorrarAnteriores { get; set; }
}

public sealed class ResultadoArmado
{
    /// <summary>Estribos (de una tanda anterior de este add-in) borrados antes de generar. 0 si BorrarAnteriores estaba destildado.</summary>
    public int Eliminados { get; set; }

    /// <summary>Rebar creados -- cada grupo (el uniforme, o cada una de las tres zonas de un confinado) cuenta aparte.</summary>
    public int GruposCreados { get; set; }

    /// <summary>Tramos que no generaron nada por un problema puntual (sin recubrimiento, sin host, geometría que no encaja...).</summary>
    public int TramosOmitidos { get; set; }

    public List<string> Avisos { get; } = new();

    /// <summary>Lo que quedó creado, para no tener que ir a buscarlo a Propiedades.</summary>
    public List<string> Detalles { get; } = new();
}
