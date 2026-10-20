# Diagnóstico: traslapos de largos distintos en una misma varilla

Medido el 17-oct-2026 con `ComandoDiagnosticoTraslaposVariables` (temporal),
sobre la pila de referencia: dado 781598 + columnas 782104 y 782735. Es el
punto de control de la sección 10 de
`docs/planes/2026-10-17-diseno-metodologia-traslapos.md` (Task 1 del plan).

## Contexto

Con la nueva metodología, cada traslapo puede medir distinto (el solape del
cuadro es solo el mínimo y el redondeo de las varillas lo estira). Revit fija
el solape en el tipo de empalme, así que hacen falta varios tipos en una misma
varilla, y hasta ahora solo se había partido una varilla en una sola llamada a
`SpliceRebar` con un solo tipo.

## Qué se midió

Barra #4, gancho «Estándar - 90°» en las dos puntas, varilla de Z -0,575 a
7,00 m, traslapos centrados en 0,925 m y 3,925 m.

1. Dos largos (0,75 y 0,95 m), una llamada a `SpliceRebar` por traslapo, de
   abajo hacia arriba, cada una sobre la pieza de más arriba de la anterior.
2. Igual, con los largos al revés (0,95 y 0,75 m).
3. Control: el mismo largo (0,75 m) en una sola llamada, como hoy.
4. Largo de corte de la varilla entera, sin traslapos.

## Resultado

| Caso | Piezas en la cadena | Traslapo 1 (medido / pedido) | Traslapo 2 (medido / pedido) |
|---|---|---|---|
| 1 | 3 de 3 | 0,75 / 0,75 m, centro 0,925 / 0,925 | 0,95 / 0,95 m, centro 3,925 / 3,925 |
| 2 | 3 de 3 | 0,95 / 0,95 m, centro 0,925 / 0,925 | 0,75 / 0,75 m, centro 3,925 / 3,925 |
| 3 | 3 de 3 | 0,75 / 0,75 m | 0,75 / 0,75 m |

- Cada llamada devolvió 2 ids (las dos piezas nuevas) y la pieza partida dejó
  de existir: la pieza siguiente a partir es la de más arriba de esas dos.
- `SetHostId` a la columna que contiene cada pieza: la cadena siguió con 3
  piezas en todos los casos.
- Corrimiento lateral: la pieza del medio quedó 1,3 cm corrida en +Y (normal
  BasisY), como ya se había medido el 7-oct.
- Revit no dio ningún aviso al confirmar.
- **Largo de corte (caso 4):** `TotalLength` 7,95 m y `REBAR_ELEM_LENGTH`
  7,951 m, contra 8,019 m calculados con recta (7,575 m) + 2 × 0,222 m
  (`Gancho90` del cuadro). El gancho «Estándar - 90°» del modelo agrega
  0,188 m por punta: 3,4 cm menos que el cuadro.

## Conclusión

1. ¿Los casos 1 y 2 dejan 3 piezas en una sola cadena? **Sí.**
2. ¿Cada traslapo mide lo pedido y está centrado donde se pidió? **Sí**, exacto.
3. ¿El largo de corte de Revit coincide con recta + 2 × `Gancho90`? **No**:
   6,8 cm menos en la varilla con dos ganchos (3,4 cm por gancho).
4. ¿`SetHostId` deja la cadena intacta? **Sí.**
5. ¿Avisos de Revit? **Ninguno.**

El método de partido de Task 6 queda validado. El punto 3 queda para decidir
con Luciano antes de Task 6 (ver la decisión más abajo).

## Decisión sobre el largo del gancho

Luciano, 17-oct: **que Revit use el largo del cuadro.** Como «Estándar - 90°»
es del modelo y lo usan otras barras (vigas, por ejemplo), no se toca: la
longitudinal pasa a usar un gancho propio, «CQ Gancho 90°» (estilo estándar,
90°), que el add-in crea si falta. En cada corrida, por cada barra que se use,
se mide con una barra temporal de 1 m cuánto suma cada gancho al largo de
corte y se ajusta su largo (`SetAutoCalcHookLengths(false)` +
`SetHookLength`, el mismo método que el #64) hasta que sume el `Gancho90` del
cuadro. Si no se puede, se usa el gancho del modelo, con aviso.

## Caso 5: el gancho propio, medido (19-oct)

Barra #4, varilla de Z -0,575 a 7 m (recta 7,575 m):

- Ajuste: 0,170 → 0,225 → 0,220 m por punta (el cuadro pide 0,222).
- Varilla entera con «CQ Gancho 90°»: `TotalLength` 8,020 m contra 8,019 m
  esperados. Queda bien.
- `TotalLength` sale **redondeado al centímetro** (redondeo de armaduras del
  proyecto; en el caso 4 da 7,95 mientras `REBAR_ELEM_LENGTH` da 7,951), así
  que la medición de una punta no distingue menos de 5 mm. Con la tolerancia
  original de 1 mm el ajuste saltaba entre 0,220 y 0,225 y avisaba siempre
  "no quedó exactamente con el largo del cuadro". **Fix:** tolerancia de
  5 mm por punta (`GanchoLongitudinal.ToleranciaMetros`), que es la
  resolución real de la medición.

El comando de diagnóstico se borró al cerrar la prueba final (ver
`2026-10-19-retest-metodologia-traslapos.md`).
