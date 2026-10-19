# Caras exteriores según la línea de propiedad — Plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: usar superpowers:subagent-driven-development (recomendado) o superpowers:executing-plans para ejecutar este plan tarea por tarea. Los pasos usan casillas (`- [ ]`).

**Objetivo:** que el usuario pueda quitar (o dejar solas) las caras exteriores de columnas y vigas con un botón, sin depender de los nombres X1/X2/Y1/Y2. Hoy el exterior de una columna puede ser X1 y el de otra X2.

**Arquitectura:** el add-in lee las **líneas de propiedad** del modelo (Masa y emplazamiento → Línea de propiedad) como el contorno del edificio. Con ese contorno decide cuáles caras son exteriores:
- Una cara de columna o viga es **exterior** si un punto 30 cm hacia afuera de su centro queda **fuera** del contorno.
- Si el centro de alguna cara (1 cm hacia adentro) ya está fuera, la línea **atraviesa** el elemento (o el elemento está fuera del lote). Ese elemento no se clasifica y se avisa.

La clasificación se guarda en el JSON de cada elemento al extraer y al sincronizar, igual que las áreas por cara. En el panel "Caras para el área" aparecen dos botones nuevos:
- **"Quitar exteriores"**: caras elegidas menos las exteriores de cada elemento.
- **"Solo exteriores"**: solo las caras exteriores de cada elemento.

Los botones cambian la selección guardada ("Caras"), así que el resto del cálculo (área por caras, tabla, cantidad de la actividad, API) no cambia.

**Stack:** C# / .NET 8, WPF + CommunityToolkit.Mvvm, Revit API 2026.

**Origen:** reunión del 14/10/2026 (Carlos Quintero, min 26:16–34:26). Con ejes globales, "solo X2" toma una cara interior en una columna y una exterior en otra. Pidió una opción para tomar o no las caras exteriores y propuso marcar el límite con una línea en Revit. Se eligió la línea de propiedad nativa en vez de líneas de modelo con un estilo acordado.

**Verificado en Revit 2026.4 (14/10/2026) con el modelo de prueba:**
- `PropertyLine` no tiene métodos propios en la API 2026. Sus lados se leen con `element.get_Geometry(new Options())`, que devuelve un `Line` por lado, en orden y encadenados, con Z = 0.
- Con la línea 816671 (rectángulo de (−0.20, −0.20) a (7.20, 12.20) m) la regla de 30 cm clasifica bien las 24 columnas 0.30 × 0.30 y las 17 vigas VIG_25X35, en los dos pisos.
- Con la línea 816703, que pasa por dentro de las columnas, sin el aviso de "atravesado" saldrían X1 y X2 exteriores a la vez en A-9, 5-9 y 6-9.

## Restricciones globales

- Rama nueva `feature/caras-exteriores`, creada desde `feature/caras-area-ocupada` (usa el `ClassifyFaces` con `Transform` de esa rama).
- Solo **columnas y vigas**. Las zapatas no se clasifican y no muestran los botones.
- Solo las caras laterales (X1, X2, Y1, Y2). Z1 y Z2 nunca son exteriores.
- Sin línea de propiedad en el modelo, todo queda como hoy: no se escriben llaves nuevas y los botones se ven deshabilitados con un texto que explica por qué.
- Tablas: no se agregan ni quitan columnas. Las llaves nuevas del JSON no son columnas de la tabla.
- Commits en español con prefijo convencional, **sin** `Co-Authored-By` ni "Generated with Claude".
- Compilar en la carpeta del repo con Revit cerrado: `dotnet build CQEING_CuadroCantidades.csproj -c Debug`.
- El repo no tiene proyecto de pruebas. Se verifica compilando y con los escenarios en Revit del final, con valores esperados ya calculados en el modelo de prueba.

## Llaves nuevas en el JSON del elemento

| Llave | Valor | Cuándo se escribe |
|---|---|---|
| `"Caras exteriores"` | `"X1 Y1"`, o `""` si no tiene ninguna | Hay línea de propiedad y el elemento queda dentro del lote |
| `"Contorno"` | `"atravesado"` | Hay línea de propiedad, pero la línea atraviesa el elemento o este está fuera del lote |

Las dos llaves no existen a la vez. Si el modelo no tiene línea de propiedad, no se escribe ninguna y la sincronización borra las que hubiera.

## Review Focus

1. **Línea que atraviesa elementos** (como la 816703): esos elementos no se clasifican, el panel muestra cuántos son, y los dos botones no les cambian las caras. → Escenario E4.
2. **Mover o borrar la línea de propiedad después de usar "Quitar exteriores"**: la clasificación se actualiza al sincronizar, pero la selección "Caras" ya guardada no cambia sola. El usuario vuelve a presionar el botón. Es intencional: la selección es lo único que sobrevive al ida y vuelta con la API. → Escenario E5.
3. **Elemento sin caras exteriores con "Solo exteriores"**: su selección queda vacía y su área en 0. → Escenario E2 (5-7 y 5-8).
4. **Datos traídos de la API**: el JSON vuelve sin `"Caras exteriores"`. `ResolveMissingRevitDataAsync` lo recalcula al abrir la tabla si Revit está abierto. Mientras tanto, los botones quedan deshabilitados para esos elementos. → Escenario E6.
5. **Rendimiento**: `BoundaryFaceClassifier.Create` consulta las líneas de propiedad una vez por elemento. Son pocas y la consulta es barata, pero hay que medir que la extracción no tarde notablemente más. → Escenario E7.

---

## Archivos

| Archivo | Acción | Responsabilidad |
|---|---|---|
| `Core/Application/Services/LotContour.cs` | Crear | Contorno del lote en planta (polígonos) y prueba punto-dentro. Sin Revit. |
| `Services/BoundaryFaceClassifier.cs` | Crear | Lee las líneas de propiedad y clasifica las caras laterales de una columna o viga en exteriores o interiores, o la marca como atravesada. |
| `Services/FaceAreaExtractor.cs` | Modificar | Expone las caras planas con su cara global; escribe y sincroniza `"Caras exteriores"` y `"Contorno"`. |
| `Core/Application/Services/FaceAreaCalculator.cs` | Modificar | Constantes de las llaves nuevas; `Read` las lee; `FaceAreaData` gana `ExteriorFaces` y `CrossesBoundary`. |
| `UI/ViewModels/FaceSelectionViewModel.cs` | Modificar | Los cambios de caras reciben el `FaceAreaData` de cada elemento; comandos "Quitar exteriores" y "Solo exteriores"; textos de estado. |
| `UI/ViewModels/DynamicTableViewModel.cs` | Modificar (`ApplyFaceSelectionAsync`, líneas 848–866) | Pasa el `FaceAreaData` completo al cambio. |
| `UI/Views/FaceSelectorPanel.xaml` | Modificar (después de la línea 403) | Bloque "Caras exteriores" con los dos botones y los avisos. |

---

### Tarea 1: Clasificar las caras exteriores y guardarlas en el JSON

**Archivos:**
- Crear: `Core/Application/Services/LotContour.cs`
- Crear: `Services/BoundaryFaceClassifier.cs`
- Modificar: `Services/FaceAreaExtractor.cs`
- Modificar: `Core/Application/Services/FaceAreaCalculator.cs`

**Interfaces:**
- Produce: `LotContour(IReadOnlyList<IReadOnlyList<(double X, double Y)>> polygons)`, `bool IsEmpty`, `bool Contains(double x, double y)`.
- Produce: `BoundaryFaceClassifier.Create(Document) : BoundaryFaceClassifier?` (null si no hay líneas de propiedad cerradas) y `Classify(Element) : BoundaryClassification?` (null si no es columna ni viga, o si la geometría no se puede leer).
- Produce: `record BoundaryClassification(IReadOnlySet<ElementFace> ExteriorFaces, bool CrossesBoundary)`.
- Produce: `FaceAreaExtractor.EnumeratePlanarFaces(Element) : IEnumerable<(PlanarFace Face, Transform Transform, ElementFace GlobalFace)>` (internal).
- Produce: `FaceAreaCalculator.ExteriorFacesKey = "Caras exteriores"`, `BoundaryIssueKey = "Contorno"`, `BoundaryCrossedValue = "atravesado"`.
- Produce: `FaceAreaData.ExteriorFaces : IReadOnlySet<ElementFace>?` (null = sin clasificar) y `FaceAreaData.CrossesBoundary : bool`.

- [ ] **Paso 1: Crear `Core/Application/Services/LotContour.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace CQEING_CuadroCantidades.Core.Application.Services;

/// <summary>
/// Contorno del lote en planta: uno o varios polígonos cerrados (las líneas de propiedad del modelo). Un punto está
/// dentro del lote si cae dentro de alguno. Coordenadas del proyecto en pies, sin la Z.
/// </summary>
public sealed class LotContour
{
    private readonly IReadOnlyList<IReadOnlyList<(double X, double Y)>> _polygons;

    public LotContour(IReadOnlyList<IReadOnlyList<(double X, double Y)>> polygons)
    {
        _polygons = polygons.Where(polygon => polygon.Count >= 3).ToList();
    }

    /// <summary>
    /// Indica que no hay ningún polígono válido.
    /// </summary>
    public bool IsEmpty => _polygons.Count == 0;

    /// <summary>
    /// Indica si el punto cae dentro de alguno de los polígonos.
    /// </summary>
    public bool Contains(double x, double y) => _polygons.Any(polygon => IsInside(polygon, x, y));

    // Regla par-impar: un rayo horizontal desde el punto cruza un número impar de lados si el punto está dentro
    private static bool IsInside(IReadOnlyList<(double X, double Y)> polygon, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (xi, yi) = polygon[i];
            var (xj, yj) = polygon[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }

        return inside;
    }
}
```

- [ ] **Paso 2: Exponer las caras planas en `Services/FaceAreaExtractor.cs`**

Agregar después de `GetFaceReferences`:

```csharp
    /// <summary>
    /// Caras planas del sólido real de una columna, viga o zapata, con la cara global a la que pertenecen y la
    /// transformación a coordenadas del proyecto. No incluye las cabezas de las vigas.
    /// </summary>
    internal static IEnumerable<(PlanarFace Face, Transform Transform, ElementFace GlobalFace)> EnumeratePlanarFaces(Element element)
    {
        foreach (var (face, transform, areas) in ClassifyFaces(element, computeReferences: false, original: false))
        {
            if (face is PlanarFace planar && areas.Count == 1)
                yield return (planar, transform, areas.Keys.First());
        }
    }
```

- [ ] **Paso 3: Crear `Services/BoundaryFaceClassifier.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CQEING_CuadroCantidades.Core.Application.Services;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Caras exteriores de columnas y vigas según las líneas de propiedad del modelo, que marcan el contorno del
/// edificio. Una cara lateral es exterior si un punto 30 cm hacia afuera de su centro queda fuera del contorno, así
/// que no depende de si se llama X1 o X2. Si el centro de alguna cara ya está fuera, la línea atraviesa el elemento
/// (o el elemento está fuera del lote) y no se clasifica.
/// </summary>
public sealed class BoundaryFaceClassifier
{
    // Distancia hacia afuera de la cara a la que se mira si se sale del contorno
    private static readonly double ProbeDistance = UnitUtils.ConvertToInternalUnits(30, UnitTypeId.Centimeters);

    // Distancia hacia adentro del centro de la cara para saber si el propio elemento está dentro del contorno
    private static readonly double InsetDistance = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Centimeters);

    // Tolerancia para unir los extremos de los lados de una línea de propiedad (≈ 1 mm)
    private const double JoinTolerance = 0.003;

    private readonly LotContour _contour;

    private BoundaryFaceClassifier(LotContour contour) => _contour = contour;

    /// <summary>
    /// Lee las líneas de propiedad cerradas del documento. Devuelve null si no hay ninguna.
    /// </summary>
    public static BoundaryFaceClassifier? Create(Document document)
    {
        var polygons = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_SiteProperty)
            .WhereElementIsNotElementType()
            .Select(ReadPolygon)
            .OfType<IReadOnlyList<(double X, double Y)>>()
            .ToList();

        var contour = new LotContour(polygons);
        return contour.IsEmpty ? null : new BoundaryFaceClassifier(contour);
    }

    /// <summary>
    /// Caras exteriores de una columna o viga, o que la línea la atraviesa. Null si el elemento no es columna ni viga
    /// o su geometría no se puede leer.
    /// </summary>
    public BoundaryClassification? Classify(Element element)
    {
        if (FaceAreaExtractor.GetKind(element) is not (FaceMeasuredKind.Column or FaceMeasuredKind.Beam))
            return null;

        var exterior = new HashSet<ElementFace>();
        try
        {
            foreach (var (face, transform, globalFace) in FaceAreaExtractor.EnumeratePlanarFaces(element))
            {
                if (globalFace is ElementFace.Z1 or ElementFace.Z2)
                    continue;

                XYZ normal = transform.OfVector(face.FaceNormal);
                var horizontal = new XYZ(normal.X, normal.Y, 0);
                if (horizontal.GetLength() < 1e-9)
                    continue;

                XYZ outward = horizontal.Normalize();
                BoundingBoxUV box = face.GetBoundingBox();
                XYZ center = transform.OfPoint(face.Evaluate((box.Min + box.Max) / 2.0));

                XYZ inner = center - outward * InsetDistance;
                if (!_contour.Contains(inner.X, inner.Y))
                    return new BoundaryClassification(new HashSet<ElementFace>(), CrossesBoundary: true);

                XYZ probe = center + outward * ProbeDistance;
                if (!_contour.Contains(probe.X, probe.Y))
                    exterior.Add(globalFace);
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Geometría ilegible: el elemento queda sin clasificar
            return null;
        }

        return new BoundaryClassification(exterior, CrossesBoundary: false);
    }

    /// <summary>
    /// Polígono en planta de una línea de propiedad, o null si está abierta o no tiene lados.
    /// Los lados curvos se reparten en puntos.
    /// </summary>
    private static IReadOnlyList<(double X, double Y)>? ReadPolygon(Element propertyLine)
    {
        GeometryElement? geometry = propertyLine.get_Geometry(new Options());
        if (geometry == null)
            return null;

        var pending = geometry.OfType<Curve>().ToList();
        if (pending.Count < 2)
            return null;

        // Los lados vienen en el orden del boceto; se encadenan por sus extremos por si alguno viene invertido
        var points = new List<XYZ>();
        Curve first = pending[0];
        pending.RemoveAt(0);
        XYZ start = first.GetEndPoint(0);
        IList<XYZ> firstPoints = first.Tessellate();
        AppendWithoutLast(firstPoints, points);
        XYZ end = firstPoints[^1];

        while (pending.Count > 0)
        {
            int index = pending.FindIndex(curve => Near(curve.GetEndPoint(0), end) || Near(curve.GetEndPoint(1), end));
            if (index < 0)
                return null;

            Curve next = pending[index];
            pending.RemoveAt(index);
            var nextPoints = next.Tessellate().ToList();
            if (!Near(next.GetEndPoint(0), end))
                nextPoints.Reverse();

            AppendWithoutLast(nextPoints, points);
            end = nextPoints[^1];
        }

        return Near(end, start) ? points.Select(p => (p.X, p.Y)).ToList() : null;
    }

    // El último punto de un lado es el primero del siguiente
    private static void AppendWithoutLast(IList<XYZ> source, List<XYZ> target)
    {
        for (int i = 0; i < source.Count - 1; i++)
        {
            target.Add(source[i]);
        }
    }

    private static bool Near(XYZ a, XYZ b) => Math.Abs(a.X - b.X) < JoinTolerance && Math.Abs(a.Y - b.Y) < JoinTolerance;
}

/// <summary>
/// Caras exteriores de un elemento según la línea de propiedad, o que la línea lo atraviesa.
/// </summary>
/// <param name="ExteriorFaces">Caras laterales que dan hacia afuera del contorno (vacío si atraviesa).</param>
/// <param name="CrossesBoundary">La línea pasa por el elemento o el elemento está fuera del lote.</param>
public sealed record BoundaryClassification(IReadOnlySet<ElementFace> ExteriorFaces, bool CrossesBoundary);
```

- [ ] **Paso 4: Constantes y lectura en `Core/Application/Services/FaceAreaCalculator.cs`**

4a. Después de la constante `AreaColumn` (línea 54):

```csharp
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
```

4b. En `Read`, declarar junto a `storedSelection`:

```csharp
        IReadOnlySet<ElementFace>? exteriorFaces = null;
        bool crossesBoundary = false;
```

4c. Reemplazar el primer bloque del `foreach (var prop …)`, el que hoy lee solo `SelectionKey`:

```csharp
                        if (prop.Value.ValueKind == JsonValueKind.String
                            && string.Equals(prop.Name, SelectionKey, StringComparison.OrdinalIgnoreCase))
                        {
                            // "Caras" (lo escribe el add-in) o "CARAS" (vuelve de la API); vacío = ninguna cara
                            storedSelection = Parse(prop.Value.GetString());
                        }
```

por:

```csharp
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
```

El `else if (prop.Value.ValueKind == JsonValueKind.Number …)` que sigue no cambia.

4d. Reemplazar el `return new FaceAreaData(…)` del final de `Read`:

```csharp
        return new FaceAreaData(
            kind,
            areas,
            storedSelection ?? DefaultSelection(kind),
            storedSelection != null,
            returnedArea,
            exteriorFaces?.Where(allowed.Contains).ToHashSet(),
            crossesBoundary
        );
```

4e. Agregar al final del `record FaceAreaData` sus dos parámetros nuevos, con su documentación:

```csharp
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
```

- [ ] **Paso 5: Escribir y sincronizar las llaves en `Services/FaceAreaExtractor.cs`**

5a. Agregar el método privado:

```csharp
    /// <summary>
    /// Agrega las caras exteriores según la línea de propiedad, o el aviso de que la línea atraviesa el elemento.
    /// No agrega nada si no es columna ni viga o si el modelo no tiene línea de propiedad.
    /// </summary>
    private static void AddBoundaryData(Element element, IDictionary<string, string> data)
    {
        var classification = BoundaryFaceClassifier.Create(element.Document)?.Classify(element);
        if (classification == null)
            return;

        if (classification.CrossesBoundary)
            data[FaceAreaCalculator.BoundaryIssueKey] = FaceAreaCalculator.BoundaryCrossedValue;
        else
            data[FaceAreaCalculator.ExteriorFacesKey] = FaceAreaCalculator.Format(classification.ExteriorFaces);
    }

    private static string? AsString(object? value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
        string s => s,
        _ => null,
    };
```

5b. Reemplazar `GetData`:

```csharp
    /// <summary>
    /// Caras que se miden por defecto en una columna, viga o zapata recién extraída y, en columnas y vigas, sus caras
    /// exteriores según la línea de propiedad, como texto para el JSON.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetData(Element element)
    {
        var kind = GetKind(element);
        if (kind == FaceMeasuredKind.None)
            return new Dictionary<string, string>();

        var data = new Dictionary<string, string>
        {
            [FaceAreaCalculator.SelectionKey] = FaceAreaCalculator.Format(FaceAreaCalculator.DefaultSelection(kind)),
        };
        AddBoundaryData(element, data);
        return data;
    }
```

5c. En `TrySyncElementData`, justo antes de `if (changed)` (después del bloque de `hasSelection`):

```csharp
        // Caras exteriores según la línea de propiedad: se recalculan siempre y se borran si ya no aplican
        var boundary = new Dictionary<string, string>();
        AddBoundaryData(element, boundary);
        foreach (var key in new[] { FaceAreaCalculator.ExteriorFacesKey, FaceAreaCalculator.BoundaryIssueKey })
        {
            if (boundary.TryGetValue(key, out string? value))
            {
                if (!data.TryGetValue(key, out var existing) || AsString(existing) != value)
                {
                    data[key] = value;
                    changed = true;
                }
            }
            else if (data.Remove(key))
            {
                changed = true;
            }
        }
```

5d. Actualizar el resumen de la clase `FaceAreaExtractor` agregando al final:

```csharp
/// En columnas y vigas también guarda cuáles caras laterales dan hacia afuera de la línea de propiedad del modelo.
```

- [ ] **Paso 6: Compilar**

Run: `dotnet build CQEING_CuadroCantidades.csproj -c Debug`
Esperado: `Compilación correcta`, 0 errores.

- [ ] **Paso 7: Verificar el JSON en Revit (E1)**

Modelo de prueba con **solo** la línea 816671 (borrar la 816703). Abrir el add-in, abrir la tabla de detalles de la actividad con las 24 columnas (la sincronización corre al abrirla) y cerrar. Leer el JSON guardado en la base local `%AppData%\CQEING_CuadroCantidades\Databases\<nombre del modelo>.db`, tabla de elementos vinculados, columna `ElementDataJson`:

| Elemento | `"Caras exteriores"` esperado |
|---|---|
| 782103 A-1 / 782734 | `"X1 Y1"` |
| 782105 1-6 / 782736 | `"X2 Y1"` |
| 782106 A-7 / 782737 | `"X1"` |
| 782108 A-9 / 782739 | `"X1 Y2"` |
| 782109 5-7 / 782740 | `""` |
| 782114 6-9 / 782745 | `"X2 Y2"` |

Ninguno debe tener `"Contorno"`.

- [ ] **Paso 8: Commit**

```bash
git add Core/Application/Services/LotContour.cs Services/BoundaryFaceClassifier.cs Services/FaceAreaExtractor.cs Core/Application/Services/FaceAreaCalculator.cs
git commit -m "feat: caras exteriores de columnas y vigas según la línea de propiedad"
```

---

### Tarea 2: Botones "Quitar exteriores" y "Solo exteriores" en el panel de caras

**Archivos:**
- Modificar: `UI/ViewModels/FaceSelectionViewModel.cs`
- Modificar: `UI/ViewModels/DynamicTableViewModel.cs:848-866`
- Modificar: `UI/Views/FaceSelectorPanel.xaml` (después de la línea 403)

**Interfaces:**
- Consume: `FaceAreaData.ExteriorFaces`, `FaceAreaData.CrossesBoundary` de la Tarea 1.
- Cambia: el delegado del panel pasa de `Func<Func<IReadOnlySet<ElementFace>, IReadOnlySet<ElementFace>>, Task>` a `Func<Func<FaceAreaData, IReadOnlySet<ElementFace>>, Task>`. Cada cambio recibe los datos completos del elemento y devuelve sus caras nuevas.
- Produce (bindings): `ShowExteriorOptions`, `BoundaryText`, `HasBoundaryIssue`, `BoundaryIssueText`, `RemoveExteriorCommand`, `OnlyExteriorCommand`.

- [ ] **Paso 1: `DynamicTableViewModel.ApplyFaceSelectionAsync` recibe el `FaceAreaData`**

Cambiar la firma (línea 848):

```csharp
    private async Task ApplyFaceSelectionAsync(Func<FaceAreaData, IReadOnlySet<ElementFace>> change)
```

y la línea 865:

```csharp
                string selection = FaceAreaCalculator.Format(change(faces!));
```

Actualizar el `<param name="change">` a: `Caras nuevas a partir de los datos de caras de cada elemento.`

- [ ] **Paso 2: Firma del delegado y del cambio en `FaceSelectionViewModel`**

2a. Campo y constructor:

```csharp
    private readonly Func<Func<FaceAreaData, IReadOnlySet<ElementFace>>, Task> _applyChange;
```

```csharp
    /// <param name="applyChange">Aplica a cada elemento seleccionado un cambio de sus caras elegidas, calculado a
    /// partir de sus datos de caras, y lo guarda.</param>
    public FaceSelectionViewModel(Func<Func<FaceAreaData, IReadOnlySet<ElementFace>>, Task> applyChange)
```

Al final del constructor:

```csharp
        RemoveExteriorCommand = new AsyncRelayCommand(() => ApplyAsync(WithoutExterior), () => CanEditExterior);
        OnlyExteriorCommand = new AsyncRelayCommand(() => ApplyAsync(OnlyExterior), () => CanEditExterior);
```

2b. `ToggleAsync`:

```csharp
    internal Task ToggleAsync(FaceOptionViewModel option)
    {
        bool include = option.IsSelected != true;
        return ApplyAsync(data =>
        {
            var next = data.Selection.ToHashSet();
            if (include)
                next.Add(option.Face);
            else
                next.Remove(option.Face);
            return next;
        });
    }
```

2c. `ApplyAsync`:

```csharp
    internal async Task ApplyAsync(Func<FaceAreaData, IReadOnlySet<ElementFace>> change)
```

(el cuerpo no cambia)

2d. `FacePresetViewModel`: el constructor recibe el cambio ya armado:

```csharp
    internal FacePresetViewModel(string label, Func<FaceAreaData, IReadOnlySet<ElementFace>> change, FaceSelectionViewModel owner)
    {
        Label = label;
        ApplyCommand = new AsyncRelayCommand(() => owner.ApplyAsync(change), () => owner.CanEdit);
    }
```

y en `RebuildPresets` el `foreach` final:

```csharp
        foreach (var (label, faces) in presets)
        {
            IReadOnlySet<ElementFace> selection = faces.ToHashSet();
            Presets.Add(new FacePresetViewModel(label, _ => selection, this));
        }
```

- [ ] **Paso 3: Estado y comandos de caras exteriores en `FaceSelectionViewModel`**

3a. Propiedades nuevas, después de `SelectionAreaText`:

```csharp
    /// <summary>
    /// Columnas o vigas seleccionadas: se muestran las opciones de caras exteriores.
    /// </summary>
    public bool ShowExteriorOptions => ShowColumnPlan || ShowBeamPlan;

    /// <summary>
    /// Algún elemento seleccionado tiene sus caras exteriores clasificadas por la línea de propiedad.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditExterior))]
    private bool _hasBoundary;

    /// <summary>
    /// Cuántas caras exteriores hay en la selección, o cómo activarlas.
    /// </summary>
    [ObservableProperty]
    private string _boundaryText = string.Empty;

    /// <summary>
    /// Hay elementos atravesados por la línea de propiedad o fuera del lote.
    /// </summary>
    [ObservableProperty]
    private bool _hasBoundaryIssue;

    [ObservableProperty]
    private string _boundaryIssueText = string.Empty;

    /// <summary>
    /// Se pueden quitar o dejar solas las caras exteriores.
    /// </summary>
    public bool CanEditExterior => CanEdit && HasBoundary && ShowExteriorOptions;

    /// <summary>
    /// Quita de las caras elegidas de cada elemento las que dan hacia afuera de la línea de propiedad.
    /// </summary>
    public IAsyncRelayCommand RemoveExteriorCommand { get; }

    /// <summary>
    /// Deja elegidas solo las caras que dan hacia afuera de la línea de propiedad.
    /// </summary>
    public IAsyncRelayCommand OnlyExteriorCommand { get; }
```

3b. Avisar los cambios de `ShowExteriorOptions` y `CanEditExterior`. Reemplazar los dos parciales de `ShowColumnPlan` y `ShowBeamPlan`:

```csharp
    partial void OnShowColumnPlanChanged(bool value) => NotifyEditability();

    partial void OnShowBeamPlanChanged(bool value) => NotifyEditability();
```

y agregar:

```csharp
    private void NotifyEditability()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(ShowExteriorOptions));
        OnPropertyChanged(nameof(CanEditExterior));
    }
```

En `OnShowFootingPlanChanged`, `OnIsReadOnlyChanged` y `OnIsBusyChanged` agregar también `OnPropertyChanged(nameof(CanEditExterior));`. Por ejemplo:

```csharp
    partial void OnIsReadOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditExterior));
        NotifyCommands();
    }
```

3c. Los dos cambios, como métodos estáticos privados:

```csharp
    // Elementos sin clasificar (sin línea de propiedad, atravesados o traídos de la API) conservan sus caras
    private static IReadOnlySet<ElementFace> WithoutExterior(FaceAreaData data) =>
        data.ExteriorFaces == null ? data.Selection : data.Selection.Except(data.ExteriorFaces).ToHashSet();

    private static IReadOnlySet<ElementFace> OnlyExterior(FaceAreaData data) =>
        data.ExteriorFaces ?? data.Selection;
```

3d. En `Refresh`, antes de `RebuildPresets(kind);`:

```csharp
        var classified = _selected.Where(d => d.ExteriorFaces != null).ToList();
        int crossed = _selected.Count(d => d.CrossesBoundary);
        HasBoundary = classified.Count > 0;
        BoundaryText = HasBoundary
            ? BuildBoundaryText(classified.Sum(d => d.ExteriorFaces!.Count), classified.Count(d => d.ExteriorFaces!.Count > 0))
            : crossed > 0 ? string.Empty : "Dibuja una línea de propiedad en Revit (Masa y emplazamiento) para distinguir las caras exteriores.";
        HasBoundaryIssue = crossed > 0;
        BoundaryIssueText = crossed == 1
            ? "1 elemento está fuera o atravesado por la línea de propiedad: no se le cambian las caras."
            : $"{crossed} elementos están fuera o atravesados por la línea de propiedad: no se les cambian las caras.";
```

y el método:

```csharp
    private static string BuildBoundaryText(int faces, int elements) => (faces, elements) switch
    {
        (0, _) => "Ninguna cara da hacia afuera de la línea de propiedad.",
        (1, _) => "1 cara exterior según la línea de propiedad.",
        (_, 1) => $"{faces} caras exteriores en 1 elemento según la línea de propiedad.",
        _ => $"{faces} caras exteriores en {elements} elementos según la línea de propiedad.",
    };
```

3e. En `NotifyCommands`, al final:

```csharp
        RemoveExteriorCommand.NotifyCanExecuteChanged();
        OnlyExteriorCommand.NotifyCanExecuteChanged();
```

- [ ] **Paso 4: Bloque "Caras exteriores" en `UI/Views/FaceSelectorPanel.xaml`**

Insertar después del `</ItemsControl>` de los presets (línea 403) y antes del `<Border Background="#F0FDF4" …>` del área de la selección:

```xml
            <!-- Caras exteriores según la línea de propiedad: no dependen de si se llaman X1 o X2 -->
            <StackPanel Margin="0,8,0,4"
                        Visibility="{Binding ShowExteriorOptions, Converter={StaticResource BoolToVisibility}}">
                <TextBlock Style="{StaticResource SectionLabelStyle}" Text="CARAS EXTERIORES" />
                <TextBlock Text="{Binding BoundaryText}" TextWrapping="Wrap" FontSize="11.5" Foreground="#475569" Margin="0,0,0,6" />
                <WrapPanel>
                    <Button Content="Quitar exteriores" Command="{Binding RemoveExteriorCommand}" Style="{StaticResource PresetButtonStyle}" />
                    <Button Content="Solo exteriores" Command="{Binding OnlyExteriorCommand}" Style="{StaticResource PresetButtonStyle}" />
                </WrapPanel>
                <Border Background="#FFFBEB" BorderBrush="#FDE68A" BorderThickness="1" CornerRadius="6" Padding="10,8" Margin="0,6,0,0"
                        Visibility="{Binding HasBoundaryIssue, Converter={StaticResource BoolToVisibility}}">
                    <TextBlock Text="{Binding BoundaryIssueText}" TextWrapping="Wrap" FontSize="11.5" Foreground="#92400E" />
                </Border>
            </StackPanel>
```

- [ ] **Paso 5: Compilar**

Run: `dotnet build CQEING_CuadroCantidades.csproj -c Debug`
Esperado: `Compilación correcta`, 0 errores.

- [ ] **Paso 6: Escenarios en Revit (E2–E7)**

Modelo de prueba con la línea de propiedad 816671. Valores calculados en Revit el 14/10/2026 con la lógica de este plan:

| # | Escenario | Esperado |
|---|---|---|
| E2 | Actividad M2 con las 24 columnas 0.30 × 0.30, las 4 caras elegidas (87.20). Sin filas seleccionadas, presionar **"Solo exteriores"** | El texto dice "28 caras exteriores en 20 elementos…". Área **29.40** (28 caras × 1.05). 5-7 y 5-8 de los dos pisos quedan en 0 |
| E3 | Volver a "Las 4 caras" y presionar **"Quitar exteriores"** | Área **57.80** (87.20 − 29.40), ±0.02 por redondeo. La A-1 queda con CARAS = `X2 Y2`, la 1-6 con `X1 Y2` |
| E3b | Vigas VIG_25X35 del piso 2 con Y1 Y2 Z1 Z2 o X1 X2 Z1 Z2 (56.68). Presionar **"Quitar exteriores"** | Se quitan 10 caras: Y1 de 782552/782554, X1 de 782555/557/558, X2 de 782562/563/564, Y2 de 782569/570. Área ≈ **44.40** (56.68 − 4 × 1.12 − 6 × 1.30). Confirmar el valor exacto |
| E4 | Dejar solo una línea como la 816703: lado izquierdo en x = −0.07 y lado de arriba en y = 11.96, ambos por dentro de las columnas. Abrir la tabla de las 24 columnas | Aviso amarillo: "12 elementos están fuera o atravesados…". Por piso: A-1, A-7, A-8, A-9, 5-9 y 6-9. Los botones no cambian las caras de esos 12; los otros 12 sí se clasifican |
| E5 | Con E3 aplicado, borrar la línea de propiedad y abrir la tabla | Texto "Dibuja una línea de propiedad…", botones deshabilitados, y la selección guardada se mantiene (sigue en 57.80) |
| E6 | Con la línea puesta, abrir la tabla con Revit cerrado o con datos recién traídos de la API | Los botones quedan deshabilitados hasta que Revit está abierto y la tabla resincroniza; luego se habilitan |
| E7 | Extraer el modelo completo con y sin línea de propiedad | Anotar los dos tiempos en el PR. Avisar si la diferencia supera el 20 % |

- [ ] **Paso 7: Commit**

```bash
git add UI/ViewModels/FaceSelectionViewModel.cs UI/ViewModels/DynamicTableViewModel.cs UI/Views/FaceSelectorPanel.xaml docs/superpowers/plans/2026-10-14-caras-exteriores-linea-propiedad.md
git commit -m "feat: botones para quitar o dejar solo las caras exteriores"
```

---

## Fuera de alcance (decidido)

- **Zapatas:** no se clasifican.
- **Caras curvas** (columnas circulares): no se clasifican. Solo cuentan las caras planas.
- **Línea de área construible aparte:** no hace falta. Una sola línea de propiedad marca el contorno y la distancia de 30 cm decide.
- **Edificios que se retranquean por piso:** una sola línea vale para todos los pisos. Si un piso es más chico, sus columnas de borde no quedan a 30 cm de la línea y salen como interiores.
- **Patios interiores:** quedan dentro del contorno, así que sus caras salen como interiores. Habría que dibujar el patio como otra línea y restarlo; no se hace ahora.
- **Selección que se actualiza sola al mover la línea:** no. Los botones cambian "Caras" una vez (ver Review Focus 2).
