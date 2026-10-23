# Diagnóstico: Revit se cierra al aceptar con estribos menores y la vista 3D abierta (#123)

## Contexto

Primera prueba en Revit de la vista previa 3D (rama `columnas/vista-previa-3d-123`, ya al día
con `main`), 23-oct, modelo `PROY_JULIO_BETO.rvt`. Luciano:

1. Las longitudinales **ya se ven** en el 3D (lo del #137 era de la versión vieja de la rama).
2. Configuró estribos menores y **no aparecieron en el 3D**.
3. Pensando que hacía falta aceptarlos, apretó «Aceptar columna» (aplicar al resto) y
   **Revit se cerró** («An unrecoverable error has occurred»).

## Qué se encontró

- Journal `journal.0206.txt`:
  - 07:29:00 y 07:29:31 abre «Editar estribos menores». Al cerrarlo la primera vez (07:29:19-20)
    hay una regeneración del documento: la vista 3D volvió a simular la columna.
  - 07:29:36 `BotonAceptarColumna`. 07:29:38 `Exception occurred`,
    `ExceptionCode=0xe06d7363` (excepción C++ nativa de Revit, no de .NET). RAM libre: ~710 MB
    de 7,9 GB.
  - Al cerrarse, Revit deshace «Armado de columnas: vista 3D» (la creación de la vista).
- Los mensajes `REGEN_DOC_CONTEXT_INFO` salen en todas las sesiones, también con «Generar»
  normal (journals 0182 a 0206): no son pista.
- `journal.0206.0001.dmp` (volcado de Revit): con `dotnet-dump` no trae la pila del hilo
  principal, que es donde ocurrió el error. No alcanza para ubicarlo.
- El dibujo (`Tubos3D`) es el mismo patrón que el de vigas, que funciona. «Aceptar columna» no
  pide el 3D directamente, pero dispara el guardado de configuraciones (otro ExternalEvent,
  una transacción) y redibujos de la ventana.
- La ventana manda los menores al 3D con `LeerLongitudinalConEstribosMenores()`, lo mismo que
  usa la vista previa 2D.

No se sabe todavía dónde está la falla: simulación (generar con menores y revertir), dibujo,
guardado de configuraciones o la combinación.

## Qué se hizo

Registro temporal, solo en Debug: `Logica/Bitacora3D.cs` escribe en
`%TEMP%\practicacq-previa3d.log` y cierra el archivo en cada línea. Anota:

- cada pedido del 3D: columna, menores en el plan (y cuántos quedan para generar), grapas, cajas;
- abrir la vista; simulación: transacción abierta, estribos listos, longitudinales listas,
  Rebar leídos, revertido;
- barras clasificadas por clase y avisos del generador;
- tubos mostrados, refresco, primer dibujo (bloques y vértices);
- el guardado de configuraciones (empieza / listo).

Si Revit se vuelve a cerrar, la última línea dice en qué paso. Si no se cierra, el registro
igual dice por qué no se ven los menores: si no llegan en el plan, si el generador no los crea
(avisos) o si se clasifican mal.

## Primer registro (23-oct, 07:38-07:39)

Revit crea una carpeta temporal por sesión: los registros quedan en
`%TEMP%\<guid>\practicacq-*.log`, no en `%TEMP%` directo.

| Pedido | Menores en el plan | Rebar creados en la simulación | Clasificadas |
|---|---|---|---|
| 07:38:55 | 0 | 20 | Longitudinal 16, Estribo 38 |
| 07:39:16 | 2 | **82** | Longitudinal 16, Estribo 37, **ningún estribo menor** |

- Los menores **llegan en el plan y se crean** (62 Rebar de más = 2 grapas × 31 alturas; cada
  grapa es un Rebar con una línea recta y un gancho en cada punta, `CreateFromCurvesAndShape`).
  Se pierden **al clasificar**: el único descarte es «todas sus curvas son gancho»
  (`ClasificadorPrevia3D.Clasificar`, `.Where(p => p.Rectos.Any())`).
- Hipótesis sin medir: en la grapa, la recta con ganchos no coincide con la recta sin ganchos
  (`GetTransformedCenterlineCurves(..., suppressHooks: true, ...)`), así que todo queda como gancho.
  Con los estribos la comparación sí funciona (228 curvas de gancho en 38 barras).
- La de Estribo 38 → 37 puede ser el reparto dejando lugar al paquete de menores; sin revisar.
- «Aceptar» no hace falta para el 3D: el panel usa lo que está en la ventana.
- El diagnóstico (botón) usa la configuración por defecto, sin menores: no sirve para esto.

Se agregó al registro, para las 3 primeras barras «todo gancho» de cada simulación, las curvas
que devuelve Revit con y sin ganchos (tipo, puntas y largo, en pies).

## Causa del cierre (encontrada 23-oct, segundo cierre a las 07:45)

Se repitió igual: `BotonAceptarColumna` 07:45:10, `0xe06d7363` 07:45:12. El registro del 3D
**no anotó nada** en ese momento (ni pedido de 3D ni guardado de configuraciones): Revit se cae
dentro del propio clic de «Aceptar».

«Aceptar» redibuja la planta (`RedibujarPanelPlanta` → `LectorContexto.ListarColumnas`), que
reutiliza los `Solid` cacheados en `LectorContexto._cacheElementos`. El comentario de
`LectorContexto.InvalidarCache` ya lo documentaba (probado en Revit): esos sólidos dejan de ser
válidos cuando el documento se modifica (crear/borrar Rebar, `Regenerate`), y usarlos desde
Aceptar termina en un crash nativo. «Generar» invalida el cache al terminar
(`MostrarResultadoGeneracion`); la simulación del 3D modifica el documento (crea Rebar, regenera,
revierte; abrir la vista también) y **no lo invalidaba**.

**Fix:** `VentanaConfiguradorEstribos.MostrarResumen3D` llama a `_contexto.InvalidarCache()`.
`ManejadorVistaPrevia3D` lo llama al final de cada pedido (bien o con error), en el hilo de
Revit, así que no queda un momento en que Aceptar vea el cache viejo.

## Estribos menores que no se ven: causa medida (23-oct, 08:01)

Registro «TODO GANCHO» de una grapa en S (Rebar 394162, «Shape CQ Grapa en S»), en pies:

- Con ganchos (5 curvas): cola 0,2133 · doblez 0,2448 · **recta 0,4425** de (10,8477; 0,0735)
  a (11,2651; −0,0735) · doblez 0,2448 · cola 0,2133.
- Sin ganchos (1 curva): **recta 0,6295** de (10,7595; 0,1046) a (11,3533; −0,1046).

La recta con ganchos está sobre la recta sin ganchos (mismo rumbo, colineal), pero es más corta:
sin ganchos Revit la estira hasta donde empezaban los dobleces. El clasificador marcaba como
cuerpo solo las curvas con las **mismas puntas** que alguna de la barra sin ganchos; en la grapa
ninguna coincide → todo gancho → se descartaba. En los estribos sí coinciden (por eso se veían).

**Fix:** `SimuladorArmado.EstaSobre`: una curva es del cuerpo si sus dos puntas y su punto medio
caen sobre alguna curva sin ganchos (`Curve.Distance(XYZ)`, API pública), con tolerancia 1e-3
pies (~0,3 mm). Las colas y dobleces quedan a centímetros de la recta: siguen siendo gancho.

## Estado / pendiente de retest

- [x] Compila (2027 Debug), pruebas sin Revit: 0 fallas.
- [x] Cierre: causa encontrada y corregida (cache sin invalidar). Compila, pruebas 0 fallas, desplegado.
- [ ] Retest del cierre: «Ver en 3D» → estribos menores → «Aceptar columna», y también Aceptar
      varias veces y cambiar de columna con el 3D abierto.
- [x] Retest del cierre: «Aceptar» ya no cierra Revit (Luciano, 23-oct).
- [x] Estribos menores que no se ven: causa medida y corregida en el clasificador (compila 2027 y 2026).
- [ ] Retest: los menores se ven en el 3D; en el registro ya no aparece «TODO GANCHO» y
      «clasificadas» cuenta EstriboMenor; «Solo ganchos» muestra solo dobleces y colas de grapas.
- [ ] Sacar `Bitacora3D` y las anotaciones al cerrar el #123.
