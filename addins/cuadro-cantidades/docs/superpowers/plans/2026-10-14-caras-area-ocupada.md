# Descuento del área ocupada en las caras — Plan de implementación

> **Para agentes:** SUB-SKILL REQUERIDA: usar superpowers:subagent-driven-development (recomendado) o superpowers:executing-plans para ejecutar este plan tarea por tarea. Los pasos usan casillas (`- [ ]`).

**Objetivo:** que el área por caras de columnas y vigas (y el área lateral de columnas) mida solo el área útil, restando lo que ocupan los elementos pegados. De los muros se resta solo su núcleo.

**Arquitectura:** una clase nueva, `OccupiedFaceAreaCalculator`, se crea una vez por elemento. Reúne los sólidos de los vecinos: muros (recortados a su núcleo), vigas, columnas, losas y zapatas. Para cada cara plana extruye una lámina de 5 mm hacia afuera y le resta los sólidos vecinos. El volumen que se pierde, dividido por 5 mm, es el área ocupada. `FaceAreaExtractor` y `ColumnLateralAreaExtractor` restan esa área a cada cara plana.

**Stack:** C# / .NET 8, Revit API 2026 (`BooleanOperationsUtils`, `GeometryCreationUtilities`, `CompoundStructure`, `HostObjectUtils`).

**Origen:** reunión del 14/10/2026 con Carlos Quintero. Las caras sirven para acabados, así que se mide el área útil: una columna de 30 cm con un muro pegado debe dar 30 − 10 = 20 cm en esa cara. Decisión del 14/10/2026: de los muros se descuenta **solo el núcleo**, no las capas de acabado.

## Restricciones globales

- Rama: `feature/caras-area-ocupada`, creada desde `origin/Actualizaciones`.
- Revit 2026, `net8.0-windows`. No se agregan paquetes.
- Las zapatas **no cambian**: siguen midiendo sobre la geometría original de la familia, sin descuentos.
- Las llaves del JSON (`Área X1`…, `Caras`, `ÁREA CARAS`), las columnas de la tabla y la UI no cambian.
- Un fallo de geometría nunca interrumpe la extracción. Si el cálculo falla, la cara queda completa (comportamiento actual).
- Commits en español con prefijo convencional (`feat:`, `docs:`), **sin** líneas `Co-Authored-By` ni "Generated with Claude".
- Compilar en la carpeta del repo, con Revit cerrado (la DLL queda bloqueada si Revit está abierto). Compilar en otra carpeta reescribe el `.addin` y apunta Revit a esa DLL.
- El repo no tiene proyecto de pruebas y todo este código depende de la API de Revit, que no corre fuera de Revit. La verificación es compilación + escenarios manuales en Revit (más abajo).

## Review Focus

1. **Muro con pañete unido a una columna**: debe restar solo el núcleo. Un muro de 1.5 + 10 + 1.5 cm contra una columna de 30 cm deja 20 cm × altura en esa cara, no 17. → Escenario M2.
2. **Viga que llega al costado de una columna**: antes la cara de la columna salía completa, porque la columna corta a la viga. Ahora debe restar la sección de la viga. → Escenario M4.
3. **Cara cubierta por completo** (columna embebida entre muros): el área queda en 0, la cara desaparece de las áreas y la suma, la tabla y el selector de caras no fallan. → Escenario M6.
4. **Muros apilados**: el muro padre se ignora y deben descontar sus muros miembro. Hay que confirmar que el colector los devuelve. → Escenario M7.
5. **Rendimiento**: la extracción de un modelo real con cientos de columnas y vigas no debe tardar mucho más que antes. → Escenario M8.

---

## Archivos

| Archivo | Acción | Responsabilidad |
|---|---|---|
| `Services/OccupiedFaceAreaCalculator.cs` | Crear | Reunir los sólidos vecinos de un elemento (muros recortados a su núcleo) y calcular el área ocupada de una cara plana. |
| `Services/FaceAreaExtractor.cs` | Modificar | Restar el área ocupada en columnas y vigas al calcular el área por cara. `ClassifyFaces` pasa a devolver también el `Transform` de cada cara. |
| `Services/ColumnLateralAreaExtractor.cs` | Modificar | Restar el área ocupada de cada cara vertical plana del área lateral. |
| `Core/Application/Services/FaceAreaCalculator.cs` | Modificar (solo comentario) | El resumen de la clase dice que se mide el área útil. |
| `preguntas-equipo-cantidades.md` | Modificar | Registrar la respuesta de la sección 3 (caras contra muros). |

---

### Tarea 1: Calculadora del área ocupada y descuento en el área por caras

**Archivos:**
- Crear: `Services/OccupiedFaceAreaCalculator.cs`
- Modificar: `Services/FaceAreaExtractor.cs` (resumen de la clase, `ComputeFaceAreas`, `GetFaceReferences`, `ClassifyFaces`)
- Modificar: `Core/Application/Services/FaceAreaCalculator.cs` (resumen de la clase)

**Interfaces:**
- Produce: `OccupiedFaceAreaCalculator.Create(Element element) : OccupiedFaceAreaCalculator` (nunca null; sin vecinos devuelve uno vacío que responde 0).
- Produce: `OccupiedFaceAreaCalculator.OccupiedArea(PlanarFace face, Transform transform) : double`, área ocupada en pies², entre 0 y `face.Area`.

- [ ] **Paso 1: Crear `Services/OccupiedFaceAreaCalculator.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Área de las caras de una columna o viga que ocupan los elementos pegados a ella (muros, vigas, columnas, losas y
/// zapatas), para medir solo el área útil que recibe acabados. De los muros cuenta solo el núcleo: el pañete del muro
/// y el de la columna se encuentran en la esquina. Lo que Revit ya recortó del sólido por una unión no se vuelve a
/// descontar, porque esa parte de la cara ya no existe.
/// </summary>
public sealed class OccupiedFaceAreaCalculator
{
    // Espesor de la lámina que se extruye desde la cara para buscar contactos (5 mm)
    private static readonly double ContactDepth = UnitUtils.ConvertToInternalUnits(5, UnitTypeId.Millimeters);

    private static readonly ICollection<BuiltInCategory> NeighborCategories = new[]
    {
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_Columns,
        BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_StructuralFoundation,
    };

    private static readonly OccupiedFaceAreaCalculator Empty = new(Array.Empty<Solid>());

    private readonly IReadOnlyList<Solid> _neighborSolids;

    private OccupiedFaceAreaCalculator(IReadOnlyList<Solid> neighborSolids) => _neighborSolids = neighborSolids;

    /// <summary>
    /// Reúne los sólidos de los elementos que tocan al elemento. Si no hay vecinos o no se pueden leer,
    /// devuelve una calculadora que no descuenta nada.
    /// </summary>
    public static OccupiedFaceAreaCalculator Create(Element element)
    {
        BoundingBoxXYZ? box = element.get_BoundingBox(null);
        if (box == null)
            return Empty;

        var margin = new XYZ(ContactDepth * 2, ContactDepth * 2, ContactDepth * 2);
        var neighbors = new FilteredElementCollector(element.Document)
            .WherePasses(new ElementMulticategoryFilter(NeighborCategories))
            .WherePasses(new BoundingBoxIntersectsFilter(new Outline(box.Min - margin, box.Max + margin)))
            .WhereElementIsNotElementType()
            .Excluding(new[] { element.Id });

        var solids = neighbors.SelectMany(GetContactSolids).ToList();
        return solids.Count == 0 ? Empty : new OccupiedFaceAreaCalculator(solids);
    }

    /// <summary>
    /// Área (pies²) de la cara que tapan los vecinos: se extruye la cara 5 mm hacia afuera, se le restan los
    /// vecinos y el volumen perdido dividido por los 5 mm es el área tapada. Si dos vecinos se traslapan,
    /// la zona común se cuenta una sola vez.
    /// </summary>
    /// <param name="face">Cara plana del sólido del elemento.</param>
    /// <param name="transform">Transformación que lleva la cara a coordenadas del proyecto.</param>
    public double OccupiedArea(PlanarFace face, Transform transform)
    {
        if (_neighborSolids.Count == 0)
            return 0;

        Solid slab;
        try
        {
            var loops = face.GetEdgesAsCurveLoops()
                .Select(loop => CurveLoop.CreateViaTransform(loop, transform))
                .ToList();
            XYZ outward = transform.OfVector(face.FaceNormal).Normalize();
            slab = GeometryCreationUtilities.CreateExtrusionGeometry(loops, outward, ContactDepth);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Bordes que Revit no puede extruir: la cara queda completa
            return 0;
        }

        double slabVolume = slab.Volume;
        Solid? free = slab;
        foreach (Solid neighbor in _neighborSolids)
        {
            try
            {
                free = BooleanOperationsUtils.ExecuteBooleanOperation(free, neighbor, BooleanOperationsType.Difference);
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Revit no pudo restar este vecino (geometría muy justa): no se descuenta
                continue;
            }

            if (free == null || free.Volume <= 0)
            {
                free = null;
                break;
            }
        }

        double occupied = (slabVolume - (free?.Volume ?? 0)) / ContactDepth;
        return Math.Clamp(occupied, 0, face.Area);
    }

    /// <summary>
    /// Sólidos con los que un vecino tapa caras: los muros básicos solo con su núcleo, los muros cortina y
    /// apilados ninguno (los apilados tapan con sus muros miembro, que el colector trae aparte).
    /// </summary>
    private static IEnumerable<Solid> GetContactSolids(Element neighbor)
    {
        try
        {
            if (neighbor is not Wall wall)
                return GetSolids(neighbor);

            return wall.WallType.Kind == WallKind.Basic
                ? WallCoreSolids(wall, GetSolids(wall))
                : Array.Empty<Solid>();
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Geometría ilegible: el vecino no descuenta
            return Array.Empty<Solid>();
        }
    }

    /// <summary>
    /// Recorta los sólidos del muro a la franja de su núcleo, quitando las capas de acabado de ambos lados.
    /// Muros curvos o sin núcleo definido se devuelven completos.
    /// </summary>
    private static IReadOnlyList<Solid> WallCoreSolids(Wall wall, IReadOnlyList<Solid> solids)
    {
        CompoundStructure? structure = wall.WallType.GetCompoundStructure();
        if (structure == null || wall.Location is not LocationCurve { Curve: Line })
            return solids;

        int first = structure.GetFirstCoreLayerIndex();
        int last = structure.GetLastCoreLayerIndex();
        if (first < 0 || last < first)
            return solids;

        // Las capas van del exterior al interior
        IList<CompoundStructureLayer> layers = structure.GetLayers();
        double coreStart = layers.Take(first).Sum(layer => layer.Width);
        double coreEnd = layers.Take(last + 1).Sum(layer => layer.Width);
        if (coreStart <= 0 && coreEnd >= structure.GetWidth())
            return solids;

        PlanarFace? exterior = ExteriorFace(wall);
        if (exterior == null)
            return solids;

        // Cada medio espacio conserva el lado al que apunta su normal
        XYZ outward = exterior.FaceNormal;
        Plane insideExteriorFinish = Plane.CreateByNormalAndOrigin(outward.Negate(), exterior.Origin - outward * coreStart);
        Plane outsideInteriorFinish = Plane.CreateByNormalAndOrigin(outward, exterior.Origin - outward * coreEnd);

        var cores = new List<Solid>();
        foreach (Solid solid in solids)
        {
            try
            {
                Solid? core = BooleanOperationsUtils.CutWithHalfSpace(solid, insideExteriorFinish);
                if (core != null)
                    core = BooleanOperationsUtils.CutWithHalfSpace(core, outsideInteriorFinish);
                if (core != null && core.Volume > 0)
                    cores.Add(core);
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Revit no pudo recortar el muro: descuenta completo antes que no descontar
                cores.Add(solid);
            }
        }

        return cores;
    }

    private static PlanarFace? ExteriorFace(Wall wall) =>
        HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior)
            .Select(wall.GetGeometryObjectFromReference)
            .OfType<PlanarFace>()
            .FirstOrDefault();

    private static IReadOnlyList<Solid> GetSolids(Element element)
    {
        var solids = new List<Solid>();
        AddSolids(element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }), solids);
        return solids;
    }

    private static void AddSolids(GeometryElement? geometry, List<Solid> solids)
    {
        if (geometry == null)
            return;

        foreach (GeometryObject obj in geometry)
        {
            if (obj is Solid { Volume: > 0 } solid)
                solids.Add(solid);
            // Elementos sin cortes ni uniones traen su geometría en la instancia, ya en coordenadas del proyecto
            else if (obj is GeometryInstance instance)
                AddSolids(instance.GetInstanceGeometry(), solids);
        }
    }
}
```

> Nota para quien implemente: `Autodesk.Revit.Exceptions.InvalidOperationException` hereda de `ApplicationException`. Si al compilar `ExteriorFace` falla por la conversión del grupo de métodos, cámbialo por `.Select(reference => wall.GetGeometryObjectFromReference(reference))`.

- [ ] **Paso 2: `ClassifyFaces` devuelve el `Transform` de cada cara**

En `Services/FaceAreaExtractor.cs`, cambiar la firma y el `yield`:

```csharp
    private static IEnumerable<(Face Face, Transform Transform, Dictionary<ElementFace, double> Areas)> ClassifyFaces(
        Element element,
        bool computeReferences,
        bool original
    )
```

```csharp
        foreach (var (face, faceTransform) in EnumerateFaces(geometry, transform))
        {
            yield return (face, faceTransform, ClassifyFace(face, faceTransform, kind, beamAxis));
        }
```

Y en `GetFaceReferences` ajustar la deconstrucción:

```csharp
            foreach (var (face, _, faceAreas) in ClassifyFaces(element, computeReferences: true, original: false))
```

- [ ] **Paso 3: Restar el área ocupada en `ComputeFaceAreas`**

Reemplazar el resumen del método:

```csharp
    /// <summary>
    /// Área útil (m²) de cada cara del elemento. En columnas solo las laterales; en vigas, sin las cabezas. A columnas
    /// y vigas se les resta lo que tapan los elementos pegados (de los muros, solo su núcleo). En zapatas, las seis
    /// completas, sin descontar la columna.
    /// </summary>
```

Y el contenido del `try` (la variable externa se llama `solidFace` porque el `foreach` interno ya usa `face`):

```csharp
            var occupied = GetKind(element) is FaceMeasuredKind.Column or FaceMeasuredKind.Beam
                ? OccupiedFaceAreaCalculator.Create(element)
                : null;

            foreach (var (solidFace, transform, faceAreas) in ClassifyFaces(element, computeReferences: false, original: true))
            {
                // Una cara plana va entera a una sola cara global; las curvas se reparten y no se descuentan
                if (occupied != null && solidFace is PlanarFace planar && faceAreas.Count == 1)
                {
                    var (globalFace, area) = faceAreas.Single();
                    faceAreas[globalFace] = Math.Max(0, area - occupied.OccupiedArea(planar, transform));
                }

                foreach (var (face, area) in faceAreas)
                {
                    areasFt2[face] = areasFt2.TryGetValue(face, out double sum) ? sum + area : area;
                }
            }
```

El resto del método (catch y conversión a m² con `Where(kvp => kvp.Value > 0)`) queda igual. Una cara tapada por completo desaparece de las áreas, igual que hoy desaparecen las cabezas de las vigas.

- [ ] **Paso 4: Actualizar los resúmenes de clase**

`Services/FaceAreaExtractor.cs`, resumen de la clase:

```csharp
/// <summary>
/// Calcula el área útil (m²) de cada cara de columnas, vigas y zapatas según los ejes globales del proyecto, para que
/// en actividades en M2 se midan solo las caras elegidas. En columnas y vigas recorre el sólido real del elemento, así
/// que los recortes de sus uniones ya vienen descontados, y además resta lo que tapan los elementos pegados (de los
/// muros, solo su núcleo). En zapatas recorre la geometría original de la familia: la cara superior cuenta también el
/// área donde nace la columna y el hueco de una columna embebida no suma a los costados. Las caras curvas se reparten
/// por sus triángulos y no se les descuenta lo que tapan sus vecinos.
/// </summary>
```

`Core/Application/Services/FaceAreaCalculator.cs`, en el resumen de `FaceAreaCalculator`, cambiar la primera frase por:

```csharp
/// Área por caras de columnas, vigas y zapatas para actividades en M2. Al extraer el elemento se guarda en su JSON
/// el área útil de cada cara ("Área X1", "Área Y2"…), sin lo que tapan los elementos pegados en columnas y vigas,
/// y las caras elegidas ("Caras": "X1 X2 Y1 Y2"); la cantidad es la suma de las caras elegidas.
```

(Las dos frases siguientes del resumen se conservan.)

- [ ] **Paso 5: Compilar**

Con Revit cerrado, desde la carpeta del repo:

Run: `dotnet build CQEING_CuadroCantidades.csproj -c Debug`
Esperado: `Build succeeded`, sin warnings nuevos en los archivos tocados.

- [ ] **Paso 6: Escenarios manuales en Revit (M1–M7)**

Abrir un modelo de prueba, extraer y revisar en la tabla de detalles las áreas por cara (`Área X1`…). Cada escenario usa una columna de 30 × 30 cm de 3.00 m de alto, salvo que se diga otra cosa:

| # | Escenario | Esperado |
|---|---|---|
| M1 | Columna aislada | 4 caras de 0.90 m², igual que antes (sin regresión). |
| M2 | Muro tipo 1.5 + 10 + 1.5 cm (núcleo 10) unido en Y2, 3.00 m de alto | Y2 = 0.20 × 3.00 = 0.60 m². |
| M3 | Muro de 10 cm sin capas de acabado en X1 | X1 = 0.60 m². |
| M4 | Viga 30 × 40 que llega al costado X2 de la columna | X2 = 0.90 − 0.30 × 0.40 = 0.78 m². |
| M5 | Viga con losa encima **sin unir** | Z2 de la viga = 0 o casi; costados sin cambio. |
| M6 | Columna entre muros que cubren toda una cara | Esa cara no aparece; suma, tabla y selector de caras sin errores. |
| M7 | Muro apilado pegado a una columna | Descuenta el núcleo de cada muro miembro. Si no descuenta, anotarlo y avisar antes de seguir. |

Tolerancia: ±0.01 m².

- [ ] **Paso 7: Commit**

```bash
git add Services/OccupiedFaceAreaCalculator.cs Services/FaceAreaExtractor.cs Core/Application/Services/FaceAreaCalculator.cs
git commit -m "feat: área útil por caras de columnas y vigas sin lo que tapan sus vecinos"
```

---

### Tarea 2: Descuento en el área lateral de columnas

**Archivos:**
- Modificar: `Services/ColumnLateralAreaExtractor.cs` (resumen de la clase, `AddQuantities`, `SumVerticalFaceArea`)

**Interfaces:**
- Consume: `OccupiedFaceAreaCalculator.Create(Element)` y `OccupiedArea(PlanarFace, Transform)` de la Tarea 1.

- [ ] **Paso 1: Pasar la calculadora al recorrido de caras**

En `AddQuantities`, dentro del `try`:

```csharp
            var options = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
            areaFt2 = SumVerticalFaceArea(element.get_Geometry(options), OccupiedFaceAreaCalculator.Create(element));
```

Reemplazar `SumVerticalFaceArea`:

```csharp
    private static double SumVerticalFaceArea(GeometryElement? geometry, OccupiedFaceAreaCalculator occupied)
    {
        if (geometry == null)
            return 0;

        double total = 0;
        foreach (GeometryObject obj in geometry)
        {
            switch (obj)
            {
                case Solid solid when solid.Volume > 0:
                    foreach (Face face in solid.Faces)
                    {
                        if (!IsVertical(face))
                            continue;

                        // La geometría de instancia ya viene en coordenadas del proyecto. Las caras curvas no se descuentan.
                        total += face is PlanarFace planar
                            ? Math.Max(0, face.Area - occupied.OccupiedArea(planar, Transform.Identity))
                            : face.Area;
                    }
                    break;

                // Pilares sin cortes ni uniones traen su geometría dentro de la instancia de familia
                case GeometryInstance instance:
                    total += SumVerticalFaceArea(instance.GetInstanceGeometry(), occupied);
                    break;
            }
        }

        return total;
    }
```

- [ ] **Paso 2: Actualizar el resumen de la clase**

```csharp
/// <summary>
/// Calcula el área lateral útil de pilares (estructurales y arquitectónicos) para actividades en M2
/// como pañete, pintura o estuco. Suma las caras verticales del sólido real del elemento, así que
/// sirve para cualquier sección (rectangular, circular, en L) y, si el pilar está unido a vigas o
/// losas, Revit ya recorta el sólido y la altura sale neta. A las caras planas se les resta lo que
/// tapan los elementos pegados; de los muros, solo su núcleo.
/// </summary>
```

- [ ] **Paso 3: Compilar y verificar**

Run: `dotnet build CQEING_CuadroCantidades.csproj -c Debug` → `Build succeeded`.

En Revit, con el modelo del escenario M2: el área lateral de la columna = 0.90 × 3 + 0.60 = 3.30 m². Debe coincidir con la suma de sus 4 caras en la tabla de caras.

- [ ] **Paso 4 (M8, rendimiento): medir la extracción**

En un modelo real, medir con cronómetro la extracción completa en `Actualizaciones` (antes) y en esta rama (después). Anotar los dos tiempos en el PR. Si después tarda más del doble, avisar antes de hacer merge.

- [ ] **Paso 5: Commit**

```bash
git add Services/ColumnLateralAreaExtractor.cs
git commit -m "feat: área lateral de columnas sin lo que tapan sus vecinos"
```

---

### Tarea 3: Registrar la respuesta del equipo

**Archivos:**
- Modificar: `preguntas-equipo-cantidades.md:68-70`

- [ ] **Paso 1: Reemplazar la nota de la sección 3**

Reemplazar las líneas 68–70:

```markdown
> **Ya implementado sin respuesta:** hoy el add-in mide las 4 caras de la columna de piso a piso y cuenta también las caras pegadas a muros.
>
> ¿Se deja así? (Sí / No) ____ · Corrección del equipo: _______________
```

por:

```markdown
> **Respondido (reunión 14/10/2026):** se mide el área útil. A cada cara de columnas y vigas se le resta lo que tapan los elementos pegados (muros, vigas, columnas, losas, zapatas). De los muros se resta **solo el núcleo**: un muro de 1.5 + 10 + 1.5 cm contra una columna de 30 cm deja 20 cm en esa cara.
```

- [ ] **Paso 2: Commit**

```bash
git add preguntas-equipo-cantidades.md docs/superpowers/plans/2026-10-14-caras-area-ocupada.md
git commit -m "docs: criterio del área útil de las caras contra elementos pegados"
```

---

## Fuera de alcance (decidido)

- **Zapatas:** no se les descuenta nada (pregunta abierta con Carlos).
- **Caras curvas** (columnas circulares): sin descuento. Haría falta otra técnica de intersección.
- **Muros curvos:** descuentan el muro completo, no solo el núcleo.
- **Caras exteriores / línea de lote:** es otro requerimiento de la misma reunión y va en su propio plan.
- **Elementos de modelos vinculados:** no se buscan como vecinos.
