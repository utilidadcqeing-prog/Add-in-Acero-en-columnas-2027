namespace practicacq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using practicacq.Logica;
using practicacq.Modelo;
using practicacq.Vistas;

/// <summary>
/// TEMPORAL -- paso 1 del plan de la vista previa 3D (#123,
/// docs/planes/2026-10-21-vista-previa-3d-columnas-123.md): mide cuánto
/// tarda <see cref="SimuladorArmado"/> en generar de prueba una columna y
/// revertirlo, y comprueba que no queda nada. Usa la configuración por
/// defecto de la ventana en todos los tramos. Corre dos veces: la segunda
/// muestra el tiempo "en caliente", que es el que se pagaría en cada cambio.
/// Sacar el comando (y su botón) una vez cerrado el #123.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ComandoDiagnosticoPrevia3D : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        Reference referencia;
        try
        {
            referencia = uidoc.Selection.PickObject(ObjectType.Element, new FiltroColumna(), "Selecciona una columna");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }

        string idColumna = referencia.ElementId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ColumnaContexto contexto = new LectorContexto(doc).Leer(idColumna);
        var tipos = new ProveedorTiposReal(doc);

        var opciones = new OpcionesArmado
        {
            Plan = new PlanArmado
            {
                Columnas =
                {
                    new ColumnaPlan
                    {
                        Id = idColumna,
                        Contexto = contexto,
                        Tramos = contexto.Tramos.Select(_ => (ConfigTramo?)ValoresPorDefecto.Tramo(tipos)).ToList(),
                        Longitudinal = ValoresPorDefecto.Longitudinal(tipos),
                    },
                },
            },
        };

        int rebarAntes = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).GetElementCount();
        bool modificadoAntes = doc.IsModified;

        SimuladorArmado.Resultado primera = SimuladorArmado.Simular(doc, opciones);
        SimuladorArmado.Resultado segunda = SimuladorArmado.Simular(doc, opciones);

        int rebarDespues = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).GetElementCount();

        var sb = new StringBuilder();
        sb.AppendLine($"Columna {contexto.Marca}: {contexto.Tramos.Count} tramos, Z {contexto.ZInferiorLongitudinal:0.00} a {contexto.ZSuperior:0.00} m.");
        sb.AppendLine($"Tiempo: 1ª vez {primera.Tiempo.TotalSeconds:0.00} s, 2ª vez {segunda.Tiempo.TotalSeconds:0.00} s.");
        sb.AppendLine($"Rebar en el modelo: antes {rebarAntes}, después {rebarDespues} (tienen que ser iguales).");
        sb.AppendLine($"Documento modificado: antes {modificadoAntes}, después {doc.IsModified}.");
        sb.AppendLine($"Advertencias borradas: {segunda.AdvertenciasBorradas}.");
        sb.AppendLine();

        foreach (IGrouping<RebarStyle, SimuladorArmado.RebarLeida> grupo in segunda.Barras.GroupBy(b => b.Estilo))
        {
            int posiciones = grupo.Sum(b => b.Posiciones.Count);
            int ganchos = grupo.Sum(b => b.Posiciones.Sum(p => p.Curvas.Count(c => c.EsGancho)));
            int todoGancho = grupo.Sum(b => b.Posiciones.Count(p => p.Curvas.All(c => c.EsGancho)));
            sb.AppendLine($"{grupo.Key}: {grupo.Count()} Rebar, {posiciones} barras, {ganchos} curvas de gancho, " +
                          $"{todoGancho} barras con TODAS sus curvas como gancho (el clasificador las descarta).");
        }

        // #137: en la vista 3D no aparecían las longitudinales. Esto dice si
        // se pierden en el generador (no hay Standard arriba) o al clasificar.
        List<BarraPrevia> clasificadas = ClasificadorPrevia3D.Clasificar(segunda, opciones.Plan.Columnas[0]);
        sb.AppendLine("Clasificadas: " + string.Join(", ",
            clasificadas.GroupBy(b => b.Clase).Select(g => $"{g.Key} {g.Count()}")) + ".");

        // Para comprobar que las curvas vienen en su posición final: el primer
        // y el último estribo de un juego tienen que estar a alturas distintas.
        if (segunda.Barras.FirstOrDefault(b => b.Estilo == RebarStyle.StirrupTie && b.Posiciones.Count > 1) is { } juego)
        {
            double Z(SimuladorArmado.PosicionLeida p) => GeometriaSeccion.Metros(p.Curvas.SelectMany(c => c.Puntos).Min(q => q.Z));
            sb.AppendLine();
            sb.AppendLine($"Un juego de estribos ({juego.NombreBarra}): {juego.Posiciones.Count} barras, " +
                          $"la primera en Z {Z(juego.Posiciones[0]):0.000} m y la última en Z {Z(juego.Posiciones[^1]):0.000} m.");
        }

        if (segunda.Generacion.Avisos.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Avisos del generador:");
            foreach (string aviso in segunda.Generacion.Avisos.Take(10)) sb.AppendLine($"- {aviso}");
        }

        TaskDialog.Show("Diagnóstico vista previa 3D (#123)", sb.ToString());
        return Result.Succeeded;
    }

    private sealed class FiltroColumna : ISelectionFilter
    {
        public bool AllowElement(Element elem) =>
            elem.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
