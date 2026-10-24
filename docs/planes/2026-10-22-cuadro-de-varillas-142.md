# Cuadro de varillas en el configurador (#142)

## Contexto

En la reunión del 21-10 (#129, punto 13), Carlos pidió poder ver la
información de las barras "como en vigas" sin tener que medir en Revit.
Con Luciano (22-oct) se aclaró qué era:

- No es la lista del armado de la columna (barra por barra), sino el
  **cuadro de ganchos y traslapos de la empresa**: cómo deberían ser las
  barras según ese cuadro. Hay un solo cuadro; no hay otras normas.
- Se consulta **antes de generar**.
- Solo de consulta: no se edita nada desde ahí.

Ese cuadro ya estaba cargado en el add-in (`Modelo/CuadroVarillas.cs`)
y es con el que se generan ganchos y traslapos, pero no se mostraba en
ningún lado.

## Diseño elegido

Entre los dos bocetos (ventanita emergente o desplegable dentro del
panel), Luciano eligió la **ventanita emergente**:

- Botón "Ver cuadro de varillas…" en el panel de la derecha, debajo de
  "Editar estribos menores…".
- Abre una ventana aparte, no modal: se puede dejar abierta al lado
  mientras se configura. Si ya está abierta, el botón la trae al frente.
- Una fila por varilla, de la #2 a la #8: diámetro (mm), traslapo,
  gancho de 90°, gancho de 180° y estribo de 135° (cada uno E / D), y
  kg/m. Valores en metros, como el cuadro. "—" donde el cuadro no da el
  valor (la #2 no tiene traslapo; de la #6 a la #8 no hay estribo de
  135°).
- Las varillas que usa la columna activa salen en azul con su uso al
  lado (estribos, esquinas, cara X, cara Y, estribos menores). Se
  actualiza al cambiar una barra, de tramo o de columna, y al aceptar
  los estribos menores.

Decisiones mías (no pedidas, avisadas a Luciano):

- Los valores salen de `CuadroVarillas.Cuadro`, el mismo que usa el
  generador: lo que muestra la ventana es lo que queda en Revit.
- Los kg/m no estaban en el cuadro de columnas. Se agregaron
  (`Varilla.KgPorMetro`) con los mismos valores de las reglas de vigas
  (`acero-vigas/calculo/.../Reglas.cs`).
- El diámetro es el nominal del cuadro, el mismo con el que calcula el
  add-in y el que tienen las varillas "#N CQEING". El diámetro "de
  modelo" de Revit (con el que se dibuja el sólido) puede ser distinto
  solo en tipos de barra de otras plantillas.

## Cambios

- `Modelo/CuadroVarillas.cs`: `KgPorMetro` por varilla; `De(TipoBarra)`
  (la varilla del cuadro por nombre propio o por diámetro, que ya hacía
  `EvaluarParaEstribo` y ahora la reusa); `UsosPorNumero`, que arma el
  "en qué se usa" de cada varilla.
- `Vistas/VentanaCuadroVarillas.xaml(.cs)`: la ventana nueva.
- `Vistas/VentanaConfiguradorEstribos.xaml(.cs)`: el botón, abrir una
  sola ventana y mantener el resaltado al día.
- `tools/PreviewUI/PreviewUI.csproj`: incluye la ventana nueva.

## Verificación

- Compilan el add-in (a una carpeta aparte: Revit tenía la DLL tomada)
  y `PreviewUI`.
- `UsosPorNumero` y los kg/m, probados en un proyecto de prueba aparte
  (no en `tools/PruebasVistaPrevia`, que estaba editando otro agente):
  nombre propio y por diámetro, sin repetir usos, barras sin elegir o
  fuera del cuadro no marcan nada.
- Captura de la ventana con #3 en estribos, #4 en caras y #5 en
  esquinas: los valores coinciden con el cuadro.

## Pendiente

- Verlo dentro de Revit al próximo despliegue.
- Mostrárselo a Carlos.
