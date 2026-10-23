# Colores por diámetro repartidos (vecinos con tonos lejanos)

## Contexto

Con la paleta del 21-oct (`2026-10-21-colores-paleta-estandar.md`, #143) los
colores iban en orden de arcoíris: #3 amarillo, #4 naranja, #5 rojo. Carlos
pidió (23-oct) que diámetros parecidos no tengan colores parecidos, porque se
usan mucho juntos en la misma columna (por ejemplo #4, #5 y #6). El #3
amarillo está bien y se queda.

## Qué se decidió

Mismos colores que antes, reordenados para alternar tonos cálidos y fríos:

- #3 a #8 (los que se arman juntos, incluido el #3 de los estribos) tienen
  seis colores sin dos parecidos: amarillo, azul, rojo, verde, morado, marrón.
- El naranja pasa al #11: estaba entre el amarillo y el rojo, y era justamente
  el que se confundía con los dos.
- El marrón va en el #8 (en lugar del rosado o el naranja) porque es oscuro y
  no se parece a ningún otro de #3 a #7.
- #9 a #11 son los menos usados (fuera del cuadro de traslapos).

| Varilla | Antes (21-oct) | Ahora | Hex | RGB |
|---|---|---|---|---|
| #3 | amarillo | amarillo (sin cambio) | #FBBC04 | 251, 188, 4 |
| #4 | naranja | **azul** | #4285F4 | 66, 133, 244 |
| #5 | rojo | rojo (sin cambio) | #EA4335 | 234, 67, 53 |
| #6 | verde | verde (sin cambio) | #34A853 | 52, 168, 83 |
| #7 | morado | morado (sin cambio) | #9334E6 | 147, 52, 230 |
| #8 | azul | **marrón** | #8B4513 | 139, 69, 19 |
| #9 | marrón | **rosado fuerte** | #E91E63 | 233, 30, 99 |
| #10 | negro | negro (sin cambio) | #000000 | 0, 0, 0 |
| #11 | rosado fuerte | **naranja** | #FF6D01 | 255, 109, 1 |
| #2 y otros | gris | gris (sin cambio) | #5E6670 | 94, 102, 112 |

## Fix implementado

- `addins/armado-columnas/Modelo/ColoresVarilla.cs`: valores nuevos en
  `ColorPorNumero`. La vista previa y los filtros de vista de Revit ("CQEING
  #N" y los "ACERO #N" de la empresa) leen de esa tabla y se repintan solos al
  volver a generar.
- `tools/PruebasVistaPrevia/Program.cs`: colores esperados actualizados y una
  prueba nueva, `colores_de_3_a_8_bien_distintos`, que exige que ningún par de
  #3 a #8 quede a menos de 90 de distancia RGB (con la tabla anterior fallaba:
  #4/#5 a 70 y #3/#4 a 79). 0 fallas.
- Build Debug 2027: correcto, 0 errores.

## Estado / pendiente

- Pendiente de ver en Revit: que el marrón (#8) se distinga del borde casi
  negro de las barras en 3D (#144).
- Vigas (#154, Juan): el issue se actualizó con esta tabla.
- Ojo: en vigas había quedado anotado que Carlos pidió "#4 naranja"
  (19-oct); este pedido nuevo lo reemplaza.
