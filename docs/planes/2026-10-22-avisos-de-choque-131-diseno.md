# Diseño: avisar cuando una pieza choca con otra, sin impedirlo (#131)

Fecha: 22-oct-2026. Estado: **diseño aprobado por Luciano (22-oct)**; falta
el plan de trabajo.

## Para qué

En la reunión del 21-oct (observación 2 de
`2026-10-21-observaciones-reunion-columnas.md`) se pidió que, si el usuario
coloca algo que se mete dentro de otra pieza, el add-in **avise** ("si lo
colocas así, ten en cuenta que…") y **deje seguir**. El caso que Luciano
tiene en mente: una grapa cuando hay muchas barras longitudinales juntas.

Hoy algunos choques se detectan, pero casi todos recién **después** de
generar, en la ventana de avisos del final, cuando el acero ya está en Revit.
La idea es verlos **antes**, mientras se arma la columna y al tocar
"Generar".

## Qué tiene que pasar (decisiones de Luciano, 22-oct)

1. **Mientras se arma la columna:** la pieza que choca se marca en rojo
   sobre el dibujo de la sección (rodeada en rojo); al pasar el mouse se lee
   el motivo. Esto en **los dos lugares** donde se dibuja la sección:
   - la pestaña "Vista previa" del panel del tramo (`Vistas/PanelVistaPrevia`);
   - la ventana "Editar estribos menores" (`Vistas/VentanaEstribosMenores`).
2. **Selector "Sección normal / Zona de traslapo"** en esos dibujos: en la
   zona de traslapo cada barra tiene al lado su segunda pieza, corrida un
   diámetro, y ahí las barras quedan más juntas. Los choques se revisan en las
   dos, y el selector muestra una u otra.
3. **Al tocar "Generar":** si alguna columna del plan tiene choques, un
   mensaje "Hay choques en N columnas — Si lo generas así, ten en cuenta
   que:" con la lista (columna y motivo) y dos botones: **"Volver a revisar"**
   (no genera) y **"Generar igual"** (genera como siempre). Sin choques, se
   genera directo, sin mensaje.
4. **Nunca se impide generar** ni se corrige nada solo. Solo se avisa.

Boceto aprobado: `.superpowers/brainstorm/331-1792652100/content/avisos-choque.html`
(en la carpeta `C:\Proyecto\addins`, opción B + mensaje de Generar).

## Qué choques se revisan

| Choque | Hoy | Con esto |
|---|---|---|
| Barras longitudinales demasiado juntas (sección normal y zona de traslapo) | no se revisa | nuevo |
| Grapa o estribo menor que pasa encima de una barra que no abraza | no se revisa | nuevo |
| Estribo cerrado demasiado angosto para doblarse con su barra | antes de generar, solo en "Editar estribos menores" | también en la vista previa y en el mensaje de Generar |
| En un traslapo, las dos piezas no entran en el doblez de la grapa (abraza una y choca con la otra) | solo después de generar | antes de generar |
| Pieza de traslapo que atraviesa el gancho del estribo | solo después de generar | antes de generar, si se puede predecir (ver "Límites") |

**Distancia mínima entre barras longitudinales:** la de la NSR-10 para
columnas, **libre (borde a borde) ≥ el mayor entre 1,5 diámetros de la barra
más gruesa de las dos y 4 cm**. Decisión de Luciano: usar esta regla, **a
confirmar con Carlos**. Que quede en un solo lugar del código para cambiarla
fácil.

Los avisos que hoy salen después de generar **se quedan** (son la medida
contra lo que Revit dejó de verdad).

## Cómo se arma (lo que ya existe y conviene reutilizar)

- Posición de las barras en la sección: `Modelo/SeccionPlanta.Calcular`
  (la misma que dibuja la vista previa).
- Dónde va cada traslapo: `Modelo/PlanTraslapos` (el mismo cálculo de la
  pestaña "Traslapos" y del generador).
- Hacia dónde se corre la pieza del traslapo para no chocar:
  `Modelo/AcomodoTraslapos.Cuatro` + `Elegir`, hoy llamados desde
  `Logica/GeneradorLongitudinal` (con Revit). Para mostrar la zona de
  traslapo antes de generar, esa elección tiene que poder hacerse **sin
  Revit**, con los mismos datos, para que lo que se ve sea lo que se genera
  (regla "nada figurativo" de la reunión). Lo más probable es pasar la parte
  que arma los pedidos a `Modelo/`.
- Choque entre piezas redondas: `AcomodoTraslapos.Choque` / `MaximaMetida`
  ya miden cuánto se meten dos piezas.
- Geometría de grapas y estribos menores en planta: `Modelo/PlantaArmado`
  (`Grapa`, `Menor`, `EstriboCerrado`) y `Modelo/GanchoGrapa` (qué piezas
  entran en el doblez).
- El cerrado imposible: `VentanaEstribosMenores.Imposible`; mover su cálculo
  a `Modelo/` para usarlo también desde la vista previa y el mensaje de
  Generar.

Sugerido: un `Modelo/ChoquesSeccion.cs` sin Revit, con pruebas en
`tools/PruebasVistaPrevia`, que recibe la sección (barras, estribo, piezas
menores, traslapo) y devuelve la lista de choques: qué piezas y el texto del
motivo. Las dos ventanas lo dibujan; el botón Generar lo junta por columna.

## Textos

Español neutro, sin voseo, con medidas en cm y el nombre de la barra. Que
digan **qué pieza choca con cuál** (criterio del issue). Ejemplos:

- "Cara superior: las barras 2 y 3 quedan a 1,2 cm libres; con #5 se piden
  4,0 cm."
- "La grapa de la fila 2 pasa encima de la barra 3, que no abraza."
- "Zona de traslapo: la pieza corrida de la barra 2 queda a 0,8 cm de la
  barra 3; se piden 4,0 cm."

## No es parte de esto

- Corregir piezas solas para sacar el choque.
- Bloquear la generación.
- Dibujar los choques en la vista previa 3D (#137 todavía no muestra las
  longitudinales).
- Revisar choques con piezas que no arma este add-in (vigas, otra armadura
  del modelo).

## Límites conocidos

- Antes de generar se trabaja con la posición **calculada**. Después, Revit
  puede dejar una pieza unos milímetros distinta (por ejemplo, la barra de
  cara corrida 8,6 mm del #153). Por eso se mantienen los avisos de después
  de generar.
- **Varillas con más de un traslapo** (columnas de varios pisos con barras
  de 6 m): su zona de traslapo no se revisa, porque el acomodo de las piezas
  corridas solo está medido con un traslapo (la misma regla del generador).
  Se dice con la nota "Zona de traslapo no revisada en N varilla(s)…" en la
  vista previa, en estribos menores y en el mensaje de Generar (revisión
  final, 22-oct).
- **Un aviso por par en la columna:** las longitudinales son las mismas en
  todos los tramos, pero un tramo sin estribo (el de la zapata) ubica las
  esquinas unos mm distinto. Generar avisa cada par una sola vez
  (`ChoquesSeccion.DeLaColumna`), con las medidas del tramo de estribo más
  grueso (revisión final, 22-oct).
- El gancho del estribo como obstáculo de las piezas corridas: hoy el
  generador lo mide en Revit. Si no se puede calcular igual sin Revit, ese
  choque queda solo después de generar y se dice en el documento de cierre.

## Decisiones de Claude (revisarlas)

- **Las dos piezas de una misma varilla en su traslapo no cuentan como
  choque entre sí**: van pegadas a propósito (traslapo por contacto). La
  distancia mínima se revisa entre piezas de **varillas distintas**.
- **Distancia mínima solo entre longitudinales.** No se revisa la distancia
  entre una barra y el estribo: la barra va pegada por dentro del estribo
  por diseño.
- **"Pasa encima de una barra":** la grapa o el estribo menor, con su grosor,
  se mete en planta en una barra longitudinal que no es de las que abraza.
  Como las longitudinales son continuas en altura, todo cruce en planta es
  un choque real.
- **El mensaje de Generar** revisa todas las columnas que se van a generar
  en ese momento (las del plan que arma `BotonGenerar_Click`), con lo que
  tienen configurado, aunque su sección no se haya abierto en la ventana
  (aclarado por Luciano, 22-oct).
- **El #47 (confinamiento que no entra) queda fuera**: es un aviso de
  estribos en altura, no un choque entre piezas. Se puede sumar después al
  mismo mensaje de Generar.

## Lo que decide quien lo programe

- Cómo se dibuja exactamente el contorno rojo y el texto al pasar el mouse
  (siguiendo `Vistas/Paleta.cs` y `DibujoArmado`).
- Dónde va el selector "Sección normal / Zona de traslapo" en cada ventana.
- Qué traslapo se muestra si la columna tiene varios (sugerido: todos se
  revisan; el dibujo muestra el primero, y el motivo dice a qué altura).
- La tolerancia numérica para "tocarse".

## Resultado (22-oct)

Implementado según `2026-10-22-avisos-de-choque-131-plan.md` (Tasks 1-7),
revisión final independiente (sin críticos; los 2 importantes y los 9
menores corregidos) y probado en Revit con Luciano:

- Vista previa: marcas rojas, selector y resumen, bien.
- "Editar estribos menores": la lista de choques empujaba la sección y la
  tapaba. Corregido: una línea de resumen (el motivo se lee en la marca),
  notas con alto máximo y la sección se achica si no entra.
- Generar: el mensaje "Hay choques en N columna(s)" sale bien, con "Volver a
  revisar" y "Generar igual".
- **El generador no cambió** (el acomodo de traslapos pasó a
  `AcomodoTraslapos.PlanearSeccion`): se regeneró la columna 782105 ("1-6",
  #4, 3 intermedias por cara X y 2 por cara Y, estribo #3) y sus 28 piezas
  longitudinales quedaron idénticas a la generación anterior (cajas
  envolventes iguales en X, Y y Z). Los ganchos siguen dentro del lote
  (punta más afuera en X = 7,100 e Y = -0,100; línea en 7,15 y -0,15).

Pendiente: que Carlos confirme la regla de 1,5 diámetros / 4 cm, sobre todo
en la zona de traslapo (con #6 casi todas las piezas corridas quedan a 3-3,4
cm de la vecina).

## Cómo se verifica

- Pruebas sin Revit de `ChoquesSeccion` con casos medidos: barras juntas,
  grapa sobre barra, traslapo apretado y caso sin choques.
- En Revit: una columna con muchas barras (por ejemplo 40×40 con 5 por cara
  de #6) y grapas; comparar lo que avisa la ventana con lo que avisa la
  generación y con lo que se mide en el modelo.
