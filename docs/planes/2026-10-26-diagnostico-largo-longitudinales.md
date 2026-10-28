# Diagnóstico: las barras longitudinales salen unos cm más largas (#177)

## Contexto

En la reunión de revisión del 24-10 se vio que las barras longitudinales
que genera el add-in salen "unos centímetros" más largas de lo que
corresponde. Es el primer punto de #174 que se ataca porque es un error en
el acero real (afecta cantidades y planos).

Todavía no se sabe cuánto sobra ni en qué barras. Por la regla del repo
(medir contra Revit real antes de arreglar), primero se mide.

## Cómo se arma hoy el largo

- Cada barra se crea entera, de abajo a arriba, como una línea vertical
  sobre su eje, y después Revit la corta en cada traslapo
  (`addins/armado-columnas/Logica/BarraContinua.cs`, `CrearEntera` y `Partir`).
- Abajo arranca 7,5 cm sobre el fondo de la zapata; arriba termina justo
  en la cara superior de la columna, **sin recubrimiento**
  (`addins/armado-columnas/Logica/LectorContexto.cs`).
- Si hay zapata, lleva ganchos «CQ Gancho 90°» en las puntas, calibrados
  para que cada uno sume el largo de la tabla (tolerancia 5 mm por punta,
  `addins/armado-columnas/Logica/GanchoLongitudinal.cs`).
- La pestaña «Traslapos» calcula el largo de corte de cada pieza en
  `addins/armado-columnas/Modelo/PlanTraslapos.cs`; ese número no se le pasa a Revit: Revit lo
  obtiene solo al cortar en los traslapos.

## Sospechosos

1. Arriba no hay recubrimiento: la barra (y el doblez del gancho) llega a
   la cara superior de la columna o se pasa.
2. El redondeo de largos del proyecto (Configuración de armadura de
   Revit) puede redondear hacia arriba; el add-in nunca lo mira.
3. La calibración del gancho acepta hasta ~1 cm de error por barra.
4. "Con ganchos" se decide distinto en la pestaña (zapata **o**
   `ZInferiorLongitudinal < ZInferior`) y en el generador (solo zapata);
   también está en #109.

## Cómo midió el equipo (27-10)

Varias barras. Midieron desde la punta de la barra hasta donde termina el
gancho, con las medidas del dibujo, y lo compararon con el largo de corte
de la pestaña, con la propiedad de Revit y con la tabla de ganchos y
traslapos. En los tres casos salía más largo.

Hipótesis principal: en el dibujo cada tramo se acota hasta el borde
exterior del doblez, así que la esquina se cuenta dos veces. El largo de
corte, en cambio, va por el eje y sigue la curva. Para una #4 con doblez
normal, la cuenta da unos 3 cm de más por gancho. Si es así, la barra
está bien y la diferencia es solo la forma de medir.

Respuesta (27-10): el gancho de la tabla (por ejemplo, 22,2 cm para la #4)
es la **pata que se ve acotada en el plano**, no lo que suma al largo de
corte. Hoy el add-in lo toma como lo que suma al largo de corte
(`GanchoLongitudinal` calibra el gancho para que la barra crezca
exactamente eso), así que Revit alarga la pata y la barra queda más
larga. Probablemente tiene que ver con #138 ("el gancho de la base parece
demasiado largo").

**Precedente en vigas:** el commit `835497b` (#943, 26-10) corrigió
este mismo error en el add-in de vigas. Midieron que cada gancho sumaba
la B entera del cuadro y Revit alargaba la cola (#5 a 90°: cola de 256
en vez de 191). La corrección (`Reglas.LargoGancho`): el gancho suma su
arco por el eje más la cola E del cuadro, menos el radio y medio
diámetro que el tramo recto ya cubre hasta la cara exterior del doblez.
Revisar si se puede reusar esa regla en columnas.

## Medición en Revit real (28-10)

Columna con zapata (780869), 8 barras #4 CQEING, Z de -0,925 a 7,00,
redondeo de la pestaña 0,50 m. Registro
`%TEMP%\872c7d1b-...\practicacq-longitudinal.log`.

- **El largo de corte coincide al milímetro.** Pestaña 3,5 / 6 / 7 /
  2,5; Revit, igual (`REBAR_ELEM_LENGTH` 3,5014 / 6,0014 / 7,0014 /
  2,5014).
- **El redondeo del proyecto no influye.** Está activo, al 10 mm más
  cercano, y los largos ya caen en múltiplos de 0,50.
- **La pata del gancho sale más larga.** Por fuera mide 0,2552 m en
  todas las barras; la tabla dice 0,222. Son 3,3 cm de más por gancho.
  Luciano la acotó a mano en 0,2424 por dentro, que da 0,255 por fuera:
  coincide. La calibración dio 0,2250 m de aporte por punta, contra
  0,222 pedidos.
- **Arriba la barra llega a la cara superior de la columna** (eje en
  7,00, gancho hasta 6,994), sin recubrimiento. Es otro tema, no el de
  los cm de más.

Conclusión: los cm de más están en la pata del gancho, no en el largo
de corte ni en el redondeo.

**A definir antes de arreglar:** vigas usa el mismo cuadro (#4 a 90°:
B = 222, cola E = 152, doblado 76) y el 26-10 decidió que lo que se ve
en la pata es la **cola E**, no la B. Para columnas, Luciano dijo que
22,2 es la pata del plano. Hay que confirmar con Carlos/Juan qué medida
tiene que verse en la pata, para que vigas y columnas den lo mismo.

## Fix implementado (28-10) -- decisión: la pata mide B

Luciano: "por ahora dejémoslo con B". La pata del gancho, medida por
fuera del doblez, tiene que dar el `Gancho90` del cuadro (22,2 cm en la
#4).

- `CuadroVarillas.Varilla.AporteGancho90` (nuevo): lo que el gancho suma
  al largo de corte para que la pata mida B. Usa la misma regla que
  `Reglas.LargoGancho` de vigas, con B en lugar de la cola E: arco por el
  eje + cola − radio − medio diámetro. En la #4 da 0,1903 en vez de
  0,222.
- `PlanTraslapos`: la pestaña calcula los largos de corte con ese aporte.
- `GanchoLongitudinal`: calibra «CQ Gancho 90°» para que sume ese aporte.
  Mide con `REBAR_ELEM_LENGTH` (sin redondear) en vez de `TotalLength`,
  así que la tolerancia baja de 5 mm a 1 mm por punta.
- `Lindero.AlcanceGancho90`: la punta llega a B + medio diámetro (antes
  B + 2,5 diámetros, medido con la pata larga).
- Prueba nueva `aporte_gancho_90_pata_b` en `tools/PruebasTraslapos`.
  Todas en verde.

Ojo: el largo del gancho se guarda en el tipo de barra del modelo, así
que las barras ya generadas con ese tipo también cambian su pata al
recalibrar.

**Retest pendiente:** generar la misma columna, acotar la pata (por
fuera debe dar 22,2 en la #4) y mirar el registro: la línea del gancho
tiene que sumar ~0,190 y la "pata exterior del gancho", ~0,222. Los
largos de corte de las piezas con gancho bajan unos 3 cm y tienen que
seguir coincidiendo con la pestaña.

## Retest (28-10, Luciano) -- OK

Misma columna con zapata, 16 piezas #4 CQEING.

- **Pata del gancho:** Luciano la acotó en 0,2087 por la cara interior,
  que da 0,2214 por fuera (cuadro: 0,222). El diagnóstico de Rebar sobre
  la barra 814663 da tramo recto + arco r = 4,45 cm + pata recta 17,12 cm:
  17,12 + 4,45 + 0,635 = 22,2 cm por fuera. El registro da "pata exterior
  del gancho 0,222" en las 16 piezas. **OK.**
- **Calibración:** el primer intento dio 0,2234 y el segundo 0,1903, lo
  pedido. **OK.**
- **Largos de corte:** la pestaña da 3,5 / 6 / 7 / 2,5 y Revit
  3,5003 / 6,0003 / 7,0003 / 2,5003 (antes, 1,4 mm de más). **OK.**
- El gancho ya no se sale de la columna (Luciano).

## Qué se agregó para medir (temporal, solo Debug)

El registro `%TEMP%\practicacq-longitudinal.log` ahora anota:

- `REDONDEO del proyecto: ...` una vez por corrida (si está activo, de
  cuánto y con qué método).
- `PLAN grupo A/B Ø.. «barra»: ...` antes de cada barra: cada pieza de
  la pestaña con su largo de corte (las Z de la columna y si hay zapata
  ya salían en el registro).
- En cada línea de pieza (`creada`, `pieza`, `final`): el largo total que
  da Revit, `REBAR_ELEM_LENGTH` (sin redondear), la altura que ocupa con
  ganchos y radios, la pata exterior del gancho (lo que se ve acotado
  en un plano) y el redondeo que se aplica a esa barra.

Solo API pública y documentada (`Rebar.TotalLength`,
`Rebar.GetReinforcementRoundingManager`,
`ReinforcementSettings.GetRebarRoundingManager`, `RebarRoundingManager`,
todas desde Revit 2014). No cambia el acero que se genera.

## Pendiente

- [x] Cómo midió el equipo (ver arriba).
- [x] El gancho de la tabla es la pata acotada en el plano (no lo que suma al
      largo de corte). Precedente en vigas: commit 835497b (#943).
- [x] Desplegado en Debug (27-10, desde `C:/Proyecto/addins-177`).
- [x] Generada una columna con zapata y leído el log (28-10). Falta una sin zapata.
- [x] Causa: la pata del gancho (+3,3 cm por gancho en la #4).
- [x] Decisión: por ahora, B (Luciano, 28-10). Vigas usa la cola E; queda por unificar con Carlos/Juan.
- [x] Arreglado con la regla de vigas (ver "Fix implementado").
- [x] Retest en Revit (28-10): OK.
- [ ] Unificar con vigas (B o cola E) con Carlos/Juan.
- [ ] Recubrimiento arriba: la barra llega a la cara superior de la columna (tema aparte).
- [ ] Sacar el diagnóstico temporal cuando quede resuelto.
