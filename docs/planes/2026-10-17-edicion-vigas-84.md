# Plan: edición manual de barras, traslapos y bastones (#84)

> **Para quien lo ejecute:** usar superpowers:subagent-driven-development o superpowers:executing-plans, tarea por tarea. Los pasos llevan casillas (`- [ ]`).

**Objetivo:** el usuario corrige el cálculo automático elemento por elemento: con los botones del panel (Subir / Bajar / Automático, Aplicar traslapo, Aplicar bastón), con los ganchos de cada punta y **arrastrando en el dibujo** con 3 puntos que saltan de largo cerrado en largo cerrado. Además elige a mano qué barras van en cada fila.

**Arquitectura:** toda la cuenta vive en la librería sin Revit. Las reglas de cada cambio están en `MotorVigas` / `AplicadorCambios` / `CalculadorBastones`; la vista previa y los saltos del arrastre, en `EditorTablero` y `EditorArrastre`. La página pide la vista previa con un mensaje nuevo, `previsualizar`, y C# contesta `previa`. La página solo pinta, y mientras se arrastra pega el punto a los `Saltos` que le manda C#, como la línea 1-1 en #91. Al aplicar o soltar, el cambio entra en `OpcionesTablero.Cambios`, se manda `calcular` y C# lo vuelve a validar.

**Tecnología:** C# (.NET, xUnit) en `addins/acero-vigas/calculo`, y la página Preact + htm de `addins/acero-vigas/Tablero/web`.

**Fuentes:** manda lo que dice Carlos (`Desktop\addin vigas`):
- reunión del 16-oct, `Meeting Transcription (15).txt`: min 21:49 (traslapo), 41:47–43:45 (voladizo) y 50:13–51:42 (bastones);
- reunión del 17-oct, `Meeting Transcription (11).txt`: min 58:18 (filas) y 1:08:56–1:19:43 (arrastre y ganchos);
- `traslapos-vigas-notas.md`: §0 (Traslapos, Bastones), §9 (Redondeo y Traslapo editable) y §13 (Edición arrastrando y Otros);
- la maqueta, opción 1: https://claude.ai/artifact/3BrJfHRe2rEGmG4ztFxhFh.

## Contexto

- **Issue:** https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/84 (épica #78), más su comentario del 17-oct. Va después de #91 y antes de #100.
- **Qué dijo Carlos:**
  - §9 (1.ª reunión): *"el add-in redondea solo… el usuario puede cambiar esa decisión barra por barra… recalcula las barras vecinas"*.
  - 16-oct, 21:49: el traslapo *"o es mayor o no existe"*. Por debajo del mínimo se bloquea, sin "Aplicar igual".
  - 16-oct, 41:47–43:45: en un voladizo nunca se traslapa (*"error garrafal de novato"*); a mano, solo si el usuario insiste.
  - 16-oct, 50:13: los bastones se cambian *"uno por uno"*, centrados en su eje y sin redondear.
  - 17-oct, 1:08:56–1:11:23: *"yo moverla para acá y como me tiene que respetar el traslapo… me va también traer el traslapo… me va a saltar hasta que cumpla la condición"*; *"no me va a hacer esto fluido… tiene una redondez y va a saltar"*. Ejemplo: correr el traslapo para que no quede pegado a un muro.
  - 17-oct, 1:15:25: *"saldría un punto a la izquierda, un punto en el centro y un punto a la derecha… si cojo el de la izquierda, solamente se me va a mover la parte izquierda"*.
  - 17-oct, 1:15:54: *"si yo esto lo meto para adentro forzadamente, pues va a interferir con el otro lado, pero… si lo quiero hacer, no hay problema"*.
  - 17-oct, 1:16:04–1:17:04: botones junto a la barra para *"gancho de 135, gancho de 90, gancho de 180"*, sin ir al panel.
  - 17-oct, 1:17:04: *"cuando llegue a acercarse a los cinco centímetros… se me va a poner tipo imán"*.
  - 17-oct, 1:17:46–1:19:14: *"las barras tienen que quedar siempre en número cerrado de longitud… me dio 1.95… va a aumentar cinco para afuera"*; *"uno va a poder ver esas dos longitudes en tiempo real"*.
  - 17-oct, 58:18: *"las tres de cinco octavos arriba y las tres de media abajo… no me da opción de colocarla a mi gusto"*.
- **Qué hay hoy** (rama `vigas-corte-91`):
  - **Motor (#80):**
    - `CambiosManuales(Barras, Traslapos)` (`Modelo/CambiosManuales.cs:7`);
    - `AplicadorCambios` aplica Subir / Bajar y el largo de un traslapo (`Vigas/AplicadorCambios.cs`);
    - `MotorVigas.PrevisualizarBarra / PrevisualizarTraslapo / PrevisualizarBaston` devuelven `Previsualizacion(SePuede, Motivo, Resultado)` (`MotorVigas.cs:59-104`);
    - `ArmadoBastones.Largos` guarda el largo escrito de cada bastón (`Modelo/Bastones.cs`).
  - **Página (#83):**
    - los paneles Barra, Traslapo y Bastón ya se ven como en la maqueta, pero cada botón dice *"Esto llega con la #84."* (`tablero.js:303, 325, 334`);
    - **la página calcula sola** el efecto del traslapo y deja Bajar siempre desactivado con un texto fijo (`tablero.js:290-326`). Eso va contra la regla de que la vista solo pinta, y esta issue lo corrige;
    - `opcionesDe` siempre manda `cambios: null` (`tablero.js:53`).
  - **Sesión:** `SesionTablero.Manejar` no tiene un mensaje de vista previa.
- **Fuera de alcance:**
  - el rótulo fijo `1#4L= 2.30/2.50` sobre cada barra, colores por diámetro, capas (#100). Aquí el rótulo solo se ve **mientras se arrastra**, pero se arma con el mismo formato y la misma función, para que #100 la reutilice;
  - generar en Revit (#85);
  - cambio de sección (#92).
- **Entorno:** monorepo `C:\Users\LENOVO\Desktop\practicacq`, Revit 2026. Rama nueva `vigas-edicion-84`, que sale de `vigas-corte-91` (#91 todavía sin PR). Los cambios sin commit del instalador manual (`README.md`, `tools/empaquetar-manual.ps1`, `tools/instalador-manual/`, `docs/planes/2026-10-17-instalador-manual-bat.md`) **no se tocan ni se suman** a los commits de esta rama.

## Criterios de aceptación

**Fase A, botones del panel (cuerpo de la issue)**
- [ ] La barra muestra su largo y si es **Automático** o **Manual**.
- [ ] **Subir** pasa la barra al siguiente múltiplo. Se desactiva, con el motivo, si la barra no sale de la varilla comercial.
- [ ] **Bajar** se desactiva, con el motivo, si deja un traslapo por debajo del mínimo.
- [ ] **Automático** vuelve al cálculo del add-in. Al cambiar una barra se recalculan sus vecinas.
- [ ] El panel del traslapo muestra el mínimo y el largo actual, y el **efecto antes de aplicar**, que calcula C#. Si queda por debajo del mínimo, dice *"No puede ser menor que el mínimo (X m)"* y Aplicar queda desactivado. Si la vecina no sale de la varilla, no deja aplicar.
- [ ] **Volver al mínimo** quita el cambio.
- [ ] El bastón cambia de largo con mitad a cada lado de su eje y sin redondear. **Usar el largo general** quita el cambio.
- [ ] Los cambios manuales no se pierden al recalcular (cambiar otro campo, cambiar de pórtico y volver).

**Fase B, ganchos**
- [ ] Al elegir una barra o un bastón, junto a cada punta que llega al borde salen los botones **90° · 135° · 180°** (en el bastón, también **Sin**). Al cambiar el gancho, el largo sigue en un número cerrado: la barra crece sola (1.95 → 2.00).

**Fase C, arrastre**
- [ ] Una barra o un bastón elegido muestra **3 puntos** (izquierda, centro, derecha). El de un lado mueve esa punta; el del centro mueve todo.
- [ ] Al arrastrar, **salta** de largo cerrado en largo cerrado; un traslapo arrastrado salta hasta cumplir el mínimo y el largo cerrado.
- [ ] **Imán a 5 cm** del borde: la punta del bastón se pega sola al final de la viga.
- [ ] Mientras se arrastra, se ven **los dos largos** (recto y total) en vivo.
- [ ] El add-in nunca pone solo un traslapo en el voladizo; a mano se puede, pero pide confirmarlo.

**Fase D, filas a mano**
- [ ] En el paso 1, cada tipo de barra tiene **Fila: Auto · 1 · 2 · 3…** Por ejemplo, 3 #5 en la fila 1 y 3 #4 en la fila 2 en vez de 5 + 1. El corte y los estribos lo respetan.

**Para todo**
- [ ] La maqueta tiene los estados nuevos y el tablero se ve igual a ella.
- [ ] `rtk dotnet test CQEING-Addins.sln -c Release` en verde.
- [ ] Probado en Revit 2026 (`PROY_JULIO_BETO.rvt` y el modelo con voladizos de Carlos, si ya llegó).

## Hallazgos del código

1. `AplicadorCambios.AplicarBarras` fija el largo de una barra y vuelve a resolver la capa con `ResolvedorCapa.Resolver(g, p, lucesFijas, largosFijos)`. Si no hay solución, el cambio no se aplica y queda el aviso `CambioNoAplicado` con `SinSolucion`. Así sale el "Bajar dejaría el traslapo corto".
2. `ResolvedorCapa.Probar` limita el corrimiento de cada traslapo a un módulo de su punto ideal (`DesvioMaximo = p.Modulo`). **Por eso mover un traslapo lejos no se puede hacer con `largosFijos`**, y hace falta un cambio nuevo que lo corra directamente (decisión 6).
3. `ProblemaLinea` ya recibe dos ganchos, uno al inicio y otro al final, pero `ResolvedorCapa` les pasa el mismo `p.Gancho` (`ResolvedorCapa.cs`, `Probar`). El largo total que resuelve el núcleo ya incluye los ganchos y es múltiplo del módulo.
4. `CalculadorBastones.Armar` centra el bastón en `lugar.Centro`, lo corta en `g.InicioBarras / g.FinBarras` y solo le pone gancho de 90° en la punta cortada (`CalculadorBastones.cs`).
5. `Reglas.PorDiametro[d]` trae `Gancho90`, `Gancho180` y `Estribo135`. **No hay un gancho de 135° para barras longitudinales** (pregunta 1).
6. `RepartidorFilas.RepartirCara` llena las filas en orden: las primeras `caben` barras van a la fila 1 y así. `Acomodar` reparte cada grupo en pares simétricos desde las esquinas.
7. `ArmadorVista.Armar` arma la `EntradaVigas` en la línea 53. Hay que extraer esa línea en un `EntradaDe` para que `EditorTablero` no la repita.
8. En la página, las opciones de cada pórtico se guardan por `CLAVES_OPCIONES` (`tablero.js:28, 155`). Si `cambios` entra en `OPCIONES0`, viaja con su pórtico.
9. `demo.js` contesta los mensajes en un `switch (m.tipo)` (`demo.js:270`): ahí se agrega `previsualizar`, con las cuentas que hoy hace `tablero.js`.

## Decisiones

> "Carlos" = lo dijo él. "Nuestra" = la tomamos a partir de lo que dijo; se le muestra al probar y va a #79 si es pregunta.

1. **La vista previa la calcula C#** (nuestra, con la regla del motor): mensaje `previsualizar` → respuesta `previa`. Cada pedido lleva un número `N` que la respuesta devuelve; la página descarta las respuestas viejas. La vista previa no cambia el estado de la sesión.
2. **Subir / Bajar = ± un módulo** (Carlos, §9: *"subir A a 3.00 en vez de bajarla a 2.50"*). Al elegir una barra, C# devuelve las dos opciones con su texto. Los textos:
   - Subir bloqueado: *"No sale de una varilla de {6} m."*;
   - Bajar bloqueado: *"Bajarla dejaría un traslapo por debajo del mínimo."*;
   - si se puede: *"S1 pasa a 3.50 m."*, más una frase por cada vecina que cambia: *"S2 pasa a 4.00 m."*
3. **Traslapo** (Carlos 16-oct + §9): solo crece; crece la barra de la derecha, redondeada hacia arriba al módulo (como hoy en `AplicadorCambios`). Los textos, iguales a los de la maqueta:
   - *"No puede ser menor que el mínimo ({min} m)."*;
   - *"{S3} pasaría a {6.50} m y no sale de una varilla de {6} m."*;
   - *"El traslapo se metería en el nudo."*;
   - *"Escribe un largo mayor que el actual ({cur} m)."*;
   - si se puede: *"{S3} pasa a {4.50} m (múltiplo de {0.50}) y el traslapo queda en {1.50} m."*
4. **Bastón escrito en el panel**: centrado y sin redondear (Carlos 16-oct). Texto: *"Queda {L/2} m a cada lado de su eje. No se redondea."*; si el largo es ≤ 0: *"El largo debe ser mayor que 0."*
5. **Ganchos por punta** (Carlos 17-oct 1:16:04):
   - solo en una punta que llega al borde del pórtico, como en #89;
   - barras corridas: 90° (por defecto, Carlos 16-oct 1:26:44), 135° o 180°;
   - bastones: Sin, 90°, 135° o 180°;
   - largos: 90° → `Gancho90.B`, 180° → `Gancho180.B`, 135° → `Estribo135.B`, que es el único 135° de los cuadros (**nuestra, pregunta 1**);
   - en las corridas, el gancho entra en el `ProblemaLinea` de la capa, así que el núcleo deja el total en múltiplo del módulo y corre el traslapo lo que haga falta (hallazgo 3). La punta del borde no se mueve: queda en el recubrimiento.
6. **Arrastrar un traslapo lo corre entero** (Carlos 1:10:16, *"me va también traer el traslapo"*):
   - el traslapo conserva su largo y se corre Δ = k × módulo, porque las dos barras deben seguir en largo cerrado; la de la izquierda crece Δ y la de la derecha se acorta Δ;
   - **no se puede** si alguna de las dos no sale de la varilla, si queda a menos de 5 cm de una cara (nudo) o si choca con el traslapo siguiente;
   - se guarda en `CambiosManuales.Movidos` (id → `Desde` en mm), aparte del largo escrito, y se aplica **después** de Subir / Bajar y del largo del traslapo;
   - una barra arrastrada por una punta que está en un traslapo mueve ese traslapo; por el centro, mueve sus dos traslapos con el mismo Δ (nuestra);
   - la punta de una barra que llega al borde no se arrastra (va al recubrimiento, con su gancho), y el centro de una barra de borde tampoco (nuestra);
   - con una sola barra, sin traslapos, la barra corrida no tiene puntos.
7. **Voladizo** (Carlos 16-oct): el arrastre ofrece saltos dentro del voladizo marcados `EnVoladizo`. Al soltar ahí, la página pide confirmar dentro de la misma página, sin diálogo del navegador: *"En un voladizo nunca se traslapa. ¿Ponerlo igual?"* · **Ponerlo igual** · **Cancelar**. El cambio guarda `EnVoladizo = true`; sin esa marca, C# lo rechaza con `TraslapoEnVoladizo`.
   - Solo puede pasar al voladizo el traslapo de la luz que lo toca, cruzando su apoyo. Un traslapo que ya está en el voladizo se mueve dentro de él sin volver a preguntar, y puede volver a esa luz (Juan, 17-oct, probando la maqueta: *"¿no debería dejarme correr a la izquierda?"*).
   - Fuera de eso, un traslapo arrastrado no cambia de luz. Los textos del tope nombran la columna (*"se metería en el nudo de la columna D"*) o dicen *"se saldría por la punta del voladizo"*.
8. **Arrastrar un bastón** (Carlos 1:15:25–1:17:46):
   - una punta mueve solo esa punta: el largo total salta de módulo en módulo; el del centro corre el bastón entero de **50 mm** en 50 mm (**nuestra, pregunta 4**);
   - se puede montar sobre otros bastones (Carlos 1:15:54); solo se limita a quedar dentro de `InicioBarras`–`FinBarras` y a medir al menos un módulo;
   - **imán**: si la punta queda a 50 mm o menos de `InicioBarras` / `FinBarras`, se pega ahí; el total se redondea **hacia arriba** al módulo y el sobrante va a la **otra punta** (nuestra, pregunta 3);
   - un bastón arrastrado deja de estar centrado: se guarda en `ArmadoBastones.Puntas` (id → `PuntasBaston(Desde, Hasta)`, en mm). **Usar el largo general** borra el id de `Largos` y de `Puntas`.
9. **Número cerrado en los bastones** (Carlos 17-oct choca con Carlos 16-oct; **pregunta 2**): lo **escrito** en el panel no se redondea (16-oct). Lo **arrastrado** y el total de un bastón **con gancho elegido con los botones** se redondean hacia arriba al módulo (17-oct, el más reciente).
10. **Rótulo en vivo**: `"{n}#{d}L= {recto}/{total}"`, o `"{n}#{d}L= {total}"` sin gancho (formato de DC-CAD, §13), con 2 decimales. Lo arma `Rotulo.De(...)`, que reutiliza #100.
11. **Filas a mano** (Carlos 58:18):
    - `GrupoBarras` gana `int? Fila = null` (null = Auto);
    - primero van los grupos con fila fija, en su fila; los Auto llenan los puestos libres desde la fila 1, como hoy;
    - errores: una fila con más barras que `caben`, o una fila mayor que `MaximoFilas` → `NoCaben`; la fila 1 con menos de 2 barras → `MenosDeDosBarras` (las esquinas son del primer grupo de la fila 1, como hoy; **pregunta 5**);
    - los traslapos no cambian: la capa sigue siendo una.
12. **Maqueta primero** (Juan, 17-oct): la tarea 0 pone en la maqueta los estados nuevos y el tablero se programa igual a ella.

### Preguntas para Carlos (#79)

1. ¿Qué largo lleva el gancho de 135° en una barra longitudinal? Por ahora, el del cuadro de estribos de 135°.
2. Bastones: el 16-oct dijo que no se redondean y el 17-oct que quedan en número cerrado. ¿Lo arrastrado y lo que lleva gancho van al múltiplo, y lo escrito queda exacto?
3. "El sobrante sale hacia afuera": ¿hacia qué punta? Por ahora, hacia la que no tiene gancho.
4. ¿De a cuánto salta un bastón que se mueve entero? Por ahora, de 5 cm en 5 cm.
5. Filas a mano: ¿las barras de esquina son siempre las del primer tipo de la fila 1?

## Global Constraints

- La página no calcula acero: textos, saltos y validaciones salen de C#. Lo único que hace la página es pegar el punto al salto más cercano de los que le mandó C#.
- Unidades: `CambiosManuales`, `ArmadoBastones` y `Saltos.Cambios` en **mm**; `PreviaVista` y `Salto.X` en **m**, como el alzado.
- Nombres y textos en español, como el resto del add-in. Comentarios con la misma densidad del código de alrededor.
- Commits **sin** `Co-Authored-By` ni `Claude-Session`. Todos los comandos con `rtk` delante.
- No tocar los archivos del instalador manual que están sin commit.
- Ningún `alert` / `confirm` del navegador: las confirmaciones van dentro de la página.

## Review Focus

1. **Respuesta vieja de la vista previa:** si el usuario cambia de elemento antes de que llegue `previa`, no se pinta la del anterior. Lo cubre `N`: prueba `LaPreviaDevuelveElNumeroDelPedido` (tarea 1) y revisión a mano (tarea 2).
2. **Cambio guardado cuyo id ya no existe** (de 3 barras se pasa a 2 y desaparece `S3`): no revienta, avisa `CambioNoAplicado` y el resto se aplica. Prueba `UnCambioDeUnIdQueYaNoExisteAvisaYSigue` (tarea 1, motor); para `Movidos` y `Puntas`, la misma prueba en las tareas 5 y 6.
3. **Cambiar de pórtico con cambios:** los cambios de un pórtico no llegan al otro (`cambios` y `bastPuntas` en `OPCIONES0`) y vuelven al regresar. Revisión a mano en la tarea 2.
4. **Soltar fuera del dibujo o con Esc:** el elemento queda en el último salto válido (soltar) o como estaba (Esc); arrastrar un punto no mueve el alzado. Revisión a mano en la tarea 7.
5. **Un gancho que hace que una barra no salga de la varilla:** la capa se resuelve con la varilla larga o avisa `SinSolucion`; no revienta. Prueba `UnGanchoMasLargoQueNoSaleDeLaVarillaUsaLaLargaOAvisa` (tarea 3).

---

### Tarea 0: la maqueta con los estados de #84

**Archivos:** el artifact https://claude.ai/artifact/3BrJfHRe2rEGmG4ztFxhFh (se lee con `Artifact read` y se publica en la misma URL).

- [ ] **Paso 1:** leer la maqueta y ubicar el alzado, los paneles Barra / Traslapo / Bastón y el paso 1 "Barras".
- [ ] **Paso 2:** agregar, con los mismos colores, tipografía y tamaños de la maqueta:
  - **3 puntos** sobre la barra o el bastón elegido: círculos de 10 px; el del centro, relleno. El que no se puede arrastrar sale gris, con un `title` que dice por qué;
  - el **rótulo en vivo** sobre la barra mientras se arrastra (`3#5L= 4.00/4.28`), en mono 12 px;
  - los **botones de gancho** junto a la punta del borde: grupo segmentado `90° · 135° · 180°` (en el bastón, `Sin · 90° · 135° · 180°`), con un icono chico de cada gancho;
  - la **confirmación del voladizo** dentro de la página: *"En un voladizo nunca se traslapa. ¿Ponerlo igual?"* · Ponerlo igual · Cancelar;
  - en el paso 1, junto a cada tipo de barra, el selector **Fila: Auto · 1 · 2 · 3…**;
  - los paneles Barra / Traslapo / Bastón con los textos de las decisiones 2 a 4, sin cambiar su diseño.
- [ ] **Paso 3:** publicar en la misma URL y pedir a Juan que la revise. **No se sigue con la tarea 1 hasta que la apruebe.**

### Fase A: botones del panel

### Tarea 1: la vista previa en C# (`EditorTablero` y el mensaje `previsualizar`)

**Archivos:**
- Crear: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo/Tablero/EditorTablero.cs`
- Modificar: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo/Tablero/VistaTablero.cs` (records nuevos), `ProtocoloTablero.cs`, `SesionTablero.cs`, `ArmadorVista.cs:53` (extraer `EntradaDe`)
- Crear: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests/EditorTableroTests.cs`
- Modificar: `ProtocoloTableroTests.cs`, `SesionTableroTests.cs`, `CambiosManualesTests.cs`

**Interfaces:**
- Usa: `MotorVigas.PrevisualizarBarra / PrevisualizarTraslapo / PrevisualizarBaston`, `Previsualizacion`, `MotivoBloqueo`.
- Produce:
  ```csharp
  // Tablero/VistaTablero.cs
  public enum AccionEdicion { Barra, Traslapo, Baston, Arrastre }
  public enum PuntoArrastre { Izquierda, Centro, Derecha }
  /// <summary>Lo que la página quiere ver antes de aplicar. Valor en m (traslapo y bastón); N vuelve en la respuesta.</summary>
  public sealed record PedidoEdicion(int N, string Elemento, AccionEdicion Accion, double? Valor = null, PuntoArrastre? Punto = null);
  /// <summary>Una opción del panel: Accion es "subir", "bajar" o "aplicar"; Valor en m.</summary>
  public sealed record OpcionEdicion(string Accion, double Valor, bool SePuede, string Texto);
  public sealed record PreviaVista(int N, string Elemento, IReadOnlyList<OpcionEdicion> Opciones, IReadOnlyList<Salto> Saltos);
  // Salto y CambioArrastre se definen en la tarea 6; hasta entonces Saltos va vacío.

  // Tablero/ProtocoloTablero.cs
  public sealed record Previsualizar(string Id, OpcionesTablero Opciones, PedidoEdicion Pedido) : MensajeTablero;
  public static string Previa(PreviaVista p); // {"tipo":"previa","previa":…}

  // Tablero/ArmadorVista.cs
  internal static EntradaVigas EntradaDe(PorticoLeido p, OpcionesTablero o, Reglas r);

  // Tablero/EditorTablero.cs
  public static class EditorTablero
  {
      public static PreviaVista Previsualizar(PorticoLeido p, OpcionesTablero o, Reglas r, PedidoEdicion pedido);
  }
  ```
  JSON de la página: `{"tipo":"previsualizar","id":"P1","opciones":{…},"pedido":{"n":7,"elemento":"S2","accion":"Barra"}}`.

- [ ] **Paso 1: escribir las pruebas que fallan**, con `Datos.PorticoMaqueta()` y la tabla real:
  - `EditorTableroTests.SubirYBajarDevuelvenLasDosOpciones`: para `S1`, `Opciones` tiene `"subir"` con `Valor = largo + 0.50` y `"bajar"` con `largo − 0.50`.
  - `EditorTableroTests.SubirQueNoSaleDeLaVarillaSeBloqueaConSuMotivo`: una barra que ya mide 6.00 m (se lleva ahí con `Cambios.Barras`) → `subir.SePuede == false` y `Texto == "No sale de una varilla de 6 m."`.
  - `EditorTableroTests.BajarQueDejaUnTraslapoCortoSeBloquea`: `bajar.SePuede == false`, `Texto == "Bajarla dejaría un traslapo por debajo del mínimo."`.
  - `EditorTableroTests.SubirDiceComoQuedanLasVecinas`: `subir.Texto` empieza con `"S1 pasa a "` y nombra cada barra cuyo largo cambia.
  - `EditorTableroTests.TraslapoBajoElMinimo`: `Valor = 0.80` con #5 → `"No puede ser menor que el mínimo (0.90 m)."` y `SePuede == false`.
  - `EditorTableroTests.TraslapoQueCabeDiceElEfecto`: `Texto == "S2 pasa a {x} m (múltiplo de 0.50) y el traslapo queda en {y} m."`, con x e y leídos de `MotorVigas.PrevisualizarTraslapo`.
  - `EditorTableroTests.BastonEscritoNoSeRedondea`: `BS1` con 2.30 → `"Queda 1.15 m a cada lado de su eje. No se redondea."`, `SePuede == true`.
  - `ProtocoloTableroTests.LeePrevisualizar` y `ProtocoloTableroTests.EscribePrevia`.
  - `SesionTableroTests.LaPreviaDevuelveElNumeroDelPedido`: `N = 7` → la respuesta trae `"n":7`, y `Manejar` no cambia la vista guardada (una `Vista()` después es igual a la de antes).
  - `CambiosManualesTests.UnCambioDeUnIdQueYaNoExisteAvisaYSigue`: `Barras = {S9: 3000, S1: 3500}` → aviso `CambioNoAplicado` / `NoExiste` para `S9`, y `S1` queda en 3500, `Manual`.
- [ ] **Paso 2:** `rtk dotnet test addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests --filter "EditorTableroTests|ProtocoloTableroTests|SesionTableroTests|CambiosManualesTests"` → las nuevas FALLAN.
- [ ] **Paso 3:** implementar. `EditorTablero` arma la entrada con `ArmadorVista.EntradaDe` y llama a los `Previsualizar*` del motor. Los textos son los de las decisiones 2 a 4 y los números van con `f2` (`ToString("0.00", CultureInfo.InvariantCulture)`). `SesionTablero` contesta `[new AJs(ProtocoloTablero.Previa(...))]` sin tocar `opciones` ni `actual`; si el id no existe, `[]`. `PuenteTablero` no cambia: ya reenvía cualquier `AJs`.
- [ ] **Paso 4:** `rtk dotnet test CQEING-Addins.sln -c Release` → en verde.
- [ ] **Paso 5:** commit `feat(vigas): vista previa de Subir/Bajar, traslapo y bastón en C# (#84)`.

### Tarea 2: los botones del panel conectados

**Archivos:**
- Modificar: `addins/acero-vigas/Tablero/web/tablero.js` (`OPCIONES0`, `opcionesDe`, `valores` 278-335, receptor de mensajes ~1049)
- Modificar: `addins/acero-vigas/Tablero/web/demo.js` (`case 'previsualizar'`)

**Interfaces:**
- Usa: `previsualizar` / `previa` (tarea 1); `OpcionesTablero.Cambios` (mm).
- Produce: en `OPCIONES0`, `cambios: { barras: {}, traslapos: {}, movidos: {}, ganchos: {} }` y `bastPuntas: {}` (los dos últimos se llenan en las fases B y C). Y el método `Tablero.previa(p)`.

- [ ] **Paso 1:** `opcionesDe` manda `cambios` tal cual (los valores ya están en mm) en vez de `null`.
- [ ] **Paso 2:** al elegir un elemento, y cada vez que llega una vista nueva con algo elegido, enviar `previsualizar`:
  - barra → `{accion:'Barra'}`;
  - traslapo → `{accion:'Traslapo', valor}` con 150 ms de espera al escribir;
  - bastón → `{accion:'Baston', valor}`.
  
  Guardar `this.n` y, en `previa(p)`, ignorar si `p.n !== this.n`.
- [ ] **Paso 3:** pintar `sb` / `sl` / `stv` con `s.previa`, cuando sus `elemento` coinciden: `downOff = !bajar.sePuede`, `downWhy = bajar.texto`, y lo mismo para subir; `sl.eff = aplicar.texto`, `off = !aplicar.sePuede`. **Borrar** las cuentas de la página (`tablero.js:309-322`) y el `note` fijo.
- [ ] **Paso 4:** las acciones:
  - **Subir / Bajar** → `cambios.barras[id] = mm(valor)`;
  - **Aplicar traslapo** → `cambios.traslapos[id] = mm(valor)`;
  - **Aplicar bastón** → `bastLen[id]`, como hoy;
  - **Automático / Volver al mínimo / Usar el largo general** → borrar la clave (en el bastón, de `bastLen` y de `bastPuntas`).
  
  Todas por `this.cambiar(...)`, que manda `calcular`. Quitar `LLEGA(84)`.
- [ ] **Paso 5:** `demo.js`: `case 'previsualizar'` contesta `{tipo:'previa', previa}` con las cuentas que se borraron de `tablero.js` en el paso 3 (así la demostración sigue funcionando sin C#).
- [ ] **Paso 6: probar en Chrome con la demostración:** Subir / Bajar / Automático, traslapo bajo el mínimo (Aplicar gris con el mensaje), Volver al mínimo, bastón 2.30. Cambiar de pórtico y volver: los cambios siguen (Review Focus 3). Sin errores en la consola.
- [ ] **Paso 7:** commit `feat(vigas): Subir, Bajar, traslapo y bastón editables desde el panel (#84)`.

### Fase B: ganchos

### Tarea 3: ganchos por punta en el motor

**Archivos:**
- Modificar: `Modelo/CambiosManuales.cs`, `Modelo/Resultado.cs` (`Barra`), `Modelo/Bastones.cs` (`Baston`), `Vigas/ResolvedorCapa.cs` (`ParametrosCapa`), `MotorVigas.cs` (`Resolver`), `Vigas/CapaCalculada.cs`, `Vigas/CalculadorBastones.cs`, `Tablero/ArmadorVista.cs` + `VistaTablero.cs` (`BarraVista`, `BastonVista`)
- Crear: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests/GanchosTests.cs`

**Interfaces:**
- Produce:
  ```csharp
  public enum TipoGancho { Sin, De90, De135, De180 }
  // CambiosManuales gana: IReadOnlyDictionary<string, TipoGancho>? Ganchos = null
  //   claves: "S.inicio", "S.fin", "I.inicio", "I.fin" (punta del borde de la capa de corridas; no dependen de cuántas
  //   barras salgan) y "BS1.inicio", "BI2.fin"… (bastones)
  // ParametrosCapa: Gancho pasa a GanchoInicio y GanchoFin (mm)
  // Barra y Baston ganan, al final: TipoGancho TipoInicio = TipoGancho.Sin, TipoGancho TipoFin = TipoGancho.Sin
  // BarraVista y BastonVista ganan: string GanchoInicioTipo, string GanchoFinTipo ("", "90°", "135°", "180°")
  //   y bool PuntaInicioEnBorde, bool PuntaFinEnBorde (dónde se ponen los botones)
  public static int LargoGancho(ReglaDiametro r, TipoGancho t); // en Reglas.cs: Sin→0, De90→Gancho90.B, De135→Estribo135.B, De180→Gancho180.B
  ```
- [ ] **Paso 1: escribir las pruebas que fallan** (`GanchosTests`):
  - `PorDefectoLasPuntasDelBordeLlevan90`: `S1.TipoInicio == De90` y `S1.GanchoInicio == 278` con #5 (lo de hoy no cambia).
  - `Gancho180EnElInicioCambiaElLargoYSigueEnMultiplo`: `Ganchos = {"S.inicio": De180}` → `S1.GanchoInicio == 238`, `S1.Largo % 500 == 0` y `S1.Desde` igual que antes.
  - `Gancho135UsaElDelCuadroDeEstribos`: `{"S.inicio": De135}` → `S1.GanchoInicio == 189`.
  - `SinGanchoNoSePermiteEnLasCorridas`: `{"S.inicio": Sin}` → `CambioNoAplicado` con el motivo nuevo `GanchoNoValido`, y queda el de 90°.
  - `GanchoEnUnaPuntaQueNoEstaEnElBordeSeIgnoraConAviso`: el bastón de una columna interior, `{"BS2.inicio": De90}` → `CambioNoAplicado` con el motivo nuevo `NoLlegaAlBorde`.
  - `BastonConGanchoElegidoRedondeaHaciaArribaYCreceLaOtraPunta`: un bastón cortado en el borde con `{"BS1.inicio": De90}` → `Largo % 500 == 0`, `Desde == InicioBarras` y `Hasta` mayor que antes.
  - `BastonSinGanchoEscritoNoSeRedondea`: `Largos = {BS1: 2300}` y sin gancho → `LargoRecto == 2300`.
  - `UnGanchoMasLargoQueNoSaleDeLaVarillaUsaLaLargaOAvisa` (Review Focus 5): no lanza; o `Varilla == 12000` o aviso `SinSolucion`.
- [ ] **Paso 2:** `rtk dotnet test … --filter GanchosTests` → FALLAN.
- [ ] **Paso 3:** implementar.
  - `MotorVigas.Resolver` arma `ParametrosCapa` con `LargoGancho` de `Cambios.Ganchos["{prefijo}.inicio"]` y `["{prefijo}.fin"]` (por defecto, `De90`).
  - `CalculadorBastones` usa `TipoGancho` por punta (por defecto: `ConGancho ? De90 : Sin`, como hoy) y aplica la decisión 9.
  - Agregar `NoLlegaAlBorde` y `GanchoNoValido` a `MotivoBloqueo`.
- [ ] **Paso 4:** `rtk dotnet test CQEING-Addins.sln -c Release` → en verde (las pruebas viejas de bastones y del motor siguen pasando).
- [ ] **Paso 5:** commit `feat(vigas): gancho de 90°, 135° o 180° en cada punta del borde (#84)`.

### Tarea 4: botones de gancho junto a la punta

**Archivos:** `addins/acero-vigas/Tablero/web/alzado.js`, `tablero.js`, `demo.js`

**Interfaces:** usa `BarraVista/BastonVista.GanchoInicioTipo`, `GanchoFinTipo`, `PuntaInicioEnBorde` y `PuntaFinEnBorde`; escribe `cambios.ganchos`. Las claves: `"S.inicio"` / `"S.fin"` (corridas de arriba), `"I.inicio"` / `"I.fin"` (de abajo) y `"BS1.inicio"`… (bastones).

- [ ] **Paso 1:** en `Alzado`, si hay algo elegido, junto a cada punta con `PuntaXEnBorde` va un grupo de botones HTML encima del SVG, ubicado con `X(m)` y la transformación del zoom, como en la maqueta de la tarea 0. Con `onPointerDown` detenido, para que no empiece el paneo.
- [ ] **Paso 2:** el clic pasa por un `onHook(clave, tipo)` nuevo → `this.cambiar({cambios: …})`.
- [ ] **Paso 3:** `demo.js` suma o resta el largo del gancho, con las cuentas de la maqueta.
- [ ] **Paso 4: probar en Chrome:** cambiar a 180° y volver a 90° en S1; en un bastón del borde, Sin ↔ 90°. Los kilos y el largo cambian. Sin errores en la consola.
- [ ] **Paso 5:** commit `feat(vigas): botones de gancho junto a la punta en el alzado (#84)`.

### Fase C: arrastre

### Tarea 5: traslapos movidos y bastones con puntas a mano (motor)

**Archivos:**
- Modificar: `Modelo/CambiosManuales.cs`, `Modelo/Bastones.cs`, `Vigas/AplicadorCambios.cs`, `Vigas/CalculadorBastones.cs`
- Modificar: `CambiosManualesTests.cs`, `BastonesTests.cs`

**Interfaces:**
- Produce:
  ```csharp
  /// <summary>Un traslapo corrido a mano: dónde empieza (mm). EnVoladizo = el usuario confirmó ponerlo en el voladizo.</summary>
  public sealed record TraslapoMovido(int Desde, bool EnVoladizo = false);
  // CambiosManuales gana: IReadOnlyDictionary<string, TraslapoMovido>? Movidos = null
  public sealed record PuntasBaston(int Desde, int Hasta); // parte recta, mm desde el eje A
  // ArmadoBastones gana: IReadOnlyDictionary<string, PuntasBaston>? Puntas = null
  // MotivoBloqueo gana: ChocaConOtroTraslapo, TraslapoEnVoladizo
  ```
- [ ] **Paso 1: escribir las pruebas que fallan**:
  - `MoverUnTraslapoUnModuloCreceLaIzquierdaYAcortaLaDerecha`: `Movidos = {"I1–I2": new(t.Desde + 500)}` → `I1.Largo + 500`, `I2.Largo − 500`, el traslapo con el mismo largo, y los tres `Manual`.
  - `MoverUnTraslapoQueNoEsMultiploSeLlevaAlModuloMasCercano`: `Desde + 260` → queda en `Desde + 500`.
  - `MoverUnTraslapoAlNudoSeRechaza`: `CambioNoAplicado` / `EntraAlNudo`, y el traslapo queda donde estaba.
  - `MoverUnTraslapoHastaElSiguienteSeRechaza`: `ChocaConOtroTraslapo`.
  - `MoverAlVoladizoSinConfirmarSeRechazaYConfirmadoSeAplica`: `TraslapoEnVoladizo` sin la marca; con `EnVoladizo: true` el traslapo queda dentro del voladizo.
  - `ElMovidoSeAplicaDespuesDeSubirOBajar`: `Barras = {I1: x}` + `Movidos` → el resultado es el de aplicarlos en ese orden.
  - `BastonConPuntasAMano`: `Puntas = {BS2: new(3250, 5550)}` → `Desde == 3250`, `Hasta == 5550`, `Manual`, `Eje` sin cambio.
  - `PuntasFueraDelPorticoSeCortanEnElBorde`.
  - `UnCambioDeUnIdQueYaNoExisteAvisaYSigue` para `Movidos` y `Puntas` (Review Focus 2).
- [ ] **Paso 2:** `rtk dotnet test … --filter "CambiosManualesTests|BastonesTests"` → las nuevas FALLAN.
- [ ] **Paso 3:** implementar `AplicarMovidos` en `AplicadorCambios`, después de `AplicarTraslapos`, con las reglas de las decisiones 6 y 7: la luz del traslapo, `GeometriaPortico.DistanciaACara` y `SaleDeLaVarilla`. En `CalculadorBastones.Armar`, si hay `Puntas[id]`, se usan en vez de centrar.
- [ ] **Paso 4:** `rtk dotnet test CQEING-Addins.sln -c Release` → en verde.
- [ ] **Paso 5:** commit `feat(vigas): traslapos corridos a mano y bastones con sus puntas (#84)`.

### Tarea 6: los saltos del arrastre (`EditorArrastre`) y el rótulo

**Archivos:**
- Crear: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo/Tablero/EditorArrastre.cs`, `Tablero/Rotulo.cs`
- Modificar: `Tablero/VistaTablero.cs` (`Salto`, `CambioArrastre`), `Tablero/EditorTablero.cs` (acción `Arrastre`)
- Crear: `addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests/EditorArrastreTests.cs`

**Interfaces:**
- Usa: `TraslapoMovido`, `PuntasBaston`, `TipoGancho` (tareas 3 y 5).
- Produce:
  ```csharp
  /// <summary>Un cambio que deja el arrastre al soltar. Tipo "movido" (Id del traslapo, Desde en mm, EnVoladizo) o "puntas" (Id del bastón, Desde y Hasta en mm).</summary>
  public sealed record CambioArrastre(string Tipo, string Id, int Desde, int Hasta = 0, bool EnVoladizo = false);
  /// <summary>Un lugar donde puede caer el punto arrastrado: X en m, los largos del elemento en m y su rótulo.</summary>
  public sealed record Salto(double X, double Recto, double Total, string Rotulo, bool EnVoladizo, IReadOnlyList<CambioArrastre> Cambios);

  public static class EditorArrastre
  {
      public const int Iman = 50;          // mm, decisión 8
      public const int PasoPosicion = 50;  // mm, bastón entero (decisión 8, pregunta 4)
      /// <summary>Vacía si ese punto no se puede arrastrar.</summary>
      public static IReadOnlyList<Salto> Saltos(EntradaVigas e, ResultadoVigas r, string elemento, PuntoArrastre punto);
  }
  public static class Rotulo
  {
      public static string De(IReadOnlyList<GrupoBarras> grupos, int recto, int total); // "3#5L= 4.00/4.28" o "3#5L= 4.00"
  }
  ```
- [ ] **Paso 1: escribir las pruebas que fallan** (`EditorArrastreTests`):
  - `LaPuntaIzquierdaDeUnaBarraMueveSuTraslapoDeModuloEnModulo`: `I2`, `Izquierda` → los `X` consecutivos se separan 0.50 y cada salto trae un `CambioArrastre("movido", "I1–I2", …)`.
  - `ElCentroDeUnaBarraMueveSusDosTraslapos`: cada salto trae dos cambios con el mismo Δ.
  - `LaPuntaDelBordeYElCentroDeUnaBarraDeBordeNoSeArrastran`: `S1`, `Izquierda` y `Centro` → vacía.
  - `SinTraslaposLaBarraNoTienePuntos`.
  - `LosSaltosNoEntranAlNudoNiPasanDeLaVarilla`: ningún salto queda a menos de 50 mm de una cara ni deja una barra de más de 6000.
  - `LosSaltosDelVoladizoVanMarcados`: hay saltos con `EnVoladizo` cuando la luz toca el voladizo.
  - `LaPuntaDelBastonSaltaDeModuloEnModuloYSePegaAlBorde`: el último salto a la izquierda tiene `Cambios[0].Desde == InicioBarras` y `Total % 500 == 0`.
  - `ElCentroDelBastonSaltaDe5cm`.
  - `RotuloConYSinGancho`: `Rotulo.De([new(3,5)], 4000, 4278) == "3#5L= 4.00/4.28"` y `Rotulo.De([new(1,4)], 2300, 2300) == "1#4L= 2.30"`.
  - `EditorTableroTests.ArrastreDevuelveLosSaltos`: `PedidoEdicion(…, Accion: Arrastre, Punto: Izquierda)` → `PreviaVista.Saltos` no vacía.
- [ ] **Paso 2:** `rtk dotnet test … --filter "EditorArrastreTests|EditorTableroTests"` → FALLAN.
- [ ] **Paso 3:** implementar. Cada salto se arma con aritmética (Δ = k × módulo, o 50 mm en el centro del bastón), sin resolver la capa entera; C# vuelve a validar al soltar (tarea 5). El rango de k va hasta donde deja de cumplirse alguna regla, sin saltarse ninguna.
- [ ] **Paso 4:** `rtk dotnet test CQEING-Addins.sln -c Release` → en verde.
- [ ] **Paso 5:** commit `feat(vigas): saltos del arrastre y rótulo con los dos largos (#84)`.

### Tarea 7: los 3 puntos en el alzado

**Archivos:** `addins/acero-vigas/Tablero/web/alzado.js`, `tablero.js`, `demo.js`

**Interfaces:** usa `previsualizar` con `{accion:'Arrastre', punto}` → `previa.saltos`; escribe `cambios.movidos` (`{[id]: {desde, enVoladizo}}`) y `bastPuntas` (`{[id]: {desde, hasta}}`).

- [ ] **Paso 1:** en `Alzado`, el elemento elegido pinta 3 círculos (izquierda, centro, derecha) como en la maqueta de la tarea 0. Al poner el mouse encima, se pide `previsualizar` de ese punto; sin saltos, el punto sale gris con su `title`.
- [ ] **Paso 2:** al arrastrar, se usa `setPointerCapture` y se detiene el paneo, como `cutDown` (`alzado.js:140`). La x del mouse pasa a m con la fórmula del hallazgo 8 de #91, se pega al `Salto` más cercano y se pinta la barra fantasma con `salto.rotulo` encima.
- [ ] **Paso 3:** al soltar:
  - si `salto.enVoladizo`, sale la confirmación de la decisión 7 dentro de la página;
  - si no, o si se confirma, se agregan `salto.cambios` a `cambios.movidos` o `bastPuntas` y se manda `calcular`;
  - **Esc** cancela; soltar fuera del dibujo deja el último salto (Review Focus 4).
- [ ] **Paso 4:** `demo.js` contesta los saltos con módulo fijo, para poder probar en el navegador.
- [ ] **Paso 5: probar en Chrome:**
  - correr un traslapo de abajo, que salta de 0.50 en 0.50 y respeta el nudo;
  - mover una barra por el centro;
  - estirar un bastón hasta el borde: se pega con el imán y con gancho redondea;
  - llevar un traslapo al voladizo: pide confirmar.
  
  El alzado no se mueve al arrastrar un punto. Sin errores en la consola.
- [ ] **Paso 6:** commit `feat(vigas): arrastrar barras, traslapos y bastones con 3 puntos (#84)`.

### Fase D: filas a mano

### Tarea 8: `GrupoBarras.Fila` en el reparto

**Archivos:** `Modelo/Entrada.cs`, `Seccion/RepartidorFilas.cs`, `RepartidorFilasTests.cs`; revisar `ArmadorVista.Cantidad` y `Seccion(p, o, r)` para que pasen la fila.

**Interfaces:**
- Produce: `public sealed record GrupoBarras(int Cantidad, int Diametro, int? Fila = null);` (null = Auto). `OpcionesTablero.Superior / Inferior` la traen desde la página (`{cantidad, diametro, fila}`).
- [ ] **Paso 1: escribir las pruebas que fallan** (`RepartidorFilasTests`):
  - `TresDe5EnLaFila1YTresDe4EnLa2`: `[new(3,5,1), new(3,4,2)]` → `Filas[0]` tiene 3 #5 y `Filas[1]` tiene 3 #4.
  - `LosAutoLlenanLosPuestosLibresDesdeLaFila1`: `[new(2,5,2), new(4,4)]` → los 4 #4 en la fila 1 y los 2 #5 en la 2.
  - `UnaFilaFijaConMasDeLasQueCabenEsNoCaben`.
  - `UnaFilaMayorQueElMaximoEsNoCaben`.
  - `LaFila1ConMenosDeDosEsMenosDeDosBarras`.
  - `SinFilasFijasTodoQuedaComoAntes`: las pruebas viejas pasan sin tocarlas.
- [ ] **Paso 2:** `rtk dotnet test … --filter RepartidorFilasTests` → FALLAN.
- [ ] **Paso 3:** implementar en `RepartirCara`: primero arma la lista de cada fila con los grupos fijos y luego completa con los Auto, en orden, hasta `caben` por fila. Después, cada fila sigue como hoy (`Acomodar`, puestos de la fila 1 llena para las demás).
- [ ] **Paso 4:** `rtk dotnet test CQEING-Addins.sln -c Release` → en verde.
- [ ] **Paso 5:** commit `feat(vigas): elegir a mano en qué fila va cada tipo de barra (#84)`.

### Tarea 9: el selector de fila en el paso 1

**Archivos:** `addins/acero-vigas/Tablero/web/tablero.js` (filas de `sup` / `inf` y `opcionesDe.grupos`), `demo.js`

- [ ] **Paso 1:** cada fila `{n, d}` gana `f: ''` ('' = Auto) y, a su lado, el selector **Fila** de la maqueta (Auto, 1 … `maxCapas`). `grupos()` manda `fila: r.f === '' ? null : +r.f`.
- [ ] **Paso 2:** el texto "Fila 1: 5 · Fila 2: 1" sigue saliendo de `CaraVista.PorFila`, que ya calcula C#.
- [ ] **Paso 3: probar en Chrome:** 3 #5 en la fila 1 + 3 #4 en la fila 2 → el corte muestra 3 y 3. Una fila llena → el error de "máximo".
- [ ] **Paso 4:** commit `feat(vigas): selector de fila por tipo de barra en el paso 1 (#84)`.

### Tarea 10: prueba en Revit y cierre

- [ ] **Paso 1:** `rtk dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2026` y `powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2026`.
- [ ] **Paso 2:** en Revit 2026, con `PROY_JULIO_BETO.rvt` (y el modelo con voladizos de Carlos, si llegó), recorrer los criterios de aceptación uno por uno y mirarlos por el MCP de Revit. Anotar los valores en "Estado".
- [ ] **Paso 3:** actualizar `traslapos-vigas-notas.md` (§0 Traslapos / Bastones y §13), dejar las preguntas 1 a 5 como comentario en #79 y un comentario en #84 con lo hecho.
- [ ] **Paso 4:** commit `docs(vigas): edición probada en Revit (#84)`.

## Cómo probarlo

```
rtk dotnet test CQEING-Addins.sln -c Release
rtk dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2026
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2026
```

1. Barra: Subir, Bajar (gris con su motivo cuando corresponde), Automático; las vecinas cambian.
2. Traslapo: 0.80 con #5 → mensaje y Aplicar gris; 1.50 → el efecto; Aplicar; Volver al mínimo.
3. Bastón: 2.30 → 1.15 a cada lado; Usar el largo general.
4. Ganchos: S1 a 180° y de vuelta a 90°; bastón del borde Sin ↔ 90°.
5. Arrastre: traslapo de 0.50 en 0.50; barra por el centro; bastón con imán; voladizo con confirmación; Esc.
6. Filas: 3 #5 en la fila 1 + 3 #4 en la 2.
7. Cambiar de pórtico y volver: los cambios siguen.

## Estado

- [ ] Plan revisado con Juan.
- [x] Tarea 0: maqueta aprobada (versión 22; Juan pidió seguir con la tarea 1).
- [ ] Fase A (tareas 1–2).
  - [x] Tarea 1: `EditorTablero`, mensaje `previsualizar` / `previa` y `ArmadorVista.EntradaDe`. `rtk dotnet test CQEING-Addins.sln -c Release` → 536 en verde.
    - Ruling: `Salto` y `CambioArrastre` se declararon ya en la tarea 1 (los usa `PreviaVista`); su lógica sigue siendo de la tarea 6.
    - Ruling: el caso "no sale de la varilla" se prueba con I1, que en la maqueta ya mide 6.00; el bastón centrado, con BS2, porque BS1 se corta en el borde.
    - `UnCambioDeUnIdQueYaNoExisteAvisaYSigue` pasó al primer intento: el motor ya lo hacía. Queda como prueba de que no se rompa.
  - [x] Tarea 2: los paneles Barra, Traslapo y Bastón piden la vista previa a C# y aplican los cambios (`cambios` y `bastPuntas` en `OPCIONES0`). La página ya no calcula el efecto del traslapo. `demo.js` contesta `previsualizar` con las cuentas de la maqueta. 537 en verde.
    - **Probado en Chrome con la demostración:** Subir S1 → 3.50 Manual, Subir a 4.00; Automático. Traslapo S1–S2: 0.80 → *"No puede ser menor que el mínimo (0.90 m)."* y Aplicar gris; 1.50 → *"S2 pasa a 5.50 m… y el traslapo queda en 1.50 m."*; Aplicar y Volver al mínimo. BS2: 2.30 → 1.15 a cada lado; 0 → *"El largo debe ser mayor que 0."*; Usar el largo general. S1 en 3.50 → pórtico 2 (3.00 Automático) → pórtico 1 (3.50 Manual). Sin errores en la consola.
    - Ruling: el mensaje del traslapo sale en rojo solo si lo escrito es otro largo y no se puede; con el largo actual es gris (al aplicar o volver al mínimo no parece un error).
    - Ruling: "Volver al mínimo" pone en el campo el largo automático que llega con la vista siguiente, no el mínimo de la tabla.
    - Ruling: Aplicar del bastón no se desactiva (la maqueta lo dibuja siempre azul); si no se puede, muestra el motivo como aviso.
- [ ] Fase B (tareas 3–4).
  - Tarea 3, rulings anotados antes de programar:
    - `BastonSinGanchoEscritoNoSeRedondea` se prueba con BS2: BS1 está en la columna A y se corta en el borde, así que su parte recta nunca mide lo escrito.
    - `UnGanchoMasLargoQueNoSaleDeLaVarillaUsaLaLargaOAvisa` usa reglas de prueba con un gancho de 180° largo: en los cuadros de Carlos el de 90° es el más largo de cada diámetro, así que con ellos cambiar el gancho nunca alarga una barra.
    - `ParametrosCapa` lleva también el tipo de gancho de cada punta (`TipoInicio`, `TipoFin`), para que `CapaCalculada` se los ponga a las barras de borde.
    - `Baston` gana `PuntaInicioEnBorde` y `PuntaFinEnBorde`: en el bastón se permite "Sin", así que el largo del gancho no alcanza para saber dónde van los botones.
    - Una clave de gancho de un bastón que no existe (`"BS9.inicio"`) avisa `CambioNoAplicado` / `NoExiste`, como los demás cambios.
  - [x] Tarea 3: `TipoGancho`, `Reglas.LargoGancho`, `CambiosManuales.Ganchos`, ganchos por punta en `ParametrosCapa`, `CalculadorBastones` con el redondeo de la decisión 9, y la vista con `GanchoInicioTipo` / `GanchoFinTipo` / `PuntaInicioEnBorde` / `PuntaFinEnBorde`. `rtk dotnet test CQEING-Addins.sln -c Release` → 549 en verde.
    - Ejemplo de la decisión 9 en la maqueta: BS1 va de −0.15 a 1.00 (1.15); con 90° da 1.428, sube a 1.50 y la punta de adentro pasa a 1.072.
    - "Con gancho" del paso 2 (#89) sigue sin redondear (1.428): solo redondea el gancho elegido con los botones.
    - Para la tarea 4: la página manda los ganchos con el nombre del enum (`"De90"`, `"De135"`, `"De180"`, `"Sin"`), como los demás enums del protocolo.
  - Tarea 4, rulings anotados antes de programar:
    - El alzado dibuja cada gancho con su forma, como la maqueta: 90° una raya vertical, 135° en diagonal hacia adentro y 180° en U. Hasta ahora todos se veían de 90°.
    - Los botones van encima del dibujo, junto a la punta: arriba de las barras de arriba y abajo de las de abajo, sin salirse del alzado. Siguen la punta al hacer zoom o mover el dibujo.
    - Corregido al probar en Chrome: en los elementos de arriba, los botones van en la franja de arriba de la viga, justo encima de los rótulos de las barras y debajo de la leyenda, y no pegados al elemento: los de un bastón tapaban la barra S1 y su rótulo.
  - [x] Tarea 4: botones de gancho junto a la punta del borde (`alzado.js`), `onHook` → `cambios.ganchos` (`tablero.js`) y ganchos en la demostración (`demo.js`). 549 en verde.
    - **Probado en Chrome con la demostración:** S1 elegida → *90°·135°·180°* con 90° marcado; 180° → la punta se dibuja en U y avisa *"Gancho de 180° en la punta izquierda de las barras de arriba"*. BS1 → *Sin·90°·135°·180°*; 90° → pasa de 1.15 a 1.50; Sin → vuelve a 1.15; 135° se dibuja en diagonal. BS2 (columna B, no llega al borde) no tiene botones. I1 (abajo) los tiene debajo de la barra. Al acercar y ajustar, los botones siguen la punta. Sin errores en la consola.
    - En la demostración, una corrida que cambia de gancho conserva su largo (no hay motor que la vuelva a resolver); el bastón sí redondea como en C#. El gancho de 180° de cada diámetro va en `demo.js`, porque la pantalla de Reglas no lo muestra.
- [ ] Fase C (tareas 5–7).
- [ ] Fase D (tareas 8–9).
- [ ] Probado en Revit (tarea 10).
