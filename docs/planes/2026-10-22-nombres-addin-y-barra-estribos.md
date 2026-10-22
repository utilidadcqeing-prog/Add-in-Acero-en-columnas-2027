# Nombre visible del add-in y campo "Barra de estribos" (#140, #141)

## Contexto

Observaciones de la reunión del 21-10 (#129):

- #140: el add-in se llamaba "Estribos de Columna", pero arma toda la
  columna (longitudinales, traslapos, estribos y estribos menores).
- #141: en el panel del tramo, el campo "Tipo de barra" no decía que es
  la barra de los estribos.

## Decisión (Luciano, 22-oct)

- Nombre visible: **"Acero en columnas"**, igual que su par
  "Acero en vigas" (#126).
- Campo del tramo: **"Barra de estribos"**.

## Cambios

- `addins/armado-columnas/App.cs`: el botón del ribbon dice "Acero en\ncolumnas" y su texto de
  ayuda describe todo el armado.
- `addins/armado-columnas/Vistas/VentanaConfiguradorEstribos.xaml`: el título de la ventana es
  "Acero en columnas" y la etiqueta es "Barra de estribos:".
- `addins/armado-columnas/addin.json`: el gestor muestra "Acero en
  columnas" (antes "Armado de columnas"), con una descripción que
  incluye todo el armado. Las iniciales de la tarjeta siguen siendo "AC".

No se tocó:

- El nombre interno del botón (`ComandoEstribos`) ni las clases. Son
  identificadores, no texto visible, y cambiarlos puede romper las
  personalizaciones del ribbon.
- El id `armado-columnas` del `addin.json`: el gestor y las versiones
  publicadas se guían por él.
- El título "Configurador de Estribos Menores": esa ventana sí es solo
  de estribos.
- Los nombres de ejemplo en las pruebas del gestor (`gestor/*Tests`):
  son datos de prueba, no lo que ve el usuario.

## Estado

Compila. Falta confirmarlo con Carlos (criterio de #140) y verlo en
Revit al próximo despliegue.
