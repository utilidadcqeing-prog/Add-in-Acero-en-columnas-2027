# Varillas propias "#2 CQEING" a "#8 CQEING"

Fecha: 19-oct-2026. Estado: **implementado y compilado; falta probar en Revit real.**

## Contexto

La lista de barras del configurador muestra solo los tipos de barra que trae
el modelo abierto. En un modelo con plantilla europea salen "10 B 400 S",
"16 B 500 S", "25 B 400 S"… y ninguna de nuestras varillas #2 a #8.

Además, como el add-in reconocía las barras por diámetro (tolerancia 0,6 mm),
la "16 B" (16 mm) pasaba por nuestra #5 (15,9 mm) y la "25 B" por la #8
(25,4 mm): se les aplicaban nuestros ganchos y traslapos sin serlo.

## Lo que se decidió con Luciano

- El add-in tiene sus propios tipos de barra: "#2 CQEING" … "#8 CQEING".
- Al abrir el configurador, si al modelo le falta alguno, **pregunta** si
  agregarlos. Si dice que no, el configurador abre como antes y **vuelve a
  preguntar la próxima vez** (la respuesta no se guarda).
- Las barras que ya tiene el modelo no se tocan.
- En la plantilla habitual también va a preguntar la primera vez (ahí hay
  "#4", "#3 COL"… pero no "#4 CQEING"). Aceptado así.

## Implementación

- `Modelo/CuadroVarillas.cs`: `NombrePropio(n)` y `PorNombrePropio(nombre)`.
- `Logica/VarillasPropias.cs`: `Faltantes(doc)` y `Crear(doc, faltantes)`.
  Crea con `RebarBarType.Create` (no duplica una barra del modelo, para no
  arrastrar nada de su plantilla) y fija los diámetros con
  `SetBarTypeDiameters`: nominal = modelo = diámetro del cuadro; doblado de
  estribo 4 d, de gancho estándar y doblez estándar 6 d (el cuadro de la
  empresa, ver `2026-10-08-diagnostico-formas-ganchos-propios-64.md`). Relee el
  diámetro después de crear y deshace todo si no coincide.
- `ComandoEstribos.cs`: `OfrecerVarillasPropias` pregunta con un `TaskDialog`
  Sí/No antes de abrir la ventana.
- `Logica/ProveedorTiposReal.cs`: las propias van primero en la lista, por
  número.
- `Modelo/TipoBarra.cs` (`PorDefecto`): elige primero "#N CQEING"; si no
  existe, cae al primero de ese diámetro, como antes.
- `Modelo/EstandarCQ.cs` (`Buscar`): un tipo propio se resuelve por nombre;
  cualquier otro sigue por diámetro, para no romper modelos y configuraciones
  guardadas que usan las barras de la plantilla. **Consecuencia:** si alguien
  elige a mano la "16 B" de una plantilla europea, todavía se la trata como #5.

API usada, verificada como pública en `RevitAPI.xml` de 2027:
`RebarBarType.Create(Document)`, `RebarBarType.SetBarTypeDiameters`,
`BarTypeDiameterOptions(RebarBarType)` y sus propiedades de diámetro,
`Element.Name` (setter).

## Pendiente de probar en Revit

1. Modelo europeo: al abrir el configurador aparece la pregunta; con "No"
   abre como antes y al reabrir vuelve a preguntar.
2. Con "Sí": aparecen "#2 CQEING" … "#8 CQEING" al principio de la lista, la
   #3 queda elegida para el estribo y la #4 para la longitudinal.
3. En Revit, Administrar tipos de barra: diámetro de cada una (6,4 / 9,5 /
   12,7 / 15,9 / 19,1 / 22,2 / 25,4 mm).
4. Generar una columna con las CQEING: que los estribos salgan con gancho de
   135° y las longitudinales con su gancho en la zapata y traslapos. **No
   verificado todavía:** si un tipo creado con `RebarBarType.Create` permite
   todos los ganchos (`GetHookPermission`); si el armado falla por gancho, es
   lo primero a revisar.
5. Plantilla habitual: pregunta una vez; después de aceptar, no vuelve a
   preguntar.
