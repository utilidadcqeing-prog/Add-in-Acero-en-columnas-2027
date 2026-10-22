# Colores por diámetro: paleta "Estándar" de Google Sheets (#143)

## Contexto

En la reunión del 21-oct (observación 14 de
`2026-10-21-observaciones-reunion-columnas.md`, issue #143) se pidió que los
colores por diámetro fueran más distintos: el #6 rosado se confundía con otros
tonos. El ingeniero mostró la paleta "Estándar" de Google Sheets como
referencia de colores bien contrastados.

## Qué se decidió

- Usar los colores de la paleta "Estándar" (tema por defecto de Google Sheets).
- Excepciones: el **blanco** no se usa (no se ve sobre el fondo blanco de
  Revit), y el último color (**celeste #46BDC6**) se reemplaza por un
  **morado**.
- Si un número ya tenía un color parecido (por ejemplo, el #3 amarillo), se
  mantiene ese color con el tono nuevo.
- Con esto quedan 7 colores para 9 varillas: el #9 y el #11 (las que menos se
  usan; ni siquiera están en el cuadro de traslapos) llevan dos colores fuera
  de la paleta: marrón y rosado fuerte.

## Tabla

| Varilla | Antes | Ahora | Hex | RGB |
|---|---|---|---|---|
| #3 | amarillo oscuro | Amarillo (Estándar) | #FBBC04 | 251, 188, 4 |
| #4 | naranja | Naranja (Estándar) | #FF6D01 | 255, 109, 1 |
| #5 | rojo | Rojo (Estándar) | #EA4335 | 234, 67, 53 |
| #6 | rosado | Verde (Estándar) | #34A853 | 52, 168, 83 |
| #7 | morado | Morado (en lugar del celeste) | #9334E6 | 147, 52, 230 |
| #8 | azul | Azul (Estándar) | #4285F4 | 66, 133, 244 |
| #9 | verde azulado | Marrón (fuera de la paleta) | #8B4513 | 139, 69, 19 |
| #10 | verde | Negro (Estándar) | #000000 | 0, 0, 0 |
| #11 | marrón | Rosado fuerte (fuera de la paleta) | #E91E63 | 233, 30, 99 |
| #2 y otros | gris | gris (sin cambio) | #5E6670 | 94, 102, 112 |

## Fix implementado

- `Modelo/ColoresVarilla.cs`: valores nuevos en `ColorPorNumero`. La vista
  previa del panel y los filtros de vista de Revit (incluidos los "ACERO #N" de
  la empresa) leen de esta misma tabla, así que no hubo que tocar nada más.
- `tools/PruebasVistaPrevia/Program.cs`: colores esperados actualizados
  (0 fallas).

## Estado / pendiente

- Pendiente de ver en Revit: confirmar que el negro (#10) se distingue de las
  líneas del modelo y que el amarillo (#3) se lee bien sobre fondo blanco.
- Enviar la tabla al ingeniero Juan.
