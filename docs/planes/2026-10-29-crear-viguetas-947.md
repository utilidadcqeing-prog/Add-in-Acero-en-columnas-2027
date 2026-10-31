# Plan: crear viguetas por panel desde la planta del piso (#947)

## Contexto

- Se pide (29-oct) pasar la maqueta aprobada de la ventana de viguetas
  ([#945](https://github.com/utilidadcqeing-prog/CQEING_BaseDeDatos/issues/945),
  [#946](https://github.com/utilidadcqeing-prog/CQEING_BaseDeDatos/issues/946)) al add-in real: **ver todo el piso
  modelado en Revit, elegir un panel, ver sus viguetas, eliminar las que no van y crearlas**.
- Tarea: [#947](https://github.com/utilidadcqeing-prog/CQEING_BaseDeDatos/issues/947), con las sub-issues #948 a #953
  (Viguetas 1 a 6).
- Maqueta de referencia: https://claude.ai/artifact/YSc4UodwgYrHrmV3L44LtR (copia en
  `trabajo1\evidencia-945\maqueta-viguetas.html`, fuera de este repo).
- Reparto: Excel de Carlos «CANTIDAD Y LONG EXTREMOS POR PANEL» y reunión del 24-oct.
- **Solo local**: se compila y se despliega con `tools/desplegar-local.ps1`. Sin commit, push, PR ni versión hasta que
  el usuario lo pida.
- Fuera de alcance: detectar vacíos desde el modelo (el vacío se forma eliminando viguetas), paneles que no son
  polígonos cerrados por vigas, publicar una versión.
- Entorno: Revit 2027.3, .NET 10 (`-p:RevitVersion=2027`), modelo `PROY_JULIO_BETO.rvt`, rama `viguetas-crear-947`
  (desde `main` en `d2949cf`).

## Criterios de aceptación

- [ ] Botón «Crear viguetas» en el panel *Vigas* de la pestaña CQEING, que cumple la guía obligatoria de add-ins
      (licencia y bloqueo por versión antes de crear botones; `PushButton` de la misma DLL).
- [ ] Al abrir, se elige el **piso** (niveles del modelo que tienen vigas) y se ve la **planta completa**: vigas,
      columnas y paneles.
- [ ] Clic en un panel: se ven sus viguetas con **dirección X/Y**, **separación eje a eje** y **ancho**, y los tres
      datos (cantidad, eje a eje, libre en extremos) con la fórmula del Excel.
- [ ] **Eliminar viguetas** de la propuesta (con Deshacer) antes de crear.
- [ ] **Crear** las viguetas en Revit con un tipo de vigueta del modelo, en el nivel del panel; se deshacen con Ctrl+Z.
- [ ] **Quitar** las viguetas creadas por el add-in en un panel.
- [ ] Pruebas unitarias de reparto, paneles y planificación; `dotnet test` sin fallos.

## Hallazgos del código y de Revit real

- `addins/acero-vigas/AppVigas.cs` ya empieza con `Licencia.Vigente` y `BloqueoPorVersion.AlDia` y crea su botón con
  `CintaCqeing`: un segundo botón en la misma DLL cumple la guía sin otro `addin.json`.
- El tablero de vigas es un WebView2 (`Tablero/VentanaTablero.xaml.cs`) que sirve `Tablero/web/` con el host virtual
  `tablero.cqeing` y habla por JSON; lo que toca Revit va por un `ExternalEvent` (`Tablero/AccionesRevit.cs`).
- `Revit/LectorModelo.cs` ya lee vigas (eje, ancho por geometría, nivel de referencia, nombre del tipo) y columnas
  (huella en planta, cotas). Todo en mm.
- La regla de vigueta (#939) está en `Porticos/AgrupadorPorticos.cs` (`NombreDeVigueta`, ancho < 200 mm), privada.
- Pendiente de medir en Revit: cota de la línea de ubicación de las vigas de PROY_JULIO_BETO y si las viguetas creadas
  se unen solas con las vigas.

## Decisiones

1. **Mismo add-in de vigas, botón nuevo** (29-oct, por la reunión del 24-oct: Carlos prefería que estuviera junto al
   de vigas). Reusa licencia, bloqueo, WebView2 y despliegue.
2. **La misma ventana WebView2 del tablero**, con otra página (`web/viguetas.html`): `VentanaTablero` recibe la página
   y el título en vez de copiar la ventana.
3. **Los paneles se arman con los ejes de las vigas del nivel.** Por defecto solo cuentan las vigas (no las viguetas,
   según la regla de #939), para que las viguetas que ya existen no partan los paneles. Un interruptor deja usar
   también las viguetas como borde (viguetas de borde de la reunión). El contorno libre descuenta medio ancho de cada
   viga.
4. **Reparto del Excel**: n = REDONDEAR.MENOS(L / d); extremos e = (L − ((n − 1)·d + b)) / 2; si e > d − b se suma una
   vigueta. L es el ancho libre del panel en la dirección del reparto.
5. **Dirección X/Y del proyecto.** Cada vigueta es una línea a la cota del reparto recortada al contorno libre (para el
   largo y el tipo) y al contorno por ejes (para crearla en Revit, de eje a eje de viga).
6. **El cálculo vive en C#** (`calculo/…/Viguetas`), con pruebas; la página solo dibuja y pide. La propuesta se calcula
   sin pasar por el hilo de Revit (no usa la API).
7. **Marca de las viguetas creadas**: el parámetro *Comentarios* dice `CQEING viguetas`. «Quitar» borra las que tienen
   esa marca y caen dentro del panel.
8. **Tipo de vigueta**: lista de tipos de armazón estructural del modelo; se preselecciona uno que cumpla la regla de
   vigueta (VT, VB o «vigueta» en el nombre).

9. **Crear en un panel que ya tiene viguetas del add-in las reemplaza** (29-oct): primero borra las marcadas del
   panel y después crea, todo en la misma transacción. El botón dice «Reemplazar viguetas».
10. **Holgura de 300 mm** para unir vigas que terminan en la cara de la columna o de otra viga
    (`DetectorPaneles.Holgura`). Pendiente de comprobar en PROY_JULIO_BETO que no une vigas que no se tocan.
11. **Página fuera de Revit**: `web/viguetas-demo.js` trae un piso de ejemplo de 2 × 2 paneles para ver la página en un
    navegador, como `demo.js` del tablero. En Revit no se carga.

## Plan paso a paso

1. **Regla de vigueta pública** — `Porticos/ReglaVigueta.cs`, `AgrupadorPorticos.cs` — los tests de pórticos siguen en
   verde.
2. **Reparto del Excel** (#950) — `Viguetas/RepartoViguetas.cs` + tests con 2,00 / 3,57 / 2,84 / 2,66 / 3,12 m.
3. **Paneles** (#949) — `Viguetas/DetectorPaneles.cs` + tests: una grilla 2 × 1, un voladizo que no cierra, viguetas
   que no parten el panel.
4. **Planificación** (#950) — `Viguetas/PlanificadorViguetas.cs` + tests: posiciones, largos, tipos, borradas.
5. **Protocolo** — `Viguetas/ProtocoloViguetas.cs` (JSON de ida y vuelta) + test.
6. **Lectura del piso** (#948) — `Revit/LectorPiso.cs`.
7. **Crear y quitar** (#952) — `Revit/CreadorViguetas.cs`.
8. **Ventana** (#951) — `Viguetas/ComandoViguetas.cs`, `Viguetas/PuenteViguetas.cs`, `Tablero/VentanaTablero.xaml.cs`
   (página y título), `Tablero/web/viguetas.html`, `Tablero/web/viguetas.js`, botón en `AppVigas.cs`.
9. **Compilar, pruebas y despliegue** — comandos de abajo.
10. **Prueba en Revit** (#953) — lista de abajo, con capturas en `docs/planes/imagenes/`.

## Cómo probarlo

```
dotnet test addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests/CQEING.Vigas.Calculo.Tests.csproj
dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2027
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2027
```

1. Abrir PROY_JULIO_BETO en Revit 2027 → CQEING → Vigas → **Crear viguetas**.
2. Elegir el piso: se ve la planta con vigas, columnas y paneles.
3. Clic en un panel: viguetas en azul, cotas y los tres datos. Cambiar ↔ X / ↕ Y, separación y ancho.
4. **Eliminar viguetas**: quitar dos, Deshacer una.
5. **Crear**: aparecen en Revit en el nivel del panel. Ctrl+Z las quita.
6. **Quitar creadas** del panel: se borran solo las del add-in.

## Estado

- [x] Pasos 1 a 9 implementados (29-oct), en la rama local `viguetas-crear-947`, sin commit.
- [x] `dotnet test CQEING-Addins.sln`: 0 fallos (Vigas.Calculo 1067, Gestor.Core 256, Gestor 37, Publicador 8). Las
      pruebas nuevas están en `ViguetasTests.cs`: los cinco casos del Excel, paneles de una grilla, voladizo, viga que
      termina en la cara, viguetas que no parten el panel, reparto en X e Y, panel en L y el mensaje de crear.
- [x] Compilación para Revit 2027 sin advertencias y desplegada con `desplegar-local.ps1`.
- [x] Página revisada en el navegador con el piso de ejemplo: piso, panel, X/Y, eliminar y cotas (2,00 m da 2 viguetas,
      0,80 y 0,55).
- [x] En Revit con PROY_JULIO_BETO (29-oct): N+3.70 da 7 paneles; P1 (libre 3,13 × 3,73 m) da 4 viguetas, 0,80 y 0,62.
      «Eliminar viguetas» quedaba desactivado porque la propuesta llega después de elegir el panel; se corrigió en
      `viguetas.js` (`pie()` actualiza el botón) y el usuario confirmó que ya elimina.
- [x] En Revit (29-oct, prueba del usuario): crear, Ctrl+Z, reemplazar y quitar creadas funcionan. Las viguetas quedan a
      la altura de las vigas, de viga a viga y con el tipo elegido; «Quitar» solo borra las del add-in.

### Criterios de aceptación

- ✅ Botón «Crear viguetas» en *Vigas*, con licencia y bloqueo por versión (`AppVigas.cs`).
- ✅ Elegir el piso y ver la planta completa con vigas, columnas y paneles.
- ✅ Panel con viguetas, X/Y, eje a eje, ancho y los tres datos del Excel.
- ✅ Eliminar viguetas de la propuesta, con Deshacer.
- ✅ Crear en Revit con el tipo elegido, en el nivel del panel; Ctrl+Z las deshace.
- ✅ Quitar las creadas por el add-in en un panel.
- ✅ Pruebas unitarias y `dotnet test CQEING-Addins.sln` sin fallos.

Pendiente: commit, PR y versión (`acero-vigas` 1.2.0) cuando el usuario lo pida; detectar vacíos desde el modelo
queda fuera de esta tarea.
