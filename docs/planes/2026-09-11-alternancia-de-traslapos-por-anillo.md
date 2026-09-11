# La alternancia de traslapos se agrupa por el ANILLO del perímetro

## Contexto

Luciano preguntó cómo estaba funcionando la alternancia de traslapos y, al
explicárselo, trajo del equipo la **regla real**, con un dibujo de una sección de
10 barras (2 intermedias arriba y abajo, 1 a cada lado, más las 4 esquinas):

> **Uno sí y uno no** dando la vuelta al perímetro, de modo que **nunca se hace
> traslapo en dos barras que estén al lado**. Las marcadas son T1 y las demás
> T2 (o al revés): un grupo traslapa primero y el otro después.

Eso cierra una de las dos dudas que el propio `GeneradorLongitudinal.cs` tenía
anotadas desde Fase 5 en su encabezado:

> *"(a) qué criterio real separa las barras en el grupo par/impar -- acá es
> simplemente el orden en que ya se recorrían esquinas/cara X/cara Y"*

Y de paso demostró que **la implementación no cumplía la regla**.

## Por qué la anterior no cumplía

El grupo salía del orden de **creación** (`posicion++ % 2`): primero las 4
esquinas, después los pares de cara X, después los de cara Y. Ese orden no
recorre el perímetro, así que la paridad no tenía nada que ver con la vecindad.

Con la sección del dibujo el reparto quedaba así:

| Grupo | Barras |
|---|---|
| A | esquina inf-izq, esquina sup-izq, inferior-2ª, inferior-3ª, lateral-izq |
| B | esquina inf-der, esquina sup-der, superior-2ª, superior-3ª, lateral-der |

Dando la vuelta hay **tres pares de vecinas en el mismo grupo**: superior-2ª con
superior-3ª, inferior-2ª con inferior-3ª, y esquina inf-izq con lateral-izq.

Fallaba incluso en el caso más simple. Con **solo las 4 esquinas**, el orden de
creación es (XMin,YMin) → (XMax,YMin) → (XMin,YMax) → (XMax,YMax) = A, B, A, B;
pero el anillo se recorre inf-izq → inf-der → sup-der → sup-izq, o sea
A, B, **B**, A: las dos de la derecha quedaban juntas.

## El fix

Un solo lugar: `GenerarColumna` en `Logica/GeneradorLongitudinal.cs`. Los tres
bloques sueltos que creaban las barras (esquinas, cara X, cara Y) se reemplazaron
por **una lista `anillo` armada en orden de perímetro**, y el grupo sale del
índice en esa lista (`i % 2 == 0`).

El recorrido, dando una vuelta completa en un solo sentido:

1. esquina inferior izquierda
2. intermedias de la cara inferior, **x creciente**
3. esquina inferior derecha
4. intermedias de la cara derecha, **y creciente**
5. esquina superior derecha
6. intermedias de la cara superior, **x decreciente**
7. esquina superior izquierda
8. intermedias de la cara izquierda, **y decreciente**

`PosicionIntermedia(desde, hasta, n, i)` reparte con `i = 0` pegado a `desde`,
así que las caras "de ida" se recorren con el índice normal y las de "vuelta" en
reversa.

**No cambió nada más**: cada entrada del anillo lleva exactamente lo que ya se le
pasaba a `CrearBarraConTraslapos` (tipo de barra, posición, su `Corrimiento` con
el eje que corresponde a su cara, y su normal). Lo único distinto es el orden y
de dónde sale la paridad.

### Cualquier cantidad de varillas

El recorrido se arma desde `columnas` y `filas`, sin números fijos. En un anillo
**cerrado** la alternancia estricta solo existe con una cantidad **par** de
posiciones, y con el modelo de hoy siempre lo es: `4 + 2·columnas + 2·filas`.

Aun así no se dejó implícito. Si alguna vez saliera impar, queda exactamente un
par de vecinas en el mismo grupo (es matemáticamente inevitable) y ahora **se
avisa** en el resultado de la generación, en vez de descubrirse en obra.

## Lo que NO se tocó

- **Cuándo** traslapa cada grupo: `PlanificarCortes` ya filtra los niveles por
  paridad y eso estaba bien. Lo que estaba mal era **quién** entra en cada grupo.
- El corrimiento lateral alternado dentro de cada barra.
- La caché `cortesPorBarraYGrupo`, con clave `(barra.Id, esParGrupo)`.

## Verificación

Compilado (0 errores). Como es lógica de generación y no de interfaz, la vista
previa no sirve acá y Revit todavía no abre, así que se comprobó la aritmética
del anillo con un script descartable que reproduce el recorrido, imprime cada
posición con su grupo y verifica que **ninguna vecina repita grupo, incluido el
cierre** (la última contra la primera):

| columnas | filas | barras | resultado |
|---|---|---|---|
| 2 | 1 | 10 | OK — y el grupo coincide **exactamente** con los cinco chulitos del dibujo |
| 0 | 0 | 4 | OK (antes daba A, B, **B**, A) |
| 3 | 0 | 10 | OK |
| 0 | 4 | 12 | OK |
| 1 | 1 | 8 | OK |
| 9 | 9 | 40 | OK (el máximo que ofrece la ventana) |

### Pendiente de Revit

Generar una columna real y confirmar en 3D que los traslapos de dos barras
vecinas nunca caen a la misma altura.

## Sigue abierto

La segunda duda del encabezado, que esta conversación **no** cerró: qué hacer
cuando un corte obligado por límite de host no coincide con ningún traslapo real
del grupo. Hoy se resuelve como junta a tope, sin solape. Se cruza con el
[#41](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/41)
(una sola barra de punta a punta), que si se implementa hace desaparecer esos
cortes obligados.
