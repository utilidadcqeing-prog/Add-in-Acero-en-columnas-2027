# Plan: aclarar en Reglas que el gancho es el largo total (observaciones 3 y 4 de Santiago, 28-oct)

## Contexto

- Santiago anotó en el sheet de observaciones (pestaña "Addin - Acero Vigas", puntos 3 y 4) que la medida de los
  ganchos no cumple la regla: "135° de 0.11 y en el dibujo 0.08", "#5 a 90° de 27–28 cm y en el dibujo 25".
- Midiendo en Revit (28-oct, viga aérea VIG_25X35 del Pórtico 3 y viga de cimentación 0.30 × 0.35) los ganchos
  cumplen: el estribo 135° #3 mide 57 + 56 = 113 mm y el 180° #5, 65 + 175 = 240 mm.
- La diferencia es de lectura: Reglas solo muestra la B del cuadro "Dimensiones mínimas para ganchos estándar"
  (largo total, curva + tramo recto, la que se suma a la barra) y la cota de Revit en el dibujo es la pata medida
  por fuera (tramo recto + radio del doblez + diámetro). Estribo #3: 57 + 19 + 9.5 ≈ 0.08; 90° #5: 191 + 48 + 16 ≈ 0.25.
- Fuera de alcance: cambiar valores, cálculos o tipos de gancho de Revit.
- Rama: `vigas-reglas-largo-gancho`, desde `main`.

## Criterios de aceptación

- [ ] La pantalla de Reglas dice que los valores de gancho y estribo son el largo total del gancho.
- [ ] Dice por qué la cota del dibujo de Revit sale menor, con un ejemplo.
- [ ] No cambia ningún valor ni cálculo.

## Hallazgos del código y de Revit real

- Encabezados de la tabla: `addins/acero-vigas/Tablero/web/tablero.js:1628` ("Gancho 90°", "Gancho 180°", "Estribo 135°").
- Los valores son `Gancho.B` (`calculo/CQEING.Vigas.Calculo/Modelo/Reglas.cs:4-9`, 39-45), mostrados en
  `Tablero/ArmadorVista.cs:122`.

## Decisiones

1. **Opción 1: aclarar con un texto, sin columnas nuevas** (2026-10-28, usuario): la tabla ya es angosta (5 columnas);
   una nota debajo explica la diferencia sin hacerla más pesada.

## Plan paso a paso

1. **Nota bajo la tabla de diámetros** — archivo: `Tablero/web/tablero.js` — texto: *"Gancho y estribo: largo total
   del gancho (curva + tramo recto), el que se suma a la barra. En el dibujo de Revit la cota es solo la pata, por
   eso sale menor: #5 a 90° = 0.278 aquí y 0.25 en el dibujo."* — cómo se prueba: compilar, desplegar y abrir Reglas
   en Revit.

## Cómo probarlo

```
dotnet build addins/acero-vigas/<proyecto>.csproj -c Debug -p:RevitVersion=2027
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2027
```

1. Abrir el tablero de vigas en Revit → Reglas: la nota aparece debajo de la tabla y se lee completa.

## Estado

- [x] Plan revisado con el usuario (opción 1, 28-oct).
- [x] Pasos implementados (28-oct): nota en `tablero.js`, debajo de la tabla de diámetros. Build Debug 2027 sin
  errores y 26 pruebas de `ProtocoloTablero` en verde.
- Hallazgo (28-oct): el primer despliegue fue para Revit 2027, que no está instalado en este equipo; la nota no se
  vio. Se desplegó para 2026 (`-Revit 2026`). El ejemplo pasó de 0.278 a 0.28: las reglas guardadas muestran 0.280.
- [ ] Probado en Revit.
