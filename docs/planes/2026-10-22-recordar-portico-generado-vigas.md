# Plan: el tablero recuerda la configuración de un pórtico generado (vigas)

## Contexto

- **Qué se pide** (Juan, 22-oct-2026, probando en Revit): al cerrar y volver a abrir Revit y seleccionar un pórtico
  ya generado, el tablero tiene que mostrar con qué se generó (filas, barras, estribos, ganchos, adicionales, cambios a
  mano). Hoy arranca con los valores por defecto.
- **Fuera de alcance:**
  - guardar sin generar;
  - comparar lo guardado con el acero que hay en el modelo (si alguien lo editó a mano en Revit);
  - las reglas, que ya se guardan aparte para todos los proyectos.
  - **Siguiente paso, propuesto por Juan (22-oct):** "En vivo" de verdad. Que lo que se mueva a mano en Revit (una
    barra o un estribo generado) lo vea el tablero mientras está abierto. Necesita escuchar los cambios del documento
    (`DocumentChanged`) y comparar con la configuración guardada aquí. Lleva su propio plan; este es la base.
- **Entorno:** Revit 2026 y 2027, `addins/acero-vigas`. Rama `vigas-estribo-180`, la misma de la tarea del 180°, que
  todavía no tiene commit.

## Criterios de aceptación

- [ ] Al generar, cada viga de cada pórtico generado guarda su configuración en el `.rvt`, en la misma transacción
      del acero: un solo deshacer quita las dos cosas.
- [ ] Al abrir el tablero, o al recargar después de abrir otro documento, un pórtico con configuración guardada
      muestra esa configuración en todos los pasos, y el cálculo de C# usa esas mismas opciones.
- [ ] Lo que se cambie en esta sesión manda sobre lo guardado mientras Revit siga abierto.
- [ ] Un pórtico sin nada guardado sigue arrancando con los valores por defecto.
- [ ] Una configuración rota o de otra versión se ignora: no rompe el tablero.
- [ ] `rtk dotnet test CQEING-Addins.sln -c Release` en verde.

## Hallazgos del código

**H1. Las opciones del cálculo solo viven en memoria.** `SesionTablero.opciones` (`SesionTablero.cs:52`) guarda las
opciones de cada pórtico mientras dura la sesión.

**H2. La página también guarda su estado solo en memoria.** Lo hace por pórtico en `tablero.js`
(`opcionesPorPortico`, líneas 151 y 269-281), con las claves de `OPCIONES0`. Ese estado está "como lo escribe la
pantalla" (metros como texto, `'135°'`, adicionales con nombre), así que no se puede rehacer exacto solo con
`OpcionesTablero`.

**H3. En el modelo solo queda una marca en cada barra.** `MarcaAcero` (`Revit/MarcaAcero.cs`) guarda el pórtico y la
pieza en Extensible Storage, nada más.

**H4. Generar es una sola transacción.** `GeneradorAcero.Generar` (`Revit/GeneradorAcero.cs:36-92`) crea el acero de
todos los pórticos en una transacción.

## Decisiones

1. **Se guarda al generar** (2026-10-22, Juan). Va en la transacción de `GeneradorAcero`. Lo que se recupera es
   exactamente con lo que se generó.
2. **Se guarda en las vigas del pórtico** (2026-10-22, Juan). Cada viga lleva la configuración en Extensible Storage,
   con un esquema propio, `CQEINGVigasTablero`. Así se encuentra aunque cambie el nombre del pórtico. Si las vigas no
   tienen lo mismo, manda la primera que tenga algo.
3. **Se guardan dos cosas: las opciones de C# y el estado de la página.** La configuración es un JSON
   `{ "version": 1, "opciones": OpcionesTablero, "estado": "<estado de la página>" }`:
   - las opciones las usa el cálculo de C#;
   - el estado (las claves de `OPCIONES0`, como texto JSON) lo devuelve tal cual a la página, para no tener que
     convertir `OpcionesTablero` a la pantalla (H2).

   `ConfiguracionGuardada` (en Calculo, sin Revit) escribe y lee ese JSON. Si no lo entiende, devuelve null.
4. **Cómo viaja:**
   - `Generar` trae `estados: { id: "<json>" }` desde la página.
   - `SesionTablero` arma la configuración de cada pórtico con sus opciones y su estado: el que mandó la página o, si
     no mandó, el recuperado.
   - `PorticoAGenerar.Configuracion` la lleva a Revit.
   - Al leer el modelo, `SesionTablero` recibe `configuracionDeViga(id)` y, a los pórticos que no tienen opciones en
     memoria, les carga las guardadas.
   - `VistaPortico.Estado` manda el estado a la página, que lo usa si no tiene uno propio en memoria para ese pórtico.

## Plan paso a paso

1. **`ConfiguracionGuardada`** en `calculo/.../Tablero/`. Pruebas:
   - ida y vuelta con 180°, una grapa en S y cambios a mano;
   - un JSON roto da null;
   - otra versión da null.
2. **Protocolo:** `Generar(Ids, Estados?)`. Prueba en `ProtocoloTableroTests`.
3. **`SesionTablero`:**
   - parámetro `configuracionDeViga`, con su uso en `Recargar`;
   - `estados`;
   - `PorticoAGenerar.Configuracion`;
   - `VistaPortico.Estado`.

   Pruebas en `SesionTableroTests`:
   - lo guardado se recupera al leer;
   - lo cambiado en la sesión manda;
   - generar lleva la configuración.
4. **Revit:**
   - `ConfiguracionPortico` (Extensible Storage) con `Guardar(viga, json)` y `Leer(viga)`;
   - `GeneradorAcero` la escribe en cada viga;
   - `PuenteTablero` pasa la lectura a la sesión.

   Cómo se prueba: compila para 2026 y 2027, y se mide en Revit.
5. **Página** (`tablero.js`):
   - `generar` manda `estados` (el del pórtico abierto y los de `opcionesPorPortico`);
   - al abrir un pórtico sin estado en memoria, usa `vista.actual.estado`.
6. **Prueba en Revit:**
   - generar un pórtico con 180°, una grapa en S y cambios a mano;
   - cerrar Revit, abrirlo y seleccionar una viga del pórtico;
   - el tablero muestra lo mismo.

## Estado

- [x] Plan revisado con Juan (decisiones 1 y 2).
- [x] **Pasos 1-5**, 22-oct, sin commit.
  - **Paso 1:** `ConfiguracionGuardada`, con `ConfiguracionGuardadaTests`: ida y vuelta, sin estado, y 4 casos rotos u
    otra versión. `ProtocoloTablero.Opciones` pasa a `internal` para leer las opciones con los mismos valores por
    defecto que un `calcular`.
  - **Paso 2:** `Generar(Ids, Estados?)`, con `GenerarTraeElEstadoDeLaPaginaDeCadaPortico`.
  - **Paso 3:** en `SesionTablero`, `configuracionDeViga`, `estados`, `Recargar` y `AGenerar` arman y llevan
    `PorticoAGenerar.Configuracion`; se suma `VistaPortico.Estado`. Pruebas:
    - `LoGuardadoEnLasVigasSeRecuperaAlLeerElModelo`;
    - `SinNadaGuardadoArrancaConLosValoresPorDefecto`;
    - `LoCambiadoEnLaSesionMandaSobreLoGuardado`;
    - `GenerarLlevaLaConfiguracionConElEstadoQueMandaLaPagina`;
    - `SinEstadoDeLaPagina_GenerarLlevaElRecuperado`.
  - **Paso 4:** `Revit/ConfiguracionPortico.cs`, con el esquema `CQEINGVigasTablero` (GUID
    b3e7d1a4-6c25-4f98-9a0b-2d84e6c1f573). `GeneradorAcero` lo escribe en cada viga después de borrar lo anterior;
    `PuenteTablero.ConfiguracionDeViga` lo lee.
  - **Paso 5:** en `tablero.js`, `estadoDeLaPagina`, `estadosParaGenerar` y `estadoGuardado`; al abrir un pórtico sin
    estado en memoria, se usa el guardado.

  Comprobación: la suite da 829 en verde, el build de 2027 da 0 errores y 0 advertencias, y `node --check` pasa.
  Desplegado en Revit 2026, con Revit cerrado.
- [ ] Probado en Revit (paso 6).
