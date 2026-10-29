# Ganchos que salen en el traslapo de las longitudinales

## Contexto

29-oct, prueba de la ventana nueva (#205) en Revit 2027, columna «1-5» del modelo de prueba. Luciano: «hay lugares donde salen ganchos en los que no deberían salir». En la captura, a media altura de la columna, justo donde se traslapan las varillas, salen patas de gancho horizontales. Dice que en otro modelo que no se había tocado pasó algo parecido, así que no parece venir solo de formas que quedaron en el modelo (#176).

## Qué se encontró

Registro de Debug `%TEMP%\<carpeta de Revit>\practicacq-longitudinal.log` (corrida de las 14:31). Varilla «#6 CQEING» en (3,412, -0,088):

```
creada: Rebar 815467 ... Z -0,925 -> 7 ganchos[GG]
SpliceRebar en Z=1,538 devolvió 2 id(s)
pieza: Rebar 815472 ... Z -0,925 -> 2,289 ganchos[-G]
pieza: Rebar 815478 ... Z 0,786 -> 7 ganchos[-G]
final: Rebar 815472 ... Z -0,925 -> 2,289 ganchos[GG]
    asa StartOfBar «Start of Bar»: ToOtherRebar ... destino: elemento 815478
    asa EndOfBar «End of Bar»: FixedDistanceToHostFace ... destino: elemento 781598 (la pieza de abajo)
```

- Al partir, Revit **dio vuelta** la pieza de abajo: su extremo 0 («Start of Bar») quedó en el traslapo, atado a la otra pieza, y el extremo 1 abajo. Su gancho de abajo ya estaba bien (`[-G]` = gancho en el extremo 1, que es el de abajo).
- `BarraContinua.Crear` repone después el gancho de abajo en `piezas[0]`, extremo `p.DeArribaAAbajo ? 1 : 0`: da por hecho que cada pieza conserva el sentido de la varilla entera. Con la pieza dada vuelta, el gancho va al extremo 0, que está en el traslapo, y la pieza queda `[GG]`.
- Pasó en 12 piezas de esa columna (`final: ... ganchos[GG]`).
- La orientación del gancho (`Orientacion`) depende del mismo supuesto: con la pieza dada vuelta, izquierda y derecha se invierten.

No se sabe todavía por qué Revit da vuelta la pieza en este modelo y no en las pruebas del #130. No hace falta saberlo para el arreglo: el sentido se lee de cada pieza.

## Arreglo

`Logica/BarraContinua.cs`, después de partir:

- Las piezas se ordenan por altura (la de más abajo primero), sin confiar en el orden de `SpliceRebar`.
- De cada pieza se lee si está dada vuelta (`AlReves`: el extremo 0 de su línea central, sin ganchos, está más alto que el 1).
- La de más abajo recibe el gancho de abajo en su extremo de abajo, y la de más arriba el de arriba en su extremo de arriba, con la orientación invertida si la pieza está dada vuelta.
- Un gancho que haya quedado en un extremo de traslapo se quita.

## Cómo probarlo

1. Compilar y desplegar (`tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027`, con Revit cerrado).
2. Generar de nuevo la columna «1-5» y otra con traslapos.
3. En el registro, ninguna línea `final:` de una pieza con traslapo arriba y abajo a la vez tiene `[GG]`; en la 3D no sale ninguna pata a media altura. Los ganchos de la zapata y del remate siguen igual que antes (mismo lado).
4. Repetir en el otro modelo donde Luciano vio lo mismo.

## Estado

- [x] Arreglo implementado y compilado (2026 y 2027 sin advertencias).
- [x] Probado en Revit 2027 en el modelo de prueba (Luciano, 29-oct: «Listo, todo bien»).
- [ ] Repetir en el otro modelo donde Luciano vio lo mismo.
