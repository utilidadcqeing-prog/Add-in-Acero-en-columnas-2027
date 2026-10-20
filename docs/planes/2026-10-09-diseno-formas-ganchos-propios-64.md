# Diseño: formas y ganchos propios del add-in para estribos, cajas y grapas (#64, #65)

Aprobado en conversación por Luciano el 9-oct-2026. Se apoya en lo medido
contra Revit real en
`docs/planes/2026-10-08-diagnostico-formas-ganchos-propios-64.md`.

## Qué se quiere lograr

- Estribos, cajas y grapas salen con las medidas del cuadro "Dimensiones
  mínimas para ganchos estándar" de la empresa, en cualquier modelo, sin
  depender de qué formas y ganchos traiga cargados cada uno.
- La ventana no ofrece para estribos una barra cuyo doblado no coincida con
  el cuadro, y explica por qué.
- El add-in no modifica ningún tipo de barra del modelo.

Fuera de alcance:

- **Barras propias del add-in** ("CQ #3"...). Se descartó por ahora:
  duplicarían las barras en planillas y etiquetas, y el único desajuste real
  medido es la #2. Queda como idea para un issue aparte.
- **Selector "por defecto del add-in / del modelo"** para ganchos y formas
  (punto 2 del #64). Se descartó: con el filtro por medidas, un gancho o una
  forma del modelo que cumpla da el mismo resultado que el propio, y los
  selectores de gancho ya se ocultaron a pedido de William (#50).
- El gancho de anclaje de la longitudinal en la zapata (90°) y las listas de
  barras longitudinales (eso sigue siendo el #46).

## Comportamiento

### Datos del cuadro

`Modelo/CuadroVarillas.cs` pasa a tener, por barra y por tipo de gancho, la
extensión recta (E) y el diámetro mínimo de doblado (D) del cuadro 2, en
metros, tal como están transcritos en el #65:

- estribo de 135°: #2 a #5;
- gancho de 90°: #2 a #8;
- gancho de 180°: #2 a #8.

Se guardan los valores del cuadro, no la regla que los genera (6 d, 4 d,
etc.): el cuadro manda. `GanchoEstribo`, `Gancho90` y `Gancho180` (la "B")
se conservan.

### Ganchos y formas propios

En cada corrida, antes de generar, el add-in busca por nombre y crea si
faltan:

| Nombre | Qué es | Definición |
|---|---|---|
| `CQ Estribo 135°` | gancho de estribos y cajas | 135°, estilo `StirrupTie`, multiplicador 6 |
| `CQ Grapa 180°` | gancho de grapas | 180°, estilo `Standard`, multiplicador 4 |
| `CQ Estribo` | forma rectangular cerrada | 4 lados en sentido antihorario, cotas por fuera del doblez, ganchos de 135° en las dos puntas, estilo `StirrupTie` |
| `CQ Grapa` | forma de un tramo recto | 1 segmento, ganchos de 180° en las dos puntas, estilo `Standard` |

Reglas:

- **Estilo y multiplicador se vuelven a fijar en cada corrida**, por si
  alguien los cambió a mano: el nombre promete las medidas del cuadro. Mismo
  criterio que `TiposEmpalme`.
- **Extensión por barra.** Para cada barra que la corrida vaya a usar con un
  gancho propio, se compara la extensión que da Revit
  (`RebarHookType.GetHookExtensionLength`) con la E del cuadro. Si difieren
  en más de 1 mm (el cuadro está redondeado al milímetro), se apaga el cálculo automático de ESE gancho en
  esa barra y se fija el largo
  (`SetAutoCalcHookLengths(false)` + `SetHookLength`). Hoy eso pasa solo con
  el de 180° de la #2 a la #5 (mínimo de 6,5 cm). El ajuste es por barra y
  por gancho: no cambia los demás ganchos de esa barra ni su doblado.
- **Parámetros compartidos de las formas.** Las cotas de las formas son
  cuatro parámetros compartidos propios (`CQ A`, `CQ B`, `CQ C`, `CQ D`),
  creados desde un archivo temporal de parámetros compartidos. El archivo de
  parámetros compartidos que tenga configurado el usuario se restaura al
  terminar, pase lo que pase.
- **Forma existente pero alterada.** Si ya hay una forma con el nombre
  propio pero no tiene el estilo o la cantidad de lados esperados, no se usa:
  se trata como si no se hubiera podido crear.
- **Si algo no se puede crear**, esa pieza cae a lo que hace hoy el add-in
  con lo del modelo (`BuscarFormaEstribo`, `BuscarFormaGrapa`,
  `BuscarGanchoGrapa`, el gancho configurado del tramo), con un aviso que
  nombra la pieza y el motivo. Nunca se deja de generar por esto.

### Filtro de la barra de estribo

Una barra se evalúa contra el cuadro para su uso como estribo (que incluye
cajas y grapas, porque usan la misma barra):

1. Si no está en el cuadro o el cuadro no le da estribo de 135° (#6 a #8):
   **no apta**.
2. Se comparan dos doblados del tipo de barra con el cuadro:
   `StirrupTieBendDiameter` contra la D del estribo de 135°, y
   `StandardHookBendDiameter` contra la D del gancho de 180° (el de las
   grapas).
3. Con la mayor de las dos diferencias:
   - hasta 1 mm (el cuadro está redondeado al milímetro): **apta**;
   - hasta 1 diámetro de barra: **apta con aviso**;
   - más de 1 diámetro: **no apta**.

El aviso y el motivo dicen qué doblado, cuánto mide y cuánto pide el cuadro,
en centímetros.

En la ventana:

- La lista "barra de estribo" solo ofrece las aptas y las aptas con aviso.
- Debajo, una nota lista las que quedaron fuera con su motivo, y los avisos
  de las aceptadas con diferencia.
- La barra por defecto sigue siendo la #3. Si no es apta, se usa la primera
  apta de la lista, con el aviso que ya existe para cuando falta la barra
  por defecto.
- Una configuración guardada que nombre una barra no apta pasa a la barra
  por defecto, con aviso.
- Si ninguna barra es apta, la lista queda vacía y la nota lo explica; no se
  pueden generar estribos hasta corregir una barra en Revit.

Al generar se vuelve a evaluar (el plan puede venir de una configuración
guardada o de otro modelo): un tramo cuya barra de estribo no es apta se
omite con aviso, igual que hoy cuando el tipo de barra no existe.

Con lo medido el 8-oct en el modelo de prueba: #3, #4 y #5 quedan aptas (la
#3 con aviso por el doblado estándar, 6,67 d contra 6 d); la #2 queda fuera
(7,81 d contra 4 d).

### Generación

- **Estribo principal** (`GeneradorArmado`): forma `CQ Estribo` y gancho
  `CQ Estribo 135°`. El gancho configurado en el tramo (`TipoGanchoNombre`)
  deja de usarse, salvo como respaldo si el propio no se pudo crear; el
  campo se conserva para no romper las configuraciones guardadas.
- **Cajas** (`GeneradorLongitudinal`): misma forma y mismo gancho. Se
  elimina `GanchoAMedidaParaMenores` (el duplicado achicado del gancho del
  modelo) y `GanchoQueEntra`: el gancho de la caja es el del cuadro. Si no
  entra, se mantiene el reintento de hoy: la caja sale cerrada sin gancho,
  con aviso.
- **Grapas**: forma `CQ Grapa` y gancho `CQ Grapa 180°`. La medición de la
  grapa de prueba y la corrección de posición no cambian.
- **Esquina de los ganchos.** La forma propia deja los ganchos en la esquina
  de arranque (x mínimo, y mínimo del rectángulo); la «T1» del modelo los
  dejaba en la opuesta. No hay ninguna regla acordada sobre en qué esquina
  van; se acepta el cambio y se mira en el retest.
- **Aviso de gancho contra el cuadro.** `AvisarSiGanchoNoCumpleNorma`
  compara `GetHookLength` con la "B" del cuadro, y Revit mide ese largo de
  otra manera (#3 a 135°: 8,47 cm contra 11,3 cm): con los ganchos propios
  saltaría siempre. Se reemplaza por la comparación de la extensión recta
  contra la E, y solo aplica cuando se cayó al gancho del modelo.

### Limpieza

Se borra `ComandoDiagnosticoFormasPropias.cs` y su botón en `App.cs`.

## Cómo se reparte el código

- `Modelo/CuadroVarillas.cs`: los datos E y D, y la evaluación de una barra
  para estribo (apta / apta con aviso / no apta, con su texto). Sin Revit,
  para que la use la ventana y `tools/PreviewUI`.
- `Modelo/TipoBarra.cs`, `Modelo/IProveedorTipos.cs`,
  `Logica/ProveedorTiposReal.cs`, `Modelo/ProveedorTiposFalso.cs`:
  `TipoBarra` lleva además los dos doblados, en milímetros.
- `Logica/TiposPropios.cs` (nuevo): una instancia por corrida; crea o
  reutiliza ganchos, formas y parámetros, ajusta la extensión por barra y
  devuelve cada pieza con su aviso si no se pudo. Mismo molde que
  `Logica/TiposEmpalme.cs`.
- `Logica/GeneradorArmado.cs`, `Logica/GeneradorLongitudinal.cs`: piden las
  piezas a `TiposPropios` en vez de buscarlas en el modelo; re-evalúan la
  barra.
- `Vistas/VentanaConfiguradorEstribos.xaml(.cs)`: lista filtrada y nota.

## Cómo se comprueba

El proyecto no tiene pruebas automáticas. Se comprueba en Revit 2027:

1. Columna de 30×30 del modelo de prueba, estribo #3: colas de 5,7 cm,
   dobleces de 4 diámetros, forma `CQ Estribo`, sin avisos de gancho.
2. Columna con cajas y grapas: cajas con el mismo gancho; grapas con colas
   de 6,5 cm y el gancho abrazando la barra longitudinal.
3. La #2 no aparece en la lista de barra de estribo y la nota dice por qué;
   la #3 aparece con su aviso.
4. Segunda corrida en el mismo modelo: no se duplican ganchos ni formas.
5. El archivo de parámetros compartidos configurado en Revit es el mismo
   antes y después de generar.
6. `tools/PreviewUI`: la ventana abre y muestra la lista filtrada con el
   proveedor falso.

## Pendiente fuera de este diseño

- Confirmar con el equipo si de la #6 a la #8 se usan alguna vez como
  estribo. Hoy quedan fuera porque el cuadro 2 no les da estribo de 135°.
- Un modelo con `RebarShapeDefinesHooks = True` no se probó.
