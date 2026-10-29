# Redondear el largo de los ganchos (vigas)

## Contexto

Reunión del 29-oct (09:50, Carlos): «los ganchos normalmente también se redondean… cada cinco centímetros o cada 10…
no queremos que sea 0,222 sino que sería 0,25… para poderlo cortar o doblar». Que sea configurable y que lo demás se
ajuste solo: el traslapo crece un poco con el redondeo (10:42).

## Decisiones (Juan, 29-oct)

1. **Se redondea el largo total del gancho (B del cuadro)**, el 0.222 que dijo Carlos, no la pata que acota Revit en el
   dibujo. Hacia arriba, al siguiente múltiplo.
2. **Por defecto, 5 cm.** Se elige en Reglas: «No», «5 cm» o «10 cm». Un `reglas.json` guardado antes toma los 5 cm de
   base.
3. **Solo ganchos de barras y bastones.** Los estribos tienen sus propias medidas y no cambian.
4. **Lo que crece va a la cola recta**; el doblez no cambia.
5. **Un solo lugar** (Claude): `Reglas.LargoGanchoRedondeado` lo usan el motor (largo de barra, bastones, arrastre), el
   plano de acero, la vista previa 3D y los tipos de gancho propios de Revit (`TiposAcero.Gancho`). Así el cálculo y
   lo que se dibuja coinciden.

## Ejemplos (con 5 cm)

| Gancho | B | Redondeado | Suma a la barra |
|---|---|---|---|
| #4 a 90° | 0.222 | 0.25 | +0.028 |
| #5 a 90° | 0.278 | 0.30 | +0.022 (215 → 237 mm) |

## Código

- `Modelo/Reglas.cs`: `RedondeoGancho` (0, 50 o 100 mm), `LargoGancho(r, t, redondeo)` y `LargoGanchoRedondeado`.
- Motor, planificador, vista previa, arrastre y `TiposAcero`: usan el redondeado.
- `VistaReglas.RedondeoGanchoCm` y `ArmadorVista`; `tablero.js`: control «Redondear ganchos» bajo «Varilla».
- Pruebas: `ReglasTests`, `GanchosTests`, `AlmacenReglasTests`, `ArmadorVistaTests`. Las pruebas de la lógica de
  ganchos usan `Datos.ReglasSinRedondeo`, para seguir midiendo con el largo exacto.

## Cómo probarlo

```
dotnet test addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2026
```

En Revit: Reglas muestra «Redondear ganchos: 5 cm». Generar un pórtico con gancho de 90° en un #4 y medir el gancho
(0.25 de largo total). Cambiar a «No», guardar y regenerar: vuelve a 0.222.

## Estado

- [x] Código y pruebas (29-oct): 1068 pruebas de vigas pasan; compila para 2026 y 2027.
- [ ] Prueba en Revit.
