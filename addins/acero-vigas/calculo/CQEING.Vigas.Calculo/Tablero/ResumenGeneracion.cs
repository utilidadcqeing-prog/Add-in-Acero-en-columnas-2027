namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>
/// Una barra cuyo largo en Revit no coincide con el del tablero (decisión 8 del plan
/// docs/planes/2026-10-19-generar-acero-vigas-85.md): el add-in no cambia los tipos del proyecto, solo lo avisa. En mm.
/// </summary>
public sealed record DiferenciaLargo(string Portico, string Pieza, int Diametro, int LargoRevit, int LargoTablero);

/// <summary>
/// Lo que quedó después de "Generar en Revit" (#85): cuántos pórticos y piezas se crearon, lo que no se pudo (un texto
/// por línea, también los pórticos saltados) y las barras con otro largo en Revit.
/// </summary>
public sealed record ResumenGeneracion(int Porticos, int Barras, int Bastones, int Estribos, IReadOnlyList<string> Fallas,
    IReadOnlyList<DiferenciaLargo> Diferencias);
