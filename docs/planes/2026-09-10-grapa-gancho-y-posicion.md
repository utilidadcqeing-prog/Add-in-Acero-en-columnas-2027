# Grapas: el gancho de 180° salía enorme y al costado de la barra

## Contexto

Décima vuelta sobre el mismo gancho (las nueve anteriores están en
`docs/planes/2026-09-10-gancho-mas-chico-y-cajas-irresolubles.md`). El usuario
trajo un dibujo con dos grapas: arriba como las genera el addin, abajo como
deberían ser, y dos pedidos:

1. **La forma**: el gancho "se pasa" mucho. Pidió expresamente **mantener los
   180°** — es el ejemplo que dio la empresa, y si el modelo de referencia lo
   logra, tiene que ser posible.
2. **La posición**: el largo de la barra termina **en el eje de la barra
   longitudinal**, cuando la longitudinal debería quedar **en el medio del
   gancho**.

## Tres hallazgos que explican las nueve vueltas anteriores

### 1. La grapa se hacía con el tipo de barra LONGITUDINAL

`GenerarMenores` le pasaba a `CrearGrapa` la barra `barraCaraX`/`barraCaraY` —
el tipo de barra **longitudinal de cara** (`TipoBarraCaraX/YNombre`, típicamente
#5 o #6), no el del estribo (`ConfigTramo.TipoBarraNombre`, #3). Toda la
geometría del gancho (radio de doblado y extensión recta) es proporcional al
diámetro, así que una grapa hecha con la longitudinal tenía un gancho **del
doble de tamaño** que la del modelo de la empresa, que es **#3**. Lo mismo valía
para las cajas (`barraCaja = barraCaraX ?? barraCaraY`).

### 2. Nunca se había medido la geometría real del gancho

`ComandoDiagnosticoRebar` pedía las curvas con
`GetCenterlineCurves(false, /*suppressHooks*/ true, /*suppressBendRadius*/ true, …)`.
Con esos flags Revit devuelve **solo el tramo recto**: por eso todos los reportes
mostraban una única línea y ningún arco. Todas las conclusiones sobre el tamaño
del gancho salieron de `GetHookLength` — un número, no la geometría — y el
**radio de doblado**, que es justamente lo que hace bulto en un gancho de 180°,
no se miró nunca.

### 3. No hay precedente de grapas en el material de referencia

Se revisaron los dos add-ins de referencia y todos los `.dyn`/`.dyf`/`.txt` de
Dynamo: **cero** código de grapas/crossties, solo estribos cerrados y
longitudinales rectas. Lo que sí confirman dos veces, de forma independiente
(`Addin_Acero_Columnas/Services/RebarGenerationService.cs:140` y
`Dymanos de referencia/definitions/Lineas_Acero_Vigas_Rebar.dyf`, con
`-Rec-Re-(Re+Rb)`): el **eje de la longitudinal** está a
`recubrimiento + Ø_estribo + Ø_longitudinal/2` de la cara de hormigón — que es
exactamente donde la grapa terminaba su tramo recto.

## Fix implementado

### `Logica/GeneradorLongitudinal.cs`

**Grapas y cajas con la barra del estribo.** `GenerarMenores` resuelve
`barraEstriboTramo` desde `cfgTramo.TipoBarraNombre` y la usa para grapas y
cajas. La barra longitudinal de cara queda solo como respaldo
(`barraMenoresRespaldo`), con aviso si se llega a usar. Efecto lateral esperado
y deseado: con una barra más delgada, las cajas angostas que Revit rechazaba
("no se puede doblar una #3 en 3,7 cm") tienen más margen.

**El extremo apunta al eje de la longitudinal.** Desapareció `InsetDeGrapa`, que
usaba el diámetro de la propia grapa. `GenerarMenores` ahora recibe `insetX` e
`insetY` — los mismos valores con los que se colocaron las barras
longitudinales de cara — y los usa como objetivo de cada punta.

**La longitudinal, en el medio del gancho (`MedirCorreccionDeGrapa`).** Un
gancho de 180° es un semicírculo: su "medio" es el **centro del arco**, y el
punto que coincidía con la longitudinal era la **punta del tramo recto**, o sea
el punto de tangencia. Por eso el gancho enroscaba al costado de la barra en vez
de abrazarla.

La corrección **no se calcula con una fórmula** — ese fue el error repetido
nueve veces. Se **mide contra Revit**: se crea una grapa de prueba con la línea
cruda, se le leen las curvas con `GetCenterlineCurves(false, false, false, …)`
(los flags que el diagnóstico nunca había usado), se toma el **centro del arco**
del gancho y se compara con el eje de la longitudinal. La grapa de prueba se
borra. La diferencia se guarda en el marco local de la grapa (`Axial` a lo largo
de la barra, `Lateral` hacia el centro de la sección) y se aplica en
`AplicarCorreccion`: los dos extremos se corren simétricamente a lo largo y la
línea entera se corre de costado. Se mide **una sola vez por tramo**, porque
todas las grapas del tramo comparten barra y gancho. El número medido sale en
los detalles del reporte.

**El gancho de 180° gira hacia AFUERA.** Consecuencia directa de lo anterior, y
es lo contrario de la regla del 10-sep: como el centro del semicírculo es la
barra longitudinal, el tramo recto de la grapa queda a un radio de ese eje **del
lado contrario al que gira el gancho**. Girando hacia el centro, el tramo recto
se iría hacia afuera, contra el recubrimiento. Girando hacia afuera, la grapa
queda dentro del núcleo y el gancho abraza la barra desde el lado de la cara.
Para un gancho de **135°** sigue valiendo la regla vieja (la cola apoya en el
núcleo), así que la decisión se toma por ángulo dentro de `IntentarGrapa`.

**Aviso si no hay gancho de 180°.** `GeometriaSeccion.BuscarGanchoGrapa` devuelve
`null` si el documento no tiene uno de 180° con estilo `Standard`, y la grapa caía
al de 135° del estribo **en silencio**. Ahora avisa.

`IntentarGrapa` pasó de devolver `bool` a devolver el `Rebar?` creado, que es lo
que necesita la medición.

### `ComandoDiagnosticoRebar.cs`

- Dos pasadas de geometría por posición: la de siempre (sin ganchos, comparable
  con los reportes anteriores) y una nueva **con ganchos y radios reales**.
- Las curvas salen **tipadas**: `RECTA` con su largo, `ARCO` con centro, radio,
  ángulo barrido y normal del plano. El centro del arco es el dato que decide
  dónde tiene que caer la longitudinal; la normal delata un gancho fuera de
  plano.
- Del `RebarBarType`: diámetro nominal y de modelo, los tres **diámetros de
  doblado** (`StandardBendDiameter`, `StandardHookBendDiameter`,
  `StirrupTieBendDiameter`) y un volcado de todos sus parámetros, más la forma y
  el estilo de la barra.
- `Rebar.GetHookRotationAngle` **no existe** en la API de 2027 (se intentó y no
  compila); el giro fuera de plano se lee igual en la normal del arco.

## Estado

Compilado (0 errores) y desplegado a Revit 2027.

## Verificación pendiente (retest del usuario)

1. **Medir el modelo de la empresa primero**: correr "Diagnóstico de Rebar"
   sobre una grapa del modelo de referencia. El reporte ahora trae los arcos del
   gancho (centro, radio) y el `StandardHookBendDiameter` de esa barra #3. Ese es
   el número contra el cual comparar todo lo demás.
2. Generar el armado y correr el mismo diagnóstico sobre una grapa generada:
   comparar **arco contra arco**.
3. En planta:
   - la grapa figura con el tipo de barra del **estribo** (#3), no con la
     longitudinal;
   - el gancho **abraza** la longitudinal (la barra queda dentro del
     semicírculo, no al costado);
   - el tramo recto de la grapa queda **dentro** del hormigón, corrido un radio
     hacia el núcleo respecto de la fila de longitudinales — eso es lo esperado,
     no un error;
   - probar en una columna chica **y** en una de 40×40.
4. En el reporte tiene que aparecer el detalle "gancho de grapa medido en
   Revit — radio … cm; la línea se corrige … cm". Si ese detalle **no** aparece,
   la medición falló y las grapas salieron sin corregir (habrá un aviso
   diciéndolo).
5. Regresión: el estribo principal igual que antes; las cajas se siguen
   generando, y alguna caja angosta que antes fallaba puede pasar a generarse
   ahora que la barra es más delgada.

## Si el gancho todavía se ve grande

Con el diagnóstico nuevo quedan dos caminos sin salirse de los 180°, y esta vez
con números:

- **`StandardHookBendDiameter` del tipo de barra**: nunca se miró. Si el nuestro
  está inflado respecto del de la #3 de la empresa, ahí está el bulto. Es un tipo
  **compartido**: tocarlo cambiaría armaduras existentes, así que la salida sería
  un tipo de barra propio del addin, no mutar el del equipo.
- **El plano del gancho**: si la normal del arco no es vertical, el gancho no
  está en planta y hay que corregirlo.

Descartado y no se vuelve a intentar: achicar el largo por multiplicador o por
`RebarBarType.SetHookLength` (vueltas 7 a 9, Revit contesta "Can't solve Rebar
Shape"), y cambiar a 135°+90° (el usuario pidió 180°).

## Pendiente de Carlos (sigue abierto de la vuelta anterior)

- Si vale la regla de la NSR-10 de 135° en un extremo y 90° en el otro,
  alternando en altura, contra los 180° en las dos puntas del modelo de la
  empresa.
- Qué mide exactamente `SetHookLength`: el total con arco o solo la extensión
  recta.
