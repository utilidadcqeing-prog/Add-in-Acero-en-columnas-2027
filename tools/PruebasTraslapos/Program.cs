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
    Igual(0.019, p.Alto, "alto");
    Igual(0.00475, p.DesplazamientoPrincipal, "principal");
    Igual(-0.00475, p.Piezas.Single().Desplazamiento, "grapa");
});

Probar("paquete_grapas_paralelas_comparten", () =>
{
    PaqueteEstribos.Paquete p = PaqueteEstribos.Calcular(
        new[] { Pieza(FormaMenor.GrapaC, EjeCaja.Fila, 0, 0), Pieza(FormaMenor.GrapaS, EjeCaja.Fila, 1, 1) }, N3, Diam);
    Cierto(p.Piezas.Select(n => n.Nivel).Distinct().Count() == 1, "un solo nivel");
    Igual(0.019, p.Alto, "alto");
    Igual(0.00475, p.DesplazamientoPrincipal, "principal");
});

Probar("paquete_cruzadas", () =>
{
    EstriboMenor fila = Pieza(FormaMenor.GrapaC, EjeCaja.Fila, 0, 0), columna = Pieza(FormaMenor.GrapaC, EjeCaja.Columna, 0, 0);
    PaqueteEstribos.Paquete p = PaqueteEstribos.Calcular(new[] { columna, fila }, N3, Diam);
    Igual(-0.0095, p.Piezas.Single(n => n.Pieza == fila).Desplazamiento, "la de fila, debajo");
    Igual(0.0095, p.Piezas.Single(n => n.Pieza == columna).Desplazamiento, "la de columna, encima");
    Igual(0.0285, p.Alto, "alto");
    Igual(0, p.DesplazamientoPrincipal, "principal en el centro");
});

Probar("paquete_caja_mas_cerca", () =>
{
    EstriboMenor caja = Pieza(FormaMenor.Cerrado, EjeCaja.Columna, 0, 1), grapa = Pieza(FormaMenor.GrapaC, EjeCaja.Fila, 0, 0);
    PaqueteEstribos.Paquete p = PaqueteEstribos.Calcular(new[] { grapa, caja }, N3, Diam);
    Cierto(p.Piezas.Single(n => n.Pieza == caja).Nivel == 1, "la caja en el nivel 1");
    Cierto(p.Piezas.Single(n => n.Pieza == grapa).Nivel == 2, "la grapa en el nivel 2");
    Igual(-0.0095, p.Piezas.Single(n => n.Pieza == caja).Desplazamiento, "la caja, debajo");
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
    Igual(0.0111, p.DesplazamientoPrincipal - p.Piezas.Single().Desplazamiento, "distancia entre ejes");
    Igual(0.0222, p.Alto, "alto");
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

Probar("cerrado_abraza_las_barras", () =>
{
    // Barras #4 (12,7 mm) en los ejes 100 y 200; estribo #3 (9,5 mm). En un
    // traslapo la pieza de arriba queda 12,7 mm hacia menos: el lazo tiene que
    // tocar por dentro el lado de afuera de las dos posiciones.
    (double desde, double hasta) = EstriboMenor.LadosQueAbrazan(100, 200, 12.7, 9.5);
    Igual(100 - 12.7 - 6.35 - 9.5, desde, "lado de abajo");
    Igual(200 + 6.35 + 9.5, hasta, "lado de arriba");
});

Probar("gancho_pasa_sobre_la_barra", () =>
{
    // Gancho de 180° con radio de eje 33 mm; la barra longitudinal queda 11 mm
    // por dentro de la punta del gancho. El arco pasa sobre el eje de la barra
    // a sqrt(33² − 22²) = 24,6 mm del centro del gancho, de costado.
    Igual(24.6, Math.Round(EstriboMenor.CorrimientoSobreBarra(33, 11), 1), "caso normal");
    Igual(33.0, EstriboMenor.CorrimientoSobreBarra(33, 40), "barra más honda que el radio: va por el tramo recto");
    Igual(0.0, EstriboMenor.CorrimientoSobreBarra(33, -2), "barra afuera de la punta: el centro");
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

Console.WriteLine(fallas == 0 ? "0 fallas" : $"{fallas} falla(s)");
return fallas == 0 ? 0 : 1;
