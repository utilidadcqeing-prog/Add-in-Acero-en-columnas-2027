# Plan: acero en viguetas dentro del add-in de vigas (#938)

## Contexto

- **Qué se pide**: que el add-in **Acero en vigas** también arme las **viguetas** (losa nervada), con las mismas
  opciones que ya tiene para vigas. Lo pidió Carlos Quintero en la reunión del 24-oct-2026 (transcripción:
  `Desktop\addin vigas\Meeting Transcription 24-2.txt`). Issue: utilidadcqeing-prog/CQEING_BaseDeDatos#938.
- **Lo que dijo Carlos** (minuto de la reunión):
  - 07:20, 08:11, 14:03 y 15:19: el armado es igual al de las vigas. Valen los mismos estribos, zonas de
    confinamiento, ganchos, longitudes y redondeo. **Lo único que cambia son los apoyos**: la vigueta se apoya en
    vigas, y solo en las que a su vez se apoyan en columnas.
  - 04:36, 06:33 y 15:03: una viga que no llega a columnas "no existe" como apoyo. Lo que pasa del último apoyo
    real es voladizo.
  - 07:20: casi siempre lleva **una barra por fila**, una arriba y una abajo. Dos por fila solo si se cumple la
    separación de 2.5 cm.
  - 08:11: los **bastones van por dentro**. El de arriba va debajo de la barra corrida de arriba y el de abajo
    encima de la de abajo.
  - 11:30–13:19: viga o vigueta no se puede decidir solo por el tamaño, porque en proyectos grandes hay viguetas de
    15 y 20 cm. El add-in **sugiere** y **el usuario decide**, igual que con Aérea/Cimentación.
  - 28:31–29:29: la **vigueta de borde** (VB, 10 cm, en voladizos y balcones) lleva un armado mínimo "solo para
    amarrar el acero".
  - 03:53: en el cruce mandan los estribos de la viga principal. Eso ya está hecho (commit `fb0dc4d`).
- **Fuera de alcance**:
  - El add-in que **crea** las viguetas (repartirlas equidistantes en un panel y manejar vacíos). Juan lo deja para
    otro add-in (min 44:00).
  - Colorear las viguetas en las vistas (min 14:03, "más adelante").
- **Entorno**: Revit 2026 y 2027. Modelo de prueba `PROY_JULIO_BETO`, nivel N+3.70. Repo `practicacq`, rama
  nueva desde `main` (por ejemplo `vigas-viguetas-938`).

## Criterios de aceptación

- [ ] Al abrir el tablero con `PROY_JULIO_BETO`, el add-in **detecta solo** las 17 viguetas (14 `VT_10X35` y 3
      `VB_10X35`) y las 15 vigas `VIG_*`, sin que el usuario toque nada.
- [ ] Si se equivoca, el usuario lo corrige con la casilla "Es vigueta", que es lo único nuevo en el tablero.
- [ ] Una vigueta acepta **1 barra arriba y 1 abajo** y se genera en Revit. Hoy da error `MenosDeDosBarras`.
- [ ] Las demás opciones son las de vigas: diámetros, recubrimientos, bastones, estribos, ganchos, traslapos,
      cortes y choques. No hay una pantalla aparte.
- [ ] Los apoyos de una vigueta son solo las vigas que se apoyan en columnas o muros. Pasado el último apoyo, es
      voladizo.
- [ ] Un bastón de vigueta queda por dentro (fila 2) de su barra corrida.
- [ ] Los estribos de la vigueta se cortan en las caras de las vigas principales.
- [ ] Las viguetas no muestran el aviso "sin eje" y se llaman "Vigueta …", no "Pórtico sin eje N".
- [ ] Las vigas siguen igual: con 1 barra en una cara, una viga da error como antes.

## Hallazgos del código y de Revit real

Medido el 26-oct-2026 en Revit 2026 con `PROY_JULIO_BETO`, corriendo el propio motor
(`addins/acero-vigas/bin/Debug/2026/`) por reflexión desde el MCP de Revit, sin modificar el modelo.

**H1. El modelo usa nombres de tipo claros.** Hay 32 elementos de armazón estructural:

| Tipo | Cantidad | Columnas que toca |
|---|---|---|
| `VIG_25X35` | 14 (7 por nivel) | 3 o 4 cada una |
| `VIG_20X35` | 1 | ninguna. Se apoya en una vigueta y en una viga |
| `VT_10X35` (vigueta) | 14 | ninguna |
| `VB_10X35` (vigueta de borde) | 3 | ninguna |

Todas tienen `StructuralUsage = Other` o `Girder`, así que ese dato no sirve para separarlas. Carlos dijo
(02:30) que una viga mide como mínimo 20 cm de ancho.

**H2. El add-in ya lee las viguetas como pórticos.** `AgrupadorPorticos.Agrupar` devuelve "Pórtico sin eje 1"
hasta "Pórtico sin eje 18", con apoyos `Viga`. Las vigas que cruza y que se apoyan en columnas ya cuentan como
apoyo (`PrincipalQueLaCarga`, `AgrupadorPorticos.cs:303`). Las VB y la `VIG_20X35` no tocan columnas, así que no
cuentan como apoyo y la vigueta queda en voladizo hasta ellas. Ejemplo, la VT #389858:
`Voladizo 1325 | 1-2 3250 | 2-3 3730 | 3-4 3730 | Voladizo 1525`.
Esto ya es lo que pide Carlos. **No hace falta tocar los apoyos**, solo dejarlo cubierto con pruebas.

**H3. Lo que impide generar es la regla de mínimo 2 barras.** Con la vigueta #389858 y con la VB #388864:

| Opciones | Resultado |
|---|---|
| Las de fábrica (3#5, recubrimiento 4) | `NoCaben` |
| 1#4 / 1#4, recubrimiento 4 | `MenosDeDosBarras` |
| 1#4 / 1#4, recubrimiento 2.5 | `MenosDeDosBarras` |
| 2#3 / 2#3, recubrimiento 2.5 | se genera |

La regla está en `Seccion/RepartidorFilas.cs:33` y `:44`. El tablero muestra "El estribo cerrado necesita al
menos 2 barras en esta cara." (`Tablero/web/tablero.js:440`, y también en `demo.js:211`). Es lo que Juan vio en
la reunión ("no tiene la configuración").

**H4. Los bastones ya irían por dentro.** Si en la fila 1 no hay hueco (`caben = 1`), `UbicadorBastones` busca
puesto en la fila siguiente (`UbicadorBastones.cs:72`). Así queda el bastón por dentro, como pide Carlos. Falta
comprobarlo cuando la fila 1 tenga una sola barra.

**H5. Aérea o Cimentación ya funciona como "sugiere y el usuario decide".** `AgrupadorPorticos.TipoDe` lo
sugiere por el nivel. `OpcionesTablero.Tipo` (null = lo que se leyó) guarda lo que elige el usuario. El tablero lo
muestra con dos botones, "Detectado por el nivel" (`tablero.js:1089`). `ConfiguracionGuardada` guarda las
`OpcionesTablero` completas, así que un campo nuevo se guarda solo.

**H6. El lector no trae el nombre del tipo.** `Revit/LectorModelo.cs` arma `VigaLeida` sin `Symbol.Name`.

**H7 (aparte, no es de este plan).** En los pórticos 1 a 4 (`VIG_25X35` de 6750) los extremos salen con apoyo
`Viga` aunque ahí hay columna. `Unir` junta la columna con la viga y se queda con el tipo del primero. Se anota
para otra issue.

## Decisiones

1. **Vigueta es un dato aparte de Aérea/Cimentación** (2026-10-26, Claude): se agrega `Elemento { Viga, Vigueta }`
   y `TipoViga` queda como está. Una vigueta sigue siendo aérea para los traslapos. Meter "Vigueta" dentro de
   `TipoViga` obligaría a revisar cada `== TipoViga.Aerea` del motor.
2. **Cómo se sugiere que es vigueta** (2026-10-26, Claude): solo si ninguno de sus apoyos es columna o muro **y**
   se cumple al menos una de dos cosas:
   - el nombre del tipo empieza por `VT`, `VB` o contiene `vigueta` (sin importar mayúsculas ni tildes);
   - el ancho es menor de 200 mm (Carlos, 02:30).

   Si no, es Viga. Siempre se puede corregir en el tablero, como pidió Carlos (12:53).
3. **Las mismas opciones, con otro arranque** (2026-10-26, Claude): no hay pantalla aparte. Una vigueta abre el
   tablero con **1#4 arriba y 1#4 abajo** y todo lo demás igual a `OpcionesTablero.PorDefecto`.
   ~~*Por confirmar con Carlos*: el diámetro y el recubrimiento habituales.~~ **Recubrimiento 3 cm** (2026-10-26, Juan),
   como en el plano de referencia `005-001_PLN_ESTR…`, hoja de la **Vigueta T1 (0.10 × 0.35), N+3.70**:
   - arriba y abajo 1 #4, con ganchos de 0.22 y traslapo de 0.78;
   - estribos #3, 7 @ 0.15 junto a los apoyos y 4–5 @ 0.30 al centro;
   - el corte da 0.03 de la cara a la barra, arriba y abajo.
8. **En la vigueta, el estribo es una grapa en S** (2026-10-26, Juan, por el corte de la Vigueta T1): no hay cerrado.
   La grapa sube por el centro de la sección, abraza la barra de arriba y la de abajo con un gancho cada una, uno hacia
   cada lado (patas de 0.13), y mide 0.29 de alto en 0.35 (0.03 de recubrimiento arriba y abajo). Se reutiliza la
   grapa abierta en S que ya existe para los adicionales (plan 2026-10-22-estribo-180-y-grapa-s-vigas.md). Siguen las
   mismas opciones: diámetro, gancho, zonas y separaciones. La transcripción del 24-oct no lo dice (Carlos solo dijo
   "el estribo exactamente casi lo mismo"); sale del plano.
4. **Una barra por cara solo en viguetas** (2026-10-26, Claude): en una vigueta, `MenosDeDosBarras` no aplica. En
   una viga sigue igual. El estribo es el mismo cerrado de las vigas. La barra queda al centro y el estribo la
   envuelve. *Por verificar en Revit* que `RebarShape` cerrado con una sola barra por cara se ve bien.
5. **Vigueta de borde = vigueta** (2026-10-26, Claude): misma regla y mismo arranque. *Pendiente con Carlos*:
   cuál es su "armado mínimo" (min 29:29). Hasta que lo diga, el usuario lo pone a mano.
6. **Nombre en la lista** (2026-10-26, Claude): una vigueta sin eje se llama `Vigueta N` (la cuenta es una sola
   para todo el modelo, igual que "Pórtico sin eje N") y no saca el
   aviso `SinEje`. Una vigueta que sí cae en un eje conserva el nombre del eje.
7. **El tablero cambia lo mínimo** (2026-10-26, Juan): el add-in **detecta solo** que es vigueta (decisión 2) y con
   eso aplica todo lo demás (decisiones 3 a 6). No hay botones nuevos. Dentro del bloque "Tipo de viga" que ya
   existe se agrega una sola casilla, **"Es vigueta"**, que ya viene marcada o desmarcada según la detección. Solo
   se usa si el add-in se equivocó. Al volver a lo detectado, la corrección se borra (queda null) y el pórtico sigue
   lo que diga la detección.
   ~~Una casilla "Es vigueta" con una nota debajo.~~ **Cambio** (2026-10-26, Juan, después de verla en Revit): se quita
   el texto, también la nota "Detectado por el nivel…". ~~Dos botones Viga | Vigueta arriba de "Tipo de viga".~~ Al
   verlo en Revit pesaba demasiado. De las 5 opciones de la página de muestra (artifact "Botón Vigueta"), Juan eligió
   la **A**: un **chip "Vigueta"** chico (24 px, redondo) a la derecha del título "Tipo de viga". Apagado es gris
   (`#C9CFD6` / `#5E6670`) y encendido azul claro (`#EAF1FB` / `#B9CDEB` / `#1F5FAF`). Con un clic se enciende o se
   apaga.

## Plan paso a paso

Cada paso empieza con su prueba unitaria (TDD) en `calculo/CQEING.Vigas.Calculo.Tests`.

1. **Leer el nombre del tipo**
   - Archivos: `Porticos/ModeloLeido.cs` (`VigaLeida` gana `string? NombreTipo = null`) y `Revit/LectorModelo.cs`
     (`viga.Symbol.Name`).
   - Cómo se prueba: compila, y las pruebas que ya existen siguen en verde porque el campo es opcional.
2. **Sugerir viga o vigueta** (decisiones 1, 2 y 6)
   - Archivos: `Modelo/Entrada.cs` (`enum Elemento`), `Porticos/ModeloLeido.cs` (`PorticoLeido.Elemento`) y
     `Porticos/AgrupadorPorticos.cs` (`ElementoDe`, nombre `Vigueta N`, sin `SinEje`).
   - Cómo se prueba, en `AgrupadorPorticosTests`:
     - una `VT_10X35` entre dos vigas sobre columnas es Vigueta;
     - una `VIG_25X35` sobre columnas es Viga;
     - una de 150 mm sin columnas y con nombre neutro es Vigueta;
     - una de 250 mm sin columnas y con nombre neutro es Viga;
     - una vigueta que pasa de la última principal tiene voladizo (H2).
3. **Corregir a mano si se equivocó** (decisión 7)
   - Archivos: `Tablero/OpcionesTablero.cs` (`Elemento? Elemento`, null = lo detectado), `Tablero/ArmadorVista.cs`
     (manda `esVigueta` y de dónde salió), `Tablero/web/tablero.js` (la casilla **"Es vigueta"** dentro del bloque
     "Tipo de viga" que ya existe), `Tablero/web/demo.js` (igual en la demostración). No se agregan botones, pasos ni
     pantallas.
   - Cómo se prueba: `ArmadorVistaTests`, más un caso en `ConfiguracionGuardadaTests` que guarda y lee
     `Elemento = Vigueta`. Una configuración guardada antes, sin el campo, se lee como null.
4. **Una barra por cara en viguetas** (decisión 4)
   - Archivos: `Modelo/Seccion.cs` (`EntradaSeccion.PermiteUnaBarra`), `Tablero/CalculoPortico.cs` (lo pasa si el
     elemento es Vigueta), `Seccion/RepartidorFilas.cs:33,44`, el mensaje de `tablero.js:440` y `demo.js:211`.
   - Cómo se prueba, en `RepartidorFilasTests`:
     - 1#4 en 100 mm de vigueta da `Ninguno` con la barra centrada;
     - 1#4 en una viga sigue en `MenosDeDosBarras`;
     - 2#3 en 100 mm con recubrimiento 2.5 da 2 por fila solo si cumple 25 mm libres.

     En `BastonesTests` / `UbicadorBastonesTests`: 1#4 corrida con un bastón 1#4 deja el bastón en la fila 2
     (H4). En `PlanificadorAceroTests`: la vigueta se planifica sin error.
5. **Arranque de vigueta** (decisión 3)
   - Archivos: donde se crean las opciones iniciales del pórtico (`SesionTablero` / `PuenteTablero`). Si es
     vigueta y no hay configuración guardada, se usa 1#4 / 1#4.
   - Cómo se prueba: una prueba de sesión, y en el tablero se ve 1#4 al elegir una vigueta.
6. **Revit real**
   - Archivos: ninguno.
   - Cómo se prueba: la lista de "Cómo probarlo". Capturas en `docs/planes/imagenes/2026-10-26-viguetas-*.png`.

## Cómo probarlo

```
dotnet test addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests/CQEING.Vigas.Calculo.Tests.csproj
dotnet build addins/acero-vigas/CQEING.Vigas.csproj -c Debug -p:RevitVersion=2027
Get-Process -Name Revit -ErrorAction SilentlyContinue   # tiene que estar cerrado
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2027
```

En Revit, con `PROY_JULIO_BETO`:

1. Abrir el tablero sin tocar nada. Las VT y VB salen con "Es vigueta" marcada, y las `VIG_*` y la `VIG_20X35`
   #389664 sin marcar. Ninguna vigueta muestra el aviso "sin eje".
2. Elegir la VT #389858. Arranca con 1#4 / 1#4. Tiene 3 luces y 2 voladizos (H2) y no muestra errores en el corte.
3. Generar. En 3D se ve:
   - una barra arriba y una abajo, continuas, con traslapos donde corresponde;
   - estribos cerrados que se cortan en las caras de las cuatro `VIG_25X35` que cruza.
4. Ponerle un bastón 1#4 arriba en un apoyo. En el corte queda debajo de la barra corrida.
5. Desmarcar "Es vigueta" en la VT #389858. Vuelve el error de 2 barras. Al marcarla otra vez, se va.
6. Cerrar y abrir el tablero. La elección y las barras de la vigueta se mantienen.
7. Generar una VB (#388864) y una viga `VIG_25X35` para confirmar que las vigas no cambiaron.

## Estado

- [x] Plan revisado con el usuario (2026-10-26). Juan pidió que el tablero cambie lo mínimo (decisión 7).
- [x] **#939, pasos 1 y 2** (2026-10-26), rama `vigas-viguetas-938`:
  - `Modelo/Entrada.cs`: `enum Elemento`.
  - `Porticos/ModeloLeido.cs`: `VigaLeida.NombreTipo` y `PorticoLeido.Elemento`.
  - `Porticos/AgrupadorPorticos.cs`: `ClaseDe`, `NombreDeVigueta`, el nombre `Vigueta N` y quitar `SinEje` en
    viguetas.
  - `Revit/LectorModelo.cs`: `Symbol.Name`.
  - Pruebas: 11 nuevas en `AgrupadorPorticosTests`. Antes de implementar fallaban 9; las 2 que cuidan que las
    vigas sigan igual ya pasaban. La suite completa pasa (1000 pruebas) y el build de 2027 sale sin advertencias.
  - Revit 2026, `PROY_JULIO_BETO`, cargando la DLL nueva en memoria desde el MCP: las 17 VT/VB salen como Vigueta
    ("Vigueta 1…17") y las 15 `VIG_*` como Viga, incluida la `VIG_20X35` #389664. Los avisos `SinEje` bajan de 18
    a 1 (la `VIG_20X35`, que no está sobre un eje).
  - Commit `dd4a4fe`.
- [x] **#940, paso 3** (2026-10-26):
  - `Tablero/OpcionesTablero.cs`: `Elemento? Elemento`.
  - `Tablero/VistaTablero.cs`: `VistaPortico.EsVigueta` y `DetectadaVigueta`.
  - `Tablero/ArmadorVista.cs`: lo que eligió el usuario manda sobre lo detectado, y en la lista sale la etiqueta
    "Vigueta".
  - `Tablero/web/tablero.js`: la clave `vigueta` en `OPCIONES0`, `elemento` en `opcionesDe`, y la casilla con su
    nota dentro del bloque "Tipo de viga".
  - `Tablero/web/demo.js`: `esVigueta` y `detectadaVigueta` (los pórticos de la demostración son vigas).
  - Pruebas: 3 nuevas en `ArmadorVistaTests`, que fallaron antes de implementar. Hay 2 más en
    `ConfiguracionGuardadaTests` que pasaron desde el principio porque ya se guardan las opciones completas (H5);
    se quedan para cuidar que siga así. La suite completa pasa (1005 pruebas), el build de 2027 sale limpio y
    `node --check` pasa en los dos JS.
  - Commits `3a7849d` (casilla) y `955cb5a` (chip, después de verlo en Revit; ver decisión 7).
- [x] **#941, paso 4** (2026-10-26):
  - `Modelo/Seccion.cs`: `EntradaSeccion.PermiteUnaBarra`.
  - `Tablero/CalculoPortico.cs`: lo pasa si `(o.Elemento ?? p.Elemento) == Vigueta`.
  - `Seccion/RepartidorFilas.cs`: el mínimo es 1 en viguetas y 2 en vigas, en las dos reglas.
  - `Tablero/web/demo.js`: la misma regla.
  - Pruebas nuevas:
    - `RepartidorFilasTests`, 3: una barra centrada en X = 50; 2#3 van una por fila; fila 1 fija con una barra.
    - `UbicadorBastonesTests`, 2: el bastón va en la fila 2.
    - `CalculoPorticoTests`, 3: la vigueta se genera, la viga no, y manda lo que eligió el usuario.
    - `PlanificadorAceroTests`, 1.

    Antes de implementar fallaron 5. Las de "viga sigue con error" y "2#3 una por fila" ya pasaban: cuidan lo que
    ya funcionaba.
  - **Hallazgo H8** (fallo que ya estaba en el código y que la vigueta destapó):
    - Qué pasaba: el plan de generación reventaba con "Sequence contains no elements" en
      `UbicadorBastones.ConAltura`. Con un solo puesto por fila, si la barra que traslapa ocupa la fila 2, el
      bastón sube a la fila 3 y la fila 2 queda vacía. Al calcular su altura se le pedía el diámetro mayor a esa
      fila vacía.
    - Corrección: la fila vacía sube como la fila 1, porque la barra que la ocupa es de la corrida.
    - Prueba: `EnUnaVigueta_SiLaFila2LaOcupaUnTraslapo_ElBastonVaEncimaEnLaFila3`. Falló antes del arreglo.
    - Resultado: en la vigueta, el bastón queda en la fila 2, o en la 3 donde la corrida traslapa. Sigue por dentro.
  - La suite completa pasa (1014 pruebas). Commit `72293a1`.
  - En Revit (Juan, captura del 26-oct): la Vigueta 6 con 1 #4 y recubrimiento 4 decía "caben 0 por fila". En 10 cm
    quedan 100 − 80 − 19 ≈ 1 mm libres. Por eso el arranque de vigueta también lleva recubrimiento 2.5.
- [x] **#942, paso 5** (2026-10-26):
  - **Hallazgo H9:** el arranque está en dos lados que tienen que coincidir. C# (`SesionTablero.Opciones`) calcula
    la primera vista y la página (`OPCIONES0` en `tablero.js`) llena el panel.
  - `Tablero/OpcionesTablero.cs`: `DeVigueta` (1 #4 / 1 #4, recubrimiento 2.5) y `Para(Elemento)`.
  - `Tablero/SesionTablero.cs`: un pórtico sin opciones usa `Para` con lo detectado.
  - `Tablero/web/tablero.js`:
    - `VIGUETA0` y `arranqueDe`. Al abrir un pórtico detectado como vigueta y sin nada guardado, arranca con
      `VIGUETA0`, también el primero que se abre.
    - Al cambiar el chip, si las barras y el recubrimiento siguen como arrancaron, pasan a los del otro tipo; lo
      que el usuario ya cambió se queda.
  - Pruebas: 2 nuevas en `SesionTableroTests`, que fallaron antes de implementar. La suite completa pasa (1016
    pruebas) y `node --check` también. Después, el recubrimiento pasó de 2.5 a 3 (decisión 3).
- **Hallazgo H10** (2026-10-26, al generar la Vigueta 6 en Revit): "No se pudo hacer en Revit: An internal error has
  occurred."
  - El mensaje no decía dónde fallaba. Por eso `AccionesRevit` ahora guarda cada error completo en
    `%AppData%\CQEING\vigas\errores.log`.
  - El registro mostró `InternalException` en `RebarShapeDrivenAccessor.ScaleToBox`, llamado desde
    `CreadorEstribos.Cerrado`.
  - Medido en Revit con subtransacción y deshacer: `ScaleToBox` revienta si la caja por fuera es menor que el doblado
    del estribo más dos diámetros.
    - #3 (doblado 38): falla con 55 mm o menos y sale con 60.
    - 8 mm (doblado 32): falla con 45 y sale con 50.
    - #4 (doblado 50.8): falla con 70 y sale con 80.
  - La vigueta de 10 cm con recubrimiento 2.5 deja 50 mm. `GeneradorAcero` no atrapaba `InternalException` y se caía
    toda la generación.
  - Arreglo de protección:
    - `CreadorEstribos.Cerrado` borra el estribo a medio crear y devuelve null.
    - `GeneradorAcero` también atrapa `InternalException`. Ese grupo se anota en el resumen y lo demás se genera.
  - El arreglo de fondo es la decisión 8: la vigueta no lleva cerrado. Commit `82e57c9`.
- [x] **Grapa en S en la vigueta, decisión 8** (2026-10-26):
  - `Modelo/Estribos.cs`: `EntradaEstribos.Principal`, con `Cerrado` por defecto.
  - `Estribos/MotorEstribos.cs`: con `Abierto`, el `E` es una grapa en S en X = ancho/2, de recubrimiento a
    recubrimiento. Su largo es una rama más dos ganchos.
  - `Tablero/CalculoPortico.cs`: `EsVigueta` decide la grapa y la barra única.
  - `Tablero/ArmadorVista.cs`: la grapa lleva su trazo en el corte y en la lista se llama "Grapa en S".
  - `Tablero/web/tablero.js`: con grapa no se dibuja el rectángulo del cerrado, se dibuja su trazo, y el texto del
    corte dice "Grapa en S #3".
  - El plan de generación (`RamaDe`), Revit (`CreadorEstribos.Abierto`, `CorregirAbierto`) y la vista previa ya
    manejaban abiertos en S, así que no cambian.
  - Pruebas: 3 en `MotorEstribosTests`, 1 en `CalculoPorticoTests` y 1 en `ArmadorVistaTests`, más la grapa con su
    rama en X = 50 dentro de la prueba de vigueta de `PlanificadorAceroTests`. Las 6 fallaron antes de implementar. La
    suite completa pasa (1021 pruebas).
  - Desplegado en local (Revit 2026). Falta generarla en Revit.
- [ ] Pendiente con Carlos: diámetro y recubrimiento de arranque de vigueta (decisión 3) y armado mínimo de la
      vigueta de borde (decisión 5).
- [ ] Pasos implementados.
- [ ] Probado en Revit.
