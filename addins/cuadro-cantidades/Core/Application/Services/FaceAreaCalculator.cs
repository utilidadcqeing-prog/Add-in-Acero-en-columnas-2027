using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CQEING_CuadroCantidades.Core.Application.Services;

/// <summary>
/// Cara de un elemento según los ejes globales del proyecto: X1, Y1 y Z1 miran hacia el lado negativo
/// de su eje y X2, Y2 y Z2 hacia el positivo. Z1 es el fondo y Z2 la cara superior.
/// </summary>
public enum ElementFace
{
    X1,
    X2,
    Y1,
    Y2,
    Z1,
    Z2,
}

/// <summary>
/// Elementos que miden su área en M2 por caras elegidas.
/// </summary>
public enum FaceMeasuredKind
{
    None,
    Column,
    Beam,
    Footing,
}

/// <summary>
/// Área por caras de columnas, vigas y zapatas para actividades en M2. Al extraer el elemento se guarda en su JSON
/// el área útil de cada cara ("Área X1", "Área Y2"…), sin lo que tapan los elementos pegados en columnas y vigas,
/// y las caras elegidas ("Caras": "X1 X2 Y1 Y2"); la cantidad es la suma de las caras elegidas. Las vigas no guardan sus cabezas ni las columnas su base y su tope.
/// Las zapatas guardan sus seis caras completas: la cara superior incluye el área donde nace la columna.
/// </summary>
public static class FaceAreaCalculator
{
    /// <summary>
    /// Llave del JSON con las caras elegidas, separadas por espacios.
    /// </summary>
    public const string SelectionKey = "Caras";

    /// <summary>
    /// Columna de la tabla con las caras que se miden. Es también la llave con la que vuelven de la API.
    /// </summary>
    public const string SelectionColumn = "CARAS";

    /// <summary>
    /// Columna de la tabla con el área (m²) de las caras que se miden. Es también la llave con la que vuelve de la API.
    /// </summary>
    public const string AreaColumn = "ÁREA CARAS";

    /// <summary>
    /// Llave del JSON con las caras laterales que dan hacia afuera de la línea de propiedad ("X1 Y1"), vacía si
    /// ninguna. No existe si el modelo no tiene línea de propiedad o si la línea atraviesa el elemento.
    /// </summary>
    public const string ExteriorFacesKey = "Caras exteriores";

    /// <summary>
    /// Llave del JSON que avisa que la línea de propiedad atraviesa el elemento o que el elemento está fuera del lote.
    /// </summary>
    public const string BoundaryIssueKey = "Contorno";

    /// <summary>
    /// Valor de <see cref="BoundaryIssueKey"/>.
    /// </summary>
    public const string BoundaryCrossedValue = "atravesado";

    /// <summary>
    /// Caras que se pueden elegir en columnas: las cuatro laterales.
    /// </summary>
    public static IReadOnlyList<ElementFace> ColumnFaces { get; } =
        new[] { ElementFace.X1, ElementFace.X2, ElementFace.Y1, ElementFace.Y2 };

    /// <summary>
    /// Caras que se pueden elegir en vigas y zapatas: costados, fondo y cara superior.
    /// </summary>
    public static IReadOnlyList<ElementFace> BeamFaces { get; } =
        new[] { ElementFace.X1, ElementFace.X2, ElementFace.Y1, ElementFace.Y2, ElementFace.Z1, ElementFace.Z2 };

    /// <summary>
    /// Caras que se miden mientras nadie elija otras. En columnas, las cuatro laterales (igual que el área lateral);
    /// en vigas, costados y fondo; en zapatas, todas.
    /// </summary>
    public static IReadOnlySet<ElementFace> DefaultSelection(FaceMeasuredKind kind) => kind switch
    {
        FaceMeasuredKind.Column => ColumnFaces.ToHashSet(),
        FaceMeasuredKind.Beam => new HashSet<ElementFace>
        {
            ElementFace.X1, ElementFace.X2, ElementFace.Y1, ElementFace.Y2, ElementFace.Z1,
        },
        FaceMeasuredKind.Footing => BeamFaces.ToHashSet(),
        _ => new HashSet<ElementFace>(),
    };

    /// <summary>
    /// Decimales con que se guarda el área de cada cara: redondear cada cara a 2 y luego sumarlas acumula errores.
    /// </summary>
    public const int StoredDecimals = 4;

    /// <summary>
    /// Llave del JSON con el área (m²) de una cara.
    /// </summary>
    public static string AreaKey(ElementFace face) => $"Área {face}";

    /// <summary>
    /// Indica si la llave del JSON es el área de una cara.
    /// </summary>
    public static bool IsAreaKey(string key) =>
        BeamFaces.Any(face => string.Equals(key, AreaKey(face), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Redondea un área a 2 decimales con el medio hacia arriba (3.515 → 3.52), como en obra. Primero se quita el
    /// ruido binario (3.51499999… → 3.515), que con el redondeo bancario por defecto la dejaba en 3.51.
    /// </summary>
    public static double RoundArea(double area) =>
        Math.Round(Math.Round(area, StoredDecimals), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Tipo de elemento según el nombre de su categoría, con las mismas reglas que la tabla de detalles.
    /// </summary>
    public static FaceMeasuredKind KindFromCategory(string? category)
    {
        string name = category?.ToLowerInvariant() ?? string.Empty;
        if (name.Contains("column") || name.Contains("pilar"))
            return FaceMeasuredKind.Column;
        if (name.Contains("framing") || name.Contains("armazón") || name.Contains("viga"))
            return FaceMeasuredKind.Beam;
        if (name.Contains("zapata") || name.Contains("foundation") || name.Contains("cimentación"))
            return FaceMeasuredKind.Footing;
        return FaceMeasuredKind.None;
    }

    /// <summary>
    /// Cara a la que pertenece una normal: la del eje global con mayor componente, en el sentido de su signo.
    /// Así una columna girada reparte sus caras en el eje más cercano.
    /// </summary>
    public static ElementFace Classify(double x, double y, double z)
    {
        double ax = Math.Abs(x), ay = Math.Abs(y), az = Math.Abs(z);
        if (az >= ax && az >= ay)
            return z < 0 ? ElementFace.Z1 : ElementFace.Z2;
        if (ax >= ay)
            return x < 0 ? ElementFace.X1 : ElementFace.X2;
        return y < 0 ? ElementFace.Y1 : ElementFace.Y2;
    }

    /// <summary>
    /// Texto de las caras en orden fijo ("X1 X2 Y1"), o vacío si no hay ninguna.
    /// </summary>
    public static string Format(IEnumerable<ElementFace> faces) =>
        string.Join(" ", faces.Distinct().OrderBy(f => f));

    /// <summary>
    /// Caras de un texto separado por espacios o comas. Ignora lo que no sea una cara.
    /// </summary>
    public static IReadOnlySet<ElementFace> Parse(string? text)
    {
        var faces = new HashSet<ElementFace>();
        if (string.IsNullOrWhiteSpace(text))
            return faces;

        foreach (var token in text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            // Solo nombres: Enum.TryParse también acepta números ("1" sería X2)
            if (char.IsLetter(token[0]) && Enum.TryParse(token, ignoreCase: true, out ElementFace face) && Enum.IsDefined(face))
                faces.Add(face);
        }

        return faces;
    }

    /// <summary>
    /// Lee del JSON del elemento sus áreas por cara y las caras elegidas.
    /// Devuelve null si el elemento no es columna, viga ni zapata.
    /// </summary>
    public static FaceAreaData? Read(string? json, string? category)
    {
        var kind = KindFromCategory(category);
        if (kind == FaceMeasuredKind.None)
            return null;

        var areas = new Dictionary<ElementFace, double>();
        IReadOnlySet<ElementFace>? storedSelection = null;
        double? returnedArea = null;
        IReadOnlySet<ElementFace>? exteriorFaces = null;
        bool crossesBoundary = false;

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            if (string.Equals(prop.Name, SelectionKey, StringComparison.OrdinalIgnoreCase))
                            {
                                // "Caras" (lo escribe el add-in) o "CARAS" (vuelve de la API); vacío = ninguna cara
                                storedSelection = Parse(prop.Value.GetString());
                            }
                            else if (string.Equals(prop.Name, ExteriorFacesKey, StringComparison.OrdinalIgnoreCase))
                            {
                                exteriorFaces = Parse(prop.Value.GetString());
                            }
                            else if (string.Equals(prop.Name, BoundaryIssueKey, StringComparison.OrdinalIgnoreCase))
                            {
                                crossesBoundary = true;
                            }
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out double value))
                        {
                            if (string.Equals(prop.Name, AreaColumn, StringComparison.OrdinalIgnoreCase))
                            {
                                returnedArea = value;
                                continue;
                            }

                            foreach (var face in BeamFaces)
                            {
                                if (string.Equals(prop.Name, AreaKey(face), StringComparison.OrdinalIgnoreCase))
                                    areas[face] = value;
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // JSON corrupto: el elemento queda sin datos de caras
            }
        }

        var allowed = kind == FaceMeasuredKind.Column ? ColumnFaces : BeamFaces;
        foreach (var face in areas.Keys.Except(allowed).ToList())
        {
            areas.Remove(face);
        }

        return new FaceAreaData(
            kind,
            areas,
            storedSelection ?? DefaultSelection(kind),
            storedSelection != null,
            returnedArea,
            exteriorFaces?.Where(allowed.Contains).ToHashSet(),
            crossesBoundary
        );
    }
}

/// <summary>
/// Áreas por cara y caras elegidas de una columna, viga o zapata.
/// </summary>
/// <param name="Kind">Columna, viga o zapata.</param>
/// <param name="Areas">Área (m²) de cada cara que tiene el elemento.</param>
/// <param name="Selection">Caras elegidas, o las de por defecto si nadie las ha elegido.</param>
/// <param name="HasStoredSelection">Indica si las caras elegidas vienen guardadas en el JSON.</param>
/// <param name="ReturnedArea">Área de caras que trae el JSON devuelto por la API, sin áreas por cara.</param>
/// <param name="ExteriorFaces">Caras laterales que dan hacia afuera de la línea de propiedad, o null si no se
/// clasificó (sin línea de propiedad, la línea atraviesa el elemento o los datos vienen de la API).</param>
/// <param name="CrossesBoundary">La línea de propiedad atraviesa el elemento o el elemento está fuera del lote.</param>
public sealed record FaceAreaData(
    FaceMeasuredKind Kind,
    IReadOnlyDictionary<ElementFace, double> Areas,
    IReadOnlySet<ElementFace> Selection,
    bool HasStoredSelection,
    double? ReturnedArea,
    IReadOnlySet<ElementFace>? ExteriorFaces = null,
    bool CrossesBoundary = false
)
{
    /// <summary>
    /// Indica si el JSON trae el área de cada cara.
    /// </summary>
    public bool HasFaceAreas => Areas.Count > 0;

    /// <summary>
    /// Caras elegidas que el elemento tiene; sin áreas por cara, todas las elegidas.
    /// </summary>
    public IReadOnlyList<ElementFace> MeasuredFaces =>
        Selection.Where(f => !HasFaceAreas || Areas.ContainsKey(f)).OrderBy(f => f).ToList();

    /// <summary>
    /// Área (m²) de las caras elegidas, redondeada una sola vez a 2 decimales, o null si el elemento no tiene
    /// datos de caras. Es el valor de la fila, del total de la tabla y de la cantidad de la actividad.
    /// </summary>
    public double? SelectedArea => HasFaceAreas
        ? FaceAreaCalculator.RoundArea(Selection.Sum(f => Areas.TryGetValue(f, out double area) ? area : 0))
        : ReturnedArea;

    /// <summary>
    /// Indica si el elemento ya se mide por caras.
    /// </summary>
    public bool HasData => HasFaceAreas || HasStoredSelection || ReturnedArea != null;
}
