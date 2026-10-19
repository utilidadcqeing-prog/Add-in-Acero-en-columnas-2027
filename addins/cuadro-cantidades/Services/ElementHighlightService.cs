using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CQEING_CuadroCantidades.Core.Application.Interfaces;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Servicio responsable de aplicar resaltados gráficos personalizados a elementos específicos de Revit dentro de una vista 3D.
/// Mantiene la visibilidad del resto del modelo aplicando un efecto semitransparente (half-tone) a los elementos no seleccionados.
/// </summary>
public sealed class ElementHighlightService : IElementHighlightService
{
    /// <summary>
    /// Aplica una sobreescritura gráfica a la vista 3D seleccionada, resaltando los elementos especificados en azul y atenuando el resto.
    /// </summary>
    /// <param name="view">La vista 3D de Revit en la que se aplicará el resaltado.</param>
    /// <param name="elementIds">Colección de identificadores de los elementos de Revit a resaltar.</param>
    /// <exception cref="ArgumentNullException">Se lanza si la vista especificada es nula.</exception>
    public void ApplyHighlight(View view, IReadOnlyCollection<ElementId> elementIds)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));
        if (elementIds == null || elementIds.Count == 0) return;

        Document doc = view.Document;

        // Localizar el patrón de relleno sólido para colorear fuertemente los elementos seleccionados.
        FillPatternElement? solidFillPattern = null;
        try
        {
            solidFillPattern = FillPatternElement.GetFillPatternElementByName(doc, FillPatternTarget.Drafting, "<Solid fill>");
            if (solidFillPattern == null)
            {
                solidFillPattern = FillPatternElement.GetFillPatternElementByName(doc, FillPatternTarget.Model, "<Solid fill>");
            }
        }
        catch
        {
            // Si falla GetFillPatternElementByName por compatibilidad o excepciones de Revit, se busca de forma genérica
        }

        if (solidFillPattern == null)
        {
            // Búsqueda alternativa recorriendo todos los patrones de relleno sólidos
            solidFillPattern = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(p => p.GetFillPattern() != null && p.GetFillPattern().IsSolidFill);
        }

        // Definir los colores para resaltar y atenuar
        var selectedSurfaceColor = new Color(0, 85, 255); // Azul brillante para la superficie de los seleccionados
        var selectedLineColor = new Color(0, 35, 180);   // Azul oscuro para las líneas de contorno de los seleccionados
        var mutedLineColor = new Color(155, 155, 155);    // Gris atenuado para el resto de elementos

        // Configurar los ajustes gráficos para los elementos a resaltar
        var highlightSettings = new OverrideGraphicSettings();
        highlightSettings.SetHalftone(false);
        highlightSettings.SetSurfaceTransparency(0); // Totalmente opaco
        if (solidFillPattern != null)
        {
            highlightSettings.SetSurfaceForegroundPatternId(solidFillPattern.Id);
            highlightSettings.SetSurfaceForegroundPatternColor(selectedSurfaceColor);

            highlightSettings.SetCutForegroundPatternId(solidFillPattern.Id);
            highlightSettings.SetCutForegroundPatternColor(selectedSurfaceColor);
        }

        highlightSettings.SetProjectionLineColor(selectedLineColor);
        highlightSettings.SetCutLineColor(selectedLineColor);
        highlightSettings.SetProjectionLineWeight(8); // Grosor de línea aumentado para resaltar
        highlightSettings.SetCutLineWeight(8);

        // Configurar la vista general: mantener visible todo el modelo, pero atenuar el resto de categorías (no seleccionadas)
        var mutedSettings = new OverrideGraphicSettings();
        mutedSettings.SetHalftone(true);
        mutedSettings.SetSurfaceTransparency(70); // 70% de transparencia para el fondo
        mutedSettings.SetProjectionLineColor(mutedLineColor);
        mutedSettings.SetCutLineColor(mutedLineColor);
        mutedSettings.SetProjectionLineWeight(1);
        mutedSettings.SetCutLineWeight(1);

        // Aplicar la atenuación a nivel de categorías del modelo
        foreach (Category cat in doc.Settings.Categories)
        {
            if (cat.CategoryType == CategoryType.Model)
            {
                try
                {
                    view.SetCategoryOverrides(cat.Id, mutedSettings);
                }
                catch
                {
                    // Ignorar categorías que no se pueden sobreescribir gráficamente en esta vista
                }
            }
        }

        // Aplicar el resaltado de sobreescritura gráfica a nivel de elemento específico para los seleccionados
        foreach (var id in elementIds)
        {
            try
            {
                view.SetElementOverrides(id, highlightSettings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HighlightService] Error overriding element {id}: {ex.Message}");
            }
        }
    }
}
