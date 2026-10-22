# Observaciones de la reunión del 21-10-2026 -- acero en columnas

## Contexto

En la reunión diaria, Carlos revisó con Juan el acero que generó el
add-in de columnas en el modelo de Juan (columna de 25×30, 4+4
barras #6 y #7, esquinas #8, estribos #3, recubrimiento 4 cm).

La idea general: **"no puede haber nada figurativo o esquemático"**.
Lo que se ve en el add-in (vista previa de la sección, vista previa
3D) tiene que ser igual a lo que después queda en Revit. Carlos lo
marcó como **ruta crítica**: quería empezar a armar columnas de un
proyecto real.

Issue general: #129 -- un sub-issue por punto (punto N = issue #129+N,
ver la tabla de Estado al final).

## Prioridad alta: colocación del acero

1. **Los estribos menores no abrazan las barras longitudinales.** En
   el 3D, el extremo de las cajas y grapas no toca la barra roja que
   debería sujetar (cara de 25). En la cara de 30 (moradas) parecía que
   sí entraba, pero hay que revisarlo igual. Barras y estribos son
   piezas sólidas: el estribo envuelve la barra por fuera, nunca se
   meten una dentro de la otra. Medir contra Revit real, no a ojo.
2. **Avisar cuando hay choque, sin impedirlo.** Si el usuario coloca
   algo que se mete dentro de otra pieza, el add-in avisa ("si lo
   colocas así, ten en cuenta que…") y deja seguir. Relacionado: #47.
3. **Revisar el aviso "estribo cerrado de 4,2 cm entre ejes, necesita
   4,8 cm"** en la sección de 25×30. Confirmar si la cuenta es correcta.

## Prioridad alta: vista previa de la sección ("Editar estribos menores")

4. **Sección a escala real.** 25×30 se dibuja cuadrada. Va a haber
   columnas de 120×50 y hay que ver si los estribos caben.
5. **Mostrar las medidas de la sección** en esa ventana (hoy hay que
   volver atrás).
6. **Dibujar el estribo exterior** (hoy no aparece).
7. **Barras y estribos con su diámetro real**, y que el "no cabe" se
   note en el dibujo, no solo como texto.
8. **Las longitudinales no aparecen en la vista previa 3D** (#123).

## Prioridad media: ganchos de las longitudinales

9. **El gancho de la base parece demasiado largo.** Con #4: 3,50 m en
   total, ~3,28 m rectos y ~22 cm de gancho. Revisar contra la tabla y
   el recubrimiento, probar en un modelo con zapata; si no cabe, gancho
   de 180°.
10. **En el borde del lote, los ganchos doblan hacia dentro del
    proyecto**, nunca hacia el vecino. Hace falta saber dónde está el
    límite: línea de propiedad (la usó Juan en cantidades), planos de
    área, o una línea del modelador. Carlos pidió **proponer un método**.

## Después: interfaz y nombres

11. **Cambiar el nombre "Estribos de columnas"**: el add-in arma toda la
    columna. Relacionado: #12, #17.
12. **Aclarar "Tipo de barra"**: es la de los estribos.
13. **Tabla con la información de las barras** (diámetro real), como en
    vigas, solo para consultar.
14. **Colores por diámetro más distintos** (#6 rosado y #7 naranja se
    confunden). Relacionado: #122.
15. **Aristas de las barras** en "colores coherentes": no se ven los
    bordes. Ver si se puede configurar (también pasa en vigas).
16. **Al regenerar, reemplazar el acero generado antes** y armar la
    columna completa. Va en sentido contrario a #61 -- revisarlo.

## No es de columnas

- Voladizos (siempre confinados) y la barra que no se puede
  deseleccionar: add-in de vigas (Juan).
- Actualizar sin cerrar Revit: lo revisa Edgeryz en el gestor.
- Las varillas "#N CQEING" no aparecían porque Juan tenía el add-in
  desactualizado; no es un error.

## Estado

Nada implementado todavía. Se va resolviendo punto por punto.

| Punto | Issue | Tema | Estado |
|---|---|---|---|
| 1 | #130 | Estribos menores no abrazan las longitudinales | Pendiente |
| 2 | #131 | Aviso de choque sin impedirlo | Pendiente |
| 3 | #132 | Aviso "4,2 cm, necesita 4,8 cm" en 25×30 | Pendiente |
| 4 | #133 | Sección a escala real | Pendiente |
| 5 | #134 | Medidas de la sección en la ventana | Pendiente |
| 6 | #135 | Estribo exterior en la sección | Pendiente |
| 7 | #136 | Diámetros reales en la sección | Pendiente |
| 8 | #137 | Longitudinales en la vista previa 3D | Pendiente |
| 9 | #138 | Gancho de la base demasiado largo | Pendiente |
| 10 | #139 | Ganchos hacia dentro en el borde del lote | Pendiente |
| 11 | #140 | Nombre "Estribos de columnas" | Pendiente |
| 12 | #141 | Texto "Tipo de barra" | Pendiente |
| 13 | #142 | Tabla con información de barras | Pendiente |
| 14 | #143 | Colores más distintos | Pendiente |
| 15 | #144 | Aristas en "colores coherentes" | Pendiente |
| 16 | #145 | Regenerar reemplaza lo anterior (ver #61) | Pendiente |
