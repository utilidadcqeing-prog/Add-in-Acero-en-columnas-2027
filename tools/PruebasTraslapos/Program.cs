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
    foreach (PlanTipo p in new[] { r.A, r.B })
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
                Cierto((ordinal % 2 == 0) == (p.Nombre == "A"), $"{p.Nombre}: traslapo en un tramo del otro tipo");
        }
    }

    if (r.A.TieneSolucion && r.B.TieneSolucion)
        foreach (Traslapo a in r.A.Traslapos)
            Cierto(r.B.Traslapos.All(b => a.ZSuperior <= b.ZInferior + 1e-9 || a.ZInferior >= b.ZSuperior - 1e-9),
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
    Piezas(r.A, 6.00);
    Piezas(r.B, 6.00);
    Cierto(r.A.EnTramosDelOtro, "A no tiene tramos propios en una columna de un tramo");
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
    Piezas(r.B, 3.50, 3.50);
    UnTraslapo(r.B, 2.33, 3.50);
    Piezas(r.A, 2.00, 5.00);
    UnTraslapo(r.A, 0.83, 2.00);
    CumpleReglas(col, new Opciones(12, 0.50, false), r);
});

Probar("ejemplo_de_luciano_esquiva_el_nudo", () =>
{
    // El ejemplo con el que se eligió el orden de preferencias (17-oct):
    // tramo 0-2,60, nudo 2,60-3,00, tramo 3,00-5,60; #4, redondeo 0,50, barra de 6.
    var col = new Columna(new[] { T(0, 2.60), T(2.60, 3.00, TipoTramo.DentroDeViga), T(3.00, 5.60) }, 0, 5.60, false);
    var op = new Opciones(6, 0.50, false);
    Resultado r = Calcular(col, N4, N4, op);
    Piezas(r.B, 1.50, 5.00);
    UnTraslapo(r.B, 0.60, 1.50);
    Piezas(r.A, 4.50, 2.00);
    UnTraslapo(r.A, 3.60, 4.50);
    CumpleReglas(col, op, r);
});

Probar("forzado_un_solo_tramo", () =>
{
    // Review Focus 1: sin zapata, el tramo arranca en la misma cota que la barra.
    // B: 1,10-2,00. A, en lo que queda: 0,10-1,00 (empata con 2,10-3,00, gana el de abajo).
    var col = new Columna(new[] { T(0, 3.10) }, 0, 3.10, false);
    var op = new Opciones(12, 0.50, false);
    Resultado r = Calcular(col, N4, N4, op);
    UnTraslapo(r.B, 1.10, 2.00);
    UnTraslapo(r.A, 0.10, 1.00);
    Cierto(r.A.EnTramosDelOtro && !r.B.EnTramosDelOtro, "solo A va en los tramos del otro");
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
    Cierto(!con6.TieneSolucion && !con6.B.TieneSolucion, "con 6 m, B no debería tener solución");
    Cierto(con6.B.Motivo!.Contains("12 m"), $"B: el motivo no sugiere 12 m: {con6.B.Motivo}");
    Resultado con12 = Calcular(col, N4, N4, new Opciones(12, 0.50, false));
    Cierto(con12.TieneSolucion, $"con 12 m debería haber solución: {con12.A.Motivo} {con12.B.Motivo}");
    CumpleReglas(col, new Opciones(12, 0.50, false), con12);
});

Probar("barra_fuera_del_cuadro", () =>
{
    // Review Focus 3.
    Resultado r = Calcular(c12, 30.0, N4, new Opciones(12, 0.50, false));
    Cierto(!r.A.TieneSolucion && !string.IsNullOrWhiteSpace(r.A.Motivo), "A debería fallar con motivo");
    Cierto(r.B.TieneSolucion, $"B no depende de A: {r.B.Motivo}");
});

Probar("solape_mayor_de_los_dos", () =>
{
    Resultado cadaUno = Calcular(c12, N4, N6, new Opciones(12, 0.50, false));
    Igual(0.75, cadaUno.A.SolapeMinimo!.Value, "A con su propio solape");
    Igual(1.10, cadaUno.B.SolapeMinimo!.Value, "B con su propio solape");
    Resultado mayor = Calcular(c12, N4, N6, new Opciones(12, 0.50, true));
    Igual(1.10, mayor.A.SolapeMinimo!.Value, "A con el mayor");
    Igual(1.10, mayor.B.SolapeMinimo!.Value, "B con el mayor");
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
    Cierto(r14.TieneSolucion, $"14 m con barra de 6 debería tener solución: {r14.A.Motivo} {r14.B.Motivo}");
    Cierto(r14.B.Traslapos.Count == 2, $"B: {r14.B.Traslapos.Count} traslapos, se esperaban 2");
    CumpleReglas(col14, op6, r14);

    var col26 = new Columna(new[] { T(0, 26.00) }, 0, 26.00, false);
    var op12 = new Opciones(12, 0.50, false);
    Resultado r26 = Calcular(col26, N4, N4, op12);
    Cierto(r26.TieneSolucion, $"26 m con barra de 12 debería tener solución: {r26.A.Motivo} {r26.B.Motivo}");
    CumpleReglas(col26, op12, r26);
});

Probar("cotas_invertidas_no_revientan", () =>
{
    // Revisión final: un dato de columna roto (remate por debajo del arranque)
    // no puede tirar una excepción dentro de Revit: sale sin solución, con motivo.
    Resultado r = Calcular(new Columna(new[] { T(0, 3.00) }, 3.00, 0, false), N4, N4, new Opciones(12, 0.50, false));
    Cierto(!r.TieneSolucion && r.A.Motivo is not null && r.B.Motivo is not null, "debería salir sin solución, con motivo");
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

Probar("migracion_config_vieja", () =>
{
    // Review Focus 2: una configuración guardada antes de los tipos A/B.
    var vieja = new ConfigLongitudinal { TipoBarraEsquinasNombre = "#5", TipoBarraCaraXNombre = "#4", TipoBarraCaraYNombre = "#3" };
    Cierto(vieja.MigrarBarrasAB(), "debería migrar");
    Cierto(vieja.TipoBarraANombre == "#5" && vieja.TipoBarraBNombre == "#4", $"A={vieja.TipoBarraANombre} B={vieja.TipoBarraBNombre}");
    Igual(0.50, vieja.RedondeoVarillas, "redondeo por defecto");
    Cierto(!vieja.SolapeMayorDeLosDos, "solape de cada diámetro por defecto");
    Cierto(!vieja.MigrarBarrasAB(), "una segunda vez no hace nada");

    var soloY = new ConfigLongitudinal { TipoBarraEsquinasNombre = "#5", TipoBarraCaraYNombre = "#3" };
    soloY.MigrarBarrasAB();
    Cierto(soloY.TipoBarraBNombre == "#3", "sin cara X, B toma la cara Y");

    var nueva = new ConfigLongitudinal { TipoBarraANombre = "#4", TipoBarraBNombre = "#4", RedondeoVarillas = 0.20, SolapeMayorDeLosDos = true };
    Cierto(!nueva.MigrarBarrasAB(), "una nueva no se migra");
    ConfigLongitudinal copia = nueva.Copiar();
    Cierto(copia.TipoBarraANombre == "#4" && copia.TipoBarraBNombre == "#4" && copia.SolapeMayorDeLosDos, "Copiar lleva A, B y solape");
    Igual(0.20, copia.RedondeoVarillas, "Copiar lleva el redondeo");
});

Console.WriteLine(fallas == 0 ? "0 fallas" : $"{fallas} falla(s)");
return fallas == 0 ? 0 : 1;
