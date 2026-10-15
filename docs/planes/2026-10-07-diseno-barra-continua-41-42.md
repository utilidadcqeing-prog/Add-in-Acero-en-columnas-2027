# Diseño: una sola barra longitudinal de punta a punta (#41, #42, #44)

Aprobado en conversación por Luciano el 7-oct-2026. Se apoya en lo medido
contra Revit real en
`docs/planes/2026-10-07-factibilidad-barra-continua-41.md`.

## Qué se quiere lograr

- Una sola varilla por posición, de la zapata al tope de la columna, sin
  cortes donde cambia la columna (#41, pedido de William el 10-sep).
- Los traslapos se aplican encima como empalmes que Revit reconoce, varios
  por varilla, y se pueden deshacer dejando la varilla entera.
- La varilla recta cabe en todas las columnas de la pila (#42).
- Un tramo sin columna asociada no deja un hueco sin acero ni se calla (#44).
- El solape sale por defecto del cuadro de la empresa, y el usuario puede
  elegir en su lugar un tipo de empalme del modelo.

Fuera de alcance: las reglas de dónde va un traslapo (media altura de un
tramo libre, alternancia por anillo, longitud comercial) no cambian. Tampoco
los estribos, las grapas ni las cajas.

## Comportamiento

### 1. Posición de las varillas (#42)

La posición X/Y de cada varilla se calcula una sola vez con la **zona común
en planta de todas las columnas de la pila** (la intersección de sus
huellas), no con la columna de más arriba como hoy. La zapata no participa.

- Si las columnas no comparten zona común, o la zona común es demasiado chica
  para el recubrimiento más la barra de esquina, no se genera el longitudinal
  de esa columna y se avisa con la marca de la columna y las medidas.
- El diámetro del estribo que se descuenta sigue siendo el mayor entre los
  tramos aceptados: es el único dato disponible, los tramos no aceptados no
  tienen estribo configurado.

### 2. Creación

Una varilla por posición del anillo, con una sola línea desde
`ZInferiorLongitudinal` (anclaje en la zapata, o la base de la columna si no
hay zapata) hasta el tope de la pila. Lleva el gancho de anclaje abajo solo
si hay zapata, y el gancho de remate arriba, los dos hacia el centro de la
sección (igual que hoy).

Host: la columna de más abajo de la pila que exista y admita armadura. La
zapata ya no se agrega como host: se midió que la varilla puede salirse de su
host sin recortarse ni dar aviso.

### 3. Dónde van los traslapos

`PlanificarSplicesReales` no cambia. De `PlanificarCortes` se eliminan los
cortes obligados por límite de host: el plan pasa a ser solo la lista de
alturas de traslapo real para cada grupo par/impar.

### 4. Tipo de empalme

Campo nuevo `ConfigLongitudinal.TipoEmpalmeNombre` (texto, vacío por
defecto), que se copia en `Copiar()` y viaja con las configuraciones
guardadas igual que `LongitudComercialBarra`.

- **Vacío = "Por defecto del add-in (cuadro CQ)"**. Para cada tipo de barra
  usado se busca o se crea un tipo de empalme llamado `CQ Traslapo #N`
  (N = número de la varilla del cuadro), con:
  - multiplicador de solape = `Traslapo del cuadro / diámetro nominal de la
    barra` (por ejemplo #4: 0,75 m / 12,7 mm = 59,06);
  - corrimiento `RebarSpliceShiftOption.BarPlane`.
  El multiplicador y el corrimiento se vuelven a fijar en cada corrida, por
  si alguien los cambió a mano. Solo se crean los tipos de las barras que se
  usan en esa corrida.
- **Con nombre** = se usa ese tipo de empalme del modelo tal cual, sin
  tocarlo. Si ya no existe, se avisa y se usa el por defecto.

Si la barra no está en el cuadro de la empresa y la opción es la por defecto,
no hay solape que aplicar: la varilla queda entera y se avisa, sugiriendo
elegir un tipo del modelo.

API usada (documentada como pública en `RevitAPI.xml` de 2027, desde 2025):
`RebarSpliceTypeUtils.GetAllRebarSpliceTypes`, `CreateRebarSpliceType`,
`SetLapLengthMultiplier`, `SetShiftOption`.

### 5. Partido

Todas las alturas de traslapo de la varilla se aplican en **una sola
llamada** a `RebarSpliceUtils.SpliceRebar(doc, id, opciones, geometrias)`,
con `RebarSplicePosition.Middle` y una `RebarSpliceGeometry(punto, BasisZ)`
por altura. El punto usa la X/Y leída de la varilla ya creada.

Si no hay ninguna altura de traslapo, la varilla queda entera y no se llama
a `SpliceRebar`.

**Corrimiento lateral** (corregido tras medir, 7-oct). La regla del equipo,
reconfirmada por Luciano: la pieza corrida va un diámetro **hacia el lado, a
lo largo de la cara** donde apoya la barra, nunca hacia adentro del hormigón.

Lo medido: Revit corre la segunda pieza de cada empalme siempre hacia el lado
positivo del eje de la normal de la barra (+X o +Y); ni el sentido de la
normal, ni la normal de la cara de empalme, ni el sentido en que se dibuja la
barra lo cambian. Las piezas de una cadena están atadas: mover una mueve
todas. Entonces:

- barras de las caras inferior/superior: normal X (corren a lo largo de X);
- barras de las caras izquierda/derecha: normal Y;
- esquinas: el eje cuyo lado positivo va a lo largo de una cara sin salirse
  por la punta (Y en las dos de abajo, X en la superior izquierda);
- esquina superior derecha: los dos lados positivos se salen, así que después
  de partir se corre la varilla ENTERA un diámetro para el otro lado. Queda
  el mismo zigzag con los turnos cambiados: ahí la pieza de abajo es la que
  queda un diámetro al lado de la esquina.

`BarraContinua` no supone hacia dónde corrió Revit: mide, y solo reubica si
alguna pieza quedó fuera del rectángulo de esquina a esquina de la armadura.

### 6. Después de partir

Las piezas se ordenan de abajo hacia arriba y a cada una:

1. se le reasigna el host (`Rebar.SetHostId`) a la columna de la pila que
   contiene su punto medio; si ninguna lo contiene (pieza que cae en un tramo
   sin columna, o dentro de la zapata), se deja el host que puso Revit;
2. se le graba `MarcaEstribos` con el id de su host, para que el borrado al
   regenerar la encuentre;
3. se reponen los ganchos: extremo inferior de la pieza de más abajo, extremo
   superior de la de más arriba.

La varilla entera (sin traslapos) se marca con su host.

### 7. Cuando algo falla

No existe más el método anterior (dos varillas sueltas con corrimiento a
mano). En todos estos casos la varilla **queda entera** y se agrega un aviso
con la marca de la columna:

- `SpliceRebar` tira una excepción o devuelve una cantidad de piezas distinta
  de `traslapos + 1`;
- no se pudo crear o configurar el tipo de empalme;
- la barra no está en el cuadro y no se eligió un tipo del modelo.

Un aviso por columna y motivo, no uno por varilla.

### 8. Tramos sin columna asociada (#44)

Un tramo con `IdElementoHost` vacío, o cuyo elemento ya no existe o no admite
armadura, ya no interrumpe la varilla. Se agrega el aviso que hoy falta:
la varilla atraviesa ese tramo pero ninguna columna del modelo lo respalda.

### 9. Interfaz

En `VentanaConfiguradorEstribos`, junto a `ComboLongitudBarra`, un combo
"Tipo de empalme" con "Por defecto del add-in (cuadro CQ)" primero y debajo
los tipos de empalme del modelo en orden alfabético. Se lee y se escribe en
los mismos tres puntos del code-behind que `LongitudComercialBarra`.

`IProveedorTipos` gana `ListarEmpalmes()`; `ProveedorTiposReal` lo responde
con `RebarSpliceTypeUtils.GetAllRebarSpliceTypes`, y `ProveedorTiposFalso`
con dos nombres inventados para `tools/PreviewUI`.

## Cómo se reparte el código

`GeneradorLongitudinal.cs` tiene 1.900 líneas; el cambio saca de ahí lo que
se reescribe en vez de engordarlo.

- **`Logica/TiposEmpalme.cs` (nuevo)**: resuelve el tipo de empalme para una
  barra y una configuración. Entrada: documento, `RebarBarType`, nombre
  elegido. Salida: el `ElementId` del tipo, o un motivo de por qué no hay.
  Cachea por corrida. No sabe nada de columnas.
- **`Logica/BarraContinua.cs` (nuevo)**: crea una varilla de punta a punta,
  la parte, reasigna hosts, marca y repone ganchos (secciones 2, 5, 6 y 7).
  Entrada: posición, normal, Z inicial y final, alturas de traslapo, lista de
  columnas de la pila con su rango Z, ganchos, tipo de empalme. Salida:
  cuántas piezas quedaron.
- **`Logica/GeneradorLongitudinal.cs`**: conserva el armado del anillo, la
  planificación de alturas y los estribos menores. Pierde
  `CrearBarraConTraslapos`, `CrearRangoConMetodoAnterior`,
  `IntentarPartirConSplice`, `CrearBarraVertical`, la función local
  `Corrimiento`, el alta de la zapata como host y los cortes obligados. La
  zona común de la sección 1 reemplaza a `referencia`.
- **`Logica/GeometriaSeccion.cs`**: se elimina `BuscarTipoEmpalme`.
- **`Modelo/ConfigLongitudinal.cs`, `Modelo/IProveedorTipos.cs`,
  `Modelo/ProveedorTiposFalso.cs`, `Logica/ProveedorTiposReal.cs`,
  `Vistas/VentanaConfiguradorEstribos.xaml(.cs)`**: sección 9.
- El comentario de cabecera de `GeneradorLongitudinal` se reescribe: hoy
  afirma que un `Rebar` no puede abarcar más de un host.

## Cómo se comprueba

El repo no tiene pruebas automáticas; la comprobación es compilar para 2027 y
2026 y medir en el modelo real con el reporte de diagnóstico (regla del
`CLAUDE.md`). Pila de referencia: dado 781598 + columnas 782104 y 782735.

Antes de dar por buena la implementación hay que medir:

1. **Multiplicador con decimales**: que `CQ Traslapo #4` dé 75 cm de solape.
2. **Ganchos con varios traslapos**: que la pieza de abajo y la de arriba
   conserven su gancho, hacia el centro.
3. **Varilla de cara lateral** (normal `BasisY`): que el corrimiento salga a
   lo largo de su cara y no hacia adentro del hormigón.
4. **Regenerar** una columna ya armada: que no queden piezas de la corrida
   anterior.
5. **Sin avisos de Revit** al confirmar ("completely outside of its host").
6. **Deshacer a mano** un empalme en Revit sobre una varilla generada: que
   quede entera.
7. Columna de una sola pieza y columna sin zapata: que sigan saliendo bien.

Al cerrar: quitar `ComandoDiagnosticoBarraContinua` y su botón, cerrar #41 y
#42, y actualizar #44.
