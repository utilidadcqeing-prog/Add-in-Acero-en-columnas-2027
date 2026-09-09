# Investigación: migrar traslapos a RebarSpliceUtils (API formal de Revit)

## Contexto

Hoy `GeneradorLongitudinal.cs` crea cada traslapo como dos `Rebar` independientes
(`Rebar.CreateFromCurves`), con un corrimiento lateral calculado a mano
(`Corrimiento`: un diámetro de barra, hacia el eje fijo de la posición). Son
barras reales, hosteadas y programables -- pero Revit no las reconoce como
un empalme formal (no hay ningún `RebarSplice` que las vincule). El usuario
pidió investigar si conviene migrar a la API formal de Revit (`RebarSpliceUtils`,
desde 2025), y también verificar que los traslapos no caigan en un nudo y que
el eje del corrimiento sea el correcto (X en cara Y, Y en cara X/esquina).

Se armaron dos comandos temporales de diagnóstico (`ComandoDiagnosticoSplice`
en `ComandoDiagnosticoRebar.cs`, botón "Diagnóstico de Empalme") para probar
todo contra Revit real antes de tocar el generador, siguiendo la regla nueva
del `CLAUDE.md`.

## Qué se probó y qué se encontró

### 1. `RebarSpliceUtils.SpliceRebar` con línea de corte manual -- descartado

Construir a mano la línea/plano de corte (`Line` + `linePlaneNormal`) para
partir una barra existente falló las dos veces probadas
(`SpliceGeometryDoesNotIntersectAllTheBarsInTheSet`), incluso con barras de
sobra de longitud. La semántica exacta de esos dos parámetros no es trivial
de adivinar bien -- se abandonó esta variante a favor de la siguiente.

### 2. `RebarSpliceUtils.GetSpliceGeometries` + `SpliceRebar` (reglas automáticas) -- funciona, pero no es apta para producción tal cual

Usando `RebarSpliceRules` (longitud máxima/mínima) para que Revit calcule
solo dónde partir, la prueba **funcionó** sobre una barra real de 8.886 m
(host 152853, longitud máxima 6 m):

```
Rebar 156804: (-4.388, -4.665, 5.154) -> (-4.388, -4.665, 11.154)
Rebar 156809: (-4.388, -4.681, 9.757) -> (-4.388, -4.681, 14.040)
RebarSplice: SplicePosition=Middle, SourceRebarId=156809, ConnectedRebarId=156804
```

- Corrimiento lateral real aplicado por Revit (`RebarSpliceShiftOption.BarPlane`):
  1.6 cm -- coincide casi exacto con el diámetro de la barra (15.9mm), el
  mismo criterio que ya usa el código a mano.
  - **X idéntico, se corrió Y** en las dos barras probadas -- ambas resultaron
    ser la misma posición de esquina (mismo X=-4.388 en las 4 pruebas hechas
    en la sesión, solo cambiaba el host/Z). Esto es consistente con la regla
    ya codificada (esquinas y cara-X corren en Y), pero **no se llegó a
    probar una barra de cara Y** (que debería correr en X) para confirmar
    que Revit también respeta esa distinción -- queda pendiente.
- Longitud de solape resultante (11.154 - 9.757 = 1.397 m) coincidió exacto
  con la longitud de solape configurada en el `RebarSpliceType` real que ya
  tiene el modelo del usuario ("Empalme solapado 60D", multiplicador 88,
  no 60 -- el nombre es una convención de la empresa, no litera el valor).
- **Problema real, confirmado sobre el host 152829** (el mismo que ya tira
  el aviso de producción "no hay nivel disponible para el traslapo
  intercalado..."): al dejar que Revit elija el punto de corte solo (más
  lejos posible dentro del máximo), lo puso en un lugar que en la vista se
  ve cruzando cerca de una viga. **Esto es esperado, no un bug**: el
  algoritmo automático de Revit no tiene ninguna noción de "no cortar en un
  nudo/vigueta/zapata" -- esa exclusión hoy solo existe en
  `PlanificarCortes`/`niveles` (filtra `TipoTramo.DentroDeViga`) y esta
  variante de la API no pasa por ahí.
  - **Conclusión**: si se migra, hay que usar la variante de geometría
    EXPLÍCITA (`SpliceRebar(doc, id, options, listaDeGeometrias)` con los
    puntos Z que ya calcula `PlanificarCortes`/`PlanificarSplicesReales`),
    nunca la automática por reglas -- la automática ignora reglas del
    equipo que ya están bien resueltas en el código actual.

### 3. Conectar dos barras YA EXISTENTES como empalme oficial (sin reconstruir nada) -- API interna, no se puede usar

La idea (evitar tocar la lógica de creación ya verificada, y solo "avisarle"
a Revit que dos barras forman un empalme) dependía de
`RebarSplice.ConnectSplicedRebarsToChain` / `ConnectUnifiedRebarToChain` /
`CreateSpliceInfo`. **Confirmado por reflexión directa sobre
`RevitAPI.dll` real (no solo la documentación XML)**: los tres son
`internal`, no públicos -- un add-in externo no los puede llamar aunque
`RevitAPI.xml` los documente. Descartado. La documentación de Revit incluye
miembros internos; hay que verificar contra el ensamblado compilado, no
solo contra el XML.

## Decisión pendiente (a confirmar con el usuario/equipo)

Con la API interna descartada, quedan dos caminos reales:

1. **Migrar de verdad**: crear una barra completa por tramo de host y
   partirla con `SpliceRebar` usando los puntos Z ya decididos por
   `PlanificarCortes`/`PlanificarSplicesReales` (no las reglas automáticas),
   position `Middle`. Falta confirmar antes de implementar: (a) que el
   corrimiento automático (`RebarSpliceShiftOption.BarPlane`) respeta el eje
   correcto también en una barra de cara Y, no solo en esquina/cara-X; (b)
   cómo interactúa con los ganchos de zapata y los cortes forzados por
   límite de host (que no son splices reales); (c) impacto en el resto del
   pipeline (`MarcaEstribos`, borrado/regeneración). Es una reescritura real
   de una parte de `GeneradorLongitudinal.cs` que hoy funciona y ya está
   validada.
2. **Mantener el enfoque actual**: dos `Rebar` reales, corrimiento propio ya
   verificado contra el modelo de referencia. Sigue siendo hormigón armado
   real, programable y correcto en obra -- solo que Revit no lo reconoce
   como un `RebarSplice` formal (no aparece así en un schedule que filtre
   específicamente por ese tipo de elemento).

## Extra: se confirmó que los cortes reales SIEMPRE caen en la mitad de un tramo, nunca en un cambio de piso

Los candidatos de traslapo real (`niveles` en `GenerarColumna`) ya se calculan
como la mitad de cada tramo físico (`(t.ZInferior + t.ZSuperior) / 2`), nunca
en el borde. La única razón por la que un traslapo real podía coincidir con
un cambio de piso era el "coalescing" de `PlanificarCortes` (cuando el
candidato cae a menos de 3cm de un límite de host) -- un caso casi teórico
(un tramo de menos de 6cm), no algo que pase en una columna real. Esto
importa para la migración: **la gran mayoría de los traslapos reales caen en
la mitad de un host** (aptos para la API formal), y el caso de "cambio de
piso" es la excepción rara, no la regla.

## Decisión: se migró (implementado)

Con el usuario confirmando que la ganancia (Revit reconociendo el traslapo
como `RebarSplice` real, para schedules/inspección) sí hace falta, se migró
`Logica/GeneradorLongitudinal.cs` (`CrearBarraConTraslapos`):

- `GeneradorLongitudinal.Generar` resuelve una vez `GeometriaSeccion.BuscarTipoEmpalme(doc)`
  (primer `RebarSpliceType` que tenga el documento -- sin tipo cargado, se
  sigue con el método anterior para todo, con aviso solo cuando hace falta un
  traslapo real y no hay tipo).
- `CrearBarraConTraslapos` agrupa los segmentos por host. Si un corte real
  cae ENTRE dos segmentos del MISMO host (el caso normal, ver arriba), se
  crea una sola barra para todo el host y se parte con
  `RebarSpliceUtils.SpliceRebar` (`RebarSpliceGeometry` en el Z exacto que ya
  decidía `PlanificarCortes`, posición `Middle`) -- Revit calcula el
  corrimiento lateral solo (`RebarSpliceShiftOption` del tipo de empalme).
  Las dos piezas resultantes se vuelven a marcar con `MarcaEstribos` (el
  split las reemplaza por elementos nuevos, la marca original se pierde).
- Si el corte real coincide con un cambio de piso (caso raro), si hay 2+
  traslapos reales en el mismo host (caso nunca verificado, ver diagnóstico
  de estrés más arriba), si no hay `RebarSpliceType` cargado, o si
  `SpliceRebar` falla por cualquier motivo (con aviso, no excepción): se cae
  al método anterior (dos `Rebar` con corrimiento a mano), igual que
  funcionaba hasta ahora -- ningún caso queda sin traslapo.

## Estado

Compilado. Pendiente: desplegar y retest del usuario generando una columna
real con al menos un traslapo esperado en la mitad de un tramo, confirmando
en 3D/schedule que ahora aparece como `RebarSplice` reconocido por Revit.

Comandos de diagnóstico (`ComandoDiagnosticoSplice`, botón "Diagnóstico de
Empalme") quedan en el add-in por ahora -- temporales, sacar junto con
`ComandoDiagnosticoRebar` una vez confirmado el retest. `ComandoDiagnosticoConectarSplice`
se agregó y se sacó en la misma sesión (API interna, no viable).
