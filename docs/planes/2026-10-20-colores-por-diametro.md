# Colores por diámetro al generar

Contexto: Tarea 7 de `2026-10-20-plan-vista-previa-y-colores.md`. Usa lo
medido en `2026-10-20-diagnostico-filtros-color.md`.

## Decisiones de Luciano (20-oct)

- Los filtros del proyecto "ACERO #2".."ACERO #8" quedan en la vista por
  encima de los nuestros y tapan nuestro color; la API no deja reordenarlos.
  Luciano eligió **cambiar sus colores** a los de la tabla. El #2 queda en gris.
- Se deja el filtro gris "CQEING otro" para lo que no está en la tabla.

## Qué se hizo

- `Logica/ColoresEnVistas.cs`, `Aplicar(doc, columnas, avisos)`. Se llama al
  final de la generación (`ManejadorGeneracionArmado`), en la misma
  transacción, así que un Ctrl+Z también deshace los colores.
  - Crea o reutiliza "CQEING #3".."#11" (regla por diámetro, con la
    tolerancia del cuadro) y "CQEING otro" (toda la armadura, al final). No
    se duplican al volver a generar.
  - Los pone en cada vista (planta, corte, alzado, detalle, 3D) donde se ve
    alguna de las columnas generadas. Si la vista tiene una plantilla que
    controla los filtros, los pone en la plantilla.
  - Repinta los "ACERO #N" en **todas** las vistas y plantillas que los usan.
    Solo cambia los colores (líneas y relleno sólido); los demás ajustes de
    esos filtros quedan igual.
  - Si una vista falla, deja un aviso y la generación sigue.
- `Modelo/ColoresVarilla.ColorDeFiltroEmpresa(nombre)`: reconoce "ACERO #N"
  por nombre. Tiene su prueba (`color_filtro_empresa`).
- Se borró el comando temporal `ComandoDiagnosticoFiltrosColor` y su botón.
- Aparte: el aviso «la vista previa calculó X estribos y Revit puso Y» ahora
  va a Avisos; antes iba a Detalles, que la ventana no muestra.

## Pendiente

- Prueba en Revit, después del deploy:
  - generar una columna;
  - revisar planta, corte, 3D y una vista donde la columna no aparece (esa
    no debe tener los filtros "CQEING");
  - volver a generar y confirmar que los filtros no se duplican;
  - confirmar que no aparece el aviso de cantidad de estribos.
- Los filtros por uso del proyecto (Acero_Columnas, etc.) también están por
  encima de los nuestros. Si alguno pinta color, para las barras #9 a #11 y
  la "otro" ganaría el suyo. Revisar en la prueba.
