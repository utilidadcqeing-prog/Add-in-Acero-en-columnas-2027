# Análisis: reunión 8-sep (traslapos, ganchos, normativa) — issues creados

## Contexto

Reunión del 8-sep-2026 (`Meeting Transcription.pdf`, William, Luciano, Carlos Quintero) con dos partes:

1. William narró en detalle, sobre el script Dynamo de referencia, la lógica completa de traslapos (alternancia T1/T2 par/impar), longitud máxima de barra (6m incluyendo gancho), y orientación de ganchos de estribo.
2. Carlos Quintero se unió y reforzó dos reglas normativas colombianas: ganchos inferiores de barras longitudinales siempre hacia adentro (nunca afuera), y que el gancho puede superar el recubrimiento externo con diámetros grandes (hay que revisarlo).
3. Luciano le mostró en vivo a Carlos el estado actual del add-in — varias de las cosas mostradas ya estaban implementadas en esta misma sesión de trabajo (ver `docs/planes/2026-09-09-selector-visual-columnas-y-longitudinal-continua.md`).

## Qué se contrastó contra el código real (no contra los issues)

- **Alternancia T1/T2, longitud comercial, traslapo intercalado por nivel par/impar (voraz: cada barra llega lo más lejos posible antes de traslapar)**: ya implementado (`GeneradorLongitudinal.PlanificarCortes`/`PlanificarSplicesReales`, Fase 5/#7) y coincide con lo narrado por William.
  - **Corrección (ver `docs/planes/2026-09-09-diagnostico-corrimiento-traslapo.md`)**: la primera pasada de este análisis dio por buena la geometría del traslapo sin verificarla contra el modelo real, y estaba incompleta -- generaba el solape en Z pero con la MISMA X/Y en ambos lados, así que el traslapo quedaba invisible (dos barras exactamente coincidentes). El usuario lo detectó probando contra el modelo de William ("se veía todo recto sin traslapos") y midiendo coordenadas reales se confirmó que las barras se corren lateralmente (~1 diámetro de barra) en el punto de traslapo. Ya corregido, ver el otro documento.
- **Traslapo no debe caer en encuentro zapata/vigueta, ni dentro de un nudo de viga**: ya excluido (`niveles` en `GeneradorLongitudinal` filtra `TipoTramo.DentroDeViga`; viguetas no generan tramo tipo nudo, `LectorContexto` ya las descarta por umbral de ancho). Confirmación, no acción nueva.
- **Estribos en C mirando al centro**: William lo consideró y **explícitamente lo descartó** ("mejor no hacerlo porque rompe la lógica de los bloques") — no es un pedido, no genera issue.
- **Gancho puede superar el recubrimiento con diámetro grande**: ya cubierto por `AvisarSiGanchoNoCumpleNorma` en `GeneradorArmado.cs` (aviso no bloqueante) — pero hoy solo aplica a estribos, no a longitudinal (que no tiene gancho en absoluto, ver siguiente punto).
- **Gancho de anclaje en la longitudinal, hacia adentro, contado contra el límite de 6/12m**: **gap real, confirmado por el propio código** (`GeneradorLongitudinal.cs` documenta explícitamente "sin gancho, no hay campo de gancho propio para longitudinal") y por la demo en vivo de Luciano a Carlos ("la Zapata no está para doblar todavía"). → issue nuevo.
- **Sincronización de selección Revit→addin y selector visual de planta**: ambos ya implementados en esta sesión (ver el otro plan) y confirmados funcionando por Luciano en la misma reunión frente a Carlos.

## Issues creados

1. [#37](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/37) — Longitudinal: falta gancho de anclaje en la zapata (hacia adentro, normativa colombiana) y contarlo contra el límite de 6/12m. **Prioridad alta**: es un requisito normativo explícito de Carlos (ingeniero estructural), no una preferencia visual.
2. [#38](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/38) — Sincronización Revit→addin (documenta lo ya implementado esta sesión).
3. [#39](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/39) — Selector visual de columnas en planta (documenta lo ya implementado esta sesión).

Comentario en [#33](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/33) (agrupar columnetas) confirmando que quedó validado en vivo por Carlos — sugerido cerrarlo.

## Prioridad sugerida

1. **#37 (gancho de anclaje en zapata)** — requisito normativo explícito, sin esto el elemento de referencia que pidió Carlos ("que haya al menos un elemento armado completo... para confirmar que cumple la normativa colombiana") no puede aprobarse. Es el próximo bloqueante real.
2. **#38 y #39** — ya implementados y confirmados en vivo, solo falta el retest final propio + commitear/pushear.
3. Sin acción nueva: el resto de la reunión (T1/T2, traslapo en zapata/vigueta, gancho vs. recubrimiento, estribos en C) ya coincide con el código actual o fue explícitamente descartado.
