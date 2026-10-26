# Grapas: ángulo del gancho por punta (180° / 135° / 90°)

## Contexto

Las grapas de los estribos menores (C y S) salían siempre con ganchos de
180°. Pedido de Luciano: que quien arma la columna elija el ángulo de cada
punta por separado: 180°, 135° o 90°. Por defecto, 180°.

- Diseño: `2026-10-26-diseno-grapas-angulo-por-punta.md`.
- Plan: `2026-10-26-plan-grapas-angulo-por-punta.md`.

## Qué se hizo

- **Modelo**:
  - `EstriboMenor.AnguloInicio` / `AnguloFin` (enum `AnguloGancho`), los dos en 180 por defecto.
  - Las configuraciones guardadas antes de este cambio cargan igual que antes.
- **Ventana «Editar estribos menores»**:
  - Junto a cada punta de la grapa elegida hay una tirita chica 90° · 135° · 180°, con los íconos del add-in de vigas.
  - Las dos tiritas de una grapa de fila no se pisan: la del inicio va debajo de su barra y la del fin, encima (`Modelo/TiritasAngulo.cs`).
- **Vista previa 2D y 3D**:
  - Dibujan cada gancho con su ángulo (`PlantaArmado.Grapa`).
  - Se redibujan al aceptar la ventana, aunque el resumen de texto no cambie.
- **Colas** (salen del cuadro):
  - 180° usa la cola de 180 del cuadro.
  - 135° usa la cola de estribo de 135 del cuadro; si no hay, usa 6 diámetros y da un aviso.
  - 90° usa 6 diámetros: el gancho de 90 del cuadro es el de anclaje de barras longitudinales, no el de estribo. Es lo mismo que hace vigas.
- **Generar**:
  - Cada punta se crea con su propio tipo de gancho («CQ Grapa {g}° estribo»).
  - Hay una forma por combinación de ángulos, por ejemplo «CQ Grapa C 135-90», porque Revit exige que los ganchos de la barra coincidan con los de la forma.
  - Si no se puede preparar el gancho o la forma, la grapa sale con 180° y un aviso que nombra la grapa.
- **Diagnóstico en Revit real** (`2026-10-26-diagnostico-ganchos-grapa.md`):
  - Los 7 casos se crearon bien.
  - El centro del doblez no cambia con el ángulo (Axial 2,85 cm, Lateral 2,38 cm), así que la línea que abraza las barras es la misma para cualquier ángulo.

## Retest (26-oct, Luciano)

Columna 387983 de PROY_JULIO_BETO: C 135/135, C 90/180, C volteada 135/90,
S 135/90 y una grapa 180/180.

- Vista previa, vista 3D y planta de Revit coinciden: **OK**.
- Las tiritas no se pisan: **OK**.
- La vista previa se actualiza al aceptar: **OK**.
- Avisos: **ninguno**.

## Pendiente / conocido

- El comando de diagnóstico temporal (`ComandoDiagnosticoGanchosGrapa`) y su botón se borraron después del retest; los resultados quedan en el documento del diagnóstico.
- Menores que quedaron anotados y no se arreglaron:
  - el texto del aviso de `LineaQueAbraza` habla de 180 aunque la grapa sea de 135/90;
  - un valor de ángulo fuera de 90/135/180 en un JSON editado a mano no se corrige;
  - una S muy corta puede hacer fallar la vista previa;
  - en secciones apretadas, una tirita puede tapar la fila de al lado;
  - al reutilizar una forma existente no se verifica que sus ganchos coincidan con el nombre.
