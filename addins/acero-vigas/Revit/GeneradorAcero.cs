using Autodesk.Revit.DB;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Revit;

/// <summary>
/// "Generar en Revit" (#85): crea el acero de varios pórticos en una sola transacción, que se deshace de una vez
/// (decisión 5 del plan docs/planes/2026-10-19-generar-acero-vigas-85.md). Antes de crear, borra lo que el add-in creó
/// en cada pórtico (decisión 6). Lo que no se puede crear se anota y se sigue.
/// </summary>
internal static class GeneradorAcero
{
    public const string NombreTransaccion = "Vigas: generar acero";

    /// <param name="saltados">Los pórticos que la sesión no deja generar, con su motivo: van primero en las fallas.</param>
    public static ResumenGeneracion Generar(Document doc, IReadOnlyList<PorticoAGenerar> porticos, IReadOnlyList<string> saltados, Reglas r)
    {
        var fallas = new List<string>(saltados);
        if (porticos.Count == 0)
            return new ResumenGeneracion(0, 0, 0, 0, fallas, []);

        using var t = new Transaction(doc, NombreTransaccion);
        FailureHandlingOptions opciones = t.GetFailureHandlingOptions();
        opciones.SetFailuresPreprocessor(new SinAdvertencias());
        t.SetFailureHandlingOptions(opciones);
        t.Start();
        // TEMPORAL (#85, tarea 6): las barras, los bastones y los estribos se crean en las tareas 7 y 8, con TiposAcero.
        foreach (PorticoAGenerar p in porticos)
            MarcaAcero.BorrarAnteriores(doc, p.Id, p.Vigas);
        t.Commit();
        fallas.Add("La creación de barras en Revit llega con la tarea 7: por ahora solo se borró lo generado antes.");
        return new ResumenGeneracion(porticos.Count, 0, 0, 0, fallas, []);
    }

    /// <summary>
    /// Descarta las advertencias de la transacción (decisión 3): se sabe que una barra corrida recorre varias vigas y Revit
    /// avisa que queda fuera de su host. Los errores no se tocan: esos sí deshacen la transacción.
    /// </summary>
    private sealed class SinAdvertencias : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            foreach (FailureMessageAccessor m in a.GetFailureMessages())
                if (m.GetSeverity() == FailureSeverity.Warning)
                    a.DeleteWarning(m);
            return FailureProcessingResult.Continue;
        }
    }
}
