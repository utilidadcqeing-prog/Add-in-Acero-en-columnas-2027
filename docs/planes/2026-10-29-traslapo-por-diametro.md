# Traslapo y gancho de cada número de barra en una capa (vigas)

## Contexto

Reunión del 29-oct en la tarde (43:07–50:48, Santiago): con dos números en la misma capa (por ejemplo #7 en las esquinas
y #5 al medio, como en el proyecto de cinco pisos), la barra menor «respeta la varilla más grande». Lleva el traslapo y
el gancho del mayor, y en Revit su largo no da la varilla: «no queda de tres metros sino de tres con diez», o 2.98.
«No puede ser un traslapo grupal… depende de la varilla.»

Causa (código): `MotorVigas.Resolver` resolvía la capa con el número mayor (`grupos.Max`) y todas las barras compartían
su parte recta. `PlanificadorAcero` dibujaba la menor con esa parte recta y con su gancho, así que su largo total no
cerraba en el módulo, y su traslapo era el del mayor (0.90 del #5 en vez de 0.75 del #4).

## Decisiones (Juan, 29-oct)

1. **La barra menor traslapa en la misma luz que la mayor**, con su traslapo mínimo y su gancho, y su largo en múltiplo
   del módulo (0.50).
2. **El tablero sigue mostrando una línea por capa**, la del número mayor, como la maqueta. El rótulo de la barra
   muestra la medida de cada número cuando son distintas: `2#8L= 3.50/4.00 · 2#5L= 3.70/4.00`.
3. **Cerca del traslapo del mayor** (Claude): el menor busca el mismo punto de referencia (inicio, centro o fin, según la
   capa) a lo sumo un módulo de distancia. Así sigue los traslapos corridos o alargados a mano, sin cambios manuales
   propios. Si así no sale, en cualquier punto de su luz; si tampoco, la parte recta del mayor con su gancho (como antes).
4. **Su traslapo puede pasar del mínimo** para que el largo cierre en el módulo (#4 junto a #5: 1.00 con mínimo 0.75).
5. **Los kg** de cada barra suman cada número con su propio largo; la cara más económica se elige con esos kg.
6. **El corte 1-1** tampoco cae en el traslapo de la barra menor (`ZonasTraslapo` lo incluye).

## Código

- `Modelo/Resultado.cs`: `MedidaBarra` y `Barra.Menores` / `Barra.Medida(diametro)`; `SolucionCapa.ZonasTraslapo`.
- `Vigas/BarrasMenores.cs`: resuelve cada número menor con las luces del mayor; lo llama `MotorVigas` después de los
  cambios manuales (y en las dos opciones de la capa de la cara).
- `Generacion/PlanificadorAcero.cs`: cada pieza toma la medida de su número.
- `Tablero/Rotulo.cs` y `ArmadorVista`: el rótulo por medida.
- Pruebas: `MotorVigasTests` (#5 con #4, #7 con #5, #8 con #5), `PlanificadorAceroTests`, `RotuloTests`.

Queda igual: el arrastre (el rótulo mientras se arrastra es el del mayor), los avisos de traslapo largo y los bastones,
que ya usaban su propio número.

## Cómo probarlo

```
dotnet test addins/acero-vigas/calculo/CQEING.Vigas.Calculo.Tests
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin acero-vigas -Revit 2026
```

En Revit: un pórtico con 2 #7 + 2 #5 arriba. En el tablero, el rótulo de cada barra trae las dos medidas. Generar y
medir las #5 (largo total en múltiplo de 0.50, traslapo de 0.90 o más, en la misma luz que el de las #7).

## Estado

- [x] Código y pruebas (29-oct): 1081 pruebas de vigas pasan; compila para 2026 y 2027.
- [ ] Prueba en Revit, con lo que mande Santiago.
