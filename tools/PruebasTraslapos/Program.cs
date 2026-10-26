using System.Diagnostics;
using practicacq.Modelo;
using static practicacq.Modelo.PlanTraslapos;

const double N4 = 12.7, N6 = 19.1;
int fallas = 0;

void Probar(string nombre, Action prueba)
{
    try { prueba(); Console.WriteLine($"OK     {nombre}"); }
    catch (Exception ex) { fallas++; Console.WriteLine($"FALLA  {nombre}: {ex.Message}"); }
}

void Igual(double esperado, double real, string que)
{
    if (Math.Abs(esperado - real) > 1e-6) throw new Exception($"{que}: esperado {esperado}, real {real}");
}

void Cierto(bool condicion, string que)
{
    if (!condicion) throw new Exception(que);
}

static Tramo T(double inf, double sup, TipoTramo tipo = TipoTramo.Libre) =>
    new() { ZInferior = inf, ZSuperior = sup, Tipo = tipo };

void Piezas(PlanTipo plan, params double[] largos)
{
    Cierto(plan.TieneSolucion, $"tipo {plan.Nombre} sin solución: {plan.Motivo}");
    Cierto(plan.Piezas.Count == largos.Length, $"tipo {plan.Nombre}: {plan.Piezas.Count} piezas, se esperaban {largos.Length}");
    for (int i = 0; i < largos.Length; i++) Igual(largos[i], plan.Piezas[i].LargoCorte, $"tipo {plan.Nombre}, pieza {i + 1}");
}

void UnTraslapo(PlanTipo plan, double inf, double sup)
{
    Cierto(plan.Traslapos.Count == 1, $"tipo {plan.Nombre}: {plan.Traslapos.Count} traslapos, se esperaba 1");
    Igual(inf, plan.Traslapos[0].ZInferior, $"tipo {plan.Nombre}, traslapo desde");
    Igual(sup, plan.Traslapos[0].ZSuperior, $"tipo {plan.Nombre}, traslapo hasta");
}

// Las reglas obligatorias del spec, sobre cualquier plan con solución.
void CumpleReglas(Columna col, Opciones op, Resultado r)
{
    List<Tramo> libres = col.Tramos.Where(t => t.Tipo != TipoTramo.DentroDeViga).OrderBy(t => t.ZInferior).ToList();
    foreach (PlanTipo p in r.Todos)
    {
        if (!p.TieneSolucion) continue;
        Cierto(p.Traslapos.Count == p.Piezas.Count - 1, $"{p.Nombre}: traslapos = piezas - 1");
        Igual(col.ZInicio, p.Piezas[0].ZDesde, $"{p.Nombre}: arranque");
        Igual(col.ZFin, p.Piezas[^1].ZHasta, $"{p.Nombre}: remate");
        foreach (Pieza pz in p.Piezas)
        {
            double pasos = pz.LargoCorte / op.Redondeo;
            Cierto(Math.Abs(pasos - Math.Round(pasos)) < 1e-6, $"{p.Nombre}: {pz.LargoCorte} no es múltiplo de {op.Redondeo}");
            Cierto(pz.LargoCorte <= op.LargoComercial + 1e-9, $"{p.Nombre}: {pz.LargoCorte} supera {op.LargoComercial}");
        }

        for (int i = 0; i < p.Traslapos.Count; i++)
        {
            Traslapo t = p.Traslapos[i];
            Cierto(t.Largo >= p.SolapeMinimo!.Value - 1e-9, $"{p.Nombre}: traslapo {t.Largo} menor que {p.SolapeMinimo}");
            Igual(p.Piezas[i].ZHasta, t.ZSuperior, $"{p.Nombre}: pieza {i + 1} termina donde termina su traslapo");
            Igual(p.Piezas[i + 1].ZDesde, t.ZInferior, $"{p.Nombre}: pieza {i + 2} arranca donde arranca su traslapo");
            Cierto(col.Tramos.Where(n => n.Tipo == TipoTramo.DentroDeViga)
                       .All(n => t.ZSuperior <= n.ZInferior + 1e-9 || t.ZInferior >= n.ZSuperior - 1e-9),
                   $"{p.Nombre}: traslapo {t.ZInferior}-{t.ZSuperior} toca un nudo");
            int ordinal = libres.FindIndex(l => t.ZInferior >= l.ZInferior - 1e-9 && t.ZSuperior <= l.ZSuperior + 1e-9) + 1;
            Cierto(ordinal > 0, $"{p.Nombre}: traslapo {t.ZInferior}-{t.ZSuperior} fuera de todo tramo libre");
            if (!p.EnTramosDelOtro)
                Cierto((ordinal % 2 == 0) == (p.Grupo == "A"), $"{p.Nombre}: traslapo en un tramo del otro tipo");
        }
    }

    // Cualquier barra A puede ser vecina de cualquier barra B, sea del diámetro que sea.
    if (r.TieneSolucion)
        foreach (Traslapo a in r.A.SelectMany<PlanTipo, Traslapo>(x => x.Traslapos))
            Cierto(r.B.SelectMany<PlanTipo, Traslapo>(x => x.Traslapos).All(b => a.ZSuperior <= b.ZInferior + 1e-9 || a.ZInferior >= b.ZSuperior - 1e-9),
                   $"traslapos de A y B se pisan en {a.ZInferior}-{a.ZSuperior}");
}

// C-12 del proveedor falso: zapata, 4 vigas de 0,45 m, 10 tramos.
var c12 = new Columna(new[]
{
    T(0.00, 0.20, TipoTramo.ContraZapata), T(0.20, 2.75), T(2.75, 3.20, TipoTramo.DentroDeViga),
    T(3.20, 5.95), T(5.95, 6.40, TipoTramo.DentroDeViga), T(6.40, 9.15), T(9.15, 9.60, TipoTramo.DentroDeViga),
    T(9.60, 12.35), T(12.35, 12.80, TipoTramo.DentroDeViga), T(12.80, 15.55),
}, -0.30, 15.55, ConGanchos: true);

Probar("una_varilla_exacta", () =>
{
    // 6,00 m justos, múltiplo de 0,50: una varilla, sin traslapos.
    var col = new Columna(new[] { T(0, 6.00) }, 0, 6.00, false);
    Resultado r = Calcular(col, N4, N4, new Opciones(12, 0.50, false));
    Piezas(r.A[0], 6.00);
    Piezas(r.B[0], 6.00);
    Cierto(r.A[0].EnTramosDelOtro, "A no tiene tramos propios en una columna de un tramo");
});

Probar("traslapo_forzado", () =>
{
    // 5,83 m entra en una barra de 12 pero no es múltiplo de 0,50: traslapo forzado.
    // B (tramo 1): el traslapo mínimo posible es 1,17 m (0,75 + lo que obliga el
    // redondeo); entre esos, el centrado es 2,33-3,50 con piezas de 3,50 y 3,50.
    // A (sin tramos propios) va en lo que dejó B: 0,83-2,00 empata en centrado
    // con 3,83-5,00 y gana el de más abajo.
    var col = new Columna(new[] { T(0, 5.83) }, 0, 5.83, false);
    Resultado r = Calcular(col, N4, N4, new Opciones(12, 0.50, false));
    Piezas(r.B[0], 3.50, 3.50);
    UnTraslapo(r.B[0], 2.33, 3.50);
    Piezas(r.A[0], 2.00, 5.00);
    UnTraslapo(r.A[0], 0.83, 2.00);
    CumpleReglas(col, new Opciones(12, 0.50, false), r);
});

Probar("ejemplo_de_luciano_esquiva_el_nudo", () =>
{
    // El ejemplo con el que se eligió el orden de preferencias (17-oct):
    // tramo 0-2,60, nudo 2,60-3,00, tramo 3,00-5,60; #4, redondeo 0,50, barra de 6.
    var col = new Columna(new[] { T(0, 2.60), T(2.60, 3.00, TipoTramo.DentroDeViga), T(3.00, 5.60) }, 0, 5.60, false);
    var op = new Opciones(6, 0.50, false);
    Resultado r = Calcular(col, N4, N4, op);
    Piezas(r.B[0], 1.50, 5.00);
    UnTraslapo(r.B[0], 0.60, 1.50);
    Piezas(r.A[0], 4.50, 2.00);
    UnTraslapo(r.A[0], 3.60, 4.50);
    CumpleReglas(col, op, r);
});

Probar("forzado_un_solo_tramo", () =>
{
    // Review Focus 1: sin zapata, el tramo arranca en la misma cota que la barra.
    // B: 1,10-2,00. A, en lo que queda: 0,10-1,00 (empata con 2,10-3,00, gana el de abajo).
    var col = new Columna(new[] { T(0, 3.10) }, 0, 3.10, false);
    var op = new Opciones(12, 0.50, false);
    Resultado r = Calcular(col, N4, N4, op);
    UnTraslapo(r.B[0], 1.10, 2.00);
    UnTraslapo(r.A[0], 0.10, 1.00);
    Cierto(r.A[0].EnTramosDelOtro && !r.B[0].EnTramosDelOtro, "solo A va en los tramos del otro");
    CumpleReglas(col, op, r);
});

Probar("sin_solucion_con_6_sugiere_12", () =>
{
    // B traslapa en 0-2,60 y 9,00-11,60: entre los dos hay un tramo de A de
    // 5,60 m, y ninguna varilla de 6 m lo salta con traslapo en las dos puntas.
    // (A sí tiene solución: dos traslapos en su tramo largo.)
    var col = new Columna(new[]
    {
        T(0, 2.60), T(2.60, 3.00, TipoTramo.DentroDeViga), T(3.00, 8.60),
        T(8.60, 9.00, TipoTramo.DentroDeViga), T(9.00, 11.60),
    }, 0, 11.60, false);
    Resultado con6 = Calcular(col, N4, N4, new Opciones(6, 0.50, false));
    Cierto(!con6.TieneSolucion && !con6.B[0].TieneSolucion, "con 6 m, B no debería tener solución");
    Cierto(con6.B[0].Motivo!.Contains("12 m"), $"B: el motivo no sugiere 12 m: {con6.B[0].Motivo}");
    Resultado con12 = Calcular(col, N4, N4, new Opciones(12, 0.50, false));
    Cierto(con12.TieneSolucion, $"con 12 m debería haber solución: {con12.A[0].Motivo} {con12.B[0].Motivo}");
    CumpleReglas(col, new Opciones(12, 0.50, false), con12);
});

Probar("barra_fuera_del_cuadro", () =>
{
    // Review Focus 3.
    Resultado r = Calcular(c12, 30.0, N4, new Opciones(12, 0.50, false));
    Cierto(!r.A[0].TieneSolucion && !string.IsNullOrWhiteSpace(r.A[0].Motivo), "A debería fallar con motivo");
    Cierto(r.B[0].TieneSolucion, $"B no depende de A: {r.B[0].Motivo}");
});

Probar("solape_mayor_de_los_dos", () =>
{
    Resultado cadaUno = Calcular(c12, N4, N6, new Opciones(12, 0.50, false));
    Igual(0.75, cadaUno.A[0].SolapeMinimo!.Value, "A con su propio solape");
    Igual(1.10, cadaUno.B[0].SolapeMinimo!.Value, "B con su propio solape");
    Resultado mayor = Calcular(c12, N4, N6, new Opciones(12, 0.50, true));
    Igual(1.10, mayor.A[0].SolapeMinimo!.Value, "A con el mayor");
    Igual(1.10, mayor.B[0].SolapeMinimo!.Value, "B con el mayor");
});

Probar("reglas_en_c12_con_todos_los_redondeos", () =>
{
    foreach (double redondeo in Redondeos)
    foreach (double comercial in new[] { 6.0, 12.0 })
    {
        var op = new Opciones(comercial, redondeo, false);
        CumpleReglas(c12, op, Calcular(c12, N4, N4, op));
    }

    Cierto(Calcular(c12, N4, N4, new Opciones(12, 0.50, false)).TieneSolucion, "C-12 con 12 m y 0,50 debería tener solución");
});

Probar("rendimiento_c12", () =>
{
    // Review Focus 4: la ventana recalcula en cada cambio de combo.
    var reloj = Stopwatch.StartNew();
    Calcular(c12, N4, N4, new Opciones(12, 0.10, false));
    reloj.Stop();
    Cierto(reloj.ElapsedMilliseconds < 1000, $"tardó {reloj.ElapsedMilliseconds} ms");
});

Probar("varios_traslapos_en_un_mismo_tramo", () =>
{
    // Revisión final, hallazgo 1: el spec no limita a un traslapo por tramo.
    // Un tramo libre de 14 m con barras de 6 m necesita 2 traslapos en él.
    var col14 = new Columna(new[] { T(0, 14.00) }, 0, 14.00, false);
    var op6 = new Opciones(6, 0.50, false);
    Resultado r14 = Calcular(col14, N4, N4, op6);
    Cierto(r14.TieneSolucion, $"14 m con barra de 6 debería tener solución: {r14.A[0].Motivo} {r14.B[0].Motivo}");
    Cierto(r14.B[0].Traslapos.Count == 2, $"B: {r14.B[0].Traslapos.Count} traslapos, se esperaban 2");
    CumpleReglas(col14, op6, r14);

    var col26 = new Columna(new[] { T(0, 26.00) }, 0, 26.00, false);
    var op12 = new Opciones(12, 0.50, false);
    Resultado r26 = Calcular(col26, N4, N4, op12);
    Cierto(r26.TieneSolucion, $"26 m con barra de 12 debería tener solución: {r26.A[0].Motivo} {r26.B[0].Motivo}");
    CumpleReglas(col26, op12, r26);
});

Probar("cotas_invertidas_no_revientan", () =>
{
    // Revisión final: un dato de columna roto (remate por debajo del arranque)
    // no puede tirar una excepción dentro de Revit: sale sin solución, con motivo.
    Resultado r = Calcular(new Columna(new[] { T(0, 3.00) }, 3.00, 0, false), N4, N4, new Opciones(12, 0.50, false));
    Cierto(!r.TieneSolucion && r.A[0].Motivo is not null && r.B[0].Motivo is not null, "debería salir sin solución, con motivo");
});

Probar("nombre_de_tipo_de_empalme_por_milimetro", () =>
{
    // Revisión final, hallazgo 2: dos largos que difieren en milímetros no
    // pueden compartir el tipo de empalme (uno le cambiaría el largo al otro).
    string redondo = NombreEmpalme.Propio(4, 0.96);
    string conMilimetros = NombreEmpalme.Propio(4, 0.956);
    Cierto(redondo != conMilimetros, $"«{redondo}» y «{conMilimetros}» no pueden ser iguales");
    Cierto(redondo == "CQ Traslapo #4 · 0,96 m", $"redondo: «{redondo}»");
    Cierto(conMilimetros == "CQ Traslapo #4 · 0,956 m", $"con milímetros: «{conMilimetros}»");
});

Probar("migracion_config_a_b", () =>
{
    // 19-oct: una configuración guardada con barras por tipo A/B vuelve a
    // barras por posición: esquinas = A, caras = B.
    var ab = new ConfigLongitudinal { TipoBarraANombre = "#5", TipoBarraBNombre = "#4" };
    Cierto(ab.MigrarBarrasPorCara(), "debería migrar");
    Cierto(ab.TipoBarraEsquinasNombre == "#5" && ab.TipoBarraCaraXNombre == "#4" && ab.TipoBarraCaraYNombre == "#4",
           $"esquinas={ab.TipoBarraEsquinasNombre} X={ab.TipoBarraCaraXNombre} Y={ab.TipoBarraCaraYNombre}");
    Cierto(!ab.MigrarBarrasPorCara(), "una segunda vez no hace nada");

    // Las guardadas antes del 17-oct ya tienen las de cara: no se tocan.
    var porCara = new ConfigLongitudinal { TipoBarraEsquinasNombre = "#5", TipoBarraCaraXNombre = "#4", TipoBarraCaraYNombre = "#3" };
    Cierto(!porCara.MigrarBarrasPorCara(), "una por cara no se migra");
    Igual(0.50, porCara.RedondeoVarillas, "redondeo por defecto");
    Cierto(!porCara.SolapeMayorDeLosDos, "solape de cada diámetro por defecto");

    porCara.RedondeoVarillas = 0.20;
    porCara.SolapeMayorDeLosDos = true;
    ConfigLongitudinal copia = porCara.Copiar();
    Cierto(copia.TipoBarraEsquinasNombre == "#5" && copia.TipoBarraCaraXNombre == "#4" && copia.TipoBarraCaraYNombre == "#3",
           "Copiar lleva las tres barras");
    Cierto(copia.SolapeMayorDeLosDos, "Copiar lleva el solape");
    Igual(0.20, copia.RedondeoVarillas, "Copiar lleva el redondeo");
});

Probar("anillo_por_posicion", () =>
{
    // 2 barras por cara X (1 intermedia por cara) y 4 por cara Y (2 por cara):
    // E X E Y Y E X E Y Y -- 10 posiciones; A = pares, B = impares.
    var cfg = new ConfigLongitudinal { BarrasPorCaraX = 2, BarrasPorCaraY = 4 };
    string anillo = string.Concat(cfg.Anillo().Select(p => p switch
    {
        PosicionBarra.Esquina => "E", PosicionBarra.CaraX => "X", _ => "Y",
    }));
    Cierto(anillo == "EXEYYEXEYY", $"anillo: {anillo}");
});

Probar("varios_diametros_por_grupo", () =>
{
    // Esquinas #6 y caras #4: A y B tienen barras de los dos números, cada
    // una con su solape; todas cumplen las reglas y A nunca pisa a B.
    var op = new Opciones(12, 0.50, false);
    Resultado r = Calcular(c12, new[] { N6, N4, N6 }, new[] { N4, N6 }, op);
    Cierto(r.A.Count == 2 && r.B.Count == 2, $"planes: A {r.A.Count}, B {r.B.Count}");
    Cierto(r.A[0].Nombre == "A · #6" && r.A[1].Nombre == "A · #4", $"nombres: {r.A[0].Nombre}, {r.A[1].Nombre}");
    Igual(1.10, r.De(true, N6).SolapeMinimo!.Value, "A #6 con su solape");
    Igual(0.75, r.De(false, N4).SolapeMinimo!.Value, "B #4 con su solape");
    CumpleReglas(c12, op, r);

    Resultado mayor = Calcular(c12, new[] { N6, N4 }, new[] { N4 }, new Opciones(12, 0.50, true));
    Cierto(mayor.Todos.All(p => Math.Abs(p.SolapeMinimo!.Value - 1.10) < 1e-9), "el mayor de la columna para todos");
});

Probar("varios_diametros_un_solo_tramo", () =>
{
    // A sin tramos propios: con dos diámetros en B, A esquiva los traslapos de los dos.
    var col = new Columna(new[] { T(0, 11.00) }, 0, 11.00, false);
    var op = new Opciones(6, 0.50, false);
    Resultado r = Calcular(col, new[] { N4 }, new[] { N4, N6 }, op);
    Cierto(r.TieneSolucion, $"debería tener solución: {string.Join(" ", r.Todos.Select(p => p.Motivo))}");
    Cierto(r.A[0].EnTramosDelOtro, "A va en los tramos del otro");
    CumpleReglas(col, op, r);
});

// ------------------------------------------------ estribos menores (20-oct)

static EstriboMenor Pieza(FormaMenor forma, EjeCaja eje, int desde, int hasta, string? barra = null) =>
    new() { Forma = forma, Eje = eje, Desde = desde, Hasta = hasta, TipoBarraNombre = barra };

Probar("migracion_menores", () =>
{
    var cfg = new ConfigLongitudinal
    {
        GrapasFila = new() { 0 },
        GrapasColumna = new() { 1 },
        Cajas = new() { new CajaMenor { Eje = EjeCaja.Fila, Desde = 0, Hasta = 1 } },
    };
    Cierto(cfg.MigrarEstribosMenores(), "debería migrar");
    string piezas = string.Join(" ", cfg.EstribosMenores.Select(p => $"{p.Forma}/{p.Eje}/{p.Desde}-{p.Hasta}"));
    Cierto(piezas == "GrapaC/Fila/0-0 GrapaC/Columna/1-1 Cerrado/Fila/0-1", $"piezas: {piezas}");
    Cierto(cfg.EstribosMenores.All(p => !p.Volteado && p.TipoBarraNombre is null), "sin voltear e igual al estribo");
    Cierto(cfg.GrapasFila.Count == 0 && cfg.GrapasColumna.Count == 0 && cfg.Cajas.Count == 0, "las listas viejas quedan vacías");
    Cierto(!cfg.MigrarEstribosMenores(), "una segunda vez no hace nada");
});

Probar("menores_fuera_de_rango", () =>
{
    // 4 barras por cara X = 2 columnas; 2 por cara Y = 1 fila.
    var cfg = new ConfigLongitudinal
    {
        BarrasPorCaraX = 4,
        BarrasPorCaraY = 2,
        EstribosMenores = new() { Pieza(FormaMenor.GrapaC, EjeCaja.Fila, 1, 1), Pieza(FormaMenor.Cerrado, EjeCaja.Columna, 0, 1) },
    };
    Cierto(cfg.DescartarMenoresFueraDeRango() == 1, "debería descartar una");
    Cierto(cfg.EstribosMenores.Count == 1 && cfg.EstribosMenores[0].Forma == FormaMenor.Cerrado, "queda solo el cerrado");
});

Probar("copiar_menores", () =>
{
    var cfg = new ConfigLongitudinal { EstribosMenores = new() { Pieza(FormaMenor.GrapaS, EjeCaja.Fila, 0, 0, "#3") } };
    ConfigLongitudinal copia = cfg.Copiar();
    copia.EstribosMenores[0].Volteado = true;
    copia.EstribosMenores[0].TipoBarraNombre = "#4";
    Cierto(!cfg.EstribosMenores[0].Volteado && cfg.EstribosMenores[0].TipoBarraNombre == "#3", "la copia no toca el original");
});

// Paquete: principal #3 (9,5 mm) salvo que se diga otra cosa; todo en metros.
const double N3 = 9.5;
double Diam(EstriboMenor p) => p.TipoBarraNombre == "#4" ? N4 : N3;

Probar("paquete_vacio", () =>
{
    PaqueteEstribos.Paquete p = PaqueteEstribos.Calcular(Array.Empty<EstriboMenor>(), N3, Diam);
    Igual(0, p.DesplazamientoPrincipal, "principal");
    Igual(0.00475, p.Debajo, "debajo");
    Igual(0.00475, p.Encima, "encima");
});

Probar("paquete_una_grapa", () =>
{
    PaqueteEstribos.Paquete p = PaqueteEstribos.Calcular(new[] { Pieza(FormaMenor.GrapaC, EjeCaja.Fila, 0, 0) }, N3, Diam);
    // Cada pieza ocupa dos pisos (#130): el principal de −14,25 a +4,75 mm,
    // la grapa justo debajo, de −33,25 a −14,25, con su eje a −19.
    Igual(0.038, Math.Round(p.Alto, 6), "alto");
    Igual(0.01425, Math.Round(p.DesplazamientoPrincipal, 6), "principal");
    Igual(-0.00475, Math.Round(p.Piezas.Single().Desplazamiento, 6), "grapa");
    Igual(-0.019, Math.Round(p.Piezas.Single().Desplazamiento - p.DesplazamientoPrincipal, 6), "grapa respecto del principal");
});

Probar("paquete_grapas_paralelas_comparten", () =>
{
    PaqueteEstribos.Paquete p = PaqueteEstribos.Calcular(
        new[] { Pieza(FormaMenor.GrapaC, EjeCaja.Fila, 0, 0), Pieza(FormaMenor.GrapaS, EjeCaja.Fila, 1, 1) }, N3, Diam);
    Cierto(p.Piezas.Select(n => n.Nivel).Distinct().Count() == 1, "un solo nivel");
    Igual(0.038, Math.Round(p.Alto, 6), "alto");
    Igual(0.01425, Math.Round(p.DesplazamientoPrincipal, 6), "principal");
});

Probar("paquete_cruzadas", () =>
{
    EstriboMenor fila = Pieza(FormaMenor.GrapaC, EjeCaja.Fila, 0, 0), columna = Pieza(FormaMenor.GrapaC, EjeCaja.Columna, 0, 0);
    PaqueteEstribos.Paquete p = PaqueteEstribos.Calcular(new[] { columna, fila }, N3, Diam);
    // Fila debajo con eje a −19 mm del principal; columna encima, con su piso
    // (eje − 1,5 d) en el techo del principal (+4,75): eje a +19.
    Igual(-0.01425, Math.Round(p.Piezas.Single(n => n.Pieza == fila).Desplazamiento, 6), "la de fila, debajo");
    Igual(0.02375, Math.Round(p.Piezas.Single(n => n.Pieza == columna).Desplazamiento, 6), "la de columna, encima");
    Igual(0.057, Math.Round(p.Alto, 6), "alto");
    Igual(0.00475, Math.Round(p.DesplazamientoPrincipal, 6), "principal");
});

Probar("paquete_caja_mas_cerca", () =>
{
    EstriboMenor caja = Pieza(FormaMenor.Cerrado, EjeCaja.Columna, 0, 1), grapa = Pieza(FormaMenor.GrapaC, EjeCaja.Fila, 0, 0);
    PaqueteEstribos.Paquete p = PaqueteEstribos.Calcular(new[] { grapa, caja }, N3, Diam);
    Cierto(p.Piezas.Single(n => n.Pieza == caja).Nivel == 1, "la caja en el nivel 1");
    Cierto(p.Piezas.Single(n => n.Pieza == grapa).Nivel == 2, "la grapa en el nivel 2");
    Igual(-0.019, Math.Round(p.Piezas.Single(n => n.Pieza == caja).Desplazamiento - p.DesplazamientoPrincipal, 6), "la caja, debajo");
});

Probar("paquete_tapada", () =>
{
    EstriboMenor caja = Pieza(FormaMenor.Cerrado, EjeCaja.Columna, 0, 1), grapa = Pieza(FormaMenor.GrapaC, EjeCaja.Columna, 0, 0);
    Cierto(PaqueteEstribos.EstaTapada(grapa, new[] { caja, grapa }), "la grapa queda tapada");
    PaqueteEstribos.Paquete p = PaqueteEstribos.Calcular(new[] { caja, grapa }, N3, Diam);
    Cierto(p.Piezas.Count == 1 && p.Piezas[0].Pieza == caja, "solo la caja");
});

Probar("paquete_diametros_mixtos", () =>
{
    PaqueteEstribos.Paquete p = PaqueteEstribos.Calcular(new[] { Pieza(FormaMenor.GrapaC, EjeCaja.Fila, 0, 0, "#4") }, N3, Diam);
    // #4 debajo del #3: eje a 14,25 + 6,35 = 20,6 mm; ocupa hasta −20,6 − 19,05.
    Igual(0.0206, Math.Round(p.DesplazamientoPrincipal - p.Piezas.Single().Desplazamiento, 6), "distancia entre ejes");
    Igual(0.0444, Math.Round(p.Alto, 6), "alto");
});

Probar("paquete_cajas_que_comparten_barra", () =>
{
    PaqueteEstribos.Paquete p = PaqueteEstribos.Calcular(
        new[] { Pieza(FormaMenor.Cerrado, EjeCaja.Columna, 0, 1), Pieza(FormaMenor.Cerrado, EjeCaja.Columna, 1, 2) }, N3, Diam);
    Cierto(p.Piezas.Select(n => n.Nivel).Distinct().Count() == 2, "dos niveles");
});

Probar("menores_para_generar", () =>
{
    // Copia migrada y sin piezas fuera de rango, sin tocar la configuración.
    var cfg = new ConfigLongitudinal
    {
        BarrasPorCaraX = 2,
        BarrasPorCaraY = 2,
        GrapasFila = new() { 0, 3 },
        EstribosMenores = new() { Pieza(FormaMenor.Cerrado, EjeCaja.Columna, 0, 0) },
    };
    List<EstriboMenor> piezas = cfg.MenoresParaGenerar();
    Cierto(piezas.Count == 2, $"piezas: {piezas.Count}");
    Cierto(cfg.GrapasFila.Count == 2 && cfg.EstribosMenores.Count == 1, "la configuración no cambia");
});

Probar("ancho_minimo_cerrado", () =>
{
    // Un lazo cerrado necesita, entre ejes, el diámetro de doblado más el de la
    // barra: con una #3 CQEING (doblado 4 d = 38 mm), 47,5 mm. La caja de 3,8 cm
    // que Revit rechazó el 20-oct queda por debajo.
    var n3 = new TipoBarra("#3 CQEING", 9.5) { DobladoEstriboMm = 38, DobladoGanchoMm = 57 };
    Igual(47.5, EstriboMenor.AnchoMinimoCerradoMm(n3), "#3");
    Cierto(EstriboMenor.AnchoMinimoCerradoMm(n3) > 38, "la caja de 3,8 cm no entra");
});

Probar("cerrado_entre_filas_vecinas_se_puede_doblar", () =>
{
    // #132 (25×30, reunión del 21-oct): un cerrado #3 entre dos filas vecinas
    // a 42 mm entre centros de las #6 salía marcado como imposible (42 < 47,5).
    // Pero el cerrado va por fuera de las barras: su lado corto, entre los ejes
    // de su barra, mide 42 + 19,05 + 9,5 + lo que se abre en las esquinas.
    var piezas = new[]
    {
        new GanchoGrapa.Pieza(60, 100, 9.525, 0), new GanchoGrapa.Pieza(190, 100, 9.525, 0),
        new GanchoGrapa.Pieza(60, 142, 9.525, 0), new GanchoGrapa.Pieza(190, 142, 9.525, 0),
    };
    (double x1, double x2, double y1, double y2) = EstriboMenor.CajaQueAbraza(piezas, 9.5, radioDoblezInterior: 19);
    double ancho = Math.Min(x2 - x1, y2 - y1) - 9.5;
    double abre = (19 - 9.525) * (1 - 1 / Math.Sqrt(2));
    Igual(Math.Round(42 + 19.05 + 9.5 + 2 * abre, 6), Math.Round(ancho, 6), "lado corto entre ejes del cerrado");
    var n3 = new TipoBarra("#3 CQEING", 9.5) { DobladoEstriboMm = 38, DobladoGanchoMm = 57 };
    Cierto(ancho >= EstriboMenor.AnchoMinimoCerradoMm(n3), $"entra: {ancho:0.0} mm");

    // De una sola fila (las dos barras de la misma fila) sí es imposible.
    (x1, x2, y1, y2) = EstriboMenor.CajaQueAbraza(piezas.Take(2), 9.5, radioDoblezInterior: 19);
    Cierto(Math.Min(x2 - x1, y2 - y1) - 9.5 < EstriboMenor.AnchoMinimoCerradoMm(n3), "una sola fila no entra");
});

Probar("cerrado_abraza_las_barras", () =>
{
    // Columna 387983 (21-oct): cerrado #3 de las filas 0 a 2 de la cara Y, con
    // las piezas REALES de las #6 a esa altura -- la fila 0 con las dos piezas
    // del traslapo (92,3 y 73,2), la 2 con una (167,6). Cada lado toca por fuera
    // la pieza más afuera: 9,55 de radio + 9,5 del cerrado.
    var piezas = new[]
    {
        new GanchoGrapa.Pieza(63.8, 92.3, 9.55, 0), new GanchoGrapa.Pieza(63.8, 73.2, 9.55, 0),
        new GanchoGrapa.Pieza(186.2, 92.3, 9.55, 0), new GanchoGrapa.Pieza(186.2, 167.6, 9.55, 0),
        new GanchoGrapa.Pieza(63.8, 167.6, 9.55, 0),
    };
    (double x1, double x2, double y1, double y2) = EstriboMenor.CajaQueAbraza(piezas, 9.5, radioDoblezInterior: 0);
    Igual(44.75, Math.Round(x1, 6), "lado izquierdo, donde va el estribo principal");
    Igual(205.25, Math.Round(x2, 6), "lado derecho");
    Igual(54.15, Math.Round(y1, 6), "lado de abajo, por fuera de la pieza del traslapo");
    Igual(186.65, Math.Round(y2, 6), "lado de arriba");

    // Con las esquinas dobladas (#3 a 4 d: 19 mm de radio interior), la curva
    // atravesaba ~4 mm las barras de las esquinas del cerrado (21-oct). Cada
    // lado se abre lo justo para que esa barra se apoye en la curva, como las
    // esquinas del estribo principal: (19 − 9,55)(1 − 1/√2) = 2,77 mm.
    (x1, x2, y1, y2) = EstriboMenor.CajaQueAbraza(piezas, 9.5, radioDoblezInterior: 19);
    double abre = (19 - 9.55) * (1 - 1 / Math.Sqrt(2));
    Igual(Math.Round(44.75 - abre, 6), Math.Round(x1, 6), "izquierdo, abierto");
    Igual(Math.Round(205.25 + abre, 6), Math.Round(x2, 6), "derecho, abierto");
    Igual(Math.Round(54.15 - abre, 6), Math.Round(y1, 6), "abajo, abierto");
    Igual(Math.Round(186.65 + abre, 6), Math.Round(y2, 6), "arriba, abierto");

    // La barra de la esquina queda tocando la cara interior de la curva.
    double centroX = x1 + 9.5 + 19, centroY = y1 + 9.5 + 19;
    Igual(Math.Round(19 - 9.55, 6), Math.Round(Math.Sqrt(Math.Pow(63.8 - centroX, 2) + Math.Pow(73.2 - centroY, 2)), 6), "apoyada en la curva");
});

Probar("traslapos_no_chocan_cerca_de_la_esquina", () =>
{
    // Columna 387983 (21-oct), cara izquierda, en mm y m: la #8 de esquina
    // corre su pieza de abajo +25,4 hacia la fila 0 (a 94,2) y la #6 de la fila
    // 0 corre la de arriba −19,1 hacia la esquina (a 96,4): se atraviesan entre
    // 1,2 y 6,0 m. La esquina no se puede voltear (se saldría); la fila 0 sí.
    static AcomodoTraslapos.Pieza P(double x, double y, double r, double z0, double z1) => new(x, y, r, z0, z1);
    var esquina = new AcomodoTraslapos.Varilla(
        new[] { P(68.8, 94.2, 12.7, 0, 6.01), P(68.8, 68.8, 12.7, 4.19, 6.71) }, null);
    var fila0 = new AcomodoTraslapos.Varilla(
        new[] { P(63.8, 115.5, 9.55, 0, 2.51), P(63.8, 96.4, 9.55, 1.19, 6.71) },
        new[] { P(63.8, 134.6, 9.55, 0, 2.51), P(63.8, 115.5, 9.55, 1.19, 6.71) });
    var fila1 = new AcomodoTraslapos.Varilla(
        new[] { P(63.8, 179.3, 9.55, 0, 5.51), P(63.8, 160.2, 9.55, 4.19, 6.71) },
        new[] { P(63.8, 198.4, 9.55, 0, 5.51), P(63.8, 179.3, 9.55, 4.19, 6.71) });

    Cierto(AcomodoTraslapos.Choque(esquina.Opciones[0], fila0.Opciones[0]) > 50, "como salió: se atraviesan");
    int[] elegidas = AcomodoTraslapos.Elegir(new[] { esquina, fila0, fila1 });
    Cierto(elegidas[0] == 0 && elegidas[1] == 1 && elegidas[2] == 0, $"se voltea solo la fila 0 ({string.Join(", ", elegidas)})");

    // Queda un roce de medio milímetro: la #6 real está 5 mm más abajo que la
    // teórica. Contra 16,75 mm que se metían antes.
    Cierto(AcomodoTraslapos.Choque(esquina.Opciones[0], fila0.Opciones[1]) < 0.6 * 4.82, "casi no se tocan");
    Igual(0, AcomodoTraslapos.Choque(fila0.Opciones[1], fila1.Opciones[0]), "la fila 0 volteada no choca con la 1");
});

Probar("traslapos_cuatro_maneras", () =>
{
    // #7 de cara (normal X) en x = 104,1 entre esquinas a 62,2 y 187,8; pieza
    // de abajo 0–2,51 m, de arriba 0,69–6,71 m. Las cuatro entran.
    var m = AcomodoTraslapos.Cuatro(104.1, 239.4, 11.1, (1, 0), 22.2, (0, 2.51), (0.69, 6.71), (62.2, 187.8, 62.2, 239.4));
    Cierto(m.Count == 4, $"cuatro maneras ({m.Count})");
    Cierto(!m[0].DeArribaAAbajo && !m[0].CorridaLaDeArriba && m[0].Signo == 1, "la de siempre: abajo corrida hacia +, dibujo hacia arriba");
    Igual(126.3, Math.Round(m[0].Piezas[0].X, 6), "la de abajo corrida");
    Cierto(m.Single(x => x.CorridaLaDeArriba && x.Signo == 1).DeArribaAAbajo, "arriba corrida hacia +: dibujo hacia abajo");
    Cierto(!m.Single(x => x.CorridaLaDeArriba && x.Signo == -1).DeArribaAAbajo, "arriba corrida hacia −: dibujo hacia arriba");
    Cierto(m.Single(x => !x.CorridaLaDeArriba && x.Signo == -1).DeArribaAAbajo, "abajo corrida hacia −: dibujo hacia abajo");

    // Una esquina solo puede correr hacia adentro de la cara.
    Cierto(AcomodoTraslapos.Cuatro(62.2, 237.8, 12.7, (1, 0), 25.4, (0, 2.51), (0.69, 6.71), (62.2, 187.8, 62.2, 237.8))
        .All(x => x.Signo == 1), "esquina izquierda: solo hacia +");
});

// Cara de arriba de la columna 387983, medida el 21-oct: #8 de esquina en
// x 68,8 y 181,2 (y 231,2), #7 de cara en 104,1 y 145,9 (y 234,7). Tipo A
// traslapa arriba (piezas 0-6,01 y 4,19-6,71 m), tipo B abajo (0-2,51 y
// 0,69-6,71 m); de izquierda a derecha B, A, B, A.
AcomodoTraslapos.Varilla[] CaraDeArriba(bool esquinasEnLosDosEjes)
{
    var lim = (68.8, 181.2, 68.8, 234.7);
    var a = ((0.0, 6.01), (4.19, 6.71));
    var b = ((0.0, 2.51), (0.69, 6.71));
    IEnumerable<IReadOnlyList<AcomodoTraslapos.Pieza>> Maneras(double x, double y, double r, (double, double) eje, ((double, double) Abajo, (double, double) Arriba) t) =>
        AcomodoTraslapos.Cuatro(x, y, r, eje, 2 * r, t.Abajo, t.Arriba, lim).Select(m => m.Piezas);
    AcomodoTraslapos.Varilla Esquina(double x, ((double, double), (double, double)) t) =>
        new(Maneras(x, 231.2, 12.7, (1, 0), t)
            .Concat(esquinasEnLosDosEjes ? Maneras(x, 231.2, 12.7, (0, 1), t) : Enumerable.Empty<IReadOnlyList<AcomodoTraslapos.Pieza>>())
            .ToList());
    AcomodoTraslapos.Varilla DeCara(double x, ((double, double), (double, double)) t) =>
        new(Maneras(x, 234.7, 11.1, (1, 0), t).ToList());
    return new[] { Esquina(68.8, b), DeCara(104.1, a), DeCara(145.9, b), Esquina(181.2, a) };
}

double ChoqueElegido(AcomodoTraslapos.Varilla[] varillas, int[] e)
{
    double total = 0;
    for (int i = 0; i < varillas.Length; i++)
        for (int j = i + 1; j < varillas.Length; j++)
            total += AcomodoTraslapos.Choque(varillas[i].Opciones[e[i]], varillas[j].Opciones[e[j]]);
    return total;
}

Probar("traslapos_esquina_solo_por_esta_cara", () =>
{
    // Si la esquina solo puede correr su pieza a lo largo de esta cara (eje
    // X, como antes del 21-oct), alguna pieza choca: es lo que se midió.
    var varillas = CaraDeArriba(esquinasEnLosDosEjes: false);
    Cierto(ChoqueElegido(varillas, AcomodoTraslapos.Elegir(varillas)) > 0, "con eje X en las esquinas choca");
});

Probar("traslapos_la_esquina_corre_por_la_otra_cara", () =>
{
    // Con los dos ejes, la esquina corre su pieza por la cara lateral y deja
    // libre su lugar justo en la altura en que la #7 vecina se le acerca --
    // como queda sola la cara de abajo.
    var varillas = CaraDeArriba(esquinasEnLosDosEjes: true);
    int[] e = AcomodoTraslapos.Elegir(varillas);
    Igual(0.0, Math.Round(ChoqueElegido(varillas, e), 9), "ningún choque");
});

Probar("traslapos_no_atraviesan_el_gancho", () =>
{
    // Esquina de arriba a la derecha (#8 en 181,2; 231,2) donde cierra el
    // estribo: el gancho (#3, radio 4,75) entra en diagonal hacia el centro
    // desde la barra. Sin el gancho, la corrida va a lo largo de la cara de
    // arriba (la manera de siempre); con él, tiene que ir por la otra cara.
    var lim = (68.8, 181.2, 68.8, 231.2);
    var esquina = new AcomodoTraslapos.Varilla(
        AcomodoTraslapos.Cuatro(181.2, 231.2, 12.7, (1, 0), 25.4, (0, 6.01), (4.19, 6.71), lim).Select(m => m.Piezas)
            .Concat(AcomodoTraslapos.Cuatro(181.2, 231.2, 12.7, (0, 1), 25.4, (0, 6.01), (4.19, 6.71), lim).Select(m => m.Piezas))
            .ToList());
    var gancho = Enumerable.Range(0, 12)
        .Select(k => new AcomodoTraslapos.Pieza(181.2 - 12.7 - 4.75 - k * 3.0, 231.2 - 4.0 - k * 3.0, 4.25, 0, 6.71))
        .ToList();
    int sinGancho = AcomodoTraslapos.Elegir(new[] { esquina })[0];
    int conGancho = AcomodoTraslapos.Elegir(new[] { esquina }, gancho)[0];
    Igual(0.0, AcomodoTraslapos.Choque(esquina.Opciones[conGancho], gancho), "no toca el gancho");
    Cierto(esquina.Opciones[conGancho].All(q => Math.Abs(q.X - 181.2) < 1e-9), "corre por la cara lateral");
    Cierto(sinGancho == 0, "sin gancho queda la de siempre");
});

Probar("traslapos_elige_la_mejor_de_todas", () =>
{
    // Contra recorrer a mano todas las combinaciones, en casos al azar donde
    // hacen falta varios cambios a la vez: siempre el mismo choque mínimo y,
    // entre los empates, la que cambia menos varillas.
    var azar = new Random(130);
    for (int caso = 0; caso < 200; caso++)
    {
        int n = 6;
        var varillas = Enumerable.Range(0, n).Select(_ => new AcomodoTraslapos.Varilla(
            Enumerable.Range(0, 1 + azar.Next(3)).Select(_ => (IReadOnlyList<AcomodoTraslapos.Pieza>)new[]
            {
                new AcomodoTraslapos.Pieza(azar.Next(6) * 10.0, azar.Next(3) * 10.0, 6, 0, 1 + azar.Next(3)),
            }).ToList())).ToArray();
        var obstaculos = new[] { new AcomodoTraslapos.Pieza(azar.Next(6) * 10.0, azar.Next(3) * 10.0, 4, 0, 4) };

        double Total(int[] e)
        {
            double t = 0;
            for (int i = 0; i < n; i++)
            {
                t += AcomodoTraslapos.Choque(varillas[i].Opciones[e[i]], obstaculos);
                for (int j = i + 1; j < n; j++) t += AcomodoTraslapos.Choque(varillas[i].Opciones[e[i]], varillas[j].Opciones[e[j]]);
            }
            return t;
        }

        double mejor = double.MaxValue; int menosCambios = int.MaxValue;
        var e = new int[n];
        void Todas(int j)
        {
            if (j == n)
            {
                double t = Total(e); int c = e.Count(k => k != 0);
                if (t < mejor - 1e-9 || (Math.Abs(t - mejor) <= 1e-9 && c < menosCambios)) (mejor, menosCambios) = (t, c);
                return;
            }
            for (int k = 0; k < varillas[j].Opciones.Count; k++) { e[j] = k; Todas(j + 1); }
        }
        Todas(0);

        int[] elegida = AcomodoTraslapos.Elegir(varillas, obstaculos);
        Igual(Math.Round(mejor, 6), Math.Round(Total(elegida), 6), $"caso {caso}: choque mínimo");
        Igual(menosCambios, elegida.Count(k => k != 0), $"caso {caso}: cambios");
        Cierto(AcomodoTraslapos.UltimaFueCompleta, "recorrió todas");
    }
});

Probar("traslapos_sin_choque_no_se_tocan", () =>
{
    // Si nada choca, todas quedan como siempre.
    var a = new AcomodoTraslapos.Varilla(new[] { new AcomodoTraslapos.Pieza(0, 0, 10, 0, 6) }, new[] { new AcomodoTraslapos.Pieza(0, 20, 10, 0, 6) });
    var b = new AcomodoTraslapos.Varilla(new[] { new AcomodoTraslapos.Pieza(100, 0, 10, 0, 6) }, new[] { new AcomodoTraslapos.Pieza(100, 20, 10, 0, 6) });
    Cierto(AcomodoTraslapos.Elegir(new[] { a, b }).All(v => v == 0), "ninguna volteada");
});

Probar("gancho_abraza_la_barra", () =>
{
    // Columna 387983 (#130): grapa #3 doblada a 4 d (radio interior 19 mm) y
    // la #6 de la izquierda (radio 9,55) en (63,8; 115,5). La barra queda
    // adentro del doblez tocando la punta: el centro, 19 − 9,55 = 9,45 mm
    // hacia adentro, a la misma altura que la barra.
    var barra = new GanchoGrapa.Pieza(63.8, 115.5, 9.55, 2510);
    (IReadOnlyList<GanchoGrapa.Pieza> abrazadas, bool caben) = GanchoGrapa.Elegir(new[] { barra }, 19);
    Cierto(caben && abrazadas.Count == 1, "una #6 entra en el doblez");
    (double cx, double cy) = GanchoGrapa.CentroDelDoblez(abrazadas, 19, (1, 0));
    Igual(73.25, Math.Round(cx, 6), "centro, a lo largo");
    Igual(115.5, Math.Round(cy, 6), "centro, de costado");

    // La otra punta, mirando hacia −X.
    (double dx, _) = GanchoGrapa.CentroDelDoblez(new[] { new GanchoGrapa.Pieza(186.2, 120.7, 9.55, 5510) }, 19, (-1, 0));
    Igual(176.75, Math.Round(dx, 6), "la otra punta");
});

Probar("gancho_en_el_traslapo", () =>
{
    // Dos #4 de un traslapo (una 12,7 mm debajo de la otra) entran juntas en el
    // doblez de 38 mm: se abrazan las dos, el centro entre ellas, y las dos
    // tocan la cara interior.
    var abajo = new GanchoGrapa.Pieza(60, 100, 6.35, 1000);
    var arriba = new GanchoGrapa.Pieza(60, 87.3, 6.35, 3000);
    (IReadOnlyList<GanchoGrapa.Pieza> dos, bool caben) = GanchoGrapa.Elegir(new[] { abajo, arriba }, 19);
    Cierto(caben && dos.Count == 2, "dos #4 entran");
    (double cx, double cy) = GanchoGrapa.CentroDelDoblez(dos, 19, (1, 0));
    Igual(93.65, Math.Round(cy, 6), "de costado, en el medio");
    Igual(19 - 6.35, Math.Round(Math.Sqrt(Math.Pow(cx - 60, 2) + Math.Pow(cy - 100, 2)), 6), "tocan la cara interior");

    // Dos #6 (19,1 mm) ocupan 38,2 mm y no entran: queda la que sigue más
    // arriba, y se avisa.
    var b6 = new GanchoGrapa.Pieza(63.8, 115.5, 9.55, 2510);
    var a6 = new GanchoGrapa.Pieza(63.8, 96.4, 9.55, 6710);
    (IReadOnlyList<GanchoGrapa.Pieza> una, bool caben6) = GanchoGrapa.Elegir(new[] { b6, a6 }, 19);
    Cierto(!caben6 && una.Count == 1 && una[0] == a6, "dos #6 no entran: la de arriba");
});

Probar("linea_de_la_grapa", () =>
{
    // Con lo medido (centro del gancho 5 mm hacia adentro y 23,8 de costado,
    // hacia donde abre), la línea que se pide deja cada centro donde se quiere.
    (double, double) ci = (73.25, 115.5), cf = (176.75, 120.7);
    foreach ((int li, int lf) in new[] { (1, 1), (1, -1), (-1, -1) })
    {
        var ((x0, y0), (x1, y1)) = GanchoGrapa.Linea(ci, cf, (5, 23.8), (5, 23.8), li, lf);
        double largo = Math.Sqrt(Math.Pow(x1 - x0, 2) + Math.Pow(y1 - y0, 2));
        (double ux, double uy) = ((x1 - x0) / largo, (y1 - y0) / largo);
        Igual(73.25, Math.Round(x0 + ux * 5 - uy * li * 23.8, 6), $"inicio X ({li},{lf})");
        Igual(115.5, Math.Round(y0 + uy * 5 + ux * li * 23.8, 6), $"inicio Y ({li},{lf})");
        Igual(176.75, Math.Round(x1 - ux * 5 - uy * lf * 23.8, 6), $"fin X ({li},{lf})");
        Igual(120.7, Math.Round(y1 - uy * 5 + ux * lf * 23.8, 6), $"fin Y ({li},{lf})");
    }
});

Probar("linea_con_medidas_distintas_por_punta", () =>
{
    // Cada punta con la medida de SU gancho (ángulo por punta): el fin con
    // 8 mm hacia adentro en vez de 5 deja su centro donde se quiere igual.
    (double, double) ci = (73.25, 115.5), cf = (176.75, 120.7);
    foreach ((int li, int lf) in new[] { (1, 1), (1, -1) })
    {
        var ((x0, y0), (x1, y1)) = GanchoGrapa.Linea(ci, cf, (5, 23.8), (8, 20.1), li, lf);
        double largo = Math.Sqrt(Math.Pow(x1 - x0, 2) + Math.Pow(y1 - y0, 2));
        (double ux, double uy) = ((x1 - x0) / largo, (y1 - y0) / largo);
        Igual(73.25, Math.Round(x0 + ux * 5 - uy * li * 23.8, 6), $"inicio X ({li},{lf})");
        Igual(115.5, Math.Round(y0 + uy * 5 + ux * li * 23.8, 6), $"inicio Y ({li},{lf})");
        Igual(176.75, Math.Round(x1 - ux * 8 - uy * lf * 20.1, 6), $"fin X ({li},{lf})");
        Igual(120.7, Math.Round(y1 - uy * 8 + ux * lf * 20.1, 6), $"fin Y ({li},{lf})");
    }
});

Probar("posiciones_sin_el_ultimo_excluido", () =>
{
    // Un grupo con includeLastBar en false tiene la última posición, pero sin
    // estribo: es el primero del grupo siguiente. Contarla dos veces dejaba la
    // separación en 0 cm y se omitían los estribos menores (20-oct, tramos de
    // tres zonas).
    var conUltimo = PaqueteEstribos.PosicionesDelGrupo(4, 1.0, 0.5, incluirUltimo: true).ToList();
    var sinUltimo = PaqueteEstribos.PosicionesDelGrupo(4, 1.0, 0.5, incluirUltimo: false).ToList();
    Igual(4, conUltimo.Count, "con el último");
    Igual(3, sinUltimo.Count, "sin el último");
    Igual(2.0, sinUltimo[^1], "el último incluido");
    Igual(1, PaqueteEstribos.PosicionesDelGrupo(1, 1.0, 0, incluirUltimo: false).Count(), "un solo estribo queda");
});

// ------------------------------------------- barra apta para estribo (#64)

Probar("apta_cqeing_3", () =>
{
    var (aptitud, _) = CuadroVarillas.EvaluarParaEstribo(new TipoBarra("#3 CQEING", 9.5) { DobladoEstriboMm = 38, DobladoGanchoMm = 57 });
    Cierto(aptitud == AptitudEstribo.Apta, $"aptitud: {aptitud}");
});

Probar("apta_con_aviso_3_del_modelo", () =>
{
    // Medido el 8-oct: doblado estándar de 6,67 d en la #3 del modelo de prueba.
    var (aptitud, motivo) = CuadroVarillas.EvaluarParaEstribo(new TipoBarra("#3", 9.5) { DobladoEstriboMm = 38, DobladoGanchoMm = 63.4 });
    Cierto(aptitud == AptitudEstribo.AptaConAviso, $"aptitud: {aptitud}");
    Cierto(motivo is not null && motivo.Contains("6,3") && motivo.Contains("5,7"), $"motivo: {motivo}");
});

Probar("no_apta_2_del_modelo", () =>
{
    var (aptitud, _) = CuadroVarillas.EvaluarParaEstribo(new TipoBarra("#2", 6.4) { DobladoEstriboMm = 50, DobladoGanchoMm = 50 });
    Cierto(aptitud == AptitudEstribo.NoApta, $"aptitud: {aptitud}");
});

Probar("no_apta_6", () =>
{
    var (aptitud, motivo) = CuadroVarillas.EvaluarParaEstribo(new TipoBarra("#6", 19.1) { DobladoEstriboMm = 76.4, DobladoGanchoMm = 114.6 });
    Cierto(aptitud == AptitudEstribo.NoApta && motivo is not null && motivo.Contains("135"), $"{aptitud}: {motivo}");
});

Probar("cuadro_2_cargado", () =>
{
    CuadroVarillas.Varilla n3 = CuadroVarillas.Cuadro.Single(v => v.Numero == 3);
    Igual(0.057, n3.GanchoEstribo135!.Extension, "#3 135° E");
    Igual(0.038, n3.GanchoEstribo135!.DiametroDoblado, "#3 135° D");
    Igual(0.065, n3.Gancho180Cuadro.Extension, "#3 180° E");
    Igual(0.152, CuadroVarillas.Cuadro.Single(v => v.Numero == 8).Gancho90Cuadro.DiametroDoblado, "#8 90° D");
    Cierto(CuadroVarillas.Cuadro.Single(v => v.Numero == 6).GanchoEstribo135 is null, "#6 sin 135°");
});

// ---- #131: el acomodo de traslapos de una sección, sin Revit

// Esquina inferior izquierda de una 40×40 (metros), #5, traslapo a 1,5–2,2 m.
AcomodoTraslapos.PedidoSeccion Esquina40(bool conGancho) =>
    new(0.06, 0.06, 0.00795, 0.0159, Esquina: true, ConGancho: conGancho, Normal: (1, 0), CentroSeccion: (0.20, 0.20),
        Entera: (0, 6), Abajo: (0, 2.2), Arriba: (1.5, 6), Limites: (0.06, 0.34, 0.06, 0.34));

Probar("planear_seccion_sin_traslapo", () =>
{
    var sola = Esquina40(false) with { Abajo = null, Arriba = null };
    var r = AcomodoTraslapos.PlanearSeccion(new[] { sola }, null);
    Cierto(r[0].Manera is null, "sin traslapo no hay manera");
    Cierto(r[0].Eje == (1, 0), "eje = la normal");
});

Probar("planear_seccion_igual_que_elegir", () =>
{
    // Una sola varilla sin obstáculos: Elegir se queda con la de siempre
    // (la primera manera del primer eje, el de la normal), como el generador.
    var p = Esquina40(false);
    var r = AcomodoTraslapos.PlanearSeccion(new[] { p }, null);
    AcomodoTraslapos.Manera esperada = AcomodoTraslapos.Cuatro(
        p.X, p.Y, p.Radio, p.Normal, p.Diametro, p.Abajo!.Value, p.Arriba!.Value,
        (p.Limites.XMin, p.Limites.XMax, p.Limites.YMin, p.Limites.YMax))[0];
    Cierto(r[0].Eje == (1, 0), $"eje {r[0].Eje}");
    Cierto(r[0].Manera!.Piezas.SequenceEqual(esperada.Piezas), "las mismas piezas");
});

Probar("planear_seccion_con_gancho_no_prueba_diagonal", () =>
{
    // Con obstáculos donde iría la corrida en X y en Y, sin gancho la elegida
    // es la diagonal; con gancho tiene que quedar en X o en Y.
    var obstaculos = new[]
    {
        new AcomodoTraslapos.Pieza(0.0759, 0.06, 0.00795, 0, 6), new AcomodoTraslapos.Pieza(0.06, 0.0759, 0.00795, 0, 6),
    };
    var r = AcomodoTraslapos.PlanearSeccion(new[] { Esquina40(true) }, obstaculos);
    Cierto(r[0].Eje == (1, 0) || r[0].Eje == (0, 1), $"eje {r[0].Eje}");
    var sinGancho = AcomodoTraslapos.PlanearSeccion(new[] { Esquina40(false) }, obstaculos);
    Cierto(sinGancho[0].Eje.X != 0 && sinGancho[0].Eje.Y != 0, $"sin gancho, diagonal: {sinGancho[0].Eje}");
});

Console.WriteLine(fallas == 0 ? "0 fallas" : $"{fallas} falla(s)");
return fallas == 0 ? 0 : 1;
