# Investigación: datos automáticos de puertas y ventanas desde Revit

Tareas relacionadas: **7** (ampliar el JSON que envía el add-in) y **12** (puertas y ventanas, por tipo). Origen: reunión con Carlos Quintero (08/10/2026).

Probado con la API de Revit 2026 el 09/10/2026 en un modelo con 2 puertas (una abatible interior y una balconera corredera) y 3 ventanas, con habitaciones.

## Columnas del cuadro y quién las llena

Columnas del listado de puertas y ventanas de la arquitecta: Ítem, Código, Descripción, Cantidad, Hojas, Ubicación, Apertura, Medidas en plano, Material y Observaciones.

| Columna | Quién la llena | Cómo se obtiene | Probado en el modelo |
|---|---|---|---|
| **Ítem** | Add-in | Número consecutivo de la fila. | — |
| **Código (P1, V1…)** | Add-in, automático | Parámetro estándar **"Marca de tipo"** del tipo. Ojo: en las plantillas por defecto de Revit la etiqueta de puerta muestra **"Marca"** (de ejemplar: 1, 2, 3…) y la de ventana "Marca de tipo"; hay que confirmar cuál usan las etiquetas de la empresa. | ✅ P1, P2, V1, V2 |
| **Descripción** (corta) | Add-in, automático | Parámetro estándar **"Descripción"** del tipo; si está vacío, el nombre del tipo. | ✅ "Ventana batiente de 2 hojas, 1,20 x 1,80" |
| **Cantidad** | Add-in, automático | Se agrupan los elementos por tipo y se cuentan. Unidad: **und**. | ✅ |
| **Ubicación: piso** | Add-in, automático | El nivel del elemento. El add-in ya lo extrae. | ✅ "N+0.00 Piso 1" |
| **Ubicación: lugar en el piso** | Add-in, automático | **Habitaciones** de Revit: la habitación de cada lado (`FromRoom`/`ToRoom`), en puertas y ventanas. En fachada un lado queda vacío (exterior). | ✅ Puerta interior: Sala 1 → Alcoba 1 2. Ventanas y balconera: exterior → Alcoba 1 2 / Sala 1 |
| **Medidas en plano** | Add-in, automático | Parámetros estándar **Anchura** y **Altura**. Se lee primero el ejemplar y luego el tipo. Revit los da en pies; el add-in los convierte a metros y arma el campo "1,20 × 1,80". | ✅ Están en el tipo |
| **Apertura** | Add-in, manual | Lista desplegable en la tabla de la actividad. Sacarlo de Revit no es confiable: la regla de `ToRoom` solo sirve en puertas exteriores, `HandFlipped`/`FacingFlipped` son relativos a cómo se dibujó la familia, y doble sentido o corrediza no tienen parámetro estándar. | Algunas familias traen "Operación" (la abatible: "BatienteDerecha"), pero la balconera no |
| **Material** | Add-in, automático | Los parámetros de material del tipo ("Material de estructura", "Material de cristalera", "Panel", "Vidrio"…). Se listan los nombres sin repetir, por ejemplo "Aluminio + Vidrio", como en el Excel. Si ninguno tiene material asignado, se usan los materiales de la geometría (`GetMaterialIds`). | ⚠️ Hoy saldría vacío: en las familias del modelo todos los materiales están en "Por categoría", y la puerta solo devuelve "Tirador". El modelador tiene que asignar el material en cada tipo |
| **Observaciones** | Interfaz, manual | No va en el add-in. | — |
| **Hojas** | Add-in, manual (por ahora) | Texto en la tabla de la actividad. Partes que se mueven (abren, giran o corren); las que no se mueven son "fijos". Ejemplos: "1" es una puerta de una hoja, "3 + fijo" son 3 hojas y 1 paño fijo, "2 + 2 fijos" son 2 hojas y 2 paños fijos. No hay parámetro estándar en Revit. Más adelante puede ser automático con un parámetro de texto "Hojas" en cada tipo. | Solo aparece en el nombre de algunas familias ("2 hojas"), y nunca indica los fijos |
| **Descripción detallada** | Interfaz | "Suministro e instalación de…" es la descripción detallada de la actividad del presupuesto, no del cuadro de cantidades. | — |

Sobre la ubicación: Carlos propuso **Áreas**, pero lo que pidió ("esta ventana hace parte de la alcoba 1, de la sala…") es justo lo que dan las **Habitaciones**, con un dato directo de la API. Con Áreas habría que calcular la posición con geometría, que es más frágil.

## Resumen para Carlos

- **Automático desde Revit (add-in):** código, descripción corta, cantidad, piso, lugar en el piso, medidas en plano y material.
- **Condiciones para el modelador:** llenar "Marca de tipo" y "Descripción" en cada tipo, asignar los materiales del tipo (no dejarlos en "Por categoría") y modelar Habitaciones con nombre.
- **Manual en el add-in:** hojas (texto) y apertura (lista desplegable).
- **Manual en la interfaz:** descripción detallada y observaciones. Nada más se llena en la interfaz.

## Estado actual del add-in

Puertas y Ventanas ya aparecen en la lista, porque las dos categorías tienen cantidades de material. Pero hoy usan la tabla genérica, que muestra columnas sin sentido para estos elementos:

- No sale la **anchura**: la lista de dimensiones (`MainWindowViewModel.cs:1184`) busca "Ancho" y "Base", no "Anchura".
- **Largo / Length / Longitud** copian la altura, porque el elemento no tiene longitud (`MainWindowViewModel.cs:1240`).
- **Espesor / Grosor** salen de dividir volumen entre área (`MainWindowViewModel.cs:1205`).
- **Área** es la superficie de toda la geometría de la familia (3,89 m² en una ventana de 1,20 × 1,80), no el vano.
- El nivel salía "Sin Nivel" al volver de la API: corregido con `ElementDataJsonReader`.

## Cambios necesarios en el add-in

1. Tabla propia para Puertas y Ventanas, como la de muros o zapatas: Código, Descripción, Cantidad, Nivel, Lugar en el piso, Medidas en plano y Material.
2. Extraer "Marca de tipo", "Descripción", Anchura × Altura, las habitaciones de cada lado y los materiales del tipo, y enviarlos en el JSON (tarea 7).
3. Unidad de medida de las actividades de puertas y ventanas: **UND**. Cada elemento cuenta 1 y el total de la actividad es la suma.

## Mockup de las tablas

Datos tomados del modelo de prueba. Material, hojas y apertura son de ejemplo.

### 1. Add-in: tabla de elementos de una actividad

Igual que en muros o zapatas, una fila por elemento y una fila de TOTAL. Las columnas marcadas con ✎ (HOJAS y APERTURA) se llenan a mano en el add-in; las demás salen de Revit. Al llenarlas en una fila se copian a las demás filas del mismo código, porque son datos del tipo.

**Actividad:** Ventana batiente 2 hojas 1,20 × 1,80 — **Unidad: UND**

| No. | CÓDIGO | DESCRIPCIÓN | CANTIDAD | NIVEL | LUGAR | MEDIDAS | MATERIAL | HOJAS ✎ | APERTURA ✎ |
|---|---|---|---|---|---|---|---|---|---|
| 1 | V1 | Ventana batiente de 2 hojas, 1,20 x 1,80 | 1 | N+0.00 Piso 1 | Alcoba 1 2 (fachada) | 1,20 × 1,80 | Aluminio + Vidrio | 2 | ▾ Batiente |
| 2 | V1 | Ventana batiente de 2 hojas, 1,20 x 1,80 | 1 | N+0.00 Piso 1 | Alcoba 1 2 (fachada) | 1,20 × 1,80 | Aluminio + Vidrio | 2 | ▾ Batiente |
| **TOTAL** | | | **2** | | | | | | |

**Actividad:** Puerta batiente madera 0,90 × 2,10 — **Unidad: UND**

| No. | CÓDIGO | DESCRIPCIÓN | CANTIDAD | NIVEL | LUGAR | MEDIDAS | MATERIAL | HOJAS ✎ | APERTURA ✎ |
|---|---|---|---|---|---|---|---|---|---|
| 1 | P1 | Puerta batiente de madera, 1 hoja, 0,90 x 2,10 | 1 | N+0.00 Piso 1 | Sala 1 / Alcoba 1 2 | 0,90 × 2,10 | Madera | 1 | ▾ Hacia el interior |
| **TOTAL** | | | **1** | | | | | | |

Cómo se arma LUGAR con las habitaciones de cada lado:

- Habitación a los dos lados (puerta interior): `Sala 1 / Alcoba 1 2`.
- Habitación a un solo lado (fachada): `Alcoba 1 2 (fachada)`.
- Sin habitación en ningún lado: `Sin habitación`. Es el aviso de que falta modelarla.

### 2. Interfaz: cuadro de puertas y ventanas

Así quedaría el cuadro, con el formato del Excel de la arquitecta: agrupado por piso y con una fila por tipo. Solo Observaciones (✎) se llena a mano en la interfaz; las demás columnas llegan del add-in.

**PUERTAS PRIMER PISO**

| Ítem | Código | Descripción | Cantidad (UND) | Hojas | Ubicación | Apertura | Medidas en plano | Material | Observaciones ✎ |
|---|---|---|---|---|---|---|---|---|---|
| 1 | P1 | Puerta batiente de madera, 1 hoja, 0,90 x 2,10 | 1 | 1 | Primer piso (Sala 1 / Alcoba 1 2) | Hacia el interior | 0,90 × 2,10 | Madera | |
| 2 | P2 | Balconera corredera de 2 hojas, 1,60 x 2,10 | 1 | 2 | Primer piso (Sala 1, fachada) | Corrediza | 1,60 × 2,10 | Aluminio + Vidrio | |

**VENTANAS PRIMER PISO**

| Ítem | Código | Descripción | Cantidad (UND) | Hojas | Ubicación | Apertura | Medidas en plano | Material | Observaciones ✎ |
|---|---|---|---|---|---|---|---|---|---|
| 1 | V1 | Ventana batiente de 2 hojas, 1,20 x 1,80 | 2 | 2 | Primer piso (Alcoba 1 2, fachada) | Batiente | 1,20 × 1,80 | Aluminio + Vidrio | |
| 2 | V2 | Ventana batiente de 2 hojas, 1,50 x 1,30 | 1 | 2 | Primer piso (Sala 1, fachada) | Batiente | 1,50 × 1,30 | Aluminio + Vidrio | |

Notas del mockup:

- **Cantidad** es el total de UND de ese tipo en ese piso. Si un tipo está en varios pisos, sale una fila en cada grupo de piso.
- **Ubicación** junta el piso (del nivel) con los lugares de todos los elementos de ese tipo en ese piso, sin repetir.
- **Apertura** es una lista desplegable (▾) en el add-in. Opciones propuestas, por confirmar con la arquitecta: hacia el interior, hacia el exterior, vaivén, corrediza, plegable, batiente, proyectante y fija.
- La **descripción detallada** ("Suministro e instalación de…") no está en el cuadro: es de la actividad del presupuesto y se ve aparte en la interfaz.

## Aviso: el campo "Comentarios" ya está ocupado

El add-in escribe el código de la actividad en el parámetro **"Comentarios"** de cada elemento asignado (`ElementAvailabilityService.cs:222` y `:276`) y lo borra al desasignarlo. Ningún dato de puertas y ventanas debe ir en ese parámetro.

## Falta verificar

- Qué marca muestran las etiquetas de puerta de la empresa ("Marca" o "Marca de tipo").
- Cómo se comportan `FromRoom`/`ToRoom` con las fases del proyecto. El modelo de prueba solo tiene "Nueva construcción".
- Fases en general: el add-in no filtra por fase, así que las puertas demolidas o existentes también se cuentan. Afecta a todas las categorías, pero pesa más en remodelaciones.
