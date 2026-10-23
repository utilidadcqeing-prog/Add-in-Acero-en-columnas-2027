namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// La vista previa 3D (#123) sin dejar nada en el modelo: corre el MISMO
/// «Generar» de siempre (<see cref="GeneradorArmado"/> + <see cref="GeneradorLongitudinal"/>)
/// dentro de una transacción, copia la forma real de cada barra creada y la
/// revierte. Así lo que se ve es exactamente lo que se generaría: el
/// generador de columnas no arma grapas, ganchos ni traslapos solo con
/// cuentas, los mide dentro de Revit (docs/planes/2026-10-21-vista-previa-3d-columnas-123.md,
/// hallazgo 3 y decisión 1).
///
/// Una transacción revertida no deja entrada en el Ctrl+Z ni elementos en el
/// modelo. Las advertencias se borran antes de revertir, para que Revit no
/// muestre diálogos por barras que no van a quedar.
/// </summary>
public static class SimuladorArmado
{
    /// <summary>
    /// Tolerancia para reconocer una curva del cuerpo de la barra sobre la
    /// versión sin ganchos, en pies (~0,3 mm). Las colas y dobleces de los
    /// ganchos quedan a centímetros: no se confunden.
    /// </summary>
    private const double ToleranciaPies = 1e-3;

    /// <summary>Un tramo de curva de una barra, ya en su posición final (pies, coordenadas del modelo).</summary>
    /// <param name="EsGancho">No está sobre ninguna curva de la barra sin ganchos: es doblez o cola de anclaje.</param>
    public sealed record CurvaLeida(IReadOnlyList<XYZ> Puntos, bool EsGancho);

    /// <summary>Una barra del juego (una posición), con sus curvas de punta a punta.</summary>
    public sealed record PosicionLeida(IReadOnlyList<CurvaLeida> Curvas);

    /// <summary>Un <see cref="Rebar"/> creado por la simulación, leído antes de revertir.</summary>
    public sealed record RebarLeida(RebarStyle Estilo, double DiametroMm, string NombreBarra, IReadOnlyList<PosicionLeida> Posiciones);

    /// <param name="Generacion">Lo que devolvió el generador (avisos incluidos), como si se hubiera generado.</param>
    public sealed record Resultado(IReadOnlyList<RebarLeida> Barras, ResultadoArmado Generacion, TimeSpan Tiempo, int AdvertenciasBorradas);

    /// <summary>
    /// Genera <paramref name="opciones"/> de prueba y lo revierte. Tiene que
    /// llamarse en contexto de API válido (un ExternalEvent) y sin otra
    /// transacción abierta. El acero que el add-in generó antes se borra
    /// dentro de la transacción (#145) y vuelve al revertir; el ajeno no se
    /// toca: la simulación ignora <see cref="OpcionesArmado.BorrarArmadoAjeno"/>.
    /// </summary>
    public static Resultado Simular(Document doc, OpcionesArmado opciones)
    {
        var reloj = Stopwatch.StartNew();
        _detallesAnotados = 0;
        var sinBorrar = new OpcionesArmado { Plan = opciones.Plan, BorrarArmadoAjeno = false };

        HashSet<ElementId> antes = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).ToElementIds().ToHashSet();
        var advertencias = new BorrarAdvertencias();
        var leidas = new List<RebarLeida>();
        ResultadoArmado res;

        using (var t = new Transaction(doc, "CQEING - vista previa 3D (se revierte)"))
        {
            FailureHandlingOptions manejo = t.GetFailureHandlingOptions()
                .SetFailuresPreprocessor(advertencias)
                .SetClearAfterRollback(true);
            t.SetFailureHandlingOptions(manejo);
            t.Start();
            Bitacora3D.Anotar($"simular: transacción abierta ({antes.Count} Rebar en el modelo)");
            try
            {
                res = GeneradorArmado.Generar(doc, sinBorrar);
                Bitacora3D.Anotar($"simular: estribos listos ({res.GruposCreados} grupos)");
                GeneradorLongitudinal.Generar(doc, sinBorrar, res);
                Bitacora3D.Anotar("simular: longitudinales listas");

                // Sin regenerar, las curvas de lo último creado pueden no estar
                // calculadas todavía (ver GeneradorArmado.RegistrarPosiciones).
                doc.Regenerate();

                foreach (Rebar rebar in new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>())
                {
                    if (antes.Contains(rebar.Id)) continue;
                    if (Leer(doc, rebar) is RebarLeida leida) leidas.Add(leida);
                }
            }
            finally
            {
                Bitacora3D.Anotar($"simular: {leidas.Count} Rebar leídos, revirtiendo");
                t.RollBack();
                Bitacora3D.Anotar("simular: revertido");
            }
        }

        reloj.Stop();
        return new Resultado(leidas, res, reloj.Elapsed, advertencias.Borradas);
    }

    /// <summary>TEMPORAL: cuántas barras "todo gancho" se anotaron en esta simulación (se anotan 3).</summary>
    private static int _detallesAnotados;

    private static RebarLeida? Leer(Document doc, Rebar rebar)
    {
        if (doc.GetElement(rebar.GetTypeId()) is not RebarBarType tipo) return null;

        var posiciones = new List<PosicionLeida>();
        for (int i = 0; i < rebar.NumberOfBarPositions; i++)
        {
            if (!rebar.DoesBarExistAtPosition(i)) continue;

            // GetTransformedCenterlineCurves: "The curves are in the final
            // position" (documentación oficial, 2022+). La versión sin ganchos
            // sirve solo para saber qué curvas son gancho.
            IList<Curve> completa = rebar.GetTransformedCenterlineCurves(
                false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, i);
            IList<Curve> sinGanchos = rebar.GetTransformedCenterlineCurves(
                false, true, false, MultiplanarOption.IncludeAllMultiplanarCurves, i);

            var leida = new PosicionLeida(completa
                .Select(c => new CurvaLeida(c.Tessellate().ToList(), !sinGanchos.Any(s => EstaSobre(c, s))))
                .ToList());
            posiciones.Add(leida);

            // TEMPORAL (docs/planes/2026-10-23-cierre-revit-vista-3d.md): las
            // grapas desaparecían del 3D. Si todas las curvas quedan como
            // gancho, se anota lo que devolvió Revit con y sin ganchos.
            if (leida.Curvas.All(c => c.EsGancho) && _detallesAnotados < 3)
            {
                _detallesAnotados++;
                static string Texto(Curve c) =>
                    $"{c.GetType().Name} ({c.GetEndPoint(0).X:0.0000}, {c.GetEndPoint(0).Y:0.0000}, {c.GetEndPoint(0).Z:0.0000}) -> " +
                    $"({c.GetEndPoint(1).X:0.0000}, {c.GetEndPoint(1).Y:0.0000}, {c.GetEndPoint(1).Z:0.0000}) largo {c.Length:0.0000}";
                Bitacora3D.Anotar($"TODO GANCHO: Rebar {rebar.Id} ({rebar.Name}), posición {i}. Con ganchos ({completa.Count}): " +
                                  string.Join(" | ", completa.Select(Texto)) + $". Sin ganchos ({sinGanchos.Count}): " +
                                  string.Join(" | ", sinGanchos.Select(Texto)) + " (pies)");
            }
        }

        // El estilo (estándar o estribo) es de la forma de la barra. GetShapeId
        // lanza con una barra libre de varias formas: el add-in no crea esas,
        // pero si aparece una se toma como estándar.
        RebarStyle estilo = RebarStyle.Standard;
        try
        {
            if (doc.GetElement(rebar.GetShapeId()) is RebarShape forma) estilo = forma.RebarStyle;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException) { /* ver arriba */ }
        return new RebarLeida(estilo, EstandarCQ.DiametroMm(tipo), tipo.Name, posiciones);
    }

    /// <summary>
    /// <paramref name="a"/> (de la barra con ganchos) está sobre <paramref name="b"/>
    /// (de la barra sin ganchos): sus puntas y su punto medio caen en ella.
    /// No alcanza con comparar puntas: en una grapa la recta con ganchos es
    /// más corta que sin ganchos (sin ganchos se estira hasta donde empezaban
    /// los dobleces), medido en Revit el 23-oct
    /// (docs/planes/2026-10-23-cierre-revit-vista-3d.md).
    /// </summary>
    private static bool EstaSobre(Curve a, Curve b) =>
        b.Distance(a.GetEndPoint(0)) < ToleranciaPies &&
        b.Distance(a.GetEndPoint(1)) < ToleranciaPies &&
        b.Distance(a.Evaluate(0.5, normalized: true)) < ToleranciaPies;

    /// <summary>Borra las advertencias: con la transacción revertida no tienen a qué referirse.</summary>
    private sealed class BorrarAdvertencias : IFailuresPreprocessor
    {
        public int Borradas { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accesor)
        {
            foreach (FailureMessageAccessor falla in accesor.GetFailureMessages())
            {
                if (falla.GetSeverity() != FailureSeverity.Warning) continue;
                accesor.DeleteWarning(falla);
                Borradas++;
            }

            return FailureProcessingResult.Continue;
        }
    }
}
