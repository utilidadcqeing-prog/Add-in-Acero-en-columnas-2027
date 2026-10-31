# Plan: mover barras y textos en el paso 2 del despiece (pedido del 31-oct)

## Contexto

- **Qué se pide** (Juan, 31-oct): "lo que quiero es poder mover los textos y barras", y que el paso 3 muestre cómo quedó.
- **Fuera de alcance:** mover cotas, mover cosas en la sección, mover las barras reales del modelo.
- **Entorno:** Revit 2026.4, `PROY_JULIO_BETO`, vigas 388501 y 392699, rama `despiece-vista-longitudinal-222`.

## Criterios de aceptación

- [ ] En el paso 2 de la vista larga, una barra se arrastra solo hacia arriba y abajo, y su texto y su gancho van con ella.
- [ ] Un texto de barra o de estribos se arrastra libre.
- [ ] Lo dibujado por la página coincide con lo que dibuja Revit.
- [ ] El paso 3 es la imagen real de Revit con lo movido.
- [ ] "Crear vistas" deja las vistas con lo movido.
- [ ] Las barras del modelo no cambian.
- [ ] Hay un botón para volver a la posición automática.

## Hallazgos

- `CalibracionImagen` y `ExportadorImagen.Calibrar` ya pasan de coordenadas de vista a píxeles con dos cruces magenta (planta, #221).
- Los colores de `ColoresDiametro` deben quedar fuera del umbral del magenta (azul > 180, verde < 90, rojo > 180) o la calibración se confunde; se comprueba en la tabla.
- `RebarBendingDetail.SetPosition` y `TextElement.Coord` son públicos; el add-in ya usa el primero.
- **Cruces y recorte (31-oct, viga 392699):** puestas arriba y abajo de todo, el recorte se las come y la calibración falla (el respaldo de la decisión 6 funcionó). Dentro de la viga sí se ven, pero muy juntas en altura: la escala vertical se toma de la horizontal (`ConEscalaIgual`). Resultado: 342.6 px/m.
- **Número del traslapo (31-oct, Juan: "quedan muy arriba"):** iba a la altura de toda la fila. Ahora va justo sobre la más alta de las dos barras que se traslapan; solo sube si hay una etiqueta ahí mismo a su altura. Vigas 388501 y 392699: 0 textos encimados, medido en Revit.
- **Tamaño de letra:** Revit mide la mayúscula; en la página hay que dividir entre 0.72 (Arial).

## Decisiones

1. **Barras solo arriba y abajo; textos libres** (31-oct, Claude, aceptado por Juan con "ok dale"): la barra sigue alineada con su posición real a lo largo de la viga.
2. **Cotas fijas; etiquetas de estribos movibles; sección fija; el botón 3D se queda** (31-oct, igual).
3. **Paso 2 = imagen sin lo movible + capa de la página** (31-oct, Claude): la viga, los estribos, los ejes y las cotas siguen siendo de Revit.
4. **Paso 3 = imagen real regenerada**, con los movimientos aplicados en Revit.
5. **Los movimientos van en metros y por id de barra** (31-oct, Claude): si una barra desaparece del modelo, su movimiento se ignora.
6. **Si falla la calibración, el paso 2 muestra la imagen completa sin arrastre** (31-oct, Claude): mejor fija que mal alineada.

## Plan paso a paso

1. **Revit** — `Revit/GeneradorPrevia.cs`: registrar qué notas son de cada barra, aplicar movimientos, separar lo movible, calibrar, entregar la capa.
2. **Puente** — `Interfaz/PuenteDespiece.cs`: `previa` y `crearVistas` reciben movimientos.
3. **Página** — `web/pasos.js`: dibujar la capa, arrastrar, restablecer, pedir la imagen final en el paso 3.
4. **Comprobar** — por MCP con `LoadFromStream` (sin `say_hello`): comparar la capa con la imagen completa de Revit y mover algo; después, en la ventana real.

## Cómo probarlo

```
dotnet build addins/despiece/CQEING.Despiece.csproj -c Debug -p:RevitVersion=2026
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin despiece -Revit 2026
```

1. Viga 388501, paso 2: arrastrar una barra. Solo sube y baja, con su texto y su gancho.
2. Arrastrar un texto: va a donde sea.
3. Paso 3: la imagen de Revit lo muestra igual.
4. Pulsar "Crear vistas" y abrir la vista: igual.
5. "Restablecer": todo vuelve a su sitio.

## Estado

- [x] Pasos 1 a 3. Sin commit.
- [x] Por MCP con `LoadFromStream`, viga 392699: imagen separada con 15 piezas y 30 elementos ocultos; imagen con la barra 397193 subida 0.50 m y su texto corrido (1.0, 0.2): sale bien; 39 vistas antes y después.
- [x] Página real en Edge sin ventana, con la capa real y eventos de puntero simulados: la barra solo sube y baja y el texto va libre (captura revisada).
- [ ] Sin probar: arrastrar con la mano en la ventana de Revit, "Crear vistas" con movimientos, "Volver a la posición automática" y el paso 3 desde la ventana. Viga 388501 con traslapos y sin marca.
- ⚠️ `Crear vistas` no reemplaza vistas que ya existen: lo movido no llega a una viga con vistas ya creadas.
