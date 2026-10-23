# Plan: borde oscuro de las barras en la vista previa 3D de columnas (#144, #123)

## Contexto

- Luciano, 23-oct, probando `main` antes de publicar `armado-columnas-v1.3.0`: "se dañó lo de las
  aristas/borde otra vez". Es lo mismo que pidió Carlos en el #144 (22-oct): en «colores
  coherentes» dos barras pegadas se ven como una mancha si no tienen borde.
- Fuera de alcance: el borde de las barras **generadas** (filtros de `ColoresEnVistas`, #144), que
  sigue igual en `main`; el add-in de vigas y `addins/compartido` (decisión 2 del plan del #123).
- Entorno: Revit 2027 / net10.0-windows, modelo PROY_JULIO_BETO. Rama
  `columnas/contorno-previa-3d-144`, worktree `C:\Proyecto\addins-main`, desde `origin/main` 829688e.

## Criterios de aceptación

- [ ] En la vista «CQEING Columnas 3D», cada barra de la vista previa tiene un contorno casi negro
      (30, 30, 30), igual que la vista previa de vigas, y dos barras pegadas se distinguen.
- [ ] El contorno no parpadea sobre la superficie del tubo.
- [ ] Una barra partida en pedazos (gancho / recta / traslapo, o cada curva de un estribo) no
      muestra anillos en las uniones entre sus pedazos, solo en sus puntas reales.
- [ ] Los filtros siguen funcionando igual; nada cambia en vigas.

## Hallazgos del código y de Revit real

1. `Logica/Tubos3D.cs` es una copia del dibujo de vigas hecha el 21-oct
   (`addins/acero-vigas/Revit/PreviaAcero3D.cs`). El 22-oct vigas le agregó el contorno (commit
   `7752af1`, "contorno casi negro en las barras de la vista previa 3D"); la copia de columnas
   quedó sin él. El arreglo del #144 (`ColoresEnVistas.ConColor`, commit `96214bb`) está intacto
   en `main`: es otra cosa (las barras generadas).
2. Cómo lo hace vigas (`PreviaAcero3D.Tubo` y `RenderScene`): cuatro líneas a lo largo de cada
   tubo (cada cuarto de vuelta) y un anillo en cada punta, a 1,06 × el radio (sobre la superficie
   misma las líneas y los triángulos se pelean el píxel y parpadean), color (30, 30, 30)
   (`ColoresDiametro.RgbContorno`; negro puro se ve blanco con fondo oscuro). Se dibujan con
   `VertexFormatBits.PositionColored`, `PrimitiveType.LineList`, en bloques aparte.
3. Diferencia con vigas: allí cada tubo es una barra entera. En columnas cada tubo es un
   **pedazo** de barra (`ManejadorVistaPrevia3D.Tubo`: cada curva de Revit y cada tramo
   recto/gancho/traslapo es un pedazo). Con anillos en todas las puntas, un estribo mostraría
   anillos en cada esquina.

4. **Lo que vio Luciano era el modelo, no la vista previa** (23-oct, después de implementar el
   paso 1). En `PROY_JULIO_BETO.rvt` la última vez que se generó fue el 21-oct a las 23:58
   (`journal.0189.txt`); el borde oscuro del #144 entró el 22-oct a las 08:52 (`96214bb`, ya en
   `armado-columnas-v1.2.0` y en `main`). Hoy en ese modelo solo se usó la vista previa 3D
   (journals 0205-0211: ningún «Generar»). Los filtros de color de ese modelo siguen pintados como
   los dejó la versión del 21-oct: líneas del color del diámetro (y la paleta de antes del #164).
   El código de `main` está bien: `ColoresEnVistas.ConColor` pone las líneas en (30, 30, 30) en 3D;
   los filtros de vigas («CQEING Acero #N») son otros y también van con líneas oscuras. Se arregla
   volviendo a generar en ese modelo con la versión actual. Pendiente confirmarlo leyendo los
   filtros de la vista en Revit.

## Decisiones

1. **Mismo contorno que vigas, copiado en `Tubos3D`** (2026-10-23): mismas constantes y misma
   forma de dibujarlo, sin compartir código (decisión 2 del plan del #123).
2. **Anillos solo en las puntas sueltas** (2026-10-23): una punta de un tubo lleva anillo solo si
   ninguna otra punta de otro tubo cae en el mismo lugar (tolerancia 1e-4 pies). Así las uniones
   entre pedazos de una misma barra no se marcan.

## Plan paso a paso

1. **Contorno en `Logica/Tubos3D.cs`**: `Malla` suma su lista de líneas; `Malla.De` agrega las
   cuatro líneas por tramo y los anillos de las puntas sueltas; `Mostrar` calcula qué puntas son
   sueltas; `RenderScene` dibuja los bloques de líneas después de los tubos. — cómo se prueba:
   compila 2027 y 2026.
2. **Probar en Revit** (lista de abajo).

## Cómo probarlo

```
cd C:\Proyecto\addins-main
dotnet build addins/armado-columnas/practicacq.csproj -c Debug -p:RevitVersion=2027
Get-Process -Name Revit -ErrorAction SilentlyContinue   # tiene que estar cerrado
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027
```

1. «Ver en 3D»: cada barra con borde oscuro, también en «colores coherentes».
2. Acercar a un estribo y a una longitudinal con traslapo: sin anillos en las esquinas ni en el
   cambio de recta a traslapo; sí en las puntas de la barra.
3. Filtros («Solo ganchos», un diámetro): el borde acompaña a lo que se ve.

## Estado

- [x] Plan: Luciano reportó el problema y pidió arreglarlo antes de publicar 1.3.0 (23-oct).
- [x] Paso 1 implementado en `Logica/Tubos3D.cs`. Compila 2026 y 2027 (en 2027 solo falló la
      copia al `bin`, con Revit abierto). Sin cambios en vigas ni en `compartido`.
- [x] Desplegado (Revit cerrado, comprobado) y probado en Revit por Luciano: «ya todo bien» (23-oct).
- [ ] El modelo PROY_JULIO_BETO: volver a generar para que sus filtros tomen el borde oscuro y
      los colores nuevos (hallazgo 4).
