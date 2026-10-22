# Plan: la sección dibujada como queda en Revit (ganchos, grapas y barras en su lugar)

## Contexto

- Pedido de Luciano (21-10), después de probar el #133/#134
  (`2026-10-21-secciones-a-escala-133.md`): en la vista previa y en "Editar
  estribos menores"
  1. las longitudinales con su diámetro real, a escala;
  2. los ganchos del estribo a la vista;
  3. los estribos menores dibujados como van a quedar en Revit, no como un
     esquema.
- Relacionado: #135 (estribo exterior en la sección), #136 (diámetros reales).
- Al revisar el generador apareció un error: la cara exterior del estribo
  principal queda a recubrimiento + medio diámetro (4,48 cm con #3 y 4 cm de
  recubrimiento), no al recubrimiento. Ya estaba medido en
  `2026-10-21-diagnostico-grapas-130.md` ("Hallazgo aparte") pero sin arreglar.
- Rama: `columnas/grapas-abrazan-130` (worktree `C:\Proyecto\addins-130`), encima
  del #130 y del #133/#134 sin commitear.

## Criterios de aceptación

- [ ] En Revit, la cara exterior del estribo principal queda al recubrimiento
      elegido (4 cm), medido con el MCP en una columna regenerada.
- [ ] Las barras longitudinales se dibujan con su diámetro nominal, a escala,
      donde las pone Revit: las de las caras pegadas al estribo y las de las
      esquinas metidas en la curva del estribo.
- [ ] El estribo principal se dibuja con su grosor, sus esquinas curvas y sus
      dos ganchos de 135° en la esquina de abajo a la izquierda.
- [ ] Las grapas en C y en S se dibujan con sus ganchos de 180° abrazando la
      barra, abriendo hacia el lado que corresponde (C, S, volteada).
- [ ] Los estribos cerrados menores se dibujan abrazando las barras de su rango,
      con esquinas curvas y ganchos de 135°.
- [ ] Lo dibujado coincide con lo medido en Revit (tolerancia ~2 mm).

## Hallazgos del código

Rutas bajo `addins/armado-columnas/`.

- **Estribo principal** (`Logica/GeneradorArmado.cs`): el contorno se mete
  `recubrimiento + d/2` (línea ~319, pensado para el eje de la barra, que es lo
  que recibe `CreateFromCurves`). Pero el camino preferido es
  `CreateFromRebarShape` + `ScaleToBox` (`CrearDesdeForma`), y la forma propia
  "CQ Estribo" se acota **por fuera** del doblez: la caja pasada termina siendo
  la cara exterior → 4,75 mm de más con #3. Ganchos: 135°, los dos en la
  esquina de arranque (abajo a la izquierda), hacia adentro; cola recta = E del
  cuadro (`CuadroVarillas.GanchoEstribo135`), con E = 6d si la barra no está.
- **Barras**: las de las caras a `SeccionPlanta.Inset` (= recubrimiento +
  estribo + d/2, correcto una vez arreglado el estribo); las de las esquinas se
  corren adentro de la curva del estribo (`EnElDoblez`, GeneradorLongitudinal).
- **Grapas** (#130): el centro de cada gancho de 180° queda hacia adentro de la
  barra lo justo para que la barra toque la cara interior en la punta
  (`GanchoGrapa.CentroDelDoblez`); el tramo recto queda a un radio (al eje) del
  centro, del lado contrario al que abre. Radio interior = doblez de estribo
  de la barra / 2; cola = `Gancho180Cuadro.Extension`.
- **Cerrados menores**: `EstriboMenor.CajaQueAbraza` (medidas por fuera), misma
  forma y ganchos que el principal.
- Medido en Revit (diagnóstico del #130, columna 387983 25×30, #3): radio al eje
  del doblez 23,8 mm; colas de 135° a ~23,7 mm de cada lado de la diagonal.

## Decisiones

1. **Arreglar el generador** (21-10, Luciano): el estribo queda al
   recubrimiento elegido. El acero ya generado conserva el error hasta que se
   regenere.
2. **Diámetro nominal** (21-10, Luciano): el mismo que usa el generador para
   todas las cuentas.
3. **Una sola cuenta para el dibujo** (21-10): la geometría en planta (estribo,
   ganchos, grapas, cerrados, barras) sale de un archivo nuevo sin Revit
   (`Modelo/PlantaArmado.cs`), probado con los números medidos; las dos vistas
   lo dibujan igual. El corrimiento de las esquinas (`EnElDoblez`) pasa a
   `SeccionPlanta` y lo usan el generador y el dibujo.
4. **Traslapos**: el dibujo muestra la sección fuera del traslapo (una pieza por
   barra).

## Plan paso a paso

1. **Arreglo del estribo** — `Logica/GeneradorArmado.cs`, `CrearDesdeForma`:
   después de `ScaleToBox` medir el eje real (`GetCenterlineCurves` sin ganchos
   ni radios) y, si no coincide con el contorno pedido, volver a escalar con la
   caja corregida. Sirve para cualquier forma (acotada por fuera o por el eje).
2. **`EnElDoblez` sin Revit** — a `Modelo/SeccionPlanta.cs`; el generador lo
   llama con su alcance en pies. `SeccionPlanta.Calcular` recibe el doblez del
   estribo y corre las esquinas igual.
3. **`Modelo/PlantaArmado.cs`** — estribo cerrado con ganchos de 135° (para el
   principal y los cerrados menores), grapa con ganchos de 180°, como listas de
   puntos en metros. Pruebas en `tools/PruebasVistaPrevia`.
4. **Vista previa** (`Vistas/PanelVistaPrevia.xaml.cs`, `DibujarPlanta`) y
   **"Editar estribos menores"** (`Vistas/VentanaEstribosMenores.xaml.cs`):
   dibujar con `PlantaArmado`, cada trazo con el grosor de su barra.
5. **Build, pruebas, despliegue**, regenerar la columna 387983 y medir con el
   MCP de Revit.

## Cómo probarlo

```
dotnet run --project tools/PruebasVistaPrevia
dotnet run --project tools/PruebasTraslapos
dotnet build addins/armado-columnas/practicacq.csproj -c Debug -p:RevitVersion=2027
Get-Process -Name Revit -ErrorAction SilentlyContinue   # tiene que no devolver nada
powershell -ExecutionPolicy Bypass -File tools/desplegar-local.ps1 -Addin armado-columnas -Revit 2027
```

En Revit: regenerar la columna 387983 y medir con el MCP las cajas del estribo
principal y de las barras; comparar con lo que dice el dibujo.

## Estado

- [x] Decisiones con Luciano (21-10).
- [x] Pasos 1 a 4 implementados (21-10):
  - `Logica/GeneradorArmado.cs`: `EjeEnPlanta` + segundo `ScaleToBox` en
    `CrearDesdeForma` (tolerancia ~0,03 mm).
  - `Modelo/SeccionPlanta.cs`: `EnElDoblez` (movido del generador),
    `DoblecesEstribo`, y `Calcular(..., doblezEstriboMm)`.
  - `Modelo/PlantaArmado.cs` (nuevo): `EstriboCerrado`, `Grapa`, `Menor`,
    `CentroDelGancho`, `Cola135`, `Cola180`, `Doblez`.
  - `Vistas/DibujoArmado.cs` (nuevo): el trazo WPF con el grosor de la barra.
  - `PanelVistaPrevia.Mostrar` recibe el tipo de barra (para el doblado) en vez
    de solo su diámetro.
  - Las barras se dibujan a escala, con un mínimo de 1 a 1,5 px solo para que no
    desaparezcan; en "Editar estribos menores" se siguen eligiendo con un radio
    de captura de 12 px.
- [x] Pruebas sin Revit: `PruebasVistaPrevia` 0 fallas (4 nuevas, con los
  números medidos en el #130), `PruebasTraslapos` 0 fallas, build del add-in y
  de `PreviewUI` sin errores.
- [x] Revisado en imágenes sin Revit: `imagenes/2026-10-21-como-en-revit-*.png`.
  En la 25×30 las colas de 6,5 cm de la grapa en C casi se juntan (la sección
  es chica); en la S el tramo recto queda en diagonal, igual que lo arma el
  generador (`GanchoGrapa.Linea`).
- [x] Selección en "Editar estribos menores" (21-10, reportado por Luciano:
  había que hacer clic donde estaba la línea esquemática vieja, y a veces no
  andaba). `PiezaEn` ahora busca sobre el mismo trazo que se dibuja
  (`TrazoDe`): la pieza cuya barra está a menos de 5 px del puntero (donde se
  cruzan, gana la grapa, que se dibuja encima), y si no, el cerrado más chico
  que encierra el punto. Prueba con clics simulados en 6 puntos de cada pieza
  (punta, ganchos, tramo recto): todos eligen la pieza correcta salvo donde la
  punta de una grapa pisa un cerrado, que elige la grapa.
- [x] Primera medición en Revit (21-10, columna 387983 regenerada con
  recubrimiento 2,5 cm; el recubrimiento de la columna en Revit también es
  25 mm). `ComandoDiagnosticoRebar` sobre el estribo 394405 y la grapa 394621.
  Columna de X 3,245 a 3,495 m, Y −0,150 a 0,150:
  - Eje del estribo: X 3,277 a 3,463, Y −0,118 a 0,118 → cara exterior a
    27,2 mm en los cuatro lados (pedido: 25). El error grande quedó arreglado
    (antes sobraban 4,75 mm), pero el primer arreglo quedó corto 2,2 mm por
    lado.
  - **Causa:** sin ganchos ni radios, Revit alarga ~7 mm los dos tramos que
    llegan a la esquina de los ganchos (el de abajo arranca en X 3,270 y no en
    3,277; el izquierdo termina en Y −0,125). `EjeEnPlanta` tomaba la caja que
    encierra las curvas, que incluía esa punta, y la corrección salió corta.
  - **Arreglo:** `LadosDelEje` lee la X de los lados verticales y la Y de los
    horizontales (no cambian con esa punta) y se corrige lado por lado, hasta
    3 veces o hasta que quede a menos de ~0,03 mm.
  - Barras #6 de las caras: eje a 46,4 mm (= 27,2 + 9,5 + 9,55: pegadas al
    estribo); con el estribo en su lugar deberían quedar a 44,05.
  - Radio al eje de las curvas del estribo: 2,38 cm; colas de 135°: 5,70 cm.
    Coincide con el dibujo.
  - La grapa 394621 (a 1,97 m, en un traslapo) salió con la forma "CQ Grapa"
    de estilo estándar: doblez de 57 mm (radio al eje 3,33 cm) y colas de
    6,5 cm. Es el respaldo del generador cuando en el traslapo las dos piezas
    no entran en el doblez de 38 mm (`Grande()`). El dibujo muestra la sección
    fuera de los traslapos, con el doblez de estribo.
- [x] Segunda medición (21-10, después de desplegar `LadosDelEje`; columna
  387983 regenerada con recubrimiento 2,5 cm; estribo 394048): eje en X
  3,275 / 3,465 e Y −0,120 / 0,120 (esperado 3,2748 / 3,4652 y ±0,1202; el
  diagnóstico muestra 1 mm), centros de las curvas en (3,299; −0,097) (esperado
  3,2985; −0,0965), tramos rectos de 14,30 y 19,30 cm, radio al eje 2,38 cm,
  colas de 5,70 cm. **La cara exterior queda a 25 mm en los cuatro lados.**

Criterios de aceptación:
- ✅ Estribo al recubrimiento elegido (medido arriba).
- ✅ Estribo dibujado con su grosor, esquinas curvas y ganchos de 135° (mismos
  radios y colas que en Revit).
- ✅ Barras con su diámetro nominal a escala, las de las caras pegadas al
  estribo y las de las esquinas en la curva (misma cuenta que el generador;
  en Revit se vieron pegadas al estribo en la primera medición).
- ✅ Grapas C/S con ganchos de 180° abrazando la barra y cerrados abrazando su
  rango (dibujados con la misma cuenta que el generador del #130).
- ✅ Barras medidas con el MCP (cajas, mm desde el borde al eje): #6 cara Y
  44,05 en los dos lados; #7 cara X 45,6 del borde y 99,07 a lo largo; #8
  esquinas 49,05 / 49,05 (en la curva del estribo); filas de la cara Y en
  115,7 y 184,3. Todo igual a la cuenta del dibujo.

Hallazgos de la medición (21-10), para después:

- **Traslapos:** la otra pieza de cada traslapo queda corrida un diámetro
  hacia el costado, a lo largo de su cara (#7 C2: 150,9 → 173,1; #6 fila 1:
  115,7 → 134,6 a la izquierda y → 96,6 a la derecha; #8 abajo-derecha,
  arriba-izquierda y arriba-derecha: 25,4 al costado). La esquina de abajo a la
  izquierda, la de los ganchos del estribo, se corre en diagonal (49 → 67 en X
  y en Y). En algunos lugares la pieza corrida es la de abajo, la única que hay
  en la base: ahí la barra no está donde la muestra el dibujo, que muestra la
  sección sin traslapos. **Pendiente (si se pide):** dibujar las dos piezas a la
  altura de un traslapo.
- **Grapa 394279** ("CQ Grapa C", z = 0,10 m, entre las #7 de la columna C2):
  radio al eje 2,38 cm, colas de 6,50 cm y tramo recto a un radio del centro,
  igual que el dibujo. Su tramo recto queda en diagonal porque abajo engancha la
  pieza corrida (x 173) y arriba la teórica (x 151); el generador lo hace bien.
  Pero la grapa entera quedó corrida ~2,5 mm hacia −Y a lo largo de su eje:
  centros de los ganchos en y 51 y 244 mm, con las barras en 45,6 y 254,4 (lo
  justo sería 53,5 y 246,5). La de abajo queda adentro con 2,5 mm de juego; la
  de arriba se mete ~2,5 mm en la cara interior del gancho. El diagnóstico
  muestra 1 mm. **Para revisar en el #130** (`GanchoGrapa.Linea` /
  `MedirGanchoDeGrapa`).
