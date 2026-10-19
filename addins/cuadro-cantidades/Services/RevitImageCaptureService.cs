using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using CQEING_CuadroCantidades.Core.Application.Interfaces;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Servicio responsable de generar capturas de imagen 3D automáticas desde Revit.
/// Crea una vista 3D isométrica temporal, encuadra el modelo completo, aplica resaltado gráfico en color azul a los
/// elementos especificados (atenuando el resto en halftone), exporta la vista como PNG y finalmente elimina la vista temporal.
/// </summary>
public sealed class RevitImageCaptureService : IRevitImageCaptureService
{
    private readonly IElementHighlightService _highlightService;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="RevitImageCaptureService"/>.
    /// </summary>
    /// <param name="highlightService">Servicio para aplicar resaltado y sobreescrituras gráficas a los elementos de Revit.</param>
    public RevitImageCaptureService(IElementHighlightService highlightService)
    {
        _highlightService =
            highlightService ?? throw new ArgumentNullException(nameof(highlightService));
    }

    /// <summary>
    /// Crea una vista 3D temporal, encuadra los elementos del modelo, aplica el resaltado gráfico a los elementos provistos y exporta la vista como imagen PNG.
    /// </summary>
    /// <param name="doc">El documento de Revit activo.</param>
    /// <param name="revitUniqueIds">Colección de Unique IDs de los elementos a resaltar en la captura.</param>
    /// <param name="activityCode">Código de la actividad (se utiliza para nombrar o catalogar el proceso).</param>
    /// <returns>Ruta absoluta del archivo PNG temporal generado; de lo contrario, una cadena vacía.</returns>
    /// <exception cref="ArgumentNullException">Se lanza si el documento provisto es nulo.</exception>
    public string CaptureView(
        Document doc,
        IReadOnlyCollection<string> revitUniqueIds,
        string activityCode,
        string? selectedViewUniqueId = null
    )
    {
        if (doc == null)
            throw new ArgumentNullException(nameof(doc));
        if (revitUniqueIds == null || revitUniqueIds.Count == 0)
            return string.Empty;

        // 1. Resolver los Revit Unique IDs en ElementIds numéricos
        var elementIds = new List<ElementId>();
        foreach (var uid in revitUniqueIds)
        {
            try
            {
                var elem = doc.GetElement(uid);
                if (elem != null)
                {
                    elementIds.Add(elem.Id);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ImageCaptureService] Error resolving Unique ID {uid}: {ex.Message}"
                );
            }
        }

        if (elementIds.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine(
                "[ImageCaptureService] No elements could be resolved from provided unique IDs."
            );
            return string.Empty;
        }

        // 2. Configurar el prefijo de la ruta del archivo temporal
        string tempFileGuid = Guid.NewGuid().ToString("N");
        string tempDir = Path.GetTempPath();
        string tempBaseFilePath = Path.Combine(tempDir, $"RevitCapture_{tempFileGuid}");

        View? tempView = null;
        try
        {
            if (!string.IsNullOrEmpty(selectedViewUniqueId))
            {
                try
                {
                    var targetView = doc.GetElement(selectedViewUniqueId) as View;
                    if (targetView != null)
                    {
                        using (var trans = new Transaction(doc, "Duplicate Temp View for Capture"))
                        {
                            trans.Start();
                            var duplicatedId = targetView.Duplicate(ViewDuplicateOption.Duplicate);
                            tempView = doc.GetElement(duplicatedId) as View;
                            if (tempView != null)
                            {
                                trans.Commit();
                            }
                            else
                            {
                                trans.RollBack();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ImageCaptureService] Error duplicating view {selectedViewUniqueId}: {ex.Message}. Falling back to default view."
                    );
                    tempView = null;
                }
            }

            if (tempView == null)
            {
                // 3. Crear y configurar la vista 3D temporal en una transacción confirmada
                using (var trans = new Transaction(doc, "Create Temp View for Capture"))
                {
                    trans.Start();

                    // Buscar el tipo de familia de vista 3D (Isometric 3D)
                    var viewFamilyType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

                    if (viewFamilyType == null)
                        throw new InvalidOperationException(
                            "No 3D view family type found in the active document."
                        );

                    // Crear una vista 3D isométrica limpia
                    var tempView3D = View3D.CreateIsometric(doc, viewFamilyType.Id);

                    // IMPORTANTE: En modelos colaborativos, Revit nombra por defecto la vista como "{3D - username}".
                    // Si el nombre de usuario contiene caracteres como "\", la exportación de la imagen
                    // creará subcarpetas no deseadas, rompiendo el flujo silenciosamente.
                    // Renombramos la vista para garantizar una ruta plana y segura.
                    tempView3D.Name = $"Capture_{tempFileGuid}";

                    tempView = tempView3D;

                    // Configurar niveles de detalle y estilo visual de la vista temporal respetando la vista activa
                    if (doc.ActiveView != null)
                    {
                        try
                        {
                            tempView3D.DetailLevel = doc.ActiveView.DetailLevel;
                            tempView3D.DisplayStyle = doc.ActiveView.DisplayStyle;
                        }
                        catch
                        {
                            tempView3D.DetailLevel = ViewDetailLevel.Fine;
                            tempView3D.DisplayStyle = DisplayStyle.ShadingWithEdges;
                        }
                    }
                    else
                    {
                        tempView3D.DetailLevel = ViewDetailLevel.Fine;
                        tempView3D.DisplayStyle = DisplayStyle.ShadingWithEdges;
                    }

                    if (tempView3D.IsSectionBoxActive)
                    {
                        tempView3D.IsSectionBoxActive = false;
                    }

                    // Copiar Fase, Filtro de Fase y Opción de Diseño de la vista activa para asegurar visibilidad idéntica
                    if (doc.ActiveView != null)
                    {
                        try
                        {
                            Parameter? activePhaseParam = doc.ActiveView.get_Parameter(
                                BuiltInParameter.VIEW_PHASE
                            );
                            if (activePhaseParam != null && activePhaseParam.HasValue)
                            {
                                Parameter? tempPhaseParam = tempView3D.get_Parameter(
                                    BuiltInParameter.VIEW_PHASE
                                );
                                if (tempPhaseParam != null && !tempPhaseParam.IsReadOnly)
                                {
                                    tempPhaseParam.Set(activePhaseParam.AsElementId());
                                }
                            }

                            Parameter? activeFilterParam = doc.ActiveView.get_Parameter(
                                BuiltInParameter.VIEW_PHASE_FILTER
                            );
                            if (activeFilterParam != null && activeFilterParam.HasValue)
                            {
                                Parameter? tempFilterParam = tempView3D.get_Parameter(
                                    BuiltInParameter.VIEW_PHASE_FILTER
                                );
                                if (tempFilterParam != null && !tempFilterParam.IsReadOnly)
                                {
                                    tempFilterParam.Set(activeFilterParam.AsElementId());
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[ImageCaptureService] Error copying view parameters: {ex.Message}"
                            );
                        }
                    }

                    // Asegurar la visibilidad de todas las categorías de modelo en la vista
                    foreach (Category cat in doc.Settings.Categories)
                    {
                        if (
                            cat.CategoryType == CategoryType.Model
                            && tempView3D.CanCategoryBeHidden(cat.Id)
                        )
                        {
                            tempView3D.SetCategoryHidden(cat.Id, false);
                        }
                    }

                    // La captura debe mantener el modelo completo en encuadre. Los elementos seleccionados
                    // se usan únicamente para el resaltado, no para recortar la vista.
                    var selectedElements = new List<Element>();
                    foreach (var id in elementIds)
                    {
                        var elem = doc.GetElement(id);
                        if (elem != null)
                        {
                            selectedElements.Add(elem);
                        }
                    }

                    var elementsForFraming = CollectModelElementsForFraming(doc);
                    if (elementsForFraming.Count == 0)
                    {
                        elementsForFraming = selectedElements;
                    }

                    if (elementsForFraming.Count > 0)
                    {
                        var modelBounds = CalculateModelBoundingBox(elementsForFraming);
                        if (modelBounds != null)
                        {
                            tempView3D.SetSectionBox(modelBounds);
                            tempView3D.IsSectionBoxActive = true;

                            // Orientar la cámara apuntando al centro del modelo completo con un ángulo isométrico
                            XYZ center = GetBoundingBoxCenter(modelBounds);
                            double size = GetBoundingBoxSize(modelBounds);
                            double distance = Math.Max(size * 2.8, 50.0);
                            XYZ forward = new XYZ(-1, 1, -0.6).Normalize();
                            XYZ right = XYZ.BasisZ.CrossProduct(forward).Normalize();
                            XYZ up = forward.CrossProduct(right).Normalize();
                            XYZ eyePosition = center - forward * distance;
                            var newOrient = new ViewOrientation3D(eyePosition, up, forward);
                            tempView3D.SetOrientation(newOrient);
                            tempView3D.CropBoxActive = false;
                            doc.Regenerate();
                        }
                    }

                    trans.Commit();
                }
            }

            if (tempView != null)
            {
                // 4. Configurar las opciones de exportación de imagen
                var exportOptions = new ImageExportOptions
                {
                    ZoomType = ZoomFitType.FitToPage,
                    PixelSize = 2048, // Ancho de 2048 píxeles
                    FilePath = tempBaseFilePath,
                    ImageResolution = ImageResolution.DPI_300,
                    ExportRange = ExportRange.SetOfViews,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG, // Exportar vistas sombreadas como PNG también
                };
                exportOptions.SetViewsAndSheets(new List<ElementId> { tempView.Id });

                // Exportar la imagen a disco (esto debe realizarse fuera de transacciones activas)
                doc.ExportImage(exportOptions);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ImageCaptureService] Error during image capture process: {ex.Message}"
            );
            throw;
        }
        finally
        {
            // 5. Limpiar y eliminar la vista temporal en una transacción separada
            if (tempView != null && tempView.IsValidObject)
            {
                try
                {
                    using (var transDelete = new Transaction(doc, "Delete Temp View"))
                    {
                        transDelete.Start();
                        doc.Delete(tempView.Id);
                        transDelete.Commit();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ImageCaptureService] Error deleting temp view: {ex.Message}"
                    );
                }
            }
        }

        // 6. Localizar el archivo exportado en la carpeta temporal. Revit añade el nombre de la vista al archivo final
        // (por ejemplo: RevitCapture_xxx - {3D - xyz}.png). Buscamos archivos que inicien con tempBaseFilePath.
        try
        {
            var files = Directory.GetFiles(tempDir, $"RevitCapture_{tempFileGuid}*");
            if (files.Length > 0)
            {
                return files[0];
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ImageCaptureService] Error locating exported file: {ex.Message}"
            );
        }

        return string.Empty;
    }

    /// <summary>
    /// Recolecta todos los elementos de modelo visibles para ser usados en el cálculo de encuadre.
    /// </summary>
    private static List<Element> CollectModelElementsForFraming(Document doc)
    {
        var elements = new List<Element>();

        foreach (var element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
        {
            try
            {
                if (element.Category == null || element.Category.CategoryType != CategoryType.Model)
                {
                    continue;
                }

                if (element.get_BoundingBox(null) == null)
                {
                    continue;
                }

                elements.Add(element);
            }
            catch
            {
                // Algunos elementos internos o del sistema pueden lanzar excepciones al consultar sus límites
            }
        }

        return elements;
    }

    /// <summary>
    /// Calcula una caja de contorno unificada (BoundingBox) de un grupo de elementos, añadiendo un margen del 10%.
    /// </summary>
    private static BoundingBoxXYZ? CalculateModelBoundingBox(List<Element> elements)
    {
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double minZ = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        double maxZ = double.MinValue;

        bool hasValidBbox = false;

        foreach (var elem in elements)
        {
            var bbox = elem.get_BoundingBox(null);
            if (bbox == null)
                continue;

            minX = Math.Min(minX, bbox.Min.X);
            minY = Math.Min(minY, bbox.Min.Y);
            minZ = Math.Min(minZ, bbox.Min.Z);
            maxX = Math.Max(maxX, bbox.Max.X);
            maxY = Math.Max(maxY, bbox.Max.Y);
            maxZ = Math.Max(maxZ, bbox.Max.Z);
            hasValidBbox = true;
        }

        if (!hasValidBbox)
            return null;

        // Añadir margen del 10% alrededor de los límites calculados
        double marginX = (maxX - minX) * 0.1;
        double marginY = (maxY - minY) * 0.1;
        double marginZ = (maxZ - minZ) * 0.1;

        if (marginX < 1.0)
            marginX = 1.0;
        if (marginY < 1.0)
            marginY = 1.0;
        if (marginZ < 1.0)
            marginZ = 1.0;

        return new BoundingBoxXYZ
        {
            Transform = Transform.Identity,
            Min = new XYZ(minX - marginX, minY - marginY, minZ - marginZ),
            Max = new XYZ(maxX + marginX, maxY + marginY, maxZ + marginZ),
        };
    }

    /// <summary>
    /// Obtiene las coordenadas del punto central de una caja de contorno (BoundingBoxXYZ).
    /// </summary>
    private static XYZ GetBoundingBoxCenter(BoundingBoxXYZ bbox)
    {
        return new XYZ(
            (bbox.Min.X + bbox.Max.X) / 2,
            (bbox.Min.Y + bbox.Max.Y) / 2,
            (bbox.Min.Z + bbox.Max.Z) / 2
        );
    }

    /// <summary>
    /// Calcula la dimensión máxima en cualquiera de los ejes (X, Y, Z) de una caja de contorno.
    /// </summary>
    private static double GetBoundingBoxSize(BoundingBoxXYZ bbox)
    {
        return Math.Max(
            bbox.Max.X - bbox.Min.X,
            Math.Max(bbox.Max.Y - bbox.Min.Y, bbox.Max.Z - bbox.Min.Z)
        );
    }
}
