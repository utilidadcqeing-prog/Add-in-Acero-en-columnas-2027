# Plan: activar el despiece de viguetas (pedido del 31-oct)

## Contexto

- **Qué se pide** (Juan, 31-oct): "podemos activar el despiece de viguetas, prácticamente es lo mismo que el de vigas, lo único que puede ser diferente son los estribos".
- **Fuera de alcance:** columnas y zapatas siguen en "pronto".
- **Entorno:** Revit 2026.4, `PROY_JULIO_BETO`, rama `despiece-vista-longitudinal-222`. Única vigueta con acero: 388864 (`VB_10X35`); las `VT_10X35` no tienen acero.

## Criterios de aceptación

- [ ] El botón "Viguetas" está activo y cambia lo que se puede elegir en la planta.
- [ ] En modo Vigas no se eligen viguetas, ni al revés.
- [ ] Una vigueta tiene sus dos vistas, con grapas, arrastre, 3D y botones a Revit.
- [ ] La cadena de cotas muestra los apoyos de la vigueta, que son vigas.
- [ ] Sin apoyos no sale una cota repetida.
- [ ] Las vistas de vigueta no chocan de nombre con las de viga.

## Hallazgos

- **Probado en Revit (31-oct, 388864):** el generador actual funciona con la vigueta: 2 vistas, grapas inclinadas `24 #3 L= 0.51 G= 0.11`, 34 vistas antes y después. No hay que cambiar nada de estribos.
- **Cota repetida:** sin columnas, `CotarColumnas` crea dos cotas iguales de 7.00.
- **La vigueta se apoya en vigas:** las columnas no la tocan, así que la cadena salía de un solo tramo.
- **Regla existente:** `origin/main` tiene `ReglaVigueta` pública (VT, VB, "vigueta" o menos de 200 mm); en esta rama sigue privada en `AgrupadorPorticos`, porque la rama no incluye `main`.

## Decisiones

1. **La regla se copia en `TipoElemento`, sin apoyos** (31-oct, Claude): es la de `ReglaVigueta`. Al unir con `main`, se reemplaza por enlazar ese archivo, como `ColoresDiametro`.
2. **Las vistas se llaman `Vigueta …`** (31-oct, Claude): con `V …` podrían chocar con una viga. Una vigueta con vistas ya creadas con `V …` deja de encontrarlas.
3. **Apoyos de una vigueta = armazón perpendicular que la toca y que no es vigueta** (31-oct, Claude).

4. **El total de la barra sale de sus cotas** (31-oct, Juan: "debería ser 4.5, el add-in de acero lo generó con un redondeo de 5 cm"): `REBAR_ELEM_LENGTH` da 4.47 porque descuenta el doblez. Cada gancho de 90° suma `2 (R + d/2) − R·π/2`, la cuenta de `GanchoPropio.DiferenciaCotas` en acero de vigas. Los ganchos de otro ángulo, como los de los estribos, no cambian.

## Plan paso a paso

1. **Pruebas y regla** — `calculo/CQEING.Despiece.Calculo/TipoElemento.cs`, `AgrupadorIguales.cs` y pruebas.
2. **Lectura** — `Revit/LectorDespiece.cs`, `Interfaz/PuenteDespiece.cs`: `esVigueta` por grupo.
3. **Cotas** — `Revit/GeneradorPrevia.cs`: apoyos y cota repetida.
4. **Página** — `web/pasos.js`: botón activo y filtro en la planta.
5. **Comprobar** — por MCP con `LoadFromStream` (sin `say_hello`) y captura de la página.

## Cómo probarlo

```
dotnet test addins/despiece/calculo/CQEING.Despiece.Calculo.Tests
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin despiece -Revit 2026
```

1. Modo Vigas: un clic en una vigueta no hace nada.
2. Pulsar Viguetas: la selección se limpia y un clic en una vigueta la elige.
3. Vigueta 388864, paso 2: cotas con sus apoyos y grapas; arrastrar una barra.
4. Una vigueta `VT` sin acero: avisa que no tiene acero.

## Estado

- [x] Pasos 1 a 4. Sin commit. 63 pruebas pasan; compila para 2026 y 2027.
- [x] Por MCP con `LoadFromStream`, sin dejar rastro (34 vistas antes y después): 31 elementos, 16 viguetas. Vigueta 388864: cadena `0.25 · 3.13 · 0.25 · 3.12 · 0.25` y total 7.00; viga 388501 sin cambios.
- [x] Página en Edge sin ventana con datos de ejemplo: cada botón deja elegir solo lo suyo y al cambiar se limpia la selección.
- [x] Total por cotas (decisión 4), medido en Revit: viga 388501 da 4.50, 4.50, 4.00, 4.00, 3.50 y 5.50 (antes 4.47, 4.48, 3.97, 3.98, 3.46 y 5.46); estribos y grapas no cambian. Las barras de 392699 y 388864 dan 6.07, 2.57, 4.03: no caen en múltiplos de 5 cm, parece que se generaron antes del redondeo. 66 pruebas pasan.
- [ ] Sin probar en la ventana real, ni el arrastre y el 3D con una vigueta. Solo hay una vigueta con acero en este modelo.
- ⚠️ Pendiente al unir con `main`: enlazar `ReglaVigueta` y borrar `TipoElemento` (decisión 1).
