# Plan: la columna a escala real y con medidas en todas las vistas (#133 + #134, ampliado)

## Contexto

- En la reunión del 21-10 (`2026-10-21-observaciones-reunion-columnas.md`,
  puntos 4 y 5) se pidió que nada sea "figurativo": lo que se ve en el add-in
  tiene que ser igual a lo que queda en Revit. Issues #133 (sección a escala
  real en "Editar estribos menores": una 25×30 se dibuja cuadrada) y #134
  (mostrar las medidas de la sección en esa ventana).
- Luciano (21-10) amplió el pedido: **en todos los lugares donde se dibuja la
  columna**, desde arriba o de costado (vista previa, panel de la izquierda
  para elegir columnas), se dibuja a escala y con sus medidas, no un cuadrado
  ni un ancho fijo.
- Se hace en paralelo con el #130 (rama `columnas/grapas-abrazan-130`), que
  cambia `GeneradorLongitudinal.cs`, `EstriboMenor.cs` y `PaqueteEstribos.cs`.
  Este plan no toca esos archivos.
- Fuera de alcance: dibujar el estribo exterior en "Editar estribos menores"
  con su diámetro real (#135) y los diámetros reales en la sección (#136); acá
  solo se dibuja a escala lo que ya se dibujaba. Columnas rotadas: se siguen
  dibujando sin rotar (mismo límite que el generador).
- Entorno: Revit 2027, .NET 10. Se hizo en la rama `columnas/secciones-a-escala-133`
  (worktree `C:\Proyecto\addins-133`, desde `origin/main` 6e9c9c3) y se pasó a
  `columnas/grapas-abrazan-130`, encima del #130 (21-10, pedido de Luciano), para
  probar las dos cosas juntas en Revit.

## Criterios de aceptación

- [ ] "Editar estribos menores": una 25×30 se ve más alta que ancha (proporción
      25/30), una 120×50 se ve alargada, y las barras van donde las pone el
      generador (`SeccionPlanta`).
- [ ] "Editar estribos menores" muestra las medidas de la sección (ancho, alto)
      y el recubrimiento sin tener que volver atrás.
- [ ] Vista previa desde arriba: con cotas de ancho y alto.
- [ ] Vista previa de costado: el ancho a la misma escala que el alto (sin
      "Ancho exagerado"), con cota de ancho y de alto del tramo.
- [ ] Elevación para elegir tramos y elevación de traslapos: el ancho de la
      columna a escala, con su cota.
- [ ] Panel de la izquierda: cada columna con su tamaño real a la escala de la
      planta, y sus medidas al pasar el puntero; los clics siguen andando.

## Hallazgos del código

Dónde se dibuja hoy la columna (rutas bajo `addins/armado-columnas/`):

| # | Lugar | Hoy | Medidas |
|---|---|---|---|
| 1 | "Editar estribos menores" (`Vistas/VentanaEstribosMenores.xaml.cs`, `Dibujar()`) | Cuadrado fijo de 260 px (`Lado`), barras repartidas parejas en px | Ninguna |
| 2 | Vista previa desde arriba (`Vistas/PanelVistaPrevia.xaml.cs`, `DibujarPlanta`) | Ya a escala (una escala para los dos ejes) | Ninguna |
| 3 | Vista previa de costado (`PanelVistaPrevia`, `DibujarElevacion`) | Ancho estirado a 60 px como mínimo, con la nota "Ancho exagerado" | Ancho en cm, sin alto |
| 4 | Elevación para elegir tramos (`Vistas/VentanaConfiguradorEstribos.xaml.cs`, `RedibujarElevacion`) | Ancho fijo de 46 px | Solo niveles |
| 5 | Elevación de traslapos (`Vistas/PanelTraslapos.xaml.cs`, `Mostrar`) | Ancho fijo de 26 px | Largos de piezas |
| 6 | Panel de la izquierda (`Vistas/PanelPlantaColumnas.xaml.cs`, `DibujarColumna`) | Cuadradito fijo de 12 px; `ColumnaResumen` no trae el tamaño | Ninguna (el tooltip existe pero no se ve: el cuadradito tiene `IsHitTestVisible = false`) |

- `LectorContexto.ListarColumnas` ya calcula los lados de la sección (para
  `CalcularEsColumneta`); solo falta pasarlos a `ColumnaResumen`.
- `tools/PruebasVistaPrevia` enlaza archivos de `Modelo/`, así que la cuenta de
  escala se puede probar ahí si vive en `Modelo/`.

## Decisiones

1. **Real aunque quede angosta** (2026-10-21, Luciano): en las vistas de
   costado y elevaciones el ancho usa la misma escala que el alto. Una columna
   de 25 cm en 3 m queda de unos 20 px; es lo real. Si la columna es tan ancha
   que no entra, se achica la escala de los dos ejes (no se deforma).
2. **Zona de clic mínima en el panel de la izquierda** (2026-10-21): a la escala
   de la planta una columna puede quedar de 3-4 px; se dibuja de su tamaño real,
   pero la zona de clic sigue siendo de al menos 12 px (invisible).
3. **Sin barras elegidas** (2026-10-21): "Editar estribos menores" se dibuja
   igual a escala con diámetros de reserva (#4 longitudinales, #3 estribo) y lo
   avisa en el encabezado.

## Plan paso a paso

1. **Cuenta de escala única** — `Modelo/EscalaDibujo.cs` (nuevo): `Ajustar`
   (px por metro para que un rectángulo entre en una caja sin deformarse) y
   `Cm` (texto "25 cm"). Pruebas en `tools/PruebasVistaPrevia/Program.cs`.
   Se prueba con `dotnet run`.
2. **Cotas reutilizables** — `Vistas/Cotas.cs` (nuevo): cota horizontal y
   vertical (línea, topes y texto) sobre un `Canvas`, sin capturar el mouse.
3. **"Editar estribos menores"** — `Vistas/VentanaEstribosMenores.xaml.cs` y su
   llamada en `VentanaConfiguradorEstribos.xaml.cs`: nuevo `SeccionReal` (lados,
   recubrimiento, diámetros); las funciones `X`/`Y`/`Izquierda`/… pasan de px
   fijos a metros × escala con `SeccionPlanta.Inset`/`PosicionIntermedia`; cotas
   de ancho y alto; recubrimiento y medidas en el encabezado.
4. **Vista previa desde arriba** — `DibujarPlanta`: cotas de ancho y alto.
5. **Vista previa de costado** — `DibujarElevacion`: misma escala en X y Z para
   las dos vistas, sin mínimo ni nota; cota de ancho y de alto del tramo.
6. **Elevación de tramos** — `RedibujarElevacion`: ancho = lado X × escala,
   centrado donde estaba; cota de ancho.
7. **Elevación de traslapos** — `PanelTraslapos.Mostrar`: ancho = lado X ×
   escala; cota de ancho.
8. **Panel de la izquierda** — `ColumnaResumen` (+`LadoX`, `LadoY`),
   `LectorContexto`, `ProveedorContextoFalso`, `PanelPlantaColumnas`: rectángulo
   real, zona de clic mínima de 12 px, medidas en el tooltip del lienzo.
9. **Build y pruebas**, despliegue y prueba en Revit.

## Cómo probarlo

```
dotnet run --project tools/PruebasVistaPrevia
dotnet build addins/armado-columnas/practicacq.csproj -c Debug -p:RevitVersion=2027
Get-Process -Name Revit -ErrorAction SilentlyContinue   # tiene que no devolver nada
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027
```

En Revit, con la columna 387983 (25×30) y la más alargada del modelo:

1. Panel de la izquierda: las columnas se ven con su forma; pasar el puntero
   muestra "25 × 30 cm"; clic y arrastre siguen eligiendo columnas.
2. Elevación de tramos y de traslapos: el ancho cambia según la columna, con
   su cota.
3. Vista previa: desde arriba con cotas; de costado sin "Ancho exagerado".
4. "Editar estribos menores": proporción real, cotas, recubrimiento en el
   encabezado; poner y sacar grapas y cerrados sigue andando.

## Estado

- [x] Plan revisado con el usuario (21-10).
- [x] Pasos 1 a 8 implementados (21-10). Detalles que no estaban en el plan:
  - "Editar estribos menores" usa el estribo del tramo activo para ubicar las
    barras (igual que la marca roja de "no se puede doblar"); la vista previa
    usa el más grueso de la columna. Solo difieren si los tramos tienen
    estribos de distinto diámetro.
  - Elevación de tramos: la columna crece hacia la derecha desde donde estaba
    (a la izquierda están los rótulos de nivel y los chulitos), hasta
    `CanvasElevacion.Width - 172 - 22` px.
  - Elevación de traslapos: la columna arranca en x = 14 px y llega como
    mucho a 44 px de ancho (a la derecha van las cotas del primer plan).
  - `tools/PreviewUI/PreviewUI.csproj` enlaza el nuevo `Vistas/Cotas.cs`.
- [x] Pruebas sin Revit: `dotnet run --project tools/PruebasVistaPrevia`, 0
  fallas (4 pruebas nuevas de `EscalaDibujo`). Build del add-in y de
  `PreviewUI` sin errores.
- [x] Revisado en imágenes sin Revit: un programa temporal (fuera del repo)
  dibujó los paneles con datos de prueba y los guardó como PNG:
  - `imagenes/2026-10-21-escala-menores_25x30.png`
  - `imagenes/2026-10-21-escala-menores_120x50.png`
  - `imagenes/2026-10-21-escala-previa_25x30.png`
  - `imagenes/2026-10-21-escala-previa_120x50.png`
  - `imagenes/2026-10-21-escala-planta.png`
  Una 25×30 se ve más alta que ancha, una 120×50 alargada, todas con sus cotas.
- [ ] Probado en Revit (falta: desplegar y recorrer la lista de "Cómo probarlo").

Criterios: los seis quedan cumplidos en las imágenes sin Revit (⏳ hasta
probarlos en Revit). La elevación de tramos y la de traslapos no salen en las
imágenes; se revisan en Revit.
