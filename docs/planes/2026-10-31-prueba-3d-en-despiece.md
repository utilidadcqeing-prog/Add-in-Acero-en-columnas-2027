# Plan: prueba del 3D dentro del add-in de despiece (pedido del 31-oct)

## Contexto

- **Qué se pide** (Juan, 31-oct): ver dentro del add-in cómo queda el acero, como el botón 3D de vigas pero sin salir de la ventana. Esta es la prueba corta: ¿se ve bien y gira con fluidez?
- **Fuera de alcance por ahora:** marcar barras desde la lista, arrastrar textos, paso 3 con textos movidos.
- **Entorno:** Revit 2026.4, `PROY_JULIO_BETO`, viga 388501, rama `despiece-vista-longitudinal-222`.

## Criterios de aceptación

- [ ] En el paso 2, un botón cambia la vista larga por el 3D y vuelve.
- [ ] Se ven todas las barras con su color por diámetro y la viga transparente.
- [ ] Gira y acerca con fluidez en la viga 388501, que tiene cerca de 220 estribos y grapas.
- [ ] No cambia nada en el proyecto ni en "Deshacer".

## Hallazgos

- `PreviewControl` exige una ventana modal (`RevitAPIUI.xml`) y `DirectContext3D` dibuja en vistas de Revit: ninguna sirve dentro de la página. La página sí puede dibujar con WebGL.
- `GetTransformedCenterlineCurves` da la posición real de cada barra del conjunto, incluidas las corridas con `MoveBarInSet` (ver `GeneradorPrevia.Seccion`).

## Decisiones

1. **three.js 0.147.0, copiado en `web/vendor/`** (31-oct, Claude): es la última con versión para `<script>` y `OrbitControls` sin módulos, igual que el resto de la página, y funciona sin internet.
2. **Solo lectura, sin transacción** (31-oct, Claude): se leen curvas y geometría; no hay nada que deshacer.
3. **Barras unidas en una malla por color** (31-oct, Claude): un objeto por barra serían cientos de llamadas de dibujo y giraría peor.
4. **Se dibuja solo al mover la cámara** (31-oct, Claude): en reposo no gasta nada.

## Plan paso a paso

1. **Leer** — `Revit/Modelo3D.cs`: barras como puntos con color y radio; viga como triángulos, en metros y centrado.
2. **Puente** — `Interfaz/PuenteDespiece.cs`: mensaje `ver3d`, respuesta `modelo3d`, guardada hasta que cambie el modelo.
3. **Página** — `web/pasos.js`, `web/index.html`, `web/vendor/`: componente `Vista3D` y botón.
4. **Comprobar sin Revit** — captura con Edge sin ventana y datos de ejemplo de 220 estribos.
5. **Comprobar en Revit** — ver "Cómo probarlo".

## Cómo probarlo

```
dotnet build addins/despiece/CQEING.Despiece.csproj -c Debug -p:RevitVersion=2026
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin despiece -Revit 2026
```

1. Abrir `PROY_JULIO_BETO`, **Despiece**, elegir la viga 388501 y pasar a Revisar.
2. Pulsar **3D**: girar con el botón izquierdo, mover con el derecho, acercar con la rueda.
3. Pulsar **Plano**: vuelve la vista larga.
4. Mirar "Deshacer" en Revit: nada del add-in.

## Estado

- [x] Pasos 1 a 4. `Modelo3D.Leer` probado con la viga 388501 en Revit abierto, cargando la DLL con `LoadFromStream` por MCP: 19 ms, 122 KB. Los datos reales se dibujaron en Edge sin ventana, con WebGL por software, y se revisó la captura.
- [ ] Paso 5 en la ventana real: no se ha girado con la mano ni medido la fluidez; el WebView2 de Revit puede comportarse distinto del Edge de la prueba.
