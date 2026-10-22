using practicacq.Modelo;

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

void Color(ColoresVarilla.ColorRgb real, byte r, byte g, byte b, string que) =>
    Cierto(real == new ColoresVarilla.ColorRgb(r, g, b), $"{que}: esperado ({r},{g},{b}), real ({real.R},{real.G},{real.B})");

// ---- Tarea 1: tabla de colores por diámetro

Probar("color_varillas_del_cuadro", () =>
{
    Color(ColoresVarilla.ColorDe(9.5), 184, 134, 11, "#3");
    Color(ColoresVarilla.ColorDe(15.9), 211, 47, 47, "#5");
    Color(ColoresVarilla.ColorDe(25.4), 21, 101, 192, "#8");
});

Probar("color_varillas_9_a_11_fuera_del_cuadro", () =>
{
    Color(ColoresVarilla.ColorDe(28.7), 0, 131, 143, "#9");
    Color(ColoresVarilla.ColorDe(32.3), 46, 125, 50, "#10");
    Color(ColoresVarilla.ColorDe(35.8), 109, 76, 65, "#11");
});

Probar("color_gris_para_otro_diametro", () =>
{
    Cierto(ColoresVarilla.ColorDe(6.4) == ColoresVarilla.Gris, "#2 en gris");
    Cierto(ColoresVarilla.ColorDe(12.0) == ColoresVarilla.Gris, "12 mm en gris");
    Cierto(ColoresVarilla.ColorDe(0) == ColoresVarilla.Gris, "0 en gris");
    Color(ColoresVarilla.Gris, 94, 102, 112, "gris");
});

// Una métrica de 16 mm cae dentro de la tolerancia del #5: mismo criterio
// que el resto del add-in (CuadroVarillas.PorDiametro).
Probar("color_metrica_16_sale_como_5", () => Color(ColoresVarilla.ColorDe(16.0), 211, 47, 47, "16 mm"));

Probar("numero_de_varilla_por_diametro", () =>
{
    Cierto(ColoresVarilla.NumeroDe(19.1) == 6, "19.1 -> #6");
    Cierto(ColoresVarilla.NumeroDe(28.7) == 9, "28.7 -> #9");
    Cierto(ColoresVarilla.NumeroDe(40) is null, "40 -> null");
});

Probar("tabla_de_3_a_11", () =>
{
    Cierto(ColoresVarilla.Tabla.Count == 9, $"{ColoresVarilla.Tabla.Count} filas");
    Cierto(ColoresVarilla.Tabla[0].Numero == 3 && ColoresVarilla.Tabla[^1].Numero == 11, "de #3 a #11");
});

// ---- Tarea 2: reparto mitad y mitad en las listas de cantidad de barras

Probar("texto_barras_por_cara", () =>
{
    Cierto(TextoBarras.PorCara(2) == "2 (1 + 1)", TextoBarras.PorCara(2));
    Cierto(TextoBarras.PorCara(4) == "4 (2 + 2)", TextoBarras.PorCara(4));
    Cierto(TextoBarras.PorCara(20) == "20 (10 + 10)", TextoBarras.PorCara(20));
    Cierto(TextoBarras.PorCara(0) == "0 (0 + 0)", TextoBarras.PorCara(0));
});

// ---- Tarea 3: alturas de los estribos (mismo reparto que GeneradorArmado)

// El paquete de un estribo #3 solo, sin estribos menores.
PaqueteEstribos.Paquete Solo(double diametroMm) =>
    PaqueteEstribos.Calcular(Array.Empty<EstriboMenor>(), diametroMm, _ => 0);

ConfigTramo Uniforme(double s) => new() { Modo = ModoEstribo.Uniforme, EspaciadoUniforme = s };
ConfigTramo Confinado(double l1, double sc, double snc) =>
    new() { Modo = ModoEstribo.Confinado, LongitudConfinamiento = l1, EspaciadoConfinado = sc, EspaciadoNoConfinado = snc };

void SeparacionesHasta(IReadOnlyList<double> z, double maximo, string que)
{
    for (int i = 1; i < z.Count; i++)
        Cierto(z[i] - z[i - 1] > 1e-9 && z[i] - z[i - 1] <= maximo + 1e-9, $"{que}: separación {z[i] - z[i - 1]} entre {i - 1} y {i}");
}

Probar("reparto_uniforme", () =>
{
    RepartoEstribos.Resultado r = RepartoEstribos.Calcular(0, 3.0, Uniforme(0.20), Solo(9.5));
    Igual(0.05475, r.Alturas[0], "primera");
    Igual(2.94525, r.Alturas[^1], "última");
    Cierto(r.Zonas.Count == 1 && r.Zonas[0].Nombre == "uniforme", "una zona uniforme");
    Cierto(r.Zonas[0].Cantidad == r.Alturas.Count, "cantidad de la zona");
    SeparacionesHasta(r.Alturas, 0.20, "uniforme");
    double s = r.Alturas[1] - r.Alturas[0];
    for (int i = 2; i < r.Alturas.Count; i++) Igual(s, r.Alturas[i] - r.Alturas[i - 1], $"separación {i}");
    Cierto(r.Aviso is null, "sin aviso");
});

Probar("reparto_confinado", () =>
{
    RepartoEstribos.Resultado r = RepartoEstribos.Calcular(0, 3.0, Confinado(0.50, 0.07, 0.14), Solo(9.5));
    Cierto(string.Join(",", r.Zonas.Select(z => z.Nombre)) == "confinado inferior,zona central,confinado superior",
        string.Join(",", r.Zonas.Select(z => z.Nombre)));
    Cierto(r.Alturas.Distinct().Count() == r.Alturas.Count, "sin alturas repetidas");
    Cierto(r.Zonas.Sum(z => z.Cantidad) == r.Alturas.Count, "las zonas suman el total");
    Igual(0.05475 + 0.50, r.Alturas[r.Zonas[0].Cantidad], "primera de la zona central");
    Igual(2.94525, r.Alturas[^1], "última");
    SeparacionesHasta(r.Alturas.Take(r.Zonas[0].Cantidad + 1).ToList(), 0.07, "confinado inferior");
    SeparacionesHasta(r.Alturas.TakeLast(r.Zonas[2].Cantidad).ToList(), 0.07, "confinado superior");
    SeparacionesHasta(r.Alturas, 0.14, "todo el tramo");
});

Probar("reparto_confinado_sin_zona_central", () =>
{
    RepartoEstribos.Resultado r = RepartoEstribos.Calcular(0, 3.0, Confinado(1.50, 0.07, 0.14), Solo(9.5));
    Cierto(r.Zonas.Count == 1 && r.Zonas[0].Nombre == "confinado (sin zona central)", "una zona sin central");
    Cierto(r.Aviso is not null, "con aviso");
    SeparacionesHasta(r.Alturas, 0.07, "confinado de punta a punta");
});

Probar("reparto_tramo_sin_altura_libre", () =>
{
    RepartoEstribos.Resultado r = RepartoEstribos.Calcular(0, 0.08, Uniforme(0.20), Solo(9.5));
    Cierto(r.Alturas.Count == 0 && r.Aviso is not null, "sin estribos y con aviso");
});

Probar("reparto_espaciado_cero", () =>
{
    Cierto(RepartoEstribos.Calcular(0, 3.0, Uniforme(0), Solo(9.5)).Alturas.Count == 0, "uniforme en 0");
    Cierto(RepartoEstribos.Calcular(0, 3.0, new ConfigTramo { Modo = ModoEstribo.Confinado }, Solo(9.5)).Alturas.Count == 0, "confinado en 0");
});

// Con estribos menores, el reparto deja lugar al paquete (5 cm al borde del paquete).
Probar("reparto_con_paquete", () =>
{
    var grapa = new EstriboMenor { Forma = FormaMenor.GrapaC, Eje = EjeCaja.Fila, Desde = 0, Hasta = 0 };
    PaqueteEstribos.Paquete p = PaqueteEstribos.Calcular(new[] { grapa }, 9.5, _ => 9.5);
    RepartoEstribos.Resultado r = RepartoEstribos.Calcular(0, 3.0, Uniforme(0.20), p);
    Igual(0.05 + p.Debajo + p.DesplazamientoPrincipal, r.Alturas[0], "primera");
    Igual(3.0 - 0.05 - p.Encima + p.DesplazamientoPrincipal, r.Alturas[^1], "última");
});

// La cuenta de un grupo, sin unidades (el generador la llama en pies).
Probar("cantidad_en_grupo", () =>
{
    Cierto(RepartoEstribos.CantidadEnGrupo(1.0, 0.20, incluirUltimo: true) == 6, "1 m @ 20 -> 6");
    Cierto(RepartoEstribos.CantidadEnGrupo(1.0, 0.20, incluirUltimo: false) == 5, "sin el último -> 5");
    Cierto(RepartoEstribos.CantidadEnGrupo(1.05, 0.20, incluirUltimo: true) == 7, "1,05 m @ 20 -> 7");
    Cierto(RepartoEstribos.CantidadEnGrupo(1.0, 0, incluirUltimo: true) == 0, "espaciado 0 -> 0");
});

// Los límites del reparto, compartidos con GeneradorArmado.GenerarTramo.
Probar("limites_del_reparto", () =>
{
    (double zInf, double zSup) = RepartoEstribos.Limites(1.0, 4.0, Solo(9.5));
    Igual(1.05475, zInf, "desde");
    Igual(3.94525, zSup, "hasta");
});

// ---- Tarea 4: posición de las barras en planta (misma cuenta que GeneradorLongitudinal)

double DiamDe(PosicionBarra p) => p == PosicionBarra.Esquina ? 25.4 : 19.1;

Probar("planta_40x60_x4_y2", () =>
{
    var cfg = new ConfigLongitudinal { BarrasPorCaraX = 4, BarrasPorCaraY = 2 };
    IReadOnlyList<SeccionPlanta.BarraEnPlanta> b = SeccionPlanta.Calcular(0.40, 0.60, 0.04, 9.5, cfg, DiamDe);
    Cierto(b.Count == 10, $"{b.Count} barras");
    Cierto(b[0].Posicion == PosicionBarra.Esquina, "la primera es esquina");
    Igual(0.04 + 0.0095 + 0.0127, b[0].X, "esquina X");
    Igual(0.04 + 0.0095 + 0.0127, b[0].Y, "esquina Y");
    Cierto(b[1].Posicion == PosicionBarra.CaraX && b[2].Posicion == PosicionBarra.CaraX, "dos de cara X abajo");
    Igual(b[1].Y, b[2].Y, "misma Y abajo");
    Igual(0.04 + 0.0095 + 0.00955, b[1].Y, "cara X separada según su diámetro");
    double xi = 0.04 + 0.0095 + 0.0127, xd = 0.40 - xi;
    Igual(xi + (xd - xi) / 3, b[1].X, "primer tercio");
    Igual(xi + 2 * (xd - xi) / 3, b[2].X, "segundo tercio");
    Igual(0.60 - xi, b[5].Y, "esquina superior derecha");
    Igual(25.4, b[0].DiametroMm, "diámetro esquina");
});

Probar("planta_solo_esquinas", () =>
{
    var cfg = new ConfigLongitudinal { BarrasPorCaraX = 0, BarrasPorCaraY = 0 };
    IReadOnlyList<SeccionPlanta.BarraEnPlanta> b = SeccionPlanta.Calcular(0.40, 0.40, 0.04, 9.5, cfg, DiamDe);
    Cierto(b.Count == 4 && b.All(x => x.Posicion == PosicionBarra.Esquina), "cuatro esquinas");
});

Probar("posicion_intermedia", () => Igual(1, SeccionPlanta.PosicionIntermedia(0, 3, 2, 0), "primera de dos entre 0 y 3"));

Probar("color_filtro_empresa", () =>
{
    // Los filtros "ACERO #N" del proyecto se repintan con la tabla (Luciano, 20-oct).
    Cierto(ColoresVarilla.ColorDeFiltroEmpresa("ACERO #3") == new ColoresVarilla.ColorRgb(184, 134, 11), "ACERO #3");
    Cierto(ColoresVarilla.ColorDeFiltroEmpresa("acero  #8") == new ColoresVarilla.ColorRgb(21, 101, 192), "acero  #8");
    Cierto(ColoresVarilla.ColorDeFiltroEmpresa("ACERO #2") == ColoresVarilla.Gris, "ACERO #2 en gris");
    Cierto(ColoresVarilla.ColorDeFiltroEmpresa("Acero_Columnas") is null, "Acero_Columnas no se toca");
    Cierto(ColoresVarilla.ColorDeFiltroEmpresa("CQEING #3") is null, "los nuestros no cuentan");
    Cierto(ColoresVarilla.ColorDeFiltroEmpresa("ACERO #3 vigas") is null, "solo el nombre exacto");
});

// ---- #133: la columna a escala real en todos los dibujos

Probar("escala_25x30_no_queda_cuadrada", () =>
{
    double e = EscalaDibujo.Ajustar(0.25, 0.30, 300, 300);
    Igual(1000, e, "manda el lado de 30 cm");
    Igual(250, 0.25 * e, "ancho");
    Igual(300, 0.30 * e, "alto");
});

Probar("escala_120x50_entra_sin_deformarse", () =>
{
    double e = EscalaDibujo.Ajustar(1.20, 0.50, 300, 300);
    Igual(250, e, "manda el lado de 120 cm");
    Igual(1.20 / 0.50, (1.20 * e) / (0.50 * e), "misma proporción que la columna");
    Cierto(0.50 * e <= 300, "el alto también entra");
});

Probar("escala_cuadrada_queda_cuadrada", () =>
    Igual(130 / 0.40, EscalaDibujo.Ajustar(0.40, 0.40, 230, 130), "manda el alto disponible"));

Probar("texto_cotas", () =>
{
    Cierto(EscalaDibujo.Cm(0.25) == "25 cm", EscalaDibujo.Cm(0.25));
    Cierto(EscalaDibujo.Cm(0.225) == "22,5 cm", EscalaDibujo.Cm(0.225));
    Cierto(EscalaDibujo.M(3) == "3,00 m", EscalaDibujo.M(3));
});

Console.WriteLine(fallas == 0 ? "0 fallas" : $"{fallas} falla(s)");
return fallas == 0 ? 0 : 1;
