# Medición: gancho de la base en zapata (#138) y línea de propiedad (#139)

Fecha: 22-oct-2026. Estado: **medido; falta decidir.**

## Contexto

Luciano generó acero en un modelo con zapatas y dibujó una línea de
propiedad en planta. Se midió con el MCP de Revit (coordenadas de cajas
envolventes, en pies, pasadas a metros).

## Modelo

- Columnas de 40×40 cm, sobre zapatas de 35 cm de espesor (fondo a -1,00 m,
  cara superior a -0,65 m).
- Columna A en (0 ; 12,00 m), barras #4, zapata de X -0,45..0,45 y
  Y 11,00..13,00.
- Columna B en (7,00 ; 0), barras #7 (arranque) y #6 (arriba), zapata de
  X 6,55..7,45 y Y -1,00..1,00.

## #138: largo del gancho de la base

- La barra baja hasta -0,925 m: 7,5 cm sobre el fondo de la zapata. Bien.
- #4 (barra 812348, 3,50 m en total): el gancho ocupa 26,2 cm en planta
  (contando el grosor de la barra y el doblez). Queda dentro de la columna y
  de la zapata.
- #7 (barra 812763, 4,00 m): el gancho ocupa 45,4 cm en planta. La columna
  mide 40 cm, así que **el gancho cruza toda la columna y sale 15,7 cm por la
  cara opuesta**. Sigue dentro de la zapata (quedan 9 cm hasta el borde).
- Los largos coinciden con la columna "90°" del cuadro de ganchos y traslapos
  (`CuadroVarillas.Gancho90`: #4 = 0,222 m, #7 = 0,388 m). El add-in hace lo
  que dice el cuadro; lo que se discute es qué cuadro usar.
- El cuadro 2 ("Dimensiones mínimas para ganchos estándar", ya cargado como
  `Gancho90Cuadro`) da una extensión recta de 12 diámetros (#4 = 0,152 m,
  #7 = 0,266 m), bastante más corta.

## Varillas dibujadas al revés (pendiente del #130)

En la primera generación ninguna varilla salió dibujada al revés. Luciano
regeneró la columna 782105 y en la segunda sí: **6 varillas dibujadas de
arriba hacia abajo** (piezas de arranque 813529, 813542, 813556, 813569,
813583 y 813596). En las 6:

- [x] el gancho de anclaje está en el extremo 1 (End) de la pieza que llega
  a la zapata (Z mín. -0,925 m), o sea **abajo**; la pieza de arriba lleva su
  gancho en el extremo 0 (Start), como corresponde;
- [x] el gancho **mira hacia adentro**: 813529 y 813542 hacia +Y desde la
  cara -Y, las otras 4 hacia -X desde la cara +X. Igual que las varillas
  normales vecinas.

Pendiente del #130 confirmado.

## #139: línea de propiedad

- Son 4 segmentos (categoría "Site: Property Line Segments", ids
  813434..813437) que forman un rectángulo: X -0,15..7,15 y Y -0,15..12,15,
  dibujado a la altura del nivel de la planta (Z = 7,00 m). Para el add-in
  solo importa la posición en planta.
- La línea está a 15 cm de los ejes: coincide con las caras de las columnas
  de 30×30. El tramo de 40×40 bajo el nivel 0 sobresale 5 cm y las zapatas
  (centradas) 30 cm; en un lindero real la zapata sería excéntrica, pero
  eso no lo arma este add-in.
- La columna B (782105, la que Luciano generó para esta prueba) mide 30×30
  arriba de la zapata (X 6,85..7,15 ; Y -0,15..0,15): **sus caras +X y -Y
  coinciden exactamente con la línea**. Es una columna de esquina de lote.
- De sus 14 barras de arranque, **7 tienen el gancho del lado del vecino**:
  - hacia +X (cruzan X = 7,15): 812763 (20,6 cm), 812925 y 812939 (12,6 cm);
  - hacia -Y (cruzan Y = -0,15): 812858 y 812912 (20,6 cm), 812871, 812885
    y 812898 (7,5 cm).
  Las otras 7 apuntan hacia -X o +Y, lejos de la línea.
- Causa: hoy el gancho mira "hacia el centro", pero como mide más que media
  columna (27 a 45 cm contra 15 cm), una barra de una cara cruza el centro y
  sale por la cara opuesta. Cuando esa cara es la del lindero, invade al
  vecino.
- El MCP no devuelve "Location" para los segmentos; hay que leer su geometría.
  Antes de usarla en el add-in, verificar con un comando de diagnóstico qué
  miembros públicos de la API dan la geometría de la línea de propiedad.

## Pendiente

- #138: Luciano decidió (22-oct) que el largo actual (cuadro 1) **está
  bien**: se queda así.
- #139: diagnóstico de lectura de la línea de propiedad y regla de ganchos.

## Diagnóstico de la línea de propiedad (en curso)

- API: `Autodesk.Revit.DB.PropertyLine` es pública y documentada (Revit
  2027): `GetBoundary()` devuelve `IList<CurveLoop>`, más `IsClosedLoop()`,
  `IsSketchBased()` y `Area`. Ningún miembro marcado como interno u
  obsoleto (rvtdocs.com/2027/Autodesk.Revit.DB.PropertyLine; confirmado
  también en los metadatos de RevitAPI.dll 2027). Los segmentos
  (`PropertyLineSegment`) **no** son públicos: no usarlos.
- Comando temporal `ComandoDiagnosticoLineaPropiedad` (solo Debug, solo
  lectura). Muestra cada `PropertyLine` con su contorno en metros, si Revit
  le da geometría sin vista y en la vista activa (¿se ve en 3D?) y, para las
  columnas seleccionadas, a qué distancia queda la línea de cada cara.
### Resultado (22-oct, vista "3D ACEROS", 4 columnas seleccionadas)

- 1 `PropertyLine` (id 813400), por boceto, cerrada, 89,79 m².
  `GetBoundary()` devuelve 4 líneas exactas: X -0,15..7,15 ; Y -0,15..12,15,
  todas a Z = 7,00 (la altura del nivel donde se dibujó). **Para el add-in
  solo cuenta la planta; la Z se ignora.**
- Revit le da geometría (4 líneas) sin vista y también en la vista 3D. Igual,
  la vista previa 3D del add-in conviene que dibuje la línea a partir de
  `GetBoundary()` (no depende de cómo esté configurada la vista).
- Caras sobre el lindero (tolerancia 2 cm), todas a 0,0 cm:
  - 782105 (7,00 ; 0): +X y -Y -- esquina;
  - 782108 (0 ; 12,00): -X y +Y -- esquina;
  - 782113 (7,00 ; 8,00): +X -- borde;
  - 782742 (3,50 ; 12,00, piso 2): +Y -- borde.
- Error del diagnóstico (corregido después de esta corrida): marcaba "LA
  COLUMNA SE PASA" en las caras opuestas porque tomaba la línea que queda
  detrás de la columna. Ahora solo mira las líneas que quedan hacia afuera de
  cada cara.

Conclusión: leer el lindero con la API pública funciona y alcanza para saber
qué caras de cada columna están sobre él.

## Regla de los ganchos (decisión de Luciano, 22-oct)

Se mantiene "hacia el centro" (`GeneradorLongitudinal.OrientacionesHaciaAdentro`)
y **solo se giran los ganchos que terminarían del lado del vecino**.

## Plan (aprobado por Luciano el 22-oct; pasos 1 a 3 implementados)

Implementado: `Modelo/Lindero.cs` (con pruebas en
`tools/PruebasVistaPrevia`), `ColumnaContexto.Lindero`,
`LectorContexto.LeerLindero` y `GeneradorLongitudinal.LadoLejosDelLindero`
(se aplica justo antes de `BarraContinua.Crear`, así cubre las dos elecciones
de lado). Traza `LINDERO: gancho girado ...` y un aviso por columna. Build y
pruebas en verde.

**Paso 4 verificado en Revit (22-oct).** Luciano regeneró las dos columnas
de esquina, ahora con barras #4 (ganchos de 26,2 cm en planta):

- 782108 (0 ; 12,00), lindero en X = -0,15 e Y = 12,15: la punta más a -X
  queda en X = -0,100 y la más a +Y en Y = 12,100. Todas dentro del lote
  (5 cm de margen, el recubrimiento).
- 782105 (7,00 ; 0), lindero en X = 7,15 e Y = -0,15: la punta más a +X
  queda en X = 7,100 y la más a -Y en Y = -0,100. Todas dentro del lote.
  Antes del cambio, con #7, llegaban a X = 7,357 e Y = -0,356.

Ninguna punta de gancho pasa de la línea de propiedad. Pendiente: probarlo
también con barras más gruesas (#6 a #8), donde el gancho es más largo que
la columna.

1. `Modelo/Lindero.cs` (sin Revit, con pruebas): los tramos del lindero en
   planta y una función que dice si un gancho que sale de (x, y) en una
   dirección y con cierto alcance cruza alguno.
2. `LectorContexto`: leer `PropertyLine.GetBoundary()` una vez por documento,
   pasar a planta (sin Z) y dejarlo en el contexto. Sin línea de propiedad,
   todo sigue igual que hoy.
3. `GeneradorLongitudinal`: después de elegir el lado "hacia adentro" (en los
   dos lugares donde se elige), si la punta del gancho cruza el lindero, se
   invierte el lado. El gancho queda sobre el mismo eje, apuntando hacia el
   lote. Alcance de la punta: `Gancho90` del cuadro + 2,5 diámetros (cubre lo
   medido: #4 26,2 cm, #7 45,4 cm). Se aplica a los ganchos de abajo y de
   arriba. Queda en el registro y un aviso por columna.
4. Verificar en Revit: regenerar 782105 y medir que ninguna punta pase de la
   línea.
5. Dibujar el lindero en la vista previa 3D: después, junto con el #137.
