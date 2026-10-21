# Plan: mejora visual del add-in de columnas (#122, sub-issues #115 a #121)

## Contexto

- Luciano pidió pulir lo visual del add-in de armado de columnas: botones, colores, orden de la ventana y consistencia entre ventanas, más la observación de Carlos sobre la pestaña «Traslapos». Issue madre: https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/122
- **Regla de oro:** no cambia ninguna funcionalidad (mismos cálculos, mismo armado generado en Revit). Todo cambio de flujo se lista en el PR con antes/después para que Luciano lo apruebe; lo que toque cálculos o lo generado se anota como propuesta, no se hace.
- Fuera de alcance: `Modelo/ColoresVarilla.cs` (tabla de colores por diámetro de la empresa, no se cambia).
- Entorno: Revit 2027 (también probado en 2026), .NET 10, rama de trabajo `mejora-visual-columnas` (sale de `main`). Carpeta del add-in: `addins/armado-columnas/`.

## Criterios de aceptación

Los de cada sub-issue; se evalúan en «Estado».

## Hallazgos del código

- Las rutas de las issues (`Vistas/...`) están bajo `addins/armado-columnas/` en el monorepo.
- `PanelTraslapos.DibujarTipo` escribe `{pieza.LargoCorte:0.00} m` sin número de varilla; el número solo está en el título «A · #5» (`PlanTipo.Nombre`).
- `ColoresVarilla.NumeroDe(diametroMm)` y `ColorDe(diametroMm)` ya dan número y color de la tabla de la empresa (los usa `PanelVistaPrevia`).
- Los colores de interfaz están escritos a mano en cada ventana (75 apariciones de `#RRGGBB` / `Color.FromRgb` fuera de `ColoresVarilla`); solo existen los estilos `Etiqueta` y `Panel` en `VentanaConfiguradorEstribos.xaml`.

## Decisiones

1. **Un PR por issue o par de issues, con `Closes #N`** (2026-10-20): revisión en partes chicas para Luciano.
2. **Varilla fuera de la tabla** (2026-10-20): si el diámetro no está en la tabla, se muestra `Ø<mm>` en gris (el color `Gris` de `ColoresVarilla`), en vez de ocultar el dato.

3. **Paleta en C#, expuesta a XAML** (2026-10-20): `Vistas/Paleta.cs` es la única fuente de colores; `Vistas/Estilos.xaml` los expone a los XAML con `x:Static` (`{StaticResource Exito}`) y el código que dibuja en canvas usa `Paleta.Exito` directo. Así XAML y código pintan igual sin duplicar valores ni depender de rutas `pack://` (que dan problemas dentro de Revit).
4. **Un solo verde** (2026-10-20): el verde `#1E8449` (Aceptar de Estribos menores y Generar de Columnetas) pasa a `#16A34A`, el mismo de «Aceptar este tramo». Los grises con nombre (`Gray`, `DimGray`) pasan a `TextoSecundario` (`#555555`).
5. **Hallazgo de la prueba** (2026-10-20): un atributo del elemento raíz `<Window Background="{StaticResource ...}">` no puede usar los recursos de su propio `Window.Resources` (se lee antes). En el elemento raíz se usa `{x:Static local:Paleta.Fondo}`.
6. **#115 y #116 en un solo PR** (2026-10-20): `PanelTraslapos.xaml.cs` lleva cambios de las dos (el `#N` y la paleta), así que separarlas en dos PR dejaría uno sin compilar. Van en un PR con dos commits y `Closes #115` y `Closes #116`.
7. **Prueba en Revit** (2026-10-20): la ventana abrió en Revit 2027 con la paleta (los estilos cargan bien dentro de Revit). La columna de prueba no tenía vigas ("sin solución"), así que falta verificar el `#N` con color y el armado en una columna con nudos.

## Plan paso a paso

### Día 1

1. **#115 Traslapos: `#N` con su color junto al largo** — archivo: `Vistas/PanelTraslapos.xaml.cs` (`DibujarTipo`). El largo sigue con el color del grupo (A azul, B verde); el `#N` va con el color de `ColoresVarilla.ColorDe(tipo.DiametroMm)`. Se ensancha `Separacion` para que los textos no se pisen. Prueba: build + visor `tools/PreviewUI` + columna de varios pisos en Revit.
2. **#116 Paleta y estilos comunes** — archivos: nuevo `Vistas/Estilos.xaml`, y reemplazo de colores sueltos en las ventanas y paneles. Prueba: build + visor + `grep` de colores sueltos.

### Días 2 a 4

Ver la issue madre: #117 + #119 (día 2), #118 (día 3), #120 + prueba final + este documento (día 4), #121 opcional (día 5).

## Cómo probarlo

```
dotnet build addins/armado-columnas/practicacq.csproj -c Debug -p:RevitVersion=2027
dotnet run --project tools/PreviewUI/PreviewUI.csproj
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027
```

## Evidencia (día 1)

- Antes / después de Traslapos (columna C-12 del visor, esquinas #5 y caras #4): `imagenes/2026-10-20-traslapos-antes.png` y `imagenes/2026-10-20-traslapos-despues.png`.
- Ventana principal en el visor con la pestaña Traslapos: `imagenes/2026-10-20-ventana-traslapos-previewui.png`.
- Estribos menores con la paleta común: `imagenes/2026-10-20-estribos-menores-paleta.png`.

## Estado (día 1)

- [x] Plan revisado con el usuario (5 PR, 4 días).
- [x] #115: cada varilla muestra `largo  #N` con el color de la tabla; A/B se distinguen por el color del largo y el título; `Separacion` de 130 a 150 px para que no se pisen los textos (probado con esquinas #5 + caras #4).
- [x] #116: `Paleta.cs` + `Estilos.xaml`; las tres ventanas y los cuatro paneles usan la paleta. Revisión: no queda ningún color de interfaz escrito a mano fuera de `Paleta.cs` y de `ColoresVarilla.cs` (`grep` de `#RRGGBB`, `Color.From*` y `Brushes.*` limpio, salvo `Transparent`).
- [x] Compila: `practicacq.csproj` (Revit 2027), `PreviewUI` y la solución; `PruebasTraslapos` y `PruebasVistaPrevia` con 0 fallas.
- [x] Visor `PreviewUI` y las ventanas Traslapos y Estribos menores renderizadas sin errores.
- [ ] Probado en Revit real 2027 (lo hace el usuario con la guía): la ventana abre, Traslapos muestra `#N` con color y «Generar armado» produce lo mismo que antes.
- [ ] Columnetas y Viguetas no se ve en el visor (usa la API de Revit): se revisa en Revit.
