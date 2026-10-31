# Plan: vista longitudinal limpia y "Crear vistas" de verdad (#222)

> **Para quien lo ejecute:** se sigue tarea por tarea, en orden. Commit y push solo si Juan lo pide (CLAUDE.md), sin firma de Claude.

**Objetivo:** que "Crear vistas" deje en el navegador de Revit la vista larga y la sección, y que la vista larga se vea como `V Cim A-B-C - (0.50x0.90)` del plano E-01: con columnas reales cortas, luces y total acotados, y su nombre.

**Referencia:** #222 y "Cómo se hace" de #217. **Rama:** `despiece-vista-longitudinal-222`, desde `main`. **Modelo de prueba:** `PROY_JULIO_BETO`, viga 392699.

**Fuera de alcance:** etiquetas de estribos (#224), cotas de la sección (#225), colocar en hojas (nunca), columnas como elemento (#226).

## Restricciones

- Nunca editar tipos existentes del proyecto; crear tipos propios sí.
- Nada dibujado que imite otra cosa: la columna es la real y las cotas son cotas de Revit.
- La vista previa sigue sin dejar rastro. Solo "Crear vistas" deja cosas en el proyecto.

## Hallazgos (probado en Revit el 30-oct, viga 392699, vistas temporales ya borradas)

- **Columna corta resuelta:** con la columna visible, un recorte bajo la corta. No hace falta ocultarla ni dibujarla. Con 0.70 m bajo la viga se ve como en el plano.
- **Las barras sacadas no dependen del recorte:** los detalles de doblado siguen viéndose por fuera de él. Por eso el recorte ya no tiene que abarcar las filas de barras.
- **Cotas:** `NewDimension` con `new Reference(eje)` funciona. Dio `3.38 | 3.37` y total `6.75`, en metros.
- **Cuidado con la posición de las cotas:** se pidió ponerlas 0.25 m bajo la viga y, al recortar después, Revit las bajó hasta el final de los ejes (de z 20.01 a 10.80 pies). Quedaron limpias y sin choque, pero no donde se pidieron. **Falta probar** crearlas después de recortar.
- **Los ejes siguen visibles y largos:** atraviesan la vista, como en el plano. Con `SentidoDeLectura` salen A-B-C.
- **Falta ocultar:** niveles, ya probado con `SetCategoryHidden(OST_Levels)`.
- Evidencia: imágenes `c1_alto`, `c2_corto` y `c3` de la prueba; pasar `c3` a `docs/planes/imagenes/`.

## Decisiones

1. **~~Columna corta con el recorte~~** → **~~Columna visible sin asomar~~** → **La vista es solo de la viga: las columnas se ocultan** (2026-10-30, Juan: "no debe salir, ni invisible ni nada, la columna"). Se oculta con `HideElements` como todo lo ajeno a la viga; no se dibuja nada en su lugar. Consecuencia probada: la viga queda cortada donde estaba cada columna, porque Revit las tiene unidas (`JoinGeometryUtils`: 4 columnas y 4 vigas unidas a la 388501). Es su geometría real. Quitar el corte exigiría desunirlas, y eso edita el modelo del usuario: no se hace. El recorte va justo al alto de la viga, 0.005 m de holgura.
   - La #222 pedía "columna real en los apoyos" (#219): esta decisión la reemplaza; actualizar el issue.
1b. **Los ejes se leen en orden y sus círculos van sobre las filas** (2026-10-30, Claude): la vista salió 4-3-2-1 porque la derecha de la sección sale contraria al `BasisX` pedido (con +X salió C-B-A). Los círculos chocaban con las barras: se suben con `SetCurveInView`, solo en esta vista (probado).
2. **Mismo generador para la previa y para las vistas definitivas** (2026-10-30, Claude): la diferencia es confirmar o deshacer la transacción. Así la vista creada es igual a la que se revisó.
3. **Tipos propios con nombre fijo** (2026-10-30, Claude): en la previa se deshacen; en las vistas definitivas se reutilizan si ya existen (`CQ Despiece ...`), para no llenar el proyecto de copias.
4. **Escala 1:50 sin selector, y si la vista ya existe se avisa y no se duplica** (2026-10-30, Juan: "sí, sigue con esas dos propuestas"): las vistas de la maqueta están aprobadas y la escala se cambia en Revit.
5. **El bastón se reconoce por la marca del add-in de vigas, no por la cantidad** (2026-10-30, Juan: "recuerda que aquí no se marcan los bastones"): la pieza empieza por `B` (`BS1`, `BI1`). La cantidad fallaba en los dos sentidos: en la viga 388501 hay bastones de 3 barras, y en la 388561 hay corridas de 1. Una barra sin marca no se da por bastón.
6. **La sección se corta fuera de los traslapos** (2026-10-30, Claude): en el centro de la viga podía caer un traslapo, y por eso salían 6 etiquetas arriba: eran dos corridas de 3, no bastones. Se elige el punto con menos barras corridas, lo más cerca del centro.
8. **Arriba o abajo por el tramo recto** (2026-10-30, hallado al revisar `V 3` y `V A`): las barras con gancho hacia el centro de la viga salían del lado contrario (la de arriba, abajo). Se decidía con el centro de la caja de la barra, que el gancho corre. Ahora se decide con la altura del tramo más largo. Verificado contra los datos: en la viga 392698 la corrida `S1` (6.00, gancho abajo) está arriba y `I1` (5.50, gancho arriba) está abajo, y en la vista salían al revés.
9. **Las etiquetas de una fila van a la misma altura** (2026-10-30, Juan, con captura: "aquí no está alineado"): cada etiqueta iba sobre la caja de su barra, y una barra con gancho es más alta que una recta, así que las de una misma fila quedaban escalonadas. Ahora todas van sobre la barra más alta de su fila.
10. **Cada línea de acero en una sola fila** (2026-10-30, Juan, con captura del plano de Santiago: "los de arriba arriba, los de abajo abajo, así muestran los traslapos"): las piezas `S1`, `S2`… van juntas en la fila de arriba más cercana a la viga, las `I` en la de abajo, y los bastones (`BS`, `BI`) más afuera. Las piezas que se traslapan quedan en la misma fila, una corrida 0.08 m para que se vea el traslapo. Antes cada pieza iba en su propia fila porque se traslapaban. Falta la cota del traslapo (`1.50`, `1.10` en el plano), que sigue en #223.
11. **Escala 1:15 en la vista larga** (2026-10-30, Juan, respondiendo a "¿qué escala?"): reemplaza el 1:50 de la decisión 4. Las separaciones se reducen en proporción (15/50). Ojo: una viga de 11 m a 1:15 mide unos 0.76 m en la hoja.
12. **Una fila por diámetro y piezas iguales juntas** (2026-10-30, Juan, con captura de `V 3` armada con #5 y #4: "está mal"): la pieza `S1` eran 3 conjuntos (3 #5 y dos sueltas de #4), y todos caían en la misma fila, encimados con sus etiquetas. Ahora cada diámetro tiene su fila, el más grueso junto a la viga. Las de igual pieza, diámetro y largo se dibujan una vez con la cantidad total (`2 #4 L= 6.00`).
13. **La etiqueta va junto al tramo recto, con espacio para cotas** (2026-10-30, Juan, con captura: "quedan muy lejos... y espacio para las cotas de traslapo"): iba sobre la punta del gancho, a unos 0.3 m de la barra. Ahora las filas se miden desde el tramo recto, y sobre la etiqueta quedan 0.15 m libres para las cotas de #223. La etiqueta se pisaba con la pieza corrida 0.08 m del traslapo porque la caja de la nota baja 0.09 m desde su punto a 1:15 (medido en `V 3`): se separa 0.11 m de la barra más alta de la fila.
7. **Las medidas de recubrimiento siguen en #225** (2026-10-30, Juan lo supuso y el issue lo confirma).

## Tareas

### Tarea 1: columnas, recorte y cotas en la vista larga

**Archivo:** `addins/despiece/Revit/GeneradorPrevia.cs`.

- `Limpiar` deja también las columnas que tocan la viga. Se identifican por geometría, no por nombre.
- Recortar con los márgenes de la decisión 1 y ocultar los niveles.
- Crear las cotas después de recortar: una con todos los ejes que cruzan la viga y otra con el primero y el último. Con un solo eje, sin cotas. Usar el tipo de cota en metros.

**Prueba en Revit:** viga 392699 y otra en la dirección contraria (388501). Verificar luces contra los ejes, columnas cortas y que nada choque con las filas de barras.

### Tarea 2: crear las vistas de verdad

**Archivos:** `GeneradorPrevia.cs`, `Interfaz/PuenteDespiece.cs`, `web/pasos.js`.

- Separar generar de exportar. Nuevo mensaje `crearVistas` con el grupo.
- Confirmar la transacción y nombrar las vistas `<nombre del grupo>` y `Sección <nombre del grupo>`.
- La página marca en verde solo cuando Revit confirma y dice qué vistas se crearon. Si falla, muestra el error y no marca.

**Prueba en Revit:** crear, ver las dos en el navegador, arrastrarlas a una hoja, deshacer con Ctrl+Z y volver a crear.

### Tarea 3: cierre

- Marcar lo hecho en #222 y anotar aquí los hallazgos nuevos.
- Compilar con `tools/desplegar-local.ps1 -Addin despiece -Revit 2026` y dejar que Juan lo pruebe.
