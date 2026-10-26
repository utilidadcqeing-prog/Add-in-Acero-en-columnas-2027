# Diseño: ángulo del gancho por punta en las grapas (columnas)

Fecha: 26-oct-2026. Estado: **diseño, pendiente de aprobación de Luciano.**
Rama `columnas/grapas-angulo-por-punta` (worktree `C:\Proyecto\addins-angulo`).
Se apoya en el fix del #167 (`GanchoGrapa.Lados`, vista previa tangente),
que todavía no está en `main`: integrarlo primero.

## Para qué

Hoy toda grapa (C o S) lleva gancho de 180° en las dos puntas. En la práctica
también se usan grapas con 135° y con 90° (Luciano: "he visto que se usan
también así y de forma normal"). Se quiere **dejar la opción**: quien arma la
columna elige el ángulo de cada punta de cada grapa.

## Objetivos

- Cada grapa tiene un ángulo por punta: **180°, 135° o 90°**. Las dos puntas
  son independientes.
- **Por defecto 180° en las dos** (lo de hoy). Grapas y configuraciones
  guardadas de antes se leen como 180/180, sin cambiar nada.
- Vale para la grapa en C y en S. "Voltear" sigue cambiando hacia qué lado
  abre cada gancho y **conserva** el ángulo de cada punta.
- El largo de la cola y el doblez de cada ángulo **salen del cuadro de
  varillas** (`CuadroVarillas`): 180° → `Gancho180Cuadro`, 135° →
  `GanchoEstribo135` (confirmado por Luciano). El cuadro **no trae un 90° de
  estribo**: su `Gancho90Cuadro` es el de anclaje de las longitudinales
  (mucho más largo). Para el 90° de la grapa se usan **6 diámetros**, el
  mismo criterio del add-in de vigas (`GanchoPropio.DeEstribo`).
- Vista previa 2D, vista 3D y lo que crea «Generar» muestran **el mismo gancho
  en cada punta** (regla del #167: "no puede haber nada figurativo").

## No es parte de esto

- El estribo cerrado (caja) no cambia: ya tiene sus ganchos de estribo.
- No hay reglas automáticas (por ejemplo alternar el 90° de una grapa a la
  siguiente, ni exigir 135° en zona sísmica): decide la persona.
- No se cambia el ángulo de los ganchos del estribo principal ni de las
  longitudinales.

## Lo que haría que salga mal aunque "funcione"

- Que la vista previa dibuje un gancho distinto al que crea Revit.
- Que el gancho de 135° o 90° no abrace la barra (el error que se repitió
  nueve veces con el 180°, #130).
- Que las configuraciones guardadas de antes pierdan sus grapas o cambien de
  forma al abrirlas.

## Cómo se elige (decidido con Luciano, boceto aprobado)

En la ventana **«Editar estribos menores»**, igual que en el add-in de vigas
(`addins/acero-vigas/Tablero/web/alzado.js`, botones de gancho junto a cada
punta):

- Con una grapa **seleccionada**, junto a cada punta aparece una **tirita
  pequeña** de tres botones con icono y número: **90° · 135° · 180°**. El
  ángulo actual de esa punta queda resaltado. Un clic cambia el de esa punta.
- Sin grapa seleccionada no aparece nada: no ocupa lugar.
- Los números se tienen que leer siempre: el texto de los botones no
  seleccionados lleva color oscuro fijo sobre fondo claro (en el boceto
  salían blancos sobre blanco), y el seleccionado, claro sobre el color de
  resalte.
- Puntas: "izquierda / derecha" en una grapa de fila; "abajo / arriba" en una
  de columna (inicio = izquierda o abajo, como `EstriboMenor.Desde`).

## Cómo se arma (lo que queda al que lo construye)

Regla del repo: **antes de programar el generador, diagnóstico en Revit
real** con un comando temporal (como `ComandoDiagnosticoGrapaS` el 20-oct):

1. Con la forma propia de grapa (`CQ Grapa`, `CQ Grapa S`, `CQ Grapa C`,
   `CQ Grapa en S`) y `CreateFromCurvesAndShape`, ¿Revit acepta un gancho
   distinto en cada punta (`BarTerminationsData.HookTypeIdAtStart/AtEnd`) sin
   cambiar la forma? ¿Hace falta una forma propia por combinación?
2. ¿Qué tipos de gancho usar? Hoy existen `CQ Grapa 180°`, `CQ Grapa 180°
   estribo` y `CQ Estribo 135°`. Ver si el 135° del estribo sirve para la
   grapa y crear un 90° propio si no hay (`RebarHookType.Create`, ya
   verificado el 8-oct), con la cola del cuadro (`TiposPropios.AjustarExtension`).
3. **Medir** dónde queda el centro del doblez de 135° y de 90° respecto de la
   punta de la línea pedida (como `MedirGanchoDeGrapa` con el 180°) y hacia
   dónde sale la cola, para que el gancho abrace la barra y la cola entre
   hacia el centro de la columna.

Con lo medido:

- Modelo: `EstriboMenor` gana el ángulo de cada punta (por ejemplo
  `AnguloInicio` / `AnguloFin`, 180 por defecto) y `Copiar()` los copia. Como
  se guarda en la configuración, un valor que falta se lee como 180.
- `GanchoGrapa.Linea` / `MedirGanchoDeGrapa`: la medida pasa a ser por tipo
  de gancho (ángulo), no una sola para las dos puntas.
- Generador (`GeneradorLongitudinal`, `IntentarGrapa`): gancho de cada punta
  según su ángulo; los lados siguen saliendo de `GanchoGrapa.Lados`.
- Vista previa (`PlantaArmado.Grapa`): dibujar 90° y 135° con la misma cuenta
  que el generador; el tramo recto tangente como en el #167.
  `ChoquesSeccion` usa el mismo dibujo, así que revisa choques con el gancho
  real.
- Ventana: la tirita de botones de arriba, en el lienzo de
  `VentanaEstribosMenores`.
- Pruebas sin Revit en `tools/PruebasVistaPrevia` para el dibujo de cada
  ángulo y para leer configuraciones viejas como 180/180.

## Decisiones mías (a revisar por Luciano)

- La tirita no lleva la opción "sin gancho" (en vigas sí): una grapa sin
  gancho no abraza nada.
- Si una varilla no tiene el 135° de estribo en el cuadro
  (`GanchoEstribo135` puede faltar), la cola sale de 6 diámetros, el mismo
  respaldo que ya usa `PlantaArmado.Cola135`, y se avisa. El 180° siempre
  está en el cuadro; el 90° es siempre 6 diámetros (ver arriba).
- Doblez: el 135° y el 90° se doblan como el estribo, igual que hoy el 180°
  ("dobladas como el estribo", Luciano 20-oct). En los traslapos donde las
  dos piezas no entran, se usa el doblez más grande del cuadro, como hoy.
- Hacia dónde va la cola: del lado donde abre el gancho
  (`GanchoGrapa.Lados`). En el 135° la cola entra en diagonal hacia el
  núcleo, como en el gancho del estribo; en el 90°, perpendicular a la
  grapa.
