# Análisis: reunión 11-sep (interfaz del add-in) — issues

## Contexto

Reunión del 11-sep-2026, 95 minutos (William Ramos, Carlos Quintero, Juan Felipe,
José, Luciano). La parte del add-in va de **21:29 a 46:00**; el resto es el ERP
(unidades y APU, Juan Felipe) y el módulo de personal de obra (José), que **no
tocan el add-in**.

Luciano mostró en vivo lo implementado el 10-sep sin poder abrir Revit — la
licencia sigue trabada y Carlos la volvió a reportar (46:03). William aprobó
varias cosas y pidió un **cambio de fondo en el flujo**: menos clics, sin paso de
"aplicar".

Todo lo que sigue está contrastado contra el código real, no contra lo que se
recuerda de la reunión.

## Lo que William APROBÓ (no genera issue)

- **Configuraciones guardadas**: *"Esa sí está muy buena"* (39:25). Y de paso
  contestó la duda que Luciano planteó ahí mismo: **no hace falta compartirlas
  entre modelos** — *"es muy difícil que se aplique lo mismo en otro; da lo mismo
  hacerlo de nuevo"* (40:06). Queda confirmada la decisión del #54 de guardarlas
  dentro del modelo.
- **Reglas de cajas y grapas** de la ventana de estribos menores: Luciano recorrió
  los casos, incluida una configuración rara que William le pidió probar, y quedó
  aprobado (28:13–29:35). Lo que falta ahí es la **generación** de ganchos y
  cajas, que ya está hecha y sin desplegar.
- **Ganchos por defecto**: 135° para el estribo y 90° para la zapata
  (40:45–41:03). Ya implementado.
- **Barras X/Y por defecto 2 y 2** (41:47) y **longitud comercial 12 m** (42:01).

## Los issues

### P1 — El cambio de flujo

**[#56](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/56) — Sacar el paso de "aplicar": se genera lo que está configurado, marcado con un chulito**

William lo pidió tres veces, cada vez más explícito (35:09, 38:47, 39:00–39:23):

> "El tema de aplicar a la selección también desaparecería… y también el de
> limpiar selección… se supone que tiene que ser lo más automático, la menor
> cantidad de clics posible."

> "Este panel aquí de tramos… me sigue como que generando ruido, redundancia."

Desaparecen: el botón **"Aplicar a selección"** (con su flechita), **"Limpiar
selección"**, el **panel de tramos** y las **casillas de tildar**. Se selecciona
la columna en la planta, se configura, y **"Generar armado en Revit"** genera.

**El tilde no desaparece: deja de ser algo que se clickea** (idea de Luciano). Un
tramo queda marcado con un **chulito verde** —un icono, no una casilla— cuando
**está configurado**, y **lo marcado es lo que se genera**. Así se cumple lo de
"la menor cantidad de clics posible" sin perder el control de qué se arma: el
estado sale de lo que ya se hizo, no de un clic aparte.

Dónde se ve: en la **elevación**, que ya distingue con una trama los tramos que
tienen configuración propia. Ese concepto pasa a ser el criterio de generación y
se muestra como el chulito.

### Qué cuenta como "configurado"

Un tramo queda marcado cuando pasó **una** de estas dos cosas:

1. se apretó **"Aceptar"** en el panel de configuración de ese tramo;
2. le cayó un **"aplicar al resto de los tramos libres"** / **"al resto de los
   nudos"** (el issue siguiente).

Lo que **no** cuenta: haber navegado hasta el tramo con las flechitas, ni haber
tocado un campo sin aceptar.

**Cuidado, porque hoy no existe ese "Aceptar" y "tiene configuración" significa
"fue visitado"**: `GuardarPanelEnTramoActivo` escribe en `_configPorTramo` cada
vez que se activa un tramo — es lo que hace que no se pierda lo tecleado al
navegar. Usado tal cual como criterio, recorrer la columna con las flechitas
dejaría **todo** marcado.

Hacen falta **dos estados separados**: *lo que el panel tiene cargado* para ese
tramo (que se sigue guardando solo, para no perder nada) y *el tramo está
aceptado* (lo que decide el chulito y la generación). Más el botón "Aceptar" en
el panel, que hoy no existe — William lo nombró como parte del flujo: *"lo
configuras y te dice 'aplicar al resto de nudo', pan, aceptar"* (38:26).

Toca `ConstruirPlanArmado` (hoy genera solo los tramos tildados; pasa a generar
los configurados), `RedibujarElevacion` y buena parte de
`VentanaConfiguradorEstribos`. El acero **longitudinal** no entra en esto: es por
columna y va de punta a punta siempre.

**Esto revierte parte de lo del 10-sep.** El panel de tramos es el #48, que el
propio William había pedido el día anterior. No es un capricho: ahí el problema
era que planta y lista se tapaban entre sí, y ahora que la elevación con sus
flechitas convive con la planta, la lista de tramos quedó repetida.

**[#57](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/57) — Extender la configuración al resto de los tramos del mismo tipo, desde el tramo activo**

William lo describió de dos formas en dos minutos; decidido con Luciano que vale
la de 38:14:

> "Seleccionas un tramo libre, lo configuras uniforme o confinado, y está la
> opción 'aplicar al resto de tramos libres' y pan, se coloca. Seleccionas un
> nudo y te obligará sí o sí a uniforme… 'aplicar al resto de nudos'."

O sea: **contextual al tramo activo**, no un botón genérico. Reemplaza la flechita
con atajos por tipo que se hizo el 10-sep (#45): la misma idea, pero dentro del
panel del tramo y sin el paso de "aplicar" suelto.

**[#58](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/58) — Los nudos llevan estribos uniformes, nunca confinados**

> "Obliga… que en los nudos sea uniforme. No se permita meter confinamiento en el
> nudo" (37:50), y el espaciado se define ahí mismo (38:00–38:14).

Esto **responde el #55**, que había quedado abierto el 10-sep preguntando
justamente si los nudos debían llevar estribos: **sí, uniformes**.

Hoy `Logica/GeneradorArmado.cs:151-153` los saltea con un `return true` sin
avisar, así que es un cambio en el **generador**, no solo en la ventana. Del lado
de la interfaz: con un nudo activo, "Confinado" se deshabilita.

**[#59](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/59) — Gancho de 90° también en el extremo SUPERIOR de la longitudinal, hacia adentro**

> — "¿Y el de arriba también sería para adentro?" — "Para adentro, sí claro"
> (41:02–41:23).

Hoy solo se pone gancho en el extremo **inferior**, el de la zapata:
`BarTerminationsData` solo setea `HookTypeIdAtStart`, y el comentario del código
dice que el superior nunca lleva. Va junto con el #43 (que el gancho mire al
centro), porque es el mismo cálculo de orientación.

### P2

**[#60](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/60) — Valores por defecto: estribo #3, longitudinal #4**

> "Por defecto veo que siempre pone el número diez… que por defecto sea el número
> tres" (40:26). Y para cara y esquinas: "todo número cuatro por defecto" (41:58).

Hoy el default es `ListarTiposBarra().FirstOrDefault()` — el primero
alfabéticamente, que en el modelo de William es el #10. Hay que elegir **por
diámetro contra `EstandarCQ`**, no por nombre: los nombres varían entre modelos
("#3", "#3 COL", "3"…).

**[#61](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/61) — Sacar el checkbox "Borrar TODO el armado existente"**

> "Es muy buena, pero muy peligrosa" (43:35).

El motivo concreto (43:09–43:26): un acero de viga que quedó medio metido en la
columna figura en el sistema como si perteneciera a la columna, y el add-in lo
borraría. Y Revit ya avisa solo cuando hay duplicados.

**[#62](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/62) — Planta: sacar la pestaña "Lista" y etiquetar con la marca de ubicación del pilar**

> "Acá donde dice planta/lista… esa lista enorme también desaparecería, no la veo"
> (43:58). "Le pondrías unas etiquetas solamente de coordenada… las columnas toman
> coordenadas automáticas según la rejilla" (44:00–44:21).

Lo importante es que **no hay que calcularla**. William lo nombró (44:22–45:34):

> "Existe lo que se conoce como marca de ubicación del pilar… eso existe, no hay
> que ponerse a calcular, sino dejarlo, solamente obtenerlo."

Es `BuiltInParameter.COLUMN_LOCATION_MARK`: Revit la arma del cruce de rejillas
("A-1") y le agrega solo el desfase cuando la columna no cae justo en el cruce.
Hoy `LectorContexto.Marca` (`:709`) lee `ALL_MODEL_MARK`, que es otra cosa.

Sin rejillas no hay coordenada y no se dibuja etiqueta. **Verificar el
BuiltInParameter contra la documentación oficial** antes de usarlo (regla del
`CLAUDE.md`). Nota: las etiquetas se habían sacado el 10-sep porque se encimaban
(#48); "A-1" es corto, pero hay que cuidar el encimado igual.

### P3

**[#63](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/63) — Avisar cuando las columnas seleccionadas no tienen la misma estructura**

> "Sería bueno que se deje la misma para varias columnas, **siempre y cuando
> tengan la misma estructura**: el mismo nudo, los mismos tramos… Solo que una va
> a tener el traslapo a otra altura porque sea más larga o más corta"
> (34:18–34:58).

La selección múltiple de columnas **se queda**, pero configurar varias a la vez
solo tiene sentido si comparten cantidad de tramos y de nudos. Falta avisar
cuando no es así, en vez de aplicar en silencio.

## Issues creados

| Issue | Prioridad | Tema |
|---|---|---|
| [#56](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/56) | P1 | Sacar el paso de "aplicar": se genera lo configurado, marcado con un chulito |
| [#57](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/57) | P1 | Extender la configuración al resto de los tramos del mismo tipo |
| [#58](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/58) | P1 | Los nudos llevan estribos uniformes, nunca confinados (cierra el #55) |
| [#59](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/59) | P1 | Gancho de 90° también en el extremo superior, hacia adentro |
| [#60](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/60) | P2 | Valores por defecto: estribo #3, longitudinal #4 |
| [#61](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/61) | P2 | Sacar el checkbox "Borrar TODO el armado existente" |
| [#62](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/62) | P2 | Planta: sacar la pestaña "Lista" y etiquetar con la marca de ubicación del pilar |
| [#63](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/63) | P3 | Avisar cuando las columnas seleccionadas no tienen la misma estructura |

## Lo que esta reunión da vuelta o cierra

- **[#55](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/55)** — contestado: los nudos sí llevan estribos, uniformes. Se implementa en el #58.
- **[#45](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/45)** — superado: el botón "Aplicar a selección" desaparece (#56); la idea de aplicar por tipo sobrevive como acción contextual (#57).
- **[#48](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/48)** — parcialmente revertido: se van el panel de tramos y la pestaña "Lista"; se queda la planta siempre visible y la ventana más grande.
- **[#54](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/54)** — aprobado, y confirmado que **no** hace falta compartir configuraciones entre modelos.

## Fragmento descartado

En 21:04 aparece *"peso total de kilogramo del apu…"*. La transcripción ahí está
duplicada y mezclada con el hilo del ERP; Luciano confirmó que es ruido de la
transcripción, no un pedido del add-in.

## Estado

Documento de análisis. **No se tocó código en este trabajo.**
