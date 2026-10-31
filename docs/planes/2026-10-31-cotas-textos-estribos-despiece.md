# Plan: cotas de la viga, textos en una línea y despiece de estribos (pedido del 31-oct)

## Contexto

- **Qué se pide** (31-oct, sobre la vista previa del add-in de despiece):
  1. Vista larga: la distancia de la viga completa, el ancho de cada columna y la luz entre columnas.
  2. Poder elegir los textos en negro o con los colores de las barras.
  3. El texto de las barras en una sola línea, con "/".
  4. Vista de estribos: cantidad, longitud total y longitud del gancho.
  5. Despiece de los estribos menores, que hoy no salen.
  6. Los bastones, dentro de la viga, en el medio.
  7. Los textos lo más pegados posible a su barra, con una pequeña separación, y pruebas unitarias de que no se encimen.
- **Fuera de alcance:** crear las vistas definitivas (#222), mostrar las columnas en la vista (#219).
- **Entorno:** Revit 2026.4, `PROY_JULIO_BETO`. Viga 392699 (con bastones) y 388501 (`CQ Estribo` y `CQ Grapa C`). Rama actual, sin commit.

## Criterios de aceptación

- [ ] La vista larga tiene una cadena de cotas columna / luz / columna y otra con el total.
- [ ] Un control de la página cambia los textos entre colores y negro; las cotas siempre van en negro.
- [ ] Las barras dicen `3 #5 / L= 6.00`, en una línea.
- [ ] Cada forma de estribo sale una vez, con `16 #3 / L= 0.99 / G= 0.08`.
- [ ] Los estribos menores salen junto al principal.
- [ ] Los bastones quedan centrados en el alto de la viga.
- [ ] Hay pruebas de que ningún rectángulo de barra y texto se monta sobre otro.

## Hallazgos del código y de Revit real

Medido el 31-oct con el servidor MCP:

- **Bastones:** no tienen marca propia. En 392699 son rectas de `Quantity = 1`, pero en 388561 las corridas también son de `Quantity = 1`, así que la cantidad no los distingue (el comentario de `GeneradorPrevia.cs:213` no vale en general). Lo que sí los distingue es la altura: corridas en z 3.63–3.65 y bastón en 3.62–3.63; abajo, corridas en 3.40–3.42 y bastón en 3.42–3.43. El bastón está más adentro que la capa exterior.
- **Estribos menores:** se pierden porque `GeneradorPrevia.cs:244` usa `FirstOrDefault`. En 388501 hay `CQ Estribo` 1.05 m y `CQ Grapa C` 0.52 m.
- **Una forma, varios conjuntos:** un mismo estribo viene en muchos conjuntos (392699: 8+7+8+8+7+8 = 46). La cantidad hay que sumarla.
- **Gancho:** `RebarBarType.GetHookLength(hookId)` es API pública y da 0.0845 m para `CQEING Estribo 135° #3`. El tramo recto de la punta mide 0.057 m y el arco 0.056 m.
- **Texto lejos de la barra:** `AltoEtiqueta` es fijo (0.40 m), y el texto a 1:50 mide cerca de 0.125 m.
- **Probar la DLL nueva con Revit abierto:** cargarla con `LoadFromAssemblyPath` funciona, pero solo una vez. Desde ahí el servidor MCP deja de compilar en esa sesión de Revit (`An assembly with the same simple name 'CQEING.Despiece' has already been imported`), porque ve dos DLL con el mismo nombre. Se arregla reiniciando Revit. Para la próxima: `AssemblyLoadContext.LoadFromStream`, sin ruta en disco. Esa variante todavía no se probó.
- **Primera corrida de las cotas (viga 392699 y 388501):** `NewDimension` falló con `The references are not geometric references`. Es la única corrida que llegó a Revit, y falló antes de exportar. Se cambió el código: `Regenerate` antes de acotar y la categoría de planos se oculta después. **Ese cambio compila, pero no se ha probado en Revit.**

## Decisiones

1. **Formato** (31-oct, Claude): `3 #5 / L= 6.00` y `16 #3 / L= 0.99 / G= 0.08`. Es la lectura de "/" para que quede en una línea; si Juan esperaba otro orden, se cambia solo en `TextosDespiece`.
2. **"En el medio" = centrados en el alto de la viga** (31-oct, Claude): si no caben, las filas de arriba y de abajo se alejan para que nada se encime.
3. **Bastón = recta más adentro que la capa exterior de su lado, por más de 5 mm** (31-oct, Claude). Una barra de piel a media altura también iría al medio; este modelo no tiene.
4. **Las cotas miden contra planos de referencia temporales** (31-oct, Claude): `NewDimension` es la cota nativa y los planos no dependen de cómo estén unidas las caras de viga y columna. Cuando #222 cree vistas definitivas, habrá que valorar referencias a las caras reales, para que las cotas sigan al modelo.
5. **Gancho = `GetHookLength`** (31-oct, Claude): es el valor oficial de Revit, no una cuenta propia.
6. **Medir, no suponer** (31-oct, Claude): el ancho y el alto de cada texto se leen de Revit y el reparto en filas va en un proyecto sin Revit, con pruebas.

## Plan paso a paso

1. **Pruebas y cálculo sin Revit** — `calculo/CQEING.Despiece.Calculo/{TextosDespiece,TramosViga,AcomodadorFilas,ClasificadorBastones}.cs` y sus pruebas — `dotnet test addins/despiece/calculo/CQEING.Despiece.Calculo.Tests`.
2. **Vista larga** — `Revit/GeneradorPrevia.cs`: medir textos, acomodar, bastones al medio, cotas. Se prueba con el paso 5.
3. **Estribos** — `Revit/GeneradorPrevia.cs`: todas las formas, con su texto. Se prueba con el paso 5.
4. **Negro o colores** — `Interfaz/PuenteDespiece.cs`, `web/pasos.js`: un control y una imagen guardada por grupo y por modo.
5. **Prueba en Revit abierto** — cargar la DLL compilada en un contexto aparte desde el MCP, llamar `GeneradorPrevia.Crear` dentro de una `SubTransaction`, deshacer y mirar los PNG.

## Cómo probarlo

```
dotnet test addins/despiece/calculo/CQEING.Despiece.Calculo.Tests
dotnet build addins/despiece/CQEING.Despiece.csproj -c Debug -p:RevitVersion=2026
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin despiece -Revit 2026
```

1. Con Revit cerrado, desplegar. Abrir `PROY_JULIO_BETO` y **Despiece**.
2. Elegir la viga 392699 y pasar a Revisar: cotas, textos en una línea y bastones al medio.
3. Cambiar a negro y volver a colores: la segunda vez es inmediato.
4. Elegir la viga 388501: salen el estribo y la grapa, cada uno con su texto.

## Estado

- [x] Pasos 1 a 4 programados, sin commit. `dotnet test` del cálculo: 45 de 45. Compila para Revit 2026.
- [ ] Paso 5, prueba en Revit: **bloqueado hasta reiniciar Revit** (ver Hallazgos). Ningún criterio de aceptación se ha visto todavía en una imagen.
- [ ] Revisar con Juan el formato del texto (decisión 1), los bastones (decisión 2) y el valor del gancho (decisión 5).
