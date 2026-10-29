namespace practicacq.Tablero;

using System.Linq;
using practicacq.Modelo;

/// <summary>
/// Regla #63: «la misma configuración» para varias columnas solo cae en las
/// que comparten estructura -- "el mismo nudo, los mismos tramos… solo que
/// una va a tener el traslapo a otra altura porque sea más larga o más
/// corta" (William, 11-sep). La diferencia de ALTURA no importa; la de
/// cantidad y tipo de tramos sí, porque las excepciones van por índice de
/// tramo. Salió de <c>VentanaConfiguradorEstribos.ColumnasCompatibles</c>.
/// </summary>
public static class CompatibilidadColumnas
{
    public static bool MismaEstructura(ColumnaContexto a, ColumnaContexto b) =>
        a.Tramos.Count == b.Tramos.Count &&
        a.Tramos.Select(t => t.Tipo).SequenceEqual(b.Tramos.Select(t => t.Tipo));

    public static string CuantosTramos(int cuantos) => cuantos == 1 ? "1 tramo" : $"{cuantos} tramos";
}
