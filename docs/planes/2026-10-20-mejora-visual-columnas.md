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
8. **Cuatro clases de botón** (2026-10-21): `BotonPrincipal` (verde, aceptar lo configurado), `BotonAccionFinal` (naranja, escribe en el modelo de Revit), `BotonSecundario` (blanco con borde, todo lo demás) y `BotonPeligro` (texto rojo, borra algo). Más `BotonFlecha` para ▲▼. Todos con esquinas de 4 px, color al pasar el mouse y al apretar, y gris cuando están deshabilitados.
9. **Columnetas sigue el mismo significado** (2026-10-21, a aprobar por Luciano): «Generar Columnetas y Viguetas» escribe en el modelo, así que pasa de verde a naranja (`BotonAccionFinal`), igual que «Generar armado en Revit». «Seleccionar Muros» pasa de azul a `BotonSecundario`: el azul queda solo para selección dentro de los dibujos.
10. **Tipografía común** (2026-10-21): estilos `TituloVentana` (14, seminegrita) y `TextoAyuda` (11, gris, con ajuste de línea). En Estribos menores, los encabezados «FORMAS» y «PIEZA SELECCIONADA» pasan a «Formas» y «Pieza seleccionada» con el estilo `Etiqueta` de la ventana principal. Columnetas deja su propio estilo `Etiqueta` y usa el común.
11. **Una sola rama y un solo PR en borrador** (2026-10-21, pedido de Luciano; reemplaza la 1 y la 6): desde ahora todo se trabaja en la rama `mejora-visual` y va en un único PR en borrador (draft) contra `main`, que se marca listo cuando se termine. Los PR #127 y #128 ya estaban unidos a `main` (cierran #115, #116, #117 y #119). El #146 (#118) se unió a la rama `mejora-visual-botones` y no a `main`, así que su commit se pasó a `mejora-visual` y entra a `main` con el PR único.
12. **Pulido visual extra** (2026-10-21, propuesto por el equipo y aceptado por el usuario; a aprobar por Luciano en el PR #148): estilos sin `x:Key` en `Estilos.xaml`, así se aplican solos a las tres ventanas.
    - Paneles como tarjetas: esquinas de 6 px y margen interior de 10 a 12 px; separación entre paneles y margen de la ventana de 10 a 12 px.
    - Pestañas planas con una línea azul debajo de la activa (antes, las grises de Windows).
    - Listas desplegables y campos de texto con esquinas de 4 px, 26 px de alto, borde gris al pasar el mouse y azul al usarlos. Se quitaron las alturas fijas (22/24 px) de cada lista y campo para que mande el estilo.
    - Barra de desplazamiento delgada (10 px) y redondeada.
    - Planta: la columna activa con borde de 3 px (antes 2) y las marcas de ubicación de 9 a 10 px, más oscuras.
    - Sin sombra en los paneles: un `DropShadowEffect` sobre un panel con texto lo vuelve borroso en WPF.

## Plan paso a paso

### Día 1

1. **#115 Traslapos: `#N` con su color junto al largo** — archivo: `Vistas/PanelTraslapos.xaml.cs` (`DibujarTipo`). El largo sigue con el color del grupo (A azul, B verde); el `#N` va con el color de `ColoresVarilla.ColorDe(tipo.DiametroMm)`. Se ensancha `Separacion` para que los textos no se pisen. Prueba: build + visor `tools/PreviewUI` + columna de varios pisos en Revit.
2. **#116 Paleta y estilos comunes** — archivos: nuevo `Vistas/Estilos.xaml`, y reemplazo de colores sueltos en las ventanas y paneles. Prueba: build + visor + `grep` de colores sueltos.

### Día 2 (rama `mejora-visual-botones`, sale de `main` con el PR #127 ya unido)

3. **#117 Estilos de botón** — archivos: `Vistas/Paleta.cs` (colores de hover, apretado y deshabilitado), `Vistas/Estilos.xaml` (los cinco estilos), `Vistas/VentanaConfiguradorEstribos.xaml` (los 9 botones; flechas de 26x17 a 34x24 px con tooltip «Tramo de arriba» / «Tramo de abajo»). Prueba: build + visor + muestrario de estilos.
4. **#119 Ventanas secundarias** — archivos: `Vistas/VentanaEstribosMenores.xaml` y `Vistas/VentanaColumnetas.xaml` (botones, `Panel`, `Etiqueta`, `TituloVentana`, `TextoAyuda`). Prueba: build + render de Estribos menores; Columnetas se revisa en Revit.

### Día 3 (rama `mejora-visual-orden`, sale de `mejora-visual-botones` porque toca la misma ventana; PR contra esa rama)

5. **#118 Orden y jerarquía de la ventana principal** — archivos: `Vistas/Estilos.xaml` (estilos nuevos) y `Vistas/VentanaConfiguradorEstribos.xaml`. Sin mover ni quitar ningún control: solo estilos y títulos.
   - Títulos de sección con una barrita azul a la izquierda (`EncabezadoSeccion` + `TituloSeccion`) en los tres paneles y en los cuatro grupos del panel derecho: configuración guardada / tramo activo / recubrimiento / acero longitudinal.
   - Separadores entre grupos con el mismo espacio (`SeparadorSeccion`).
   - Leyenda de la elevación de 10 a 11 px y cuadritos de 12 a 14 px (`TextoLeyenda`).
   - Un estilo único para avisos (`CajaAviso` + `TextoAviso`), validación (`TextoError`) y ayudas (`TextoAyuda`).
   - «Borrar TODO el armado existente…» dentro de una caja roja suave (`CajaPeligro` + `CasillaPeligro`); al marcarla, el texto pasa a negrita.
   - Textos de título que cambian: «Configuración guardada:» → «Configuración guardada», «Configuración del tramo activo» → «Tramo activo», «Recubrimiento (toda la columna):» → sin los dos puntos.
   Prueba: build + visor (captura antes/después) + Revit.

### Día 4 (rama `mejora-visual`, PR único en borrador)

Ver la issue madre: #120 + prueba final (día 4), #121 opcional (día 5).

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
- [x] PR #127 unido el 2026-10-21 (cierra #115 y #116).

## Evidencia (día 2)

- Muestrario de los estilos de botón (normal, mouse encima, deshabilitado) y de las flechas: `imagenes/2026-10-21-estilos-de-boton.png`.
- Ventana principal en el visor con los botones nuevos: `imagenes/2026-10-21-ventana-principal-botones.png` (antes: `imagenes/2026-10-20-ventana-traslapos-previewui.png`).
- Estribos menores con botones y tipografía comunes: `imagenes/2026-10-21-estribos-menores-botones.png`.

## Estado (día 2)

- [x] #117: los 15 botones de las tres ventanas usan uno de los estilos (revisado botón por botón); el deshabilitado se ve gris en los cinco estilos (muestrario).
- [x] #119: Estribos menores y Columnetas usan los mismos colores, botones y tipografía que la principal.
- [x] Compila (Revit 2027) sin advertencias; visor `PreviewUI` y render de Estribos menores sin errores.
- [ ] Probado en Revit 2027: botones, flechas con tooltip, y la ventana Columnetas y Viguetas.

## Evidencia (día 3)

- Ventana principal con títulos de sección, avisos y la casilla de borrar destacada: `imagenes/2026-10-21-ventana-principal-orden.png` (antes: `imagenes/2026-10-21-ventana-principal-botones.png`).
- La casilla «Borrar TODO…» marcada (negrita roja): `imagenes/2026-10-21-borrar-todo-marcada.png`.

## Estado (día 3)

- [x] #118: los tres paneles y los cuatro grupos del panel derecho tienen título de sección; separadores con el mismo espacio; leyenda de 11 px; avisos, validación y ayudas con un estilo cada uno; «Borrar TODO…» destacada.
- [x] No se movió ni se quitó ningún control: el diff solo cambia estilos, los cuatro textos de título y el `Border` alrededor de la casilla (mismo `x:Name`, misma columna del pie).
- [x] Compila (Revit 2027) sin advertencias; visor `PreviewUI` sin errores.
- [ ] Probado en Revit 2027.
- [x] PR #128 unido a `main` el 2026-10-21 (cierra #117 y #119). El #146 quedó en la rama `mejora-visual-botones`: el #118 entra con el PR único de `mejora-visual`.

## Evidencia y estado (pulido, 2026-10-21)

- Ventana principal: `imagenes/2026-10-21-ventana-principal-pulida.png` (antes: `imagenes/2026-10-21-ventana-principal-orden.png`). Estribos menores: `imagenes/2026-10-21-estribos-menores-pulida.png`.
- [x] Compila (Revit 2027) sin advertencias; visor sin errores; lista desplegable abierta y probada en el visor.
- [ ] Probado en Revit 2027 (incluida la ventana Columnetas, que no se ve en el visor).
