# Retest: nueva metodología de traslapos

Prueba final en Revit real, hecha por Luciano el 19-oct-2026, de lo
implementado según `2026-10-17-diseno-metodologia-traslapos.md` y
`2026-10-17-plan-metodologia-traslapos.md`.

## Modelo de prueba

Pila de referencia: dado 781598 + columnas 782104 y 782735, con zapata
780989. Varilla de Z -0,925 a 7,00 m, barra #4, sección común 30×30 cm,
barra de 12 m y redondeo 0,50 m.

Lo que calculó la pestaña «Traslapos», y lo que generó Revit:

| Tipo | Pieza de abajo | Traslapo | Pieza de arriba |
|---|---|---|---|
| A | 3,50 m | 1,131 m centrado en Z 1,788 | 6,00 m |
| B | 7,00 m | 1,131 m centrado en Z 5,288 | 2,50 m |

El traslapo queda en 1,131 m (mínimo de la #4: 0,75 m) porque las dos
piezas tienen que sumar múltiplos de 0,50: 8,369 m de varilla + el traslapo
≥ 9,119 → 9,50 m.

## Resultado

| # | Qué | Resultado |
|---|---|---|
| 1 | Diagnóstico (casos 1 a 5) | OK. Ver `2026-10-17-diagnostico-traslapos-variables.md`. |
| 2–3 | Largo de cada pieza en Propiedades contra la pestaña | OK. Una medición inicial de 5,78 m en vez de 6,00 era el tramo recto medido a mano, sin el gancho; Propiedades da 6,00. |
| 4 | Ningún traslapo dentro de una viga | OK |
| 5 | Columna sin solución: no genera y explica | OK |
| 6 | Configuración vieja: se migra a A/B con aviso | OK |
| 7 | Regenerar no deja piezas viejas | OK |
| 8 | Sin "completely outside of its host" en barras nuevas | OK (el aviso del elemento 787398 es un armado de área previo del modelo). |

Una primera alarma ("se generó un solo lado, con todos los traslapos a la
misma altura y sin estribos") eran las barras de prueba del comando de
diagnóstico, que las arma en una fila sobre una cara; no había habido
generación (no existía el registro de la corrida).

## Ajustes hechos después del retest

- `GanchoLongitudinal`: tolerancia de 5 mm por punta en vez de 1 mm. El aviso
  "el gancho «CQ Gancho 90°» no quedó exactamente con el largo del cuadro"
  salía siempre porque `TotalLength` está redondeado al centímetro.
- Se borró `ComandoDiagnosticoTraslaposVariables` y su botón.

## Fuera de alcance, visto en el retest

El aviso "El gancho «Estribo/Tirante - 135°.» mide 7,5 cm con la barra #3 y
el cuadro pide 11,3 cm" es de los **estribos**, que siguen usando el gancho
del modelo. Lo resuelve el #64 (formas y ganchos propios para estribos, cajas
y grapas), diseñado en `2026-10-09-diseno-formas-ganchos-propios-64.md` y
todavía sin implementar.

## Pendiente

- Desplegar con el ajuste de tolerancia y confirmar que el aviso del gancho
  ya no aparece.
