using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CQEING.Vigas.Calculo.Generacion;
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

    /// <summary>Diferencia de largo con el tablero a partir de la cual se avisa, en mm (decisión 8).</summary>
    private const int ToleranciaLargo = 10;

    private sealed record Creada(Rebar Barra, PiezaLongitudinal Pieza, PorticoAGenerar Portico, bool EsBaston);

    /// <param name="saltados">Los pórticos que la sesión no deja generar, con su motivo: van primero en las fallas.</param>
    public static ResumenGeneracion Generar(Document doc, IReadOnlyList<PorticoAGenerar> porticos, IReadOnlyList<string> saltados, Reglas r)
    {
        var fallas = new List<string>(saltados);
        if (porticos.Count == 0)
            return new ResumenGeneracion(0, 0, 0, 0, fallas, []);

        TiposAcero tipos = TiposAcero.Leer(doc, r);
        var creadas = new List<Creada>();
        using var t = new Transaction(doc, NombreTransaccion);
        FailureHandlingOptions opciones = t.GetFailureHandlingOptions();
        opciones.SetFailuresPreprocessor(new SinAdvertencias());
        t.SetFailureHandlingOptions(opciones);
        t.Start();
        foreach (PorticoAGenerar p in porticos)
        {
            MarcaAcero.BorrarAnteriores(doc, p.Id, p.Vigas);
            foreach ((PiezaLongitudinal pieza, bool esBaston) in p.Plan.Barras.Select(b => (b, false)).Concat(p.Plan.Bastones.Select(b => (b, true))))
            {
                if (Crear(doc, p, pieza, tipos) is { } barra)
                {
                    MarcaAcero.Marcar(barra, p.Id, pieza.Pieza);
                    creadas.Add(new Creada(barra, pieza, p, esBaston));
                }
                else
                    fallas.Add($"{pieza.Pieza} ({p.Id}): Revit no pudo crear la barra #{pieza.Diametro}.");
            }
        }

        // Los ganchos se miran con la geometría ya regenerada; se regenera una vez más solo si se volteó alguno.
        doc.Regenerate();
        bool volteados = false;
        foreach (Creada c in creadas)
            volteados |= CreadorBarras.CorregirGanchos(c.Barra, c.Pieza, CreadorBarras.Pies(c.Portico.Ubicacion.ZAbajo + c.Pieza.YCorte));
        if (volteados)
            doc.Regenerate();

        List<DiferenciaLargo> diferencias = creadas
            .Select(c => new DiferenciaLargo(c.Portico.Id, c.Pieza.Pieza, c.Pieza.Diametro, CreadorBarras.LargoEnRevit(c.Barra), c.Pieza.Largo))
            .Where(d => Math.Abs(d.LargoRevit - d.LargoTablero) > ToleranciaLargo)
            .ToList();
        t.Commit();

        // TEMPORAL (#85, tarea 7): los estribos se crean en la tarea 8.
        fallas.Add("Los estribos llegan con la tarea 8: por ahora solo se crean barras y bastones.");
        return new ResumenGeneracion(porticos.Count, creadas.Count(c => !c.EsBaston), creadas.Count(c => c.EsBaston), 0,
            fallas.Distinct().ToList(), diferencias);
    }

    private static Rebar? Crear(Document doc, PorticoAGenerar p, PiezaLongitudinal pieza, TiposAcero tipos)
    {
        if (tipos.Barra(pieza.Diametro) is not { } barra)
            return null;
        try
        {
            return CreadorBarras.Crear(doc, p.Ubicacion, pieza, barra, tipos);
        }
        catch (Exception e) when (e is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            return null;
        }
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
