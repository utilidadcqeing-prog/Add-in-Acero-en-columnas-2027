# Integración de `equipo/armado-columnas` a `main` (monorepo)

## Contexto

`main` se reorganizó como monorepo: cada add-in vive en `addins/<id>/`, la API de
Revit llega por NuGet y el despliegue viejo (`tools/deploy.ps1`, `tools/publish.ps1`)
se reemplazó por `tools/desplegar-local.ps1` (pruebas en la PC) y por las etiquetas
`<id>-vX.Y.Z` (publicación al equipo con el gestor).

`main` había tomado el código de la rama en el commit `515cff4`. Desde ahí la rama
siguió con: traslapos nuevos (piezas redondeadas, fuera de los nudos), varillas
propias "#2 CQEING" a "#8 CQEING", número de barra por posición, vista previa de la
sección, colores por diámetro, estribos menores y formas/ganchos propios (#64).

## Qué se hizo

- Merge de `equipo/armado-columnas` sobre `main`. Git llevó los cambios de
  `Logica/`, `Modelo/` y `Vistas/` a `addins/armado-columnas/`; se comprobó que cada
  archivo quedó idéntico al de la rama.
- `App.cs` y `practicacq.csproj`: se conserva la versión de `main` (la del monorepo);
  de la rama solo se aplicó quitar el botón del diagnóstico temporal de formas propias
  (y se borró `ComandoDiagnosticoFormasPropias.cs`).
- `tools/PreviewUI`, `tools/PruebasTraslapos` y `tools/PruebasVistaPrevia`: las rutas
  a los archivos del add-in ahora apuntan a `..\..\addins\armado-columnas\...`.

## Verificación

- `dotnet build addins/armado-columnas/practicacq.csproj -p:RevitVersion=2027` y `2026`:
  0 errores. También en Release (lo que usa el publicador).
- `dotnet run --project tools/PruebasTraslapos` y `tools/PruebasVistaPrevia`: 0 fallas.
- `dotnet build tools/PreviewUI`: 0 errores.
- Ensayo de `tools/CQEING.Publicador` (sin publicar): genera los ZIP de 2026 y 2027.

## Pendiente

- Probar en Revit con `tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027`.
- Publicar al equipo: etiqueta anotada `armado-columnas-vX.Y.Z` sobre `main`, después
  de mergear este PR.
