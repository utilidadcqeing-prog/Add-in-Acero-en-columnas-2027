# Plan: botón para ir a la vista creada en Revit (pedido del 31-oct)

## Contexto

- **Qué se pide** (Juan, 31-oct, sobre el paso 1 del add-in de despiece): "al presionar una viga o cualquier cosa que se haya seleccionado, abajo a la izquierda salga un botón que me lleve a la vista creada"; "que no lo abra en el addin sino que me lleve a la vista en el Revit como tal".
- **Fuera de alcance:** pintar de verde al abrir las vigas que ya tienen vistas.
- **Entorno:** Revit 2026.4, `PROY_JULIO_BETO`, rama `despiece-vista-longitudinal-222`, carpeta `practicacq-despiece`.

## Criterios de aceptación

- [ ] Con una viga elegida que ya tiene vistas, abajo a la izquierda sale el botón.
- [ ] Sin vistas creadas, el botón no sale.
- [ ] Al crear las vistas y volver al paso 1, el botón ya sale.
- [ ] Al pulsarlo, Revit abre la vista real como vista activa y la ventana del despiece se aparta.
- [ ] Si la vista se borró, avisa sin error de Revit.

## Hallazgos

- `GeneradorPrevia.Crear` nombra las vistas `nombre` y `Sección nombre`, y ya las busca por nombre para no duplicarlas.
- `UIDocument.RequestViewChange` es pública y la documentación (`RevitAPIUI.xml`) la permite desde un `ExternalEvent`.
- `VentanaDespiece` tiene a Revit como dueño (`Owner`) y queda encima, así que hay que minimizarla para que se vea la vista.

## Decisiones

1. ~~**Va a la vista larga** (31-oct, Claude).~~ → **Dos botones, uno por vista: "Vista larga" y "Sección"** (31-oct, Juan: "debe mostrarme las dos vistas que se generan para ver la que yo quiera").
2. **La ventana se minimiza, no se cierra** (31-oct, Claude): se conserva lo elegido y se puede volver.
3. **El botón sale si el modelo dice que la vista existe o si se acaba de crear** (31-oct, Claude).

## Plan paso a paso

1. **Buscar la vista** — `Revit/GeneradorPrevia.cs`: `VistaCreada(doc, nombre)`; se comprueba compilando.
2. **Puente** — `Interfaz/PuenteDespiece.cs`: `vistaCreada` en cada grupo y mensaje `irAVista`.
3. **Página** — `web/pasos.js`: el botón al fondo de la columna izquierda.

## Cómo probarlo

```
dotnet build addins/despiece/CQEING.Despiece.csproj -c Debug -p:RevitVersion=2026
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin despiece -Revit 2026
```

1. Elegir una viga sin vistas: no hay botón.
2. Crear sus vistas y volver al paso 1: sale el botón.
3. Pulsarlo: Revit muestra la vista larga y el despiece se minimiza.
4. Cerrar y abrir el despiece, elegir la misma viga: sale el botón.
5. Borrar la vista en Revit y pulsar el botón si sigue visible: sale el aviso.

## Estado

- [ ] Pasos implementados.
- [ ] Probado en Revit.
