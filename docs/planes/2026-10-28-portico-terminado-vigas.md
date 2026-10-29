# Plan: marcar un pórtico como terminado y verlo en verde en la planta (punto 5 de Santiago, 28-oct)

## Contexto

- Santiago (reunión 28-oct, min 10:53; sheet "Addin - Acero Vigas", punto 5): en un proyecto grande no se sabe qué
  vigas ya se modelaron; pidió marcarlas con un color. Juan lo aceptó como leyenda.
- Juan (28-oct) lo concretó así: una palomita sutil al lado del nombre del pórtico; al marcarla, ese pórtico sale en
  verde claro en la vista Planta de la barra izquierda. La pone el usuario a mano: no depende de haber generado.
- Fuera de alcance: la Lista (no cambia), colores automáticos al generar.
- Rama: `vigas-portico-terminado`. Lleva encima, sin commit, la nota de Reglas de
  `docs/planes/2026-10-28-reglas-largo-total-gancho.md` (misma carpeta de trabajo).

## Criterios de aceptación

- [ ] Al lado del nombre del pórtico abierto hay una palomita: sin marcar, un círculo gris fino ("Marcar como
      terminado"); marcada, círculo verde con palomita blanca y "Terminado" en verde, letra chica.
- [ ] Un clic marca y otro desmarca. En una vigueta de un sistema de viguetas iguales se marca todo el sistema.
- [ ] En la Planta, las vigas y viguetas de los pórticos terminados salen en verde claro; el azul del abierto y de los
      seleccionados va encima. La leyenda suma "Terminado".
- [ ] La marca se guarda en las vigas de Revit: sigue al cerrar y abrir el tablero o el modelo.

## Hallazgos del código

- La configuración del pórtico ya se guarda en cada viga con Extensible Storage (`Revit/ConfiguracionPortico.cs`,
  esquema `CQEINGVigasTablero`) y la sesión la lee con una función `configuracionDeViga`
  (`calculo/.../Tablero/SesionTablero.cs:56-58`, 281-284). El mismo patrón sirve para la marca.
- Un esquema de Extensible Storage no se puede cambiar una vez creado: la marca va en un esquema nuevo.
- La página manda mensajes que lee `ProtocoloTablero.Leer` y C# contesta con `Salida`s que atiende `PuenteTablero`.
- La planta pinta cada elemento en `tablero.js` (`barraPlanta`, cálculo de `fill`/`stroke`); el encabezado del pórtico
  abierto está en el centro de la página.

## Decisiones

1. **Marca manual, no automática** (2026-10-28, Juan): reemplaza la idea de pintar al generar.
2. **Esquema propio `CQEINGVigasTerminado`** (2026-10-28, Claude): campo booleano en cada viga del pórtico; desmarcar
   borra la entidad. Una operación de Revit "Marcar pórtico terminado" (Ctrl+Z la deshace en el modelo; el tablero lo
   muestra al volver a leerlo).
3. **Un sistema de viguetas se marca entero** (2026-10-28, Claude): igual que su acero, que es uno solo para todas.
4. **Verde**: relleno `#CDEBD5`, borde `#3B8A57`; palomita marcada `#3B8A57` (2026-10-28, Claude).

## Plan paso a paso

1. **Cálculo** — `ProtocoloTablero.cs` (mensaje `marcarTerminado` {id, terminado}), `SesionTablero.cs` (salida
   `ATerminado`, lectura `terminadaViga`, lista `Terminados` en la vista), `VistaTablero.cs` — pruebas en
   `SesionTableroTests` y `ProtocoloTableroTests`.
2. **Revit** — `Revit/MarcaTerminado.cs` (esquema) y `Tablero/PuenteTablero.cs` (leer y escribir en una transacción).
3. **Página** — `Tablero/web/tablero.js`: palomita junto al nombre, verde en la planta y leyenda.
4. Compilar, pruebas y desplegar para Revit 2026.

## Cómo probarlo

```
dotnet test addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2026
```

1. Abrir un pórtico, marcar la palomita: en Planta sale verde. Desmarcar: vuelve al gris.
2. Cerrar y abrir el tablero: sigue marcado.
3. Marcar una vigueta de un sistema: todo el sistema sale verde.

## Estado

- [x] Plan revisado con el usuario (28-oct).
- [x] Pasos implementados (28-oct): pasos 1 a 4. 1057 pruebas del cálculo en verde (6 nuevas: `LeeMarcarTerminado`,
  `MarcarTerminadoLoGuardaEnSusVigasYLoMuestra`, `DesmarcarTerminadoLoQuita`, `MarcarUnPorticoQueNoEstaNoTocaRevit`,
  `MarcarUnaViguetaMarcaTodoSuSistema`, `LoMarcadoEnLasVigasSeLeeAlRecargar`); `node --check` del tablero; desplegado
  para Revit 2026.
- [ ] Probado en Revit.
- Pendiente conocido: un Ctrl+Z en Revit deshace la marca en el modelo, pero el tablero abierto no se entera hasta
  volver a leer el modelo (cerrar y abrir el tablero): hoy no escucha los cambios del documento.
