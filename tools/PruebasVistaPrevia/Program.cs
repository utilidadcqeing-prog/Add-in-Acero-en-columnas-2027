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
    Color(ColoresVarilla.ColorDe(9.5), 251, 188, 4, "#3");
    Color(ColoresVarilla.ColorDe(15.9), 234, 67, 53, "#5");
    Color(ColoresVarilla.ColorDe(25.4), 66, 133, 244, "#8");
});

Probar("color_varillas_9_a_11_fuera_del_cuadro", () =>
{
    Color(ColoresVarilla.ColorDe(28.7), 139, 69, 19, "#9");
    Color(ColoresVarilla.ColorDe(32.3), 0, 0, 0, "#10");
    Color(ColoresVarilla.ColorDe(35.8), 233, 30, 99, "#11");
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
Probar("color_metrica_16_sale_como_5", () => Color(ColoresVarilla.ColorDe(16.0), 234, 67, 53, "16 mm"));

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
    Cierto(ColoresVarilla.ColorDeFiltroEmpresa("ACERO #3") == new ColoresVarilla.ColorRgb(251, 188, 4), "ACERO #3");
    Cierto(ColoresVarilla.ColorDeFiltroEmpresa("acero  #8") == new ColoresVarilla.ColorRgb(66, 133, 244), "acero  #8");
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

// ---- La sección como queda en Revit (2026-10-21-seccion-como-en-revit.md).
// Columna 387983: 25×30, recubrimiento 4 cm, estribo #3 (doblez 38 mm),
// esquinas #8, cara X #7, cara Y #6.

void Cerca(double esperado, double real, double tolerancia, string que)
{
    if (Math.Abs(esperado - real) > tolerancia) throw new Exception($"{que}: esperado {esperado}, real {real}");
}

Probar("esquina_dentro_del_doblez_del_estribo", () =>
{
    var cfg = new ConfigLongitudinal { BarrasPorCaraX = 4, BarrasPorCaraY = 4 };
    double D(PosicionBarra p) => p switch { PosicionBarra.Esquina => 25.4, PosicionBarra.CaraX => 22.2, _ => 19.1 };
    IReadOnlyList<SeccionPlanta.BarraEnPlanta> b = SeccionPlanta.Calcular(0.25, 0.30, 0.04, 9.5, cfg, D, doblezEstriboMm: 38);
    // Centro del doblez a 4 + 0,95 + 1,9 = 6,85 cm de cada cara; la #8 (r 12,7) toca
    // la cara interior (r 19): a 6,3 mm del centro, sobre la diagonal.
    double esperado = 0.0685 - (0.019 - 0.0127) * Math.Sqrt(2) / 2;
    Igual(esperado, b[0].X, "esquina X");
    Igual(esperado, b[0].Y, "esquina Y");
    // Las de las caras quedan pegadas al estribo: 4 + 0,95 + 0,955 = 5,905 cm
    // (medido antes del arreglo: 6,38 = esto + los 4,75 mm de más del estribo).
    Igual(0.04 + 0.0095 + 0.00955, b.Where(x => x.Posicion == PosicionBarra.CaraY).Min(x => x.X), "cara Y pegada al estribo");
});

Probar("estribo_principal_esquinas_y_ganchos", () =>
{
    // Cara exterior al recubrimiento: de 4 a 21 cm en X y de 4 a 26 en Y.
    IReadOnlyList<(double X, double Y)> e = PlantaArmado.EstriboCerrado(0.04, 0.21, 0.04, 0.26, 0.0095, 0.038, PlantaArmado.Cola135(9.5));
    Cerca(0.04475, e.Min(p => p.X), 1e-9, "eje del lado izquierdo a 4 cm + medio diámetro");
    Cerca(0.26 - 0.00475, e.Max(p => p.Y), 1e-9, "eje del lado de arriba");
    // Los dos ganchos en la esquina de abajo a la izquierda; las colas, a un
    // radio al eje (19 + 4,75 = 23,75 mm) de cada lado de la diagonal (medido: ~23,7).
    double cx = 0.04475 + 0.02375, cy = cx;
    double ADiagonal((double X, double Y) p) => ((p.X - cx) - (p.Y - cy)) / Math.Sqrt(2);
    Cerca(0.02375, Math.Abs(ADiagonal(e[0])), 1e-9, "cola del arranque");
    Cerca(0.02375, Math.Abs(ADiagonal(e[^1])), 1e-9, "cola del cierre");
    Cierto(ADiagonal(e[0]) * ADiagonal(e[^1]) < 0, "una cola a cada lado de la diagonal");
    // Las colas miden la E del cuadro (5,7 cm con #3) y entran hacia el centro.
    Cierto(e[0].X > cx && e[0].Y > cy && e[^1].X > cx && e[^1].Y > cy, "las colas van hacia adentro");
});

Probar("grapa_abraza_la_barra", () =>
{
    // Grapa en C #3 en la fila 0 (y = 12 cm), entre las #6 de las caras Y.
    double xi = 0.04 + 0.0095 + 0.00955, xd = 0.25 - xi, y = 0.12;
    (double X, double Y) ci = PlantaArmado.CentroDelGancho(xi, y, 0.00955, 0.038, (1, 0));
    (double X, double Y) cf = PlantaArmado.CentroDelGancho(xd, y, 0.00955, 0.038, (-1, 0));
    // La barra toca la cara interior del doblez en la punta: centro corrido 19 − 9,55 mm hacia adentro.
    Cerca(xi + 0.00945, ci.X, 1e-9, "centro del gancho del inicio");
    Cerca(y, ci.Y, 1e-9, "a la altura de la barra");
    IReadOnlyList<(double X, double Y)> g = PlantaArmado.Grapa(ci, cf, 1, 1, 0.0095, 0.038, PlantaArmado.Cola180(9.5));
    // Abre hacia +Y (el centro de la sección): el tramo recto queda un radio al eje por debajo.
    Cerca(y - 0.02375, g.Min(p => p.Y), 1e-9, "tramo recto");
    // Por fuera, el doblez llega a centro − radio al eje − medio diámetro.
    Cerca(ci.X - 0.02375, g.Min(p => p.X), 1e-9, "punta del gancho");
    // Colas de 6,5 cm (cuadro, #3), hacia adentro, del lado que abre.
    Cerca(ci.X + 0.065, g[0].X, 1e-9, "cola del inicio");
    Cerca(y + 0.02375, g[0].Y, 1e-9, "cola del lado de arriba");
});

Probar("grapa_en_s_abre_un_gancho_para_cada_lado", () =>
{
    IReadOnlyList<(double X, double Y)> g = PlantaArmado.Grapa((0.07, 0.12), (0.18, 0.12), 1, -1, 0.0095, 0.038, 0.065);
    Cierto(g[0].Y > 0.12 && g[^1].Y < 0.12, "la cola del inicio arriba y la del fin abajo");
});

// ---- #139: ganchos que cruzarían la línea de propiedad

// El lote del modelo de prueba del 22-oct: X -0,15..7,15 ; Y -0,15..12,15.
var lote = new List<TramoLindero>
{
    new(7.15, 12.15, 7.15, -0.15),
    new(7.15, -0.15, -0.15, -0.15),
    new(-0.15, -0.15, -0.15, 12.15),
    new(-0.15, 12.15, 7.15, 12.15),
};

Probar("lindero_alcance_gancho_90", () =>
{
    Igual(0.222 + 2.5 * 0.0127, Lindero.AlcanceGancho90(12.7), "#4");
    Igual(0.388 + 2.5 * 0.0222, Lindero.AlcanceGancho90(22.2), "#7");
    Igual(16 * 0.0358, Lindero.AlcanceGancho90(35.8), "#11, fuera del cuadro");
});

Probar("lindero_gancho_que_cruza", () =>
{
    // Columna 782105, barra #7 812763: en X 6,913 con el gancho hacia +X.
    double alcance = Lindero.AlcanceGancho90(22.2);
    Cierto(Lindero.Cruza(lote, 6.913, -0.065, 1, 0, alcance), "hacia +X cruza X = 7,15");
    Cierto(!Lindero.Cruza(lote, 6.913, -0.065, -1, 0, alcance), "girado hacia -X queda en el lote");
    // Barra 812858: en Y 0,098 con el gancho hacia -Y.
    Cierto(Lindero.Cruza(lote, 7.087, 0.098, 0, -1, alcance), "hacia -Y cruza Y = -0,15");
    Cierto(!Lindero.Cruza(lote, 7.087, 0.098, 0, 1, alcance), "girado hacia +Y queda en el lote");
});

Probar("lindero_gancho_que_no_llega", () =>
{
    // Columna interior (3,5 ; 6): ningún gancho llega al borde.
    Cierto(!Lindero.Cruza(lote, 3.5, 6.0, 1, 0, 0.45), "hacia +X");
    Cierto(!Lindero.Cruza(lote, 3.5, 6.0, 0, -1, 0.45), "hacia -Y");
    // Sin línea de propiedad, nada cruza.
    Cierto(!Lindero.Cruza(new List<TramoLindero>(), 6.913, -0.065, 1, 0, 1.0), "sin lindero");
});

Probar("lindero_punta_justo_sobre_la_linea_cuenta", () =>
    Cierto(Lindero.Cruza(lote, 7.0, 0.0, 1, 0, 0.15), "la punta toca X = 7,15"));

// ---- #131: choques entre piezas de la sección

string Barra(int i) => $"la barra {i}";

Probar("choques_separacion_minima", () =>
{
    Igual(40, ChoquesSeccion.SeparacionMinimaMm(15.9, 15.9), "dos #5: manda 4 cm");
    Igual(40, ChoquesSeccion.SeparacionMinimaMm(25.4, 19.1), "1,5 × 25,4 = 38,1 < 40");
    Igual(48.45, ChoquesSeccion.SeparacionMinimaMm(32.3, 12.7), "1,5 × la más gruesa");
});

Probar("choques_barras_juntas", () =>
{
    var juntas = new List<ChoquesSeccion.PiezaRedonda> { new(0.10, 0.10, 15.9, 0), new(0.15, 0.10, 15.9, 1) };
    IReadOnlyList<ChoquesSeccion.Choque> choques = ChoquesSeccion.BarrasJuntas(juntas, Barra);
    Cierto(choques.Count == 1, $"{choques.Count} choques");
    Cierto(choques[0].Motivo.Contains("3,4 cm") && choques[0].Motivo.Contains("4,0 cm"), choques[0].Motivo);
    Cierto(choques[0].Marcas.Count == 2, "marca las dos barras");

    var separadas = new List<ChoquesSeccion.PiezaRedonda> { new(0.10, 0.10, 15.9, 0), new(0.16, 0.10, 15.9, 1) };
    Cierto(ChoquesSeccion.BarrasJuntas(separadas, Barra).Count == 0, "a 6 cm entre ejes no chocan");
});

Probar("choques_misma_varilla_no_cuenta", () =>
{
    var traslapo = new List<ChoquesSeccion.PiezaRedonda> { new(0.10, 0.10, 15.9, 3), new(0.1159, 0.10, 15.9, 3) };
    Cierto(ChoquesSeccion.BarrasJuntas(traslapo, Barra).Count == 0, "las dos piezas de un traslapo van pegadas");
});

Probar("choques_alturas_que_no_se_cruzan", () =>
{
    var piezas = new List<ChoquesSeccion.PiezaRedonda> { new(0.10, 0.10, 15.9, 0, 0, 1), new(0.12, 0.10, 15.9, 1, 2, 3) };
    Cierto(ChoquesSeccion.BarrasJuntas(piezas, Barra).Count == 0, "a distinta altura no chocan");
});

Probar("choques_nombre_barra", () =>
{
    var cfg = new ConfigLongitudinal { BarrasPorCaraX = 2, BarrasPorCaraY = 0 };
    IReadOnlyList<SeccionPlanta.BarraEnPlanta> barras = SeccionPlanta.Calcular(0.40, 0.40, 0.04, 9.5, cfg, _ => 15.9);
    Cierto(ChoquesSeccion.NombreBarra(barras, 0) == "la esquina inferior izquierda", ChoquesSeccion.NombreBarra(barras, 0));
    Cierto(ChoquesSeccion.NombreBarra(barras, 1) == "la barra 1 de la cara inferior", ChoquesSeccion.NombreBarra(barras, 1));
});

Probar("choques_grapa_sobre_barra_que_no_abraza", () =>
{
    var trazo = new List<(double X, double Y)> { (0.05, 0.10), (0.35, 0.10) };
    var encima = new List<ChoquesSeccion.PiezaRedonda> { new(0.20, 0.11, 15.9, 4) };
    ChoquesSeccion.Choque? c = ChoquesSeccion.MenorSobreBarra(trazo, 9.5, encima, new HashSet<int>(), "la grapa de la fila 1", Barra);
    Cierto(c is not null && c.Motivo.Contains("pasa encima") && c.Marcas.Count == 1, c?.Motivo ?? "sin choque");

    var alLado = new List<ChoquesSeccion.PiezaRedonda> { new(0.20, 0.125, 15.9, 4) };
    Cierto(ChoquesSeccion.MenorSobreBarra(trazo, 9.5, alLado, new HashSet<int>(), "la grapa", Barra) is null, "a 2,5 cm del eje no la toca");
});

Probar("choques_grapa_no_choca_con_las_que_abraza", () =>
{
    var trazo = new List<(double X, double Y)> { (0.05, 0.10), (0.35, 0.10) };
    var abrazada = new List<ChoquesSeccion.PiezaRedonda> { new(0.20, 0.11, 15.9, 4) };
    Cierto(ChoquesSeccion.MenorSobreBarra(trazo, 9.5, abrazada, new HashSet<int> { 4 }, "la grapa", Barra) is null, "la abraza");
});

Probar("choques_cerrado_imposible_igual_que_antes", () =>
{
    var barra3 = new TipoBarra("#3 CQEING", 9.5) { DobladoEstriboMm = 38 };
    var cerrado = new EstriboMenor { Forma = FormaMenor.Cerrado, Eje = EjeCaja.Fila, Desde = 0, Hasta = 0 };
    // Dos barras #4 pegadas: la caja que las abraza es angosta para doblar #3.
    var juntas = new[] { new GanchoGrapa.Pieza(0.10, 0.10, 0.00635, 1), new GanchoGrapa.Pieza(0.10, 0.20, 0.00635, 1) };
    string? texto = ChoquesSeccion.CerradoImposible(cerrado, barra3, juntas, "fila 1");
    Cierto(texto is not null && texto.StartsWith("El estribo cerrado de la fila 1 mide") &&
           texto.Contains("con «#3 CQEING» necesita al menos 4,8 cm para doblarse"), texto ?? "null");

    var separadas = new[] { new GanchoGrapa.Pieza(0.05, 0.10, 0.00635, 1), new GanchoGrapa.Pieza(0.25, 0.20, 0.00635, 1) };
    Cierto(ChoquesSeccion.CerradoImposible(cerrado, barra3, separadas, "fila 1") is null, "una caja ancha se dobla");
});

var tiposDePrueba = new Dictionary<string, TipoBarra>
{
    ["#3"] = new TipoBarra("#3", 9.5) { DobladoEstriboMm = 38, DobladoGanchoMm = 57 },
    ["#5"] = new TipoBarra("#5", 15.9) { DobladoEstriboMm = 64, DobladoGanchoMm = 95 },
    ["#6"] = new TipoBarra("#6", 19.1) { DobladoEstriboMm = 115, DobladoGanchoMm = 115 },
};
TipoBarra? TipoDe(string? nombre) => nombre is not null && tiposDePrueba.TryGetValue(nombre, out TipoBarra? t) ? t : null;

ColumnaContexto ColumnaDePrueba(double lado, double alto, TipoTramo tipo = TipoTramo.Libre)
{
    var tramo = new Tramo { Indice = 0, ZInferior = 0, ZSuperior = alto, Tipo = tipo };
    return new ColumnaContexto
    {
        Marca = "C-1", BaseX = lado, BaseY = lado, Recubrimiento = 0.04,
        ZInferior = 0, ZInferiorLongitudinal = 0, ZSuperior = alto, Tramos = new List<Tramo> { tramo },
    };
}

ConfigLongitudinal LongDePrueba(string barra, int porCara, double comercial = 12) => new()
{
    BarrasPorCaraX = 2 * porCara, BarrasPorCaraY = 2 * porCara,
    TipoBarraEsquinasNombre = barra, TipoBarraCaraXNombre = barra, TipoBarraCaraYNombre = barra,
    LongitudComercialBarra = comercial,
};

ChoquesSeccion.Resultado ChoquesDe(ColumnaContexto c, ConfigLongitudinal l) =>
    ChoquesSeccion.DeTramo(c, c.Tramos[0], new ConfigTramo { TipoBarraNombre = "#3" }, l, 0.04, TipoDe, 9.5);

Probar("choques_tramo_sin_choques", () =>
{
    ChoquesSeccion.Resultado r = ChoquesDe(ColumnaDePrueba(0.40, 3), LongDePrueba("#5", 0));
    Cierto(r.Normal.Count == 0 && r.Traslapo.Count == 0, string.Join(" | ", r.Normal.Concat(r.Traslapo).Select(c => c.Motivo)));
    // 3 m con barras de 12 m: una sola pieza, sin zona de traslapo que mostrar.
    Cierto(!r.HayTraslapo && r.PiezasTraslapo.Count == 0, "sin traslapo");
});

Probar("choques_tramo_muchas_barras", () =>
{
    // 30×30 con 3 intermedias de #6 por cara: entre ejes ≈ 4,55 cm, libre ≈ 2,6 cm.
    ChoquesSeccion.Resultado r = ChoquesDe(ColumnaDePrueba(0.30, 3), LongDePrueba("#6", 3));
    Cierto(r.Normal.Any(c => c.Motivo.Contains("de la cara inferior") && c.Motivo.Contains("se piden 4,0 cm")),
        string.Join(" | ", r.Normal.Select(c => c.Motivo)));
});

Probar("choques_tramo_traslapo_mas_apretado", () =>
{
    // 40×40 con 3 intermedias de #6: libre ≈ 5,1 cm, pero la pieza corrida del
    // traslapo se arrima un diámetro a la vecina (≈ 3,2 cm).
    ChoquesSeccion.Resultado r = ChoquesDe(ColumnaDePrueba(0.40, 9), LongDePrueba("#6", 3, comercial: 6));
    Cierto(r.HayTraslapo, "tiene traslapo");
    Cierto(r.Normal.Count == 0, "normal: " + string.Join(" | ", r.Normal.Select(c => c.Motivo)));
    Cierto(r.Traslapo.Count > 0 && r.Traslapo.All(c => c.Motivo.StartsWith("En la zona de traslapo")), "sin choques de traslapo");
    Cierto(r.PiezasTraslapo.Count > 0, "piezas para dibujar");
});

Probar("choques_tramo_contra_zapata", () =>
{
    // Un cerrado en una sola fila no se puede doblar: se avisa en un tramo
    // libre, pero el tramo contra la zapata no lleva menores.
    ConfigLongitudinal l = LongDePrueba("#6", 3);
    l.EstribosMenores = new List<EstriboMenor> { new() { Forma = FormaMenor.Cerrado, Eje = EjeCaja.Fila, Desde = 1, Hasta = 1 } };
    Cierto(ChoquesDe(ColumnaDePrueba(0.40, 3), l).Normal.Any(c => c.Motivo.StartsWith("El estribo cerrado")), "libre: avisa el cerrado");
    Cierto(!ChoquesDe(ColumnaDePrueba(0.40, 3, TipoTramo.ContraZapata), l).Normal.Any(c => c.Motivo.Contains("estribo cerrado")),
        "contra zapata: no evalúa menores");
});

Probar("choques_un_aviso_por_par_en_la_columna", () =>
{
    // El tramo de la zapata no lleva estribo y corre las esquinas distinto: el
    // mismo par sale con otra distancia. En la columna se avisa una vez.
    ConfigLongitudinal l = LongDePrueba("#6", 3, comercial: 6);
    ChoquesSeccion.Resultado libre = ChoquesDe(ColumnaDePrueba(0.40, 9), l);
    ChoquesSeccion.Resultado zapata = ChoquesDe(ColumnaDePrueba(0.40, 9, TipoTramo.ContraZapata), l);
    IReadOnlyList<ChoquesSeccion.Choque> juntos = ChoquesSeccion.DeLaColumna(new[] { libre, zapata });
    Cierto(juntos.Count == juntos.Select(c => c.Clave).Distinct().Count(), "un aviso por clave");
    Cierto(juntos.Count == libre.Todos.Select(c => c.Clave).Union(zapata.Todos.Select(c => c.Clave)).Count(),
        $"{juntos.Count} avisos");
    Cierto(juntos.Take(libre.Todos.Count()).SequenceEqual(libre.Todos), "manda el primer tramo");
});

Probar("choques_varios_traslapos_se_avisan", () =>
{
    // 15 m con barras de 6 m: cada varilla lleva más de un traslapo y esa zona
    // no se revisa; tiene que decirse.
    ChoquesSeccion.Resultado r = ChoquesDe(ColumnaDePrueba(0.40, 15), LongDePrueba("#6", 3, comercial: 6));
    Cierto(r.VarillasSinRevisarEnTraslapo > 0, $"{r.VarillasSinRevisarEnTraslapo} varillas sin revisar");
});

// ---- #131: detalles de la revisión final

Probar("choques_grapa_a_ras_de_una_barra", () =>
{
    // Grapa #3 (r 4,75 mm) y barra #5 (r 7,95 mm): se tocan a 12,7 mm entre ejes.
    var trazo = new List<(double X, double Y)> { (0.05, 0.10), (0.35, 0.10) };
    ChoquesSeccion.Choque? Con(double dy) => ChoquesSeccion.MenorSobreBarra(
        trazo, 9.5, new List<ChoquesSeccion.PiezaRedonda> { new(0.20, 0.10 + dy, 15.9, 4) }, new HashSet<int>(), "la grapa", Barra);
    Cierto(Con(0.0120) is null, "se meten 0,7 mm: dentro de la tolerancia de 1 mm");
    Cierto(Con(0.0115) is not null, "se meten 1,2 mm: choque");
});

Probar("choques_tramo_dentro_de_viga", () =>
{
    ConfigLongitudinal l = LongDePrueba("#6", 3);
    l.EstribosMenores = new List<EstriboMenor> { new() { Forma = FormaMenor.Cerrado, Eje = EjeCaja.Fila, Desde = 1, Hasta = 1 } };
    Cierto(!ChoquesDe(ColumnaDePrueba(0.40, 3, TipoTramo.DentroDeViga), l).Normal.Any(c => c.Motivo.Contains("estribo cerrado")),
        "en el nudo no van menores");
});

Probar("choques_columna_normal_con_grapas_sin_avisos", () =>
{
    ConfigLongitudinal l = LongDePrueba("#5", 1);
    l.EstribosMenores = new List<EstriboMenor>
    {
        new() { Forma = FormaMenor.GrapaC, Eje = EjeCaja.Fila, Desde = 0, Hasta = 0 },
        new() { Forma = FormaMenor.GrapaS, Eje = EjeCaja.Columna, Desde = 0, Hasta = 0 },
    };
    ChoquesSeccion.Resultado r = ChoquesDe(ColumnaDePrueba(0.40, 3), l);
    Cierto(!r.Todos.Any(), string.Join(" | ", r.Todos.Select(c => c.Motivo)));
});

Probar("choques_texto_de_la_fila", () =>
{
    var barra3 = new TipoBarra("#3 CQEING", 9.5) { DobladoEstriboMm = 38 };
    var cerrado = new EstriboMenor { Forma = FormaMenor.Cerrado, Eje = EjeCaja.Fila, Desde = 0, Hasta = 0 };
    var juntas = new[] { new GanchoGrapa.Pieza(0.10, 0.10, 0.00635, 1), new GanchoGrapa.Pieza(0.10, 0.20, 0.00635, 1) };
    Cierto(ChoquesSeccion.CerradoImposible(cerrado, barra3, juntas, "fila 1")!.StartsWith("El estribo cerrado de la fila 1 mide"), "singular");
    Cierto(ChoquesSeccion.CerradoImposible(cerrado, barra3, juntas, "filas 1 a 2")!.StartsWith("El estribo cerrado de las filas 1 a 2 mide"), "plural");
});

Probar("choques_traslapo_nombra_la_pieza_corrida", () =>
{
    ChoquesSeccion.Resultado r = ChoquesDe(ColumnaDePrueba(0.40, 9), LongDePrueba("#6", 3, comercial: 6));
    Cierto(r.Traslapo.Any(c => c.Motivo.Contains("la pieza corrida de")), string.Join(" | ", r.Traslapo.Select(c => c.Motivo)));
});

// ---- Sección propia de cada tramo (dado de 40×40 debajo de una columna de 30×30, 22-oct)

Probar("tramo_sin_medida_usa_la_de_la_columna", () =>
{
    ColumnaContexto c = ColumnaDePrueba(0.30, 3);
    (double x, double y, double dx, double dy) = c.Tramos[0].Seccion(c);
    Igual(0.30, x, "lado X"); Igual(0.30, y, "lado Y"); Igual(0, dx, "dx"); Igual(0, dy, "dy");
});

Probar("tramo_con_medida_propia", () =>
{
    ColumnaContexto c = ColumnaDePrueba(0.30, 3);
    var dado = new Tramo { LadoX = 0.40, LadoY = 0.40, ComunDesdeX = 0.05, ComunDesdeY = 0.05 };
    (double x, double y, double dx, double dy) = dado.Seccion(c);
    Igual(0.40, x, "lado X"); Igual(0.40, y, "lado Y"); Igual(0.05, dx, "dx"); Igual(0.05, dy, "dy");
});

Probar("choques_igual_con_la_misma_seccion_en_toda_la_columna", () =>
{
    // Con medida propia igual a la de la columna, nada se corre: los mismos
    // choques que sin ella (el caso de siempre no cambia).
    ColumnaContexto sinMedida = ColumnaDePrueba(0.40, 9);
    Tramo t = sinMedida.Tramos[0];
    var conMedida = new ColumnaContexto
    {
        Marca = sinMedida.Marca, BaseX = 0.40, BaseY = 0.40, Recubrimiento = 0.04,
        ZInferior = 0, ZInferiorLongitudinal = 0, ZSuperior = 9,
        Tramos = new List<Tramo> { new() { Indice = 0, ZInferior = 0, ZSuperior = 9, Tipo = t.Tipo, LadoX = 0.40, LadoY = 0.40 } },
    };
    ConfigLongitudinal l = LongDePrueba("#6", 3, comercial: 6);
    string Motivos(ChoquesSeccion.Resultado r) => string.Join(" | ", r.Normal.Concat(r.Traslapo).Select(c => c.Motivo));
    Cierto(Motivos(ChoquesDe(sinMedida, l)) == Motivos(ChoquesDe(conMedida, l)), "los mismos choques");
});

Console.WriteLine(fallas == 0 ? "0 fallas" : $"{fallas} falla(s)");
return fallas == 0 ? 0 : 1;
