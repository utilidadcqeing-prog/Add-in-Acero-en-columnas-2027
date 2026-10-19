using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using CQEING_CuadroCantidades.UI.Models;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Clase responsable de exportar la estructura del presupuesto a un archivo Excel (.xlsx).
/// Utiliza ClosedXML para generar un archivo con estructura jerárquica (agrupación de filas),
/// estilos visuales y la inclusión de elementos vinculados e imágenes.
/// </summary>
public static class ExcelStructureWriter
{
    private const int ImageRows = 4;
    private const double ImageRowHeight = 18;
    private const int ImagePaddingPixels = 6;
    private const int MaxImageWidthPixels = 600;
    private const int MaxImageHeightPixels = 245;

    public static void WriteBudgetRows(string filePath, IEnumerable<BudgetRow> rows)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        var tempFilesToClean = new List<string>();

        try
        {
            using var workbook = new XLWorkbook();
            // Set font for the entire workbook
            workbook.Style.Font.FontName = "Aptos Display";
            
            var worksheet = workbook.Worksheets.Add("Cuadro_de_Cantidades");

            // Calcular globalMaxCol basado en las tablas de elementos
            int globalMaxCol = 5;
            foreach (var r in rows)
            {
                if (r.AssignedElementsTable != null)
                {
                    int visibleCols = 0;
                    foreach (DataColumn col in r.AssignedElementsTable.Columns)
                    {
                        if (col.ColumnName != "RevitUniqueId" && col.ColumnName != "IsSelected") 
                            visibleCols++;
                    }
                    globalMaxCol = Math.Max(globalMaxCol, visibleCols);
                }
            }

            // Configurar encabezados principales
            worksheet.Cell(1, 1).Value = "TIPO";
            worksheet.Cell(1, 2).Value = "CODIGO";
            worksheet.Cell(1, 3).Value = "DESCRIPCION";
            worksheet.Cell(1, 4).Value = "UND";
            worksheet.Cell(1, 5).Value = "CANTIDAD";

            var headerRange = worksheet.Range(1, 1, 1, globalMaxCol);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Font.FontColor = XLColor.White; // High contrast for dark background
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#28235B"); // Main header background color
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            worksheet.Row(1).Height = 25;

            // Anchos de columna por defecto
            worksheet.Column(1).Width = 18;
            worksheet.Column(2).Width = 24;
            worksheet.Column(3).Width = 60;
            worksheet.Column(4).Width = 12;
            worksheet.Column(5).Width = 15;
            worksheet.Column(6).Width = 25;
            worksheet.Column(7).Width = 18;
            worksheet.Column(8).Width = 18;

            int currentRow = 2;

            foreach (var row in rows)
            {
                // Escribir datos principales
                worksheet.Cell(currentRow, 1).Value = row.Type;
                worksheet.Cell(currentRow, 2).Value = row.Code;
                worksheet.Cell(currentRow, 3).Value = row.Description;
                worksheet.Cell(currentRow, 4).Value = row.Unit;
                
                // Cantidad como número si es posible
                if (row.Type.Equals("ACTIVIDAD", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(row.Quantity, out double quantityValue))
                    {
                        worksheet.Cell(currentRow, 5).Value = quantityValue;
                    }
                    else
                    {
                        worksheet.Cell(currentRow, 5).Value = row.Quantity;
                    }
                }
                else
                {
                    worksheet.Cell(currentRow, 5).Value = string.Empty;
                }

                // Aplicar agrupación
                worksheet.Row(currentRow).OutlineLevel = row.Level;

                // Estilos según el tipo
                var xlRange = worksheet.Range(currentRow, 1, currentRow, globalMaxCol);
                xlRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                xlRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                xlRange.Style.Border.TopBorderColor = XLColor.Gray;
                xlRange.Style.Border.BottomBorderColor = XLColor.Gray;

                if (row.Type.Equals("CAPITULO", StringComparison.OrdinalIgnoreCase))
                {
                    xlRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2900D"); // Chapter Color
                    xlRange.Style.Font.Bold = true;
                    worksheet.Row(currentRow).Height = 22;
                }
                else if (row.Type.Equals("SUBCAPITULO", StringComparison.OrdinalIgnoreCase))
                {
                    xlRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FED25F"); // Subchapter Color
                    xlRange.Style.Font.Bold = true;
                    worksheet.Row(currentRow).Height = 20;
                }
                else if (row.Type.Equals("ACTIVIDAD", StringComparison.OrdinalIgnoreCase))
                {
                    xlRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#B4CDEB"); // Activity Color
                    worksheet.Row(currentRow).Height = 20; // Default
                }

                currentRow++;

                // Insertar Imagen de Previsualización en una fila dedicada bajo los elementos
                if (row.Level == 2)
                {
                    List<string> imagesToInsert = new List<string>();

                    if (!string.IsNullOrWhiteSpace(row.PreviewImagesJson))
                    {
                        try
                        {
                            var snapshots = System.Text.Json.JsonSerializer.Deserialize<List<CQEING_CuadroCantidades.Core.Domain.Entities.ActivityImageSnapshot>>(row.PreviewImagesJson);
                            if (snapshots != null && snapshots.Count > 0)
                            {
                                snapshots.Sort((a, b) => b.CapturedAt.CompareTo(a.CapturedAt));
                                var selected = snapshots.Where(s => s.IsSelectedForPublication).ToList();
                                foreach (var snap in selected)
                                {
                                    string p = !string.IsNullOrWhiteSpace(snap.ImagePath) ? snap.ImagePath : snap.DebugImagePath;
                                    if (!string.IsNullOrWhiteSpace(p)) imagesToInsert.Add(p);
                                }
                            }
                        }
                        catch {}
                    }
                    else if (!string.IsNullOrEmpty(row.PreviewImagePath))
                    {
                        imagesToInsert.AddRange(row.PreviewImagePath.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()));
                    }

                    foreach (var rawUrl in imagesToInsert)
                    {
                        try
                        {
                            string imageToInsert = rawUrl;
                            string tempFilePath = "";

                            if (imageToInsert.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");
                                using (var client = new System.Net.Http.HttpClient())
                                {
                                    var imageBytes = client.GetByteArrayAsync(imageToInsert).Result;
                                    File.WriteAllBytes(tempFilePath, imageBytes);
                                }
                                imageToInsert = tempFilePath;
                                tempFilesToClean.Add(tempFilePath);
                            }

                            if (File.Exists(imageToInsert))
                            {
                                var pic = worksheet.AddPicture(imageToInsert);
                                int originalW = pic.OriginalWidth > 0 ? pic.OriginalWidth : MaxImageWidthPixels;
                                int originalH = pic.OriginalHeight > 0 ? pic.OriginalHeight : MaxImageHeightPixels;

                                // Ancho fijo deseado: ~12 cm (453 píxeles a 96 DPI)
                                int targetWidth = 453;
                                double aspectRatio = (double)originalH / originalW;
                                int targetHeight = (int)Math.Round(targetWidth * aspectRatio);

                                int requiredHeightPixels = targetHeight + (ImagePaddingPixels * 2);
                                double requiredHeightPoints = requiredHeightPixels * 0.75; // 1 pixel = 0.75 points
                                int requiredRows = Math.Max(1, (int)Math.Ceiling(requiredHeightPoints / ImageRowHeight));

                                int imageFirstRow = currentRow;
                                int imageLastRow = currentRow + requiredRows - 1;

                                for (int imageRow = imageFirstRow; imageRow <= imageLastRow; imageRow++)
                                {
                                    var xlImageRow = worksheet.Row(imageRow);
                                    xlImageRow.OutlineLevel = row.Level + 1; // Hacerlo colapsable con la Actividad
                                    xlImageRow.Height = ImageRowHeight;
                                }

                                var imageRange = worksheet.Range(
                                    imageFirstRow,
                                    1,
                                    imageLastRow,
                                    globalMaxCol);

                                imageRange.Merge();
                                imageRange.FirstCell().Value = " ";
                                imageRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                imageRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                                imageRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                                imageRange.Style.Border.OutsideBorderColor = XLColor.LightGray;
                                imageRange.Style.Fill.BackgroundColor = XLColor.White;

                                InsertAndFitPicture(worksheet, imageRange, pic);

                                currentRow = imageLastRow + 1;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error al insertar imagen en Excel: {ex.Message}");
                        }
                    }
                }

                // Si es actividad y tiene elementos asignados, agregarlos debajo
                if (row.Level == 2 && row.AssignedElementsTable != null && row.AssignedElementsTable.Rows.Count > 0)
                {
                    // Escribir encabezados de la tabla dinámica de elementos
                    var dt = row.AssignedElementsTable;
                    int elementCol = 1; // Empezar en la columna A

                    worksheet.Row(currentRow).OutlineLevel = row.Level + 1; // Agrupar bajo la actividad
                    worksheet.Row(currentRow).Height = 18;

                    var elementHeaderRange = worksheet.Range(currentRow, 1, currentRow, globalMaxCol);
                    elementHeaderRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#28235B"); // Table Headers Color
                    elementHeaderRange.Style.Font.FontColor = XLColor.White; // White font for contrast
                    elementHeaderRange.Style.Font.Bold = true;
                    elementHeaderRange.Style.Font.FontSize = 10;

                    List<string> columnsToWrite = new List<string>();
                    foreach (DataColumn col in dt.Columns)
                    {
                        if (col.ColumnName == "RevitUniqueId" || col.ColumnName == "IsSelected") 
                            continue;
                        
                        worksheet.Cell(currentRow, elementCol).Value = col.ColumnName;
                        columnsToWrite.Add(col.ColumnName);
                        elementCol++;
                    }
                    
                    currentRow++;

                    // Escribir filas de la tabla dinámica
                    foreach (DataRow dtRow in dt.Rows)
                    {
                        worksheet.Row(currentRow).OutlineLevel = row.Level + 1;
                        worksheet.Row(currentRow).Height = 16;
                        
                        var elementRowRange = worksheet.Range(currentRow, 1, currentRow, globalMaxCol);
                        elementRowRange.Style.Font.FontSize = 10;
                        
                        elementCol = 1;
                        foreach (string colName in columnsToWrite)
                        {
                            var val = dtRow[colName];
                            if (val != null && val != DBNull.Value)
                            {
                                if (double.TryParse(val.ToString(), out double numVal))
                                {
                                    worksheet.Cell(currentRow, elementCol).Value = numVal;
                                }
                                else
                                {
                                    worksheet.Cell(currentRow, elementCol).Value = val.ToString();
                                }
                            }
                            elementCol++;
                        }
                        currentRow++;
                    }
                    
                    // Agregar una fila vacía pequeña como separador
                    worksheet.Row(currentRow).OutlineLevel = row.Level + 1;
                    worksheet.Row(currentRow).Height = 8;
                    currentRow++;
                }
            }

            // Configurar panel congelado (Freeze panes)
            worksheet.SheetView.FreezeRows(1);

            // Configurar para que el resumen del esquema (botón +/-) quede arriba de los detalles
            worksheet.Outline.SummaryVLocation = XLOutlineSummaryVLocation.Top;

            // Guardar libro
            workbook.SaveAs(filePath);
        }
        finally
        {
            foreach (var tempFile in tempFilesToClean)
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }
    }

    private static void InsertAndFitPicture(IXLWorksheet worksheet, IXLRange range, IXLPicture pic)
    {

        int originalWidth = pic.OriginalWidth > 0 ? pic.OriginalWidth : MaxImageWidthPixels;
        int originalHeight = pic.OriginalHeight > 0 ? pic.OriginalHeight : MaxImageHeightPixels;

        // Calcular ancho del rango en píxeles (aprox: 1 char = 7 pixels + 5 de margen en ClosedXML)
        double rangeWidthPixels = 0;
        for (int c = range.FirstColumn().ColumnNumber(); c <= range.LastColumn().ColumnNumber(); c++)
        {
            rangeWidthPixels += (worksheet.Column(c).Width * 7d) + 5d;
        }

        // Calcular alto del rango en píxeles (1 point = 4/3 pixels a 96 DPI)
        double rangeHeightPoints = 0;
        for (int r = range.FirstRow().RowNumber(); r <= range.LastRow().RowNumber(); r++)
        {
            rangeHeightPoints += worksheet.Row(r).Height;
        }
        double rangeHeightPixels = rangeHeightPoints * 4d / 3d;

        double maxW = rangeWidthPixels - (ImagePaddingPixels * 2);
        double maxH = rangeHeightPixels - (ImagePaddingPixels * 2);

        if (maxW <= 0) maxW = MaxImageWidthPixels;
        if (maxH <= 0) maxH = MaxImageHeightPixels;

        // Calcular escala preservando proporción original
        double scaleX = maxW / originalWidth;
        double scaleY = maxH / originalHeight;
        double scale = Math.Min(scaleX, scaleY);
        scale = Math.Min(scale, 1d); // No agrandar más allá de su tamaño original

        int finalWidth = Math.Max(1, (int)Math.Round(originalWidth * scale));
        int finalHeight = Math.Max(1, (int)Math.Round(originalHeight * scale));

        // Offsets desde la esquina superior izquierda del rango completo
        // Se alinea completamente a la izquierda (offsetX = 0) y se centra verticalmente
        int offsetX = 0;
        int offsetY = (int)Math.Round((rangeHeightPixels - finalHeight) / 2d);

        // Encontrar la celda final (toCell) y sus offsets para garantizar un TwoCellAnchor perfecto
        // Esto previene que la imagen quede flotante y asegura que se oculte al contraer el grupo
        int endCol = range.FirstColumn().ColumnNumber();
        double currentX = 0;
        double targetX = offsetX + finalWidth;

        while (endCol < range.LastColumn().ColumnNumber())
        {
            double colW = (worksheet.Column(endCol).Width * 7d) + 5d;
            if (currentX + colW >= targetX) break;
            currentX += colW;
            endCol++;
        }
        int finalEndOffsetX = (int)Math.Round(targetX - currentX);

        int endRow = range.FirstRow().RowNumber();
        double currentY = 0;
        double targetY = offsetY + finalHeight;

        while (endRow < range.LastRow().RowNumber())
        {
            double rowH = worksheet.Row(endRow).Height * 4d / 3d;
            if (currentY + rowH >= targetY) break;
            currentY += rowH;
            endRow++;
        }
        int finalEndOffsetY = (int)Math.Round(targetY - currentY);

        // Configurar la imagen con anclaje a dos celdas explícito y ajuste de movimiento/tamaño con las celdas
        pic.MoveTo(range.FirstCell(), offsetX, offsetY, worksheet.Cell(endRow, endCol), finalEndOffsetX, finalEndOffsetY);
        pic.Placement = XLPicturePlacement.MoveAndSize;
    }
}
