# Plan: flujo de la ventana de columnas (#168, #178, #179, #180, #181, #182)

## Contexto

Puntos que siguen abiertos de #174 (reunión de revisión del 24-10). Se hacen
en la misma rama que #177 y #176 (`columnas/largo-longitudinales-177`)
porque son cortos y se pueden probar juntos en una sola pasada por Revit.
Casi todo está en `Vistas/VentanaConfiguradorEstribos.xaml(.cs)`.

Decisiones de Luciano (28-10):

- #178: la configuración elegida se carga sola en las columnas que no tienen
  nada propio; si la columna ya tiene lo suyo, se respeta y hay un botón
  «Aplicar configuración».
- #180: lo que se generó gana sobre todo lo demás; si la columna cambió en
  Revit, se avisa.
- #168: todo lo propuesto en el issue.
- #179: tres botones (Guardar como nueva / Guardar cambios / Eliminar).

## Orden y qué se hace

### 1. #182 -- borde de los estribos menores en la vista previa

Solo dibujo (`Vistas/PanelVistaPrevia.xaml.cs`, `DibujarPlanta`): a cada
estribo menor se le dibuja debajo una línea un poco más gruesa en un color
de contraste (el fondo/hormigón), así se separa del estribo grande aunque
tengan el mismo color. Revisar con todos los diámetros de la paleta.

### 2. #178 y #179 -- configuraciones guardadas (mismo panel)

Qué tiene cada columna, de mayor a menor prioridad:

1. Lo que se editó en esta sesión (ya existe: `_configPorTramo`,
   `_longitudinalPorColumna`).
2. Lo que se generó (#180, paso 3).
3. La configuración elegida en la lista (nuevo, #178).
4. Los valores por defecto.

- #178: al activar una columna sin nada propio, se carga la configuración
  elegida. Si tiene lo suyo, queda a la vista el botón «Aplicar
  configuración», que también sirve cuando hay una sola en la lista.
- #179: «Guardar como nueva» (pide nombre, no deja repetir),
  «Guardar cambios» (pregunta antes de sobreescribir la elegida),
  «Eliminar» (pregunta con el nombre). Un «•» junto al nombre cuando el panel
  tiene cambios sin guardar respecto de la configuración elegida.

### 3. #180 -- recordar con qué se generó cada columna

- Al generar, guardar en el modelo el JSON de lo generado por columna (tramos
  + acero longitudinal + estribos menores), con Extensible Storage, en el
  mismo estilo que `AlmacenConfiguracionesRevit` (un `DataStorage` con un
  mapa por `UniqueId` de la columna). Por `UniqueId` y no en la propia
  columna: una columna copiada no hereda una configuración que no es suya.
- Se guarda junto con la sección y la altura que tenía la columna; al
  volver a abrirla, si cambiaron, se avisa en el estado.
- Solo API pública: `Schema`, `Entity`, `DataStorage`, como el almacén actual.

### 4. #168 -- el ✓ (aceptar / quitar / generado)

- El ✓ se saca con un clic en la elevación, en la planta y en la lista (en
  planta/lista saca la columna entera).
- El botón Aceptar cambia a «Quitar ✓» si el tramo ya está aceptado; los de
  «aplicar al resto» tienen su par, y quitar respeta las columnas elegidas.
- «Quitar todos los ✓» junto al resumen, con «Deshacer» en el estado.
- Cada ✓ guarda una copia de lo aceptado: Generar usa esa copia, no lo que
  quedó tecleado. Si se edita el tramo después, el ✓ pasa a ámbar.
- Después de generar, el ✓ pasa a gris («ya generado») y queda fuera de la
  próxima generación salvo que se vuelva a aceptar.
- Al cerrar con tramos pendientes, preguntar.
- Menor: si cambió el proyecto activo, abrir una ventana nueva para ese
  proyecto (`ComandoEstribos`).

### 5. #181 -- ganchos en la vista previa y aviso si se salen

- Dibujar los ganchos de las longitudinales en la vista previa, **solo en
  las vistas de lado (elevación)**. En la planta no: ahí lo esencial es ver
  las varillas, y los ganchos las taparían (Luciano, 28-10). Con el mismo
  lado de doblez que usa el generador (hacia el
  centro, girado si cruza el lindero) y el largo del cuadro
  (`Lindero.AlcanceGancho90`, ya ajustado en #177).
- Aviso en rojo si la punta se sale del contorno de la columna (o de la
  zapata/viga donde ancla).
- Selector «Tramo / Columna completa» en la vista previa.
- Antes de dar por buena la geometría, comparar el dibujo con una columna
  generada en Revit (regla del repo).

## Verificación

- Build Debug y `tools/PruebasTraslapos` en verde después de cada paso.
- Casos nuevos en las pruebas para la lógica que no es dibujo: prioridad de
  configuraciones (#178/#180), estados del ✓ (#168), gancho dentro/fuera (#181).
- Una pasada en Revit al final (Revit cerrado antes de desplegar), con una
  lista de pruebas para Luciano por issue.
- Commits separados por issue, solo local hasta que Luciano pida push.

## Pendiente

- [x] #182 (a5d2035)
- [x] #178: `AplicarConfiguracion` lleva la configuración a todos los tramos
      de la columna y a su longitudinal; `TieneAlgoPropio` = aceptado o algo
      distinto del valor por defecto. Botón «Aplicar».
- [x] #179: «Guardar como nueva» (no pisa una existente), «Guardar cambios»
      y «Eliminar» con confirmación; línea «• El panel tiene cambios sin guardar
      en …» (`ActualizarCambiosSinGuardar`, en `VentanaConfiguradorEstribos.EstadoPanel.cs`).
- [x] #180: `Modelo/ColumnaGenerada` (lo generado por tramo + longitudinal +
      recubrimiento + medida de la columna; `Combinar` por tramo, `Cambios`),
      `Logica/RegistroGeneradosRevit` (DataStorage, clave `UniqueId`, se escribe
      en la transacción de generar). La ventana lo carga al abrir y avisa si la
      columna cambió. Falta probar en Revit.
- [ ] #168
- [ ] #181
- [ ] Retest en Revit
