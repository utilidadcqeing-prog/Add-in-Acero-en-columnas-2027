# Gestor en Revit

Componente del **gestor de add-ins**, no un add-in más: agrega el panel **«Gestor»** a la pestaña CQEING de Revit con
el botón que abre el gestor y, después, el aviso de versiones nuevas. Plan: `docs/planes/2026-10-22-gestor-en-revit-boton-y-aviso-159.md`.

- **Nunca se publica con etiqueta propia** (`gestor-revit-vX.Y.Z`): no es un add-in del catálogo y no debe aparecer
  como tarjeta en el gestor. Viaja dentro del paquete del gestor (`publicar-gestor.yml`).
- Lo instala el propio gestor en cada Revit instalado cuando se abre (`RegistroComplementoRevit`), en
  `%LOCALAPPDATA%\CQEING\Gestor\revit\<versión>\<año>\`, con su `CQEING.zz-gestor-revit.addin`.
- **Por qué «zz-»:** Revit carga los `.addin` en orden alfabético por nombre de archivo y cada add-in agrega su panel
  al final de la pestaña CQEING. Con «zz-» el panel «Gestor» se crea el último y queda a la derecha de todos los de
  los add-ins, para que no se confunda con ellos. No ponga un add-in con un id que ordene después de «zz-».
- Para probarlo en este equipo sin pasar por el gestor:
  `powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin gestor-revit -Revit 2026`
  (escribe el mismo archivo que el gestor, `CQEING.zz-gestor-revit.addin`)
