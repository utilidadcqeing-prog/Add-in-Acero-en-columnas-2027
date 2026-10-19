using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using CQEING_CuadroCantidades.UI.Models;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Clase estática encargada de la lectura y procesamiento de archivos Excel (.xlsx).
/// Extrae la estructura de presupuesto descompimiendo el archivo OpenXML, leyendo sus
/// cadenas compartidas (shared strings), la primera hoja de cálculo y estructurando
/// jerárquicamente las filas en capítulos, subcapítulos y actividades.
/// </summary>
public static class ExcelStructureReader
{
    /// <summary>
    /// Espacio de nombres XML para elementos principales de hojas de cálculo OpenXML.
    /// </summary>
    private static readonly XNamespace SpreadsheetNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>
    /// Espacio de nombres XML para las relaciones internas del documento de Office.
    /// </summary>
    private static readonly XNamespace RelationshipsNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// Espacio de nombres XML para las relaciones del paquete contenedor.
    /// </summary>
    private static readonly XNamespace PackageRelationshipsNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// Lee y analiza un archivo de Excel para extraer la lista de filas del presupuesto organizadas.
    /// </summary>
    /// <param name="filePath">Ruta absoluta del archivo de Excel (.xlsx).</param>
    /// <returns>Colección de filas de presupuesto cargadas y estructuradas.</returns>
    public static IReadOnlyList<BudgetRow> ReadBudgetRows(string filePath)
    {
        // Abrir el archivo con permisos compartidos de lectura/escritura/eliminación para evitar bloqueos
        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        
        // Tratar el archivo Excel como un archivo Zip
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);
        
        // Leer la tabla de cadenas compartidas (Shared Strings) que usa Excel para optimizar textos
        List<string> sharedStrings = ReadSharedStrings(archive);
        
        // Buscar la entrada de la primera hoja de trabajo del libro
        ZipArchiveEntry worksheetEntry = GetFirstWorksheetEntry(archive);
        XDocument worksheet = ReadXml(worksheetEntry);

        // Extraer los datos de cada celda por cada fila de la hoja
        List<List<string>> rows = worksheet
            .Descendants(SpreadsheetNs + "sheetData")
            .Elements(SpreadsheetNs + "row")
            .Select(row => ReadRow(row, sharedStrings))
            .Where(row => row.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToList();

        if (rows.Count == 0)
        {
            return Array.Empty<BudgetRow>();
        }

        // Mapear los encabezados de las columnas (e.g. "tipo", "codigo", "descripcion")
        Dictionary<string, int> headers = GetHeaderMap(rows);
        IEnumerable<List<string>> dataRows = headers.Count > 0 ? rows.Skip(1) : rows;

        // Convertir cada fila de datos en un objeto BudgetRow
        IReadOnlyList<BudgetRow> parsedRows = dataRows
            .Select((row, index) => CreateBudgetRow(row, index + 1, headers))
            .Where(row =>
                IsStructureType(row.Type)
                && !string.IsNullOrWhiteSpace(row.Description)
            )
            .ToList();

        // Normalizar los códigos y niveles de indentación de la estructura del presupuesto
        return NormalizeHierarchy(parsedRows);
    }

    /// <summary>
    /// Obtiene la entrada Zip de la primera hoja de cálculo del libro de Excel, resolviendo su relación interna.
    /// </summary>
    private static ZipArchiveEntry GetFirstWorksheetEntry(ZipArchive archive)
    {
        // Leer el archivo xl/workbook.xml para buscar la definición de las hojas
        XDocument workbook = ReadXml(GetRequiredEntry(archive, "xl/workbook.xml"));
        string? relationshipId = workbook
            .Descendants(SpreadsheetNs + "sheet")
            .FirstOrDefault()
            ?.Attribute(RelationshipsNs + "id")
            ?.Value;

        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            throw new InvalidDataException("El archivo no contiene hojas de cálculo.");
        }

        // Buscar en las relaciones del libro para obtener la ruta física de la hoja en el Zip
        XDocument relationships = ReadXml(GetRequiredEntry(archive, "xl/_rels/workbook.xml.rels"));
        string? target = relationships
            .Descendants(PackageRelationshipsNs + "Relationship")
            .FirstOrDefault(relationship => relationship.Attribute("Id")?.Value == relationshipId)
            ?.Attribute("Target")
            ?.Value;

        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidDataException("No se pudo encontrar la primera hoja del archivo.");
        }

        // Formatear correctamente la ruta del archivo xml de la hoja de cálculo
        string worksheetPath = target.StartsWith("/", StringComparison.Ordinal)
            ? target.TrimStart('/')
            : $"xl/{target}";
        worksheetPath = worksheetPath.Replace('\\', '/');

        return GetRequiredEntry(archive, worksheetPath);
    }

    /// <summary>
    /// Lee la tabla de cadenas de texto compartidas (xl/sharedStrings.xml) del archivo Excel.
    /// </summary>
    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return new List<string>();
        }

        XDocument document = ReadXml(entry);
        return document
            .Descendants(SpreadsheetNs + "si")
            .Select(item =>
                string.Concat(item.Descendants(SpreadsheetNs + "t").Select(text => text.Value))
            )
            .ToList();
    }

    /// <summary>
    /// Parsea una fila XML de Excel en una lista ordenada de cadenas representando las celdas, llenando celdas vacías intermedias.
    /// </summary>
    private static List<string> ReadRow(XElement rowElement, IReadOnlyList<string> sharedStrings)
    {
        var values = new List<string>();

        foreach (XElement cell in rowElement.Elements(SpreadsheetNs + "c"))
        {
            // Obtener el índice de la columna a partir de su referencia (ej: "A1" -> 0, "B1" -> 1)
            int columnIndex = GetColumnIndex(cell.Attribute("r")?.Value);
            while (values.Count < columnIndex)
            {
                values.Add(string.Empty);
            }

            values.Add(ReadCellValue(cell, sharedStrings));
        }

        return values;
    }

    /// <summary>
    /// Lee el valor de una celda XML de Excel, resolviéndolo si se trata de un string en línea o de un shared string indexado.
    /// </summary>
    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        string cellType = cell.Attribute("t")?.Value ?? string.Empty;

        // Strings insertados directamente en la celda
        if (cellType == "inlineStr")
        {
            return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(text => text.Value))
                .Trim();
        }

        // Buscar el valor sin procesar de la celda
        string rawValue = cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
        
        // Si el tipo de celda es "s", el valor representa un índice en sharedStrings.xml
        if (
            cellType == "s"
            && int.TryParse(
                rawValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int sharedStringIndex
            )
            && sharedStringIndex >= 0
            && sharedStringIndex < sharedStrings.Count
        )
        {
            return sharedStrings[sharedStringIndex].Trim();
        }

        return rawValue.Trim();
    }

    /// <summary>
    /// Convierte la referencia de una celda (ej: "AC10") a su índice numérico de columna basado en 0.
    /// </summary>
    private static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return 0;
        }

        int index = 0;
        foreach (char character in cellReference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            index = (index * 26) + char.ToUpperInvariant(character) - 'A' + 1;
        }

        return Math.Max(index - 1, 0);
    }

    /// <summary>
    /// Genera un mapeo de nombres de encabezados normalizados a sus respectivos índices de columna.
    /// </summary>
    private static Dictionary<string, int> GetHeaderMap(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        IReadOnlyList<string> row = rows[0];
        if (!RowLooksLikeHeader(row))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < row.Count; index++)
        {
            string normalized = NormalizeHeader(row[index]);
            if (normalized.Length > 0 && !headers.ContainsKey(normalized))
            {
                headers.Add(normalized, index);
            }
        }

        return headers.Count > 0
            ? headers
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Comprueba si los valores de una fila coinciden con los encabezados esperados (tipo, código, descripción).
    /// </summary>
    private static bool RowLooksLikeHeader(IReadOnlyList<string> row)
    {
        HashSet<string> headers = row.Select(NormalizeHeader).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return headers.Contains("tipo")
            && headers.Contains("codigo")
            && headers.Contains("descripcion");
    }

    /// <summary>
    /// Crea una instancia de <see cref="BudgetRow"/> a partir de los datos leídos de una fila de Excel.
    /// </summary>
    private static BudgetRow CreateBudgetRow(
        IReadOnlyList<string> row,
        int rowNumber,
        IReadOnlyDictionary<string, int> headers
    )
    {
        string type = GetValue(row, headers, "tipo", 0);
        string code = GetValue(row, headers, "codigo", 1);
        string description = GetValue(row, headers, "descripcion", 2);
        string unit = GetValue(row, headers, "unidad", 3);
        string quantity = GetValue(row, headers, "cantidad", 4);

        return new BudgetRow
        {
            RowNumber = rowNumber,
            Outline = IsExpandableType(type) ? "▼" : string.Empty,
            Type = NormalizeType(type),
            Code = code,
            Description = description,
            Unit = unit,
            Quantity = quantity,
        };
    }

    /// <summary>
    /// Normaliza la jerarquía de las filas del presupuesto, calculando los niveles de indentación y regenerando
    /// los códigos de estructura jerárquica unificados (ej: C0001, C0001_S0001, C0001_S0001_A0001).
    /// </summary>
    private static IReadOnlyList<BudgetRow> NormalizeHierarchy(IReadOnlyList<BudgetRow> rows)
    {
        var normalized = new List<BudgetRow>();
        string currentChapterCode = string.Empty;
        string currentSubchapterCode = string.Empty;
        int chapterCount = 0;
        int subchapterCount = 0;
        int activityCount = 0;

        foreach (BudgetRow row in rows)
        {
            if (row.Type.Equals("CAPITULO", StringComparison.OrdinalIgnoreCase))
            {
                chapterCount++;
                subchapterCount = 0;
                activityCount = 0;
                currentChapterCode = NormalizeTypedCode(row.Code, "C", chapterCount);
                currentSubchapterCode = string.Empty;

                row.RowNumber = normalized.Count + 1;
                row.Code = currentChapterCode;
                row.Level = 0; // Nivel del Capítulo
                normalized.Add(row);
                continue;
            }

            if (row.Type.Equals("SUBCAPITULO", StringComparison.OrdinalIgnoreCase))
            {
                subchapterCount++;
                activityCount = 0;
                currentSubchapterCode = NormalizeTypedCode(row.Code, "S", subchapterCount);
                string displayCode = JoinCodes(currentChapterCode, currentSubchapterCode);

                row.RowNumber = normalized.Count + 1;
                row.Code = displayCode;
                row.Level = 1; // Nivel del Subcapítulo
                normalized.Add(row);
                continue;
            }

            activityCount++;
            string activityCode = NormalizeTypedCode(row.Code, "A", activityCount);
            string code = JoinCodes(currentChapterCode, currentSubchapterCode, activityCode);

            row.RowNumber = normalized.Count + 1;
            row.Code = code;
            row.Level = 2; // Nivel de la Actividad
            row.CanEdit = true;
            normalized.Add(row);
        }

        return normalized;
    }

    /// <summary>
    /// Conecta múltiples segmentos de códigos utilizando guiones bajos.
    /// </summary>
    private static string JoinCodes(params string[] codes)
    {
        return string.Join("_", codes.Where(code => !string.IsNullOrWhiteSpace(code)));
    }

    /// <summary>
    /// Remueve tildes y normaliza los términos para los tipos de fila del presupuesto.
    /// </summary>
    private static string NormalizeType(string type)
    {
        string normalized = type.Trim().ToUpperInvariant();

        return normalized switch
        {
            "CAPÍTULO" => "CAPITULO",
            "SUBCAPÍTULO" => "SUBCAPITULO",
            _ => normalized,
        };
    }

    /// <summary>
    /// Asegura que el código de la fila comience con el prefijo correspondiente y tenga un número con formato estándar de cuatro dígitos.
    /// </summary>
    private static string NormalizeTypedCode(string value, string prefix, int fallbackNumber)
    {
        string trimmed = value.Trim().ToUpperInvariant();
        string digits = string.Concat(trimmed.Where(char.IsDigit));

        if (prefix is "S" or "A")
        {
            string compact = trimmed.Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);

            if (
                prefix == "A"
                &&
                compact.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && compact.Length is > 1 and <= 5
                && compact.Skip(1).All(char.IsDigit)
                && int.TryParse(compact[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedNumber)
            )
            {
                return $"{prefix}{parsedNumber:0000}";
            }

            return $"{prefix}{fallbackNumber:0000}";
        }

        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && digits.Length > 0)
        {
            if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedNumber))
            {
                return $"{prefix}{parsedNumber:0000}";
            }
        }

        return $"{prefix}{fallbackNumber:0000}";
    }

    /// <summary>
    /// Obtiene el valor de una celda de forma segura a partir de los encabezados mapeados, o usa una columna por defecto en su defecto.
    /// </summary>
    private static string GetValue(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headers,
        string header,
        int fallbackIndex
    )
    {
        if (headers.TryGetValue(header, out int headerIndex))
        {
            return GetCell(row, headerIndex);
        }

        return GetCell(row, fallbackIndex);
    }

    /// <summary>
    /// Obtiene de forma segura el valor en el índice especificado dentro de una lista de celdas.
    /// </summary>
    private static string GetCell(IReadOnlyList<string> row, int index)
    {
        return index >= 0 && index < row.Count ? row[index] : string.Empty;
    }

    /// <summary>
    /// Normaliza y limpia las cadenas de encabezado de Excel (quita acentos, convierte a minúsculas y mapea a términos estándar).
    /// </summary>
    private static string NormalizeHeader(string value)
    {
        string normalized = value
            .Trim()
            .ToLowerInvariant()
            .Replace("á", "a", StringComparison.Ordinal)
            .Replace("é", "e", StringComparison.Ordinal)
            .Replace("í", "i", StringComparison.Ordinal)
            .Replace("ó", "o", StringComparison.Ordinal)
            .Replace("ú", "u", StringComparison.Ordinal);

        return normalized switch
        {
            "cod" or "codigo" or "código" => "codigo",
            "descripcion" or "descripción" or "detalle" => "descripcion",
            "und" or "unidad" or "un" => "unidad",
            "cant" or "cantidad" => "cantidad",
            "tipo" or "clase" => "tipo",
            _ => normalized,
        };
    }

    /// <summary>
    /// Verifica si un tipo de fila es de tipo desplegable o expandible (Capítulo o Subcapítulo).
    /// </summary>
    private static bool IsExpandableType(string type)
    {
        string normalized = NormalizeType(type);

        return normalized.Equals("CAPITULO", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("SUBCAPITULO", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifica si el tipo de fila representa una categoría válida para la estructura del presupuesto.
    /// </summary>
    private static bool IsStructureType(string type)
    {
        string normalized = NormalizeType(type);

        return normalized.Equals("CAPITULO", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("SUBCAPITULO", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("ACTIVIDAD", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Carga y analiza una entrada XML de tipo XDocument desde una parte del zip del archivo Excel.
    /// </summary>
    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }

    /// <summary>
    /// Recupera una entrada requerida dentro del archivo Zip de Excel o lanza una excepción si no existe.
    /// </summary>
    private static ZipArchiveEntry GetRequiredEntry(ZipArchive archive, string entryPath)
    {
        return archive.GetEntry(entryPath)
            ?? throw new InvalidDataException(
                $"No se encontró '{entryPath}' dentro del archivo Excel."
            );
    }
}
