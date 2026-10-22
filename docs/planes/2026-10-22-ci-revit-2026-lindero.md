# CI: el lindero (#139) no compilaba para Revit 2026

## Qué pasó

El check "compilar" del PR #158 falló. El CI compila cada add-in para
cada año de su `addin.json` (columnas: 2026 y 2027) en Release, y en 2026
daba:

```
LectorContexto.cs(290,46): error CS1061: 'PropertyLine' does not contain a
definition for 'GetBoundary'
```

`PropertyLine.GetBoundary()` (#139, leer la línea de propiedad) es nuevo
de la API de Revit 2027. En 2026 la clase existe, pero sin ese método.
En local siempre se compilaba solo para 2027, por eso no se vio.

## Fix

- `practicacq.csproj`: constante `REVIT$(RevitVersion)` (`REVIT2026` o
  `REVIT2027`).
- `LectorContexto.LeerLindero`: las curvas salen de `CurvasDe(linea)`:
  - 2027: `GetBoundary()`, como hasta ahora (probado en Revit).
  - 2026: la geometría del elemento (`Element.get_Geometry`, API pública
    en los dos años), con las curvas sueltas y las de instancias. El
    diagnóstico del 22-oct dio 4 líneas de geometría sin vista, las
    mismas 4 del contorno, pero eso fue en Revit 2027: **en Revit 2026 no
    está probado**.

## Estado

- Compila en Release y en Debug para 2026 y 2027 (el CI corre Release).
- Se sacó `ComandoDiagnosticoLineaPropiedad` y su botón (Luciano, 22-oct):
  era temporal hasta implementar el #139, y en Debug para 2026 no
  compilaba (usaba `IsSketchBased` y `Area`, también nuevos de 2027).
