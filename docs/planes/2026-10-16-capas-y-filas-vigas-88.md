# Plan: capas y filas de barras en vigas (#88)

## Contexto

- **Qué se pide:** la issue [#88](https://github.com/utilidadcqeing-prog/Add-in-Acero-en-columnas-2027/issues/88) (épica #78, tercera en el orden de trabajo).
  - Repartir las barras de cada cara (arriba y abajo) en filas, según lo que cabe en el ancho de la viga.
  - Decirle al tablero cuántas caben por fila y dónde va cada barra en la sección (corte 1-1).
  - Lo decidió Carlos en la reunión del 16-oct-2026 (min 51:44–57:13 y 1:14:54–1:18:26).
- **Qué ve el usuario** (maqueta, paso 1 "Barras", https://claude.ai/artifact/3BrJfHRe2rEGmG4ztFxhFh):
  - "caben N por fila" junto a la cantidad de barras;
  - puntos por fila;
  - el mensaje "fila 1: 5 · fila 2: 2…" o el error en rojo;
  - en "Más opciones": recubrimiento por cara, fila pegada o separada, y máximo de filas;
  - el corte 1-1 dibuja las filas reales;
  - el dibujo del recubrimiento muestra el libre y el "al eje".
- **Referencia única:** la maqueta, opción 1 con todas sus vistas. Así va a quedar el add-in. No se toma nada del add-in de columnas, ni su código ni su interfaz (pedido de Juan, 2026-10-16).
- **Fuera de alcance:**
  - bastones que ocupan puestos libres (#89);
  - estribos adicionales (#90);
  - el dibujo del corte (#91);
  - la ventana (#83);
  - el caso "barra traslapada al lado o abajo", pendiente 2 de #79.
- **Entorno:**
  - misma librería que el motor (`addins/acero-vigas/calculo/CQEING.Vigas.Calculo`, `net8.0`, sin Revit) y mismas pruebas xUnit;
  - rama `vigas-filas-88`, que sale de `vigas-motor-80` (PR #93) porque usa sus `Reglas` y `GrupoBarras`.

## Criterios de aceptación

De #88:

Cada criterio cumplido lleva entre paréntesis la prueba que lo comprueba (en `RepartidorFilasTests`, salvo que se indique otra clase).

- [x] Capacidad por fila calculada con el ancho, el recubrimiento libre de los lados, el estribo, el diámetro y la distancia libre (2.5 cm por defecto) (`EnLaSeccionDeLaMaqueta_Caben5PorFila`, `ReglasTests`).
- [x] Junto a la cantidad se informa "caben N por fila". Si se piden más de las que caben en todas las filas: error con el máximo (`MasDeLasQueCabenEnLasTresFilas_ErrorConElMaximo`, `UnaVigaDondeNoCabeNinguna_ErrorConMaximoCero`).
- [x] Las barras de una fila quedan a la misma distancia unas de otras (`LaFila1VaDeEsquinaAEsquinaALaMismaDistancia`).
- [x] Lo que no cabe en la fila 1 pasa a la fila 2, debajo de las esquinas y hacia adentro (`SieteBarras_Van5EnLaFila1Y2EnLaFila2`, `LaFila2VaDebajoDeLasEsquinas_PegadaALaFila1`).
- [x] 3 filas por defecto, configurable (`ElMaximoDeFilasSeConfigura`).
- [x] Filas pegadas (0 cm) por defecto, o con separación de X cm (`ConFilasSeparadas_SeSumaLaSeparacion`).
- [x] Varios diámetros en una cara, `3 #5 + 2 #4` (`ConDosDiametros_LasEsquinasSonDelPrimeroYSeReparteSimetrico`, `ConDosDiametros_CadaBarraApoyaEnElEstribo`).
- [x] El estribo cerrado necesita al menos 2 barras por cara: error si hay menos (`UnaSolaBarra_ElEstriboCerradoNoSePuedeArmar`).
- [x] Nunca se viola la distancia libre entre barras de una fila (`LaFila1VaDeEsquinaAEsquinaALaMismaDistancia`).
- [x] `dotnet test CQEING-Addins.sln -c Release` en verde.

## Hallazgos del código y de Revit real

1. **La maqueta ya trae la fórmula de capacidad** (`project/Main.dc.html`, función `layerInfo`):
   - fórmula: `caben = piso((ancho − 2·recLados − 2·dEstribo + libre) / (dMayor + libre))`;
   - con 30 cm de ancho, recubrimiento 4 cm, estribo #3 (9.5 mm), barras #5 (15.9 mm) y 2.5 cm libres: `piso(226 / 40.9) = 5`, que es el "caben 5 por fila" de la maqueta;
   - la separación vertical entre filas es `dMayor + separación` (pegadas = 0).
2. **Al eje de la barra:** libre + estribo + medio diámetro = 40 + 9.5 + 7.95 = **57.45 mm** (≈ 6 cm), como dijo Carlos el 16-oct.
3. **`Reglas` (de #80) no tiene el diámetro real de cada barra ni la distancia libre.** Se agregan; los dos son parte de las reglas generales (#82).
4. **No hace falta probar contra Revit** (regla del `CLAUDE.md`). Esta issue solo calcula posiciones en la sección.
   - Cómo se crean en Revit y si el radio de doblado del estribo corre la barra de esquina se ve en #85, midiendo contra un modelo real.

## Decisiones

1. **Medidas de la sección en milímetros con decimales** (`double`) (2026-10-16, Juan).
   - Los diámetros no son enteros en mm (15.9).
   - Acá no hay múltiplos de corte, así que no hace falta trabajar con enteros como en el motor.
2. **Diámetro real por barra** en `ReglaDiametro`, en mm:
   - #2 6.4 · #3 9.5 · #4 12.7 · #5 15.9 · #6 19.1 · #7 22.2 · #8 25.4.
   - **Distancia libre** en `Reglas`: 25 mm por defecto.
3. **La capacidad usa el diámetro mayor de la cara**, como la maqueta: es el caso más estricto.
4. **Orden de las barras con varios diámetros** (2026-10-16, supuesto; Carlos no lo dijo). Se va llenando por pares simétricos, de las esquinas hacia adentro, empezando por el primer grupo (el que se escribió primero):
   - las esquinas siempre las toma el primer grupo, que son las que abraza el estribo cerrado;
   - si a un grupo le sobra una barra, va al centro de la fila (si la fila tiene un número impar de puestos) o al siguiente puesto libre;
   - ejemplo: `3 #5 + 2 #4` en una fila de 5 → #5 · #4 · #5 · #4 · #5;
   - lo que no entra en la fila 1 sigue el mismo orden en la fila 2.
5. **Fila 1 con menos barras de las que caben:** se reparten parejo entre las dos esquinas.
6. **Filas 2 en adelante:** usan los mismos puestos que la fila 1 llena, ocupados de las esquinas hacia adentro (decisión 4).
7. **Cada barra apoya en el estribo o en la fila de abajo** (2026-10-16, Juan, al programar el paso 5).
   - Con varios diámetros en una fila, cada barra tiene su centro a "borde de la fila + su medio diámetro". Así el #4 apoya en el estribo igual que el #5.
   - La fila siguiente arranca en el borde de la barra más gruesa de la fila anterior, más la separación.
   - Con un solo diámetro da lo mismo que la maqueta (`dMayor + separación` entre centros).
8. **Errores de la cara:** se devuelven con sus datos y el texto lo arma el tablero.
   - Menos de 2 barras: el estribo cerrado no se puede armar.
   - Más barras de las que caben en el máximo de filas: se informa el máximo (`caben × filas`).
   - Las filas de arriba y las de abajo se pisan en el alto.

## Plan paso a paso

Pruebas primero en cada paso.

1. **Reglas: diámetro real y distancia libre**
   - Archivo: `Modelo/Reglas.cs`.
   - Cómo se prueba: `Reglas.Generales` trae el #5 = 15.9 mm y 25 mm de distancia libre.
2. **Modelo de la sección**
   - Archivo: `Modelo/Seccion.cs`.
   - Contenido:
     - `EntradaSeccion`: ancho, alto, recubrimientos arriba, abajo y lados, diámetro del estribo, grupos de arriba y de abajo, separación entre filas y máximo de filas;
     - `ResultadoSeccion`;
     - `CaraSeccion`: caben por fila, filas, error y recubrimiento al eje;
     - `BarraEnSeccion`: diámetro, x, y.
   - Cómo se prueba: compila.
3. **Capacidad y reparto en filas**
   - Archivo: `Seccion/RepartidorFilas.cs`.
   - Cómo se prueba:
     - con la sección de la maqueta caben 5;
     - 7 barras dan 5 + 2;
     - con máximo 3 filas y 16 barras, error con el máximo de 15;
     - 1 barra da el error del estribo cerrado;
     - un máximo de filas configurable cambia el máximo del error.
4. **Posiciones en la sección**
   - Archivo: `Seccion/RepartidorFilas.cs`.
   - Cómo se prueba:
     - fila 1 de 5 #5: x de 57.45 a 242.55 con el mismo paso, y la distancia libre entre barras ≥ 25 mm;
     - fila 2 de 2 bajo las esquinas, a 15.9 mm (pegada) o a 15.9 + X (separada) de la fila 1;
     - la cara de arriba se mide desde arriba;
     - el recubrimiento por cara cambia la y de esa cara;
     - el recubrimiento al eje da 57.45.
5. **Varios diámetros**
   - Cómo se prueba:
     - `3 #5 + 2 #4` da #5 · #4 · #5 · #4 · #5;
     - la capacidad usa el #5.
6. **Choque en el alto**
   - Cómo se prueba: una viga baja, con tres filas arriba y tres abajo, da el error de alto.
7. **Cierre**
   - Correr `dotnet test` de toda la solución.
   - Llenar la sección Estado.
   - Comentar en #88.

## Cómo probarlo

```
dotnet test addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests -c Debug
dotnet test CQEING-Addins.sln -c Release
```

En esta issue no hay pruebas en Revit. Las posiciones se comparan con Revit en #85.

## Estado

- [x] Plan revisado con el usuario (aprobado el 2026-10-16, incluido el orden de diámetros de la decisión 4).
- [x] Pasos 1 a 6 implementados (2026-10-16), con las pruebas escritas antes que el código.
  - Archivos: `Modelo/Reglas.cs` (diámetro real y distancia libre), `Modelo/Seccion.cs` y `Seccion/RepartidorFilas.cs`.
- [x] Pruebas en verde (2026-10-16):
  - `dotnet test CQEING-Addins.sln -c Release` → Vigas 63/63 (16 nuevas), Gestor 34/34, Publicador 8/8;
  - compilación en Release con 0 advertencias.
- **Pendiente:**
  - commit y PR (a pedido del usuario);
  - bastones que ocupan puestos libres (#89);
  - barra traslapada al lado o abajo (#79, pendiente 2).
  - El tablero (#83) y el corte (#91) usan este resultado.
