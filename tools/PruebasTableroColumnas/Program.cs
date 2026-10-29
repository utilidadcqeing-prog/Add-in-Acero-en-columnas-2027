using System.Text.Json;
using practicacq.Modelo;
using practicacq.Tablero;

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

var proveedor = new ProveedorContextoFalso();
ColumnaContexto c12 = proveedor.Leer("C-12");
ColumnaContexto c1 = proveedor.Leer("C-1");

ConfigTramo Uniforme(double cada) => new()
{
    Modo = ModoEstribo.Uniforme,
    TipoBarraNombre = "#3",
    TipoGanchoNombre = "135°",
    EspaciadoUniforme = cada,
};

ConfigTramo Confinado() => new()
{
    Modo = ModoEstribo.Confinado,
    TipoBarraNombre = "#3",
    TipoGanchoNombre = "135°",
    LongitudConfinamiento = 0.50,
    EspaciadoConfinado = 0.07,
    EspaciadoNoConfinado = 0.14,
};

// Lo que deja hoy «Aceptar y aplicar a toda la columna» (VentanaConfiguradorEstribos.Aceptar):
// una copia de la config del panel en CADA tramo, sea libre, nudo o contra zapata.
List<ConfigTramo?> ComoAceptarTodaLaColumna(ConfigTramo cfg, ColumnaContexto ctx) =>
    ctx.Tramos.Select(_ => (ConfigTramo?)JsonSerializer.Deserialize<ConfigTramo>(JsonSerializer.Serialize(cfg))).ToList();

void MismaLista(List<ConfigTramo?> esperado, List<ConfigTramo?> real)
{
    Cierto(esperado.Count == real.Count, $"cantidad de tramos: esperado {esperado.Count}, real {real.Count}");
    for (int i = 0; i < esperado.Count; i++)
        Cierto(ComparadorConfig.Igual(esperado[i], real[i]),
            $"tramo {i}: esperado {JsonSerializer.Serialize(esperado[i])}, real {JsonSerializer.Serialize(real[i])}");
}

IEnumerable<int> Indices(ColumnaContexto ctx, TipoTramo tipo) =>
    ctx.Tramos.Where(t => t.Tipo == tipo).Select(t => t.Indice);

// ---- Tarea 2: estribos por columna y su traducción a tramos (#209)

Probar("TodaLaColumna_DaLoMismoQueAceptarTodaLaColumna", () =>
{
    ConfigTramo cfg = Uniforme(0.20);
    MismaLista(ComoAceptarTodaLaColumna(cfg, c12), ConfigEstribosColumna.Desde(cfg).ATramos(c12));
});

Probar("Nudos_SinConfinamiento", () =>
{
    List<ConfigTramo?> tramos = ConfigEstribosColumna.Desde(Confinado()).ATramos(c12);
    Cierto(Indices(c12, TipoTramo.DentroDeViga).Any(), "C-12 tiene que tener nudos");
    foreach (int i in Indices(c12, TipoTramo.DentroDeViga))
    {
        Cierto(tramos[i]!.Modo == ModoEstribo.Uniforme, $"el nudo {i} salió {tramos[i]!.Modo}");
        Igual(0.14, tramos[i]!.EspaciadoUniforme, $"espaciado del nudo {i}");
    }
    foreach (int i in Indices(c12, TipoTramo.Libre))
        Cierto(tramos[i]!.Modo == ModoEstribo.Confinado, $"el tramo libre {i} tiene que seguir confinado");
});

Probar("Nudos_CambiadosAMano", () =>
{
    ConfigEstribosColumna estribos = ConfigEstribosColumna.Desde(Confinado());
    estribos.NudoEspaciado = 0.10;
    List<ConfigTramo?> tramos = estribos.ATramos(c12);
    foreach (int i in Indices(c12, TipoTramo.DentroDeViga))
    {
        Cierto(tramos[i]!.Modo == ModoEstribo.Uniforme, $"el nudo {i} salió {tramos[i]!.Modo}");
        Igual(0.10, tramos[i]!.EspaciadoUniforme, $"espaciado del nudo {i}");
    }
});

Probar("Nudos_EnUniforme_IgualQueLaColumna", () =>
{
    ConfigEstribosColumna estribos = ConfigEstribosColumna.Desde(Uniforme(0.20));
    estribos.NudoEspaciado = 0.10; // de cuando estaba en confinado
    List<ConfigTramo?> tramos = estribos.ATramos(c12);
    foreach (int i in Indices(c12, TipoTramo.DentroDeViga))
        Igual(0.20, tramos[i]!.EspaciadoUniforme, $"espaciado del nudo {i}");
});

Probar("Excepcion_SoloCambiaSuTramo", () =>
{
    int libre = Indices(c12, TipoTramo.Libre).Skip(1).First();
    ConfigEstribosColumna estribos = ConfigEstribosColumna.Desde(Uniforme(0.20));
    estribos.Excepciones[libre] = Uniforme(0.10);
    List<ConfigTramo?> sin = ConfigEstribosColumna.Desde(Uniforme(0.20)).ATramos(c12);
    List<ConfigTramo?> con = estribos.ATramos(c12);
    for (int i = 0; i < con.Count; i++)
    {
        if (i == libre) Igual(0.10, con[i]!.EspaciadoUniforme, $"espaciado del tramo {libre}");
        else Cierto(ComparadorConfig.Igual(sin[i], con[i]), $"el tramo {i} cambió y no tenía excepción");
    }
});

Probar("ExcepcionFueraDeRango_SeDescarta", () =>
{
    ConfigEstribosColumna estribos = ConfigEstribosColumna.Desde(Uniforme(0.20));
    estribos.Excepciones[99] = Uniforme(0.10);
    List<ConfigTramo?> tramos = estribos.ATramos(c12);
    Cierto(tramos.Count == c12.Tramos.Count, $"cantidad: esperado {c12.Tramos.Count}, real {tramos.Count}");
});

Probar("ColumnaDeUnTramo_SinNudosNiZapata", () =>
{
    List<ConfigTramo?> tramos = ConfigEstribosColumna.Desde(Confinado()).ATramos(c1);
    Cierto(tramos.Count == 1, $"cantidad: esperado 1, real {tramos.Count}");
    Cierto(tramos[0]!.Modo == ModoEstribo.Confinado, "el único tramo tiene que seguir confinado");
});

Probar("ConfiguracionVieja_SeTraduceIgualQueAceptarTodaLaColumna", () =>
{
    ConfigTramo cfg = Uniforme(0.20);
    // Como la guarda la 1.4.1: sin el campo «Estribos».
    string json = JsonSerializer.Serialize(new { Nombre = "vieja", Tramo = cfg, Longitudinal = new ConfigLongitudinal() });
    ConfiguracionGuardada leida = JsonSerializer.Deserialize<ConfiguracionGuardada>(json)!;
    Cierto(leida.Estribos is null, "una configuración vieja no trae «Estribos»");
    MismaLista(ComoAceptarTodaLaColumna(cfg, c12), leida.EstribosDeLaColumna().ATramos(c12));
});

Probar("ExcepcionEnNudo_SiempreUniforme", () =>
{
    int nudo = Indices(c12, TipoTramo.DentroDeViga).First();
    ConfigEstribosColumna estribos = ConfigEstribosColumna.Desde(Uniforme(0.20));
    ConfigTramo raro = Confinado();
    raro.EspaciadoUniforme = 0.12;
    estribos.Excepciones[nudo] = raro;
    ConfigTramo t = estribos.ATramos(c12)[nudo]!;
    Cierto(t.Modo == ModoEstribo.Uniforme, $"el nudo {nudo} salió {t.Modo}");
    Igual(0.12, t.EspaciadoUniforme, $"espaciado del nudo {nudo}");
});

Probar("Copiar_NoComparteNada", () =>
{
    ConfigEstribosColumna a = ConfigEstribosColumna.Desde(Uniforme(0.20));
    a.Excepciones[2] = Uniforme(0.10);
    ConfigEstribosColumna b = a.Copiar();
    b.TramosLibres.EspaciadoUniforme = 0.30;
    b.Excepciones[2].EspaciadoUniforme = 0.30;
    b.Excepciones[4] = Uniforme(0.30);
    Igual(0.20, a.TramosLibres.EspaciadoUniforme, "tramos libres del original");
    Igual(0.10, a.Excepciones[2].EspaciadoUniforme, "excepción del original");
    Cierto(!a.Excepciones.ContainsKey(4), "el original ganó una excepción");
});

Probar("Validar_MismosMensajesQueLaVentana", () =>
{
    var sinNada = new ConfigTramo { Modo = ModoEstribo.Uniforme };
    List<string> errores = ValidacionTramo.Validar(sinNada, null);
    Cierto(errores.SequenceEqual(new[]
    {
        "Elige un tipo de barra.",
        "Elige un tipo de gancho.",
        "El espaciado tiene que ser mayor que cero.",
    }), string.Join(" | ", errores));

    var tramoCorto = new Tramo { Indice = 0, ZInferior = 0, ZSuperior = 0.90, Tipo = TipoTramo.Libre };
    errores = ValidacionTramo.Validar(Confinado(), tramoCorto);
    Cierto(errores.Count == 1 && errores[0].Contains("no queda zona central"), string.Join(" | ", errores));
});

Probar("DesdeTramos_VuelveATraducirIgual", () =>
{
    ConfigEstribosColumna original = ConfigEstribosColumna.Desde(Confinado());
    original.NudoEspaciado = 0.10;
    original.Excepciones[Indices(c12, TipoTramo.Libre).Last()] = Uniforme(0.25);
    List<ConfigTramo?> tramos = original.ATramos(c12);
    ConfigEstribosColumna vuelta = ConfigEstribosColumna.DesdeTramos(c12, tramos, Uniforme(0.20));
    MismaLista(tramos, vuelta.ATramos(c12));
    Cierto(vuelta.Excepciones.Count == 1, $"excepciones: esperado 1, real {vuelta.Excepciones.Count}");
});

// ---- Tarea 3: la sesión y la vista, sin Revit (#208-#211)

var tipos = new ProveedorTiposFalso();

VistaColumnas Mandar(SesionColumnas sesion, object mensaje) =>
    sesion.Atender(JsonSerializer.Serialize(mensaje, VistaColumnas.Json));

SesionColumnas ConC12(IProveedorContexto? contexto = null, IAlmacenConfiguraciones? almacen = null)
{
    var sesion = new SesionColumnas(contexto ?? new ProveedorContextoFalso(), tipos, almacen);
    Mandar(sesion, new { tipo = "elegirColumnas", ids = new[] { "C-12" } });
    return sesion;
}

string EstadoDe(VistaColumnas v, string id) => v.Planta.Columnas.Single(c => c.Id == id).Estado;

Probar("ElegirColumna_LaVistaTraeSuSeccionYSusTramos", () =>
{
    VistaColumnas v = ConC12().Vista();
    ColumnaVista col = v.Columna ?? throw new Exception("no vino la columna");
    Cierto(col.Marca == "C-12", $"marca {col.Marca}");
    Cierto(col.Tramos.Count == c12.Tramos.Count, $"tramos: esperado {c12.Tramos.Count}, real {col.Tramos.Count}");
    Igual(40, col.Seccion.Ancho, "ancho de la sección (cm)");
    Cierto(col.Seccion.Barras.Count == 8, $"barras: esperado 8 (4 esquinas y 1 por cara), real {col.Seccion.Barras.Count}");
    Cierto(col.Seccion.Estribo.Puntos.Count > 10, "el estribo viene con su forma");
    Cierto(v.Opciones.Longitudinal.BarrasPorCaraX == 1, $"la página ve las barras de CADA cara: {v.Opciones.Longitudinal.BarrasPorCaraX}");
    Cierto(col.ZonasEstribos.Any(z => z.Zona == "zapata" && z.Indice == 0), "falta la zapata");
    Cierto(col.ZonasEstribos.Any(z => z.Zona == "nudo"), "faltan los nudos");
    Cierto(col.Zapata is not null, "C-12 arranca sobre zapata");
    Cierto(col.Traslapos.Planes.Count > 0, "faltan los traslapos");
});

Probar("ArrancaConfinada_YLosNudosConElCentro", () =>
{
    VistaColumnas v = ConC12().Vista();
    Cierto(v.Opciones.Estribos.TramosLibres.Modo == ModoEstribo.Confinado, "los tramos libres arrancan confinados");
    Igual(0.14, v.Opciones.Estribos.NudoEspaciado, "los nudos arrancan con el espaciado del centro");
    Cierto(v.Columna!.ZonasEstribos.Any(z => z.Zona == "confinada") && v.Columna.ZonasEstribos.Any(z => z.Zona == "central"), "zonas confinadas y centrales");
});

Probar("CambiarBarrasX_CambiaLasBarrasDeLaSeccion", () =>
{
    SesionColumnas sesion = ConC12();
    VistaColumnas v = Mandar(sesion, new { tipo = "cambiarLongitudinal", config = new { barrasPorCaraX = 3 } });
    Cierto(v.Columna!.Seccion.Barras.Count == 12, $"barras: esperado 12, real {v.Columna.Seccion.Barras.Count}");
    Cierto(v.Opciones.Longitudinal.BarrasPorCaraX == 3, $"barras por cara: {v.Opciones.Longitudinal.BarrasPorCaraX}");
    Cierto(sesion.OpcionesParaGenerar(false)!.Plan.Columnas[0].Longitudinal!.BarrasPorCaraX == 6, "en ConfigLongitudinal cuentan las dos caras");
    Cierto(v.Columna.Seccion.Barras.Count(b => b.Posicion == "abajo") == 3, "3 barras en la cara de abajo");
});

Probar("CambiarBarraEsquinas_CambiaSuColor", () =>
{
    // Tarea 5, paso 1: las esquinas toman el color del diámetro nuevo; las caras no cambian.
    SesionColumnas sesion = ConC12();
    TipoBarra n6 = tipos.ListarBarras().Single(b => b.Nombre == "#6 COL");
    VistaColumnas antes = sesion.Vista();
    VistaColumnas v = Mandar(sesion, new { tipo = "cambiarLongitudinal", config = new { tipoBarraEsquinasNombre = n6.Nombre } });
    List<BarraVista> esquinas = v.Columna!.Seccion.Barras.Where(b => b.Posicion == "esquina").ToList();
    Cierto(esquinas.Count == 4, $"esquinas: {esquinas.Count}");
    Cierto(esquinas.All(b => b.Color == ArmadorVistaColumnas.Color(n6.DiametroMm) && Math.Abs(b.DiametroMm - n6.DiametroMm) < 1e-9), "color y diámetro de las esquinas");
    Cierto(v.Columna.Seccion.Barras.Where(b => b.Posicion != "esquina").Select(b => b.Color)
        .SequenceEqual(antes.Columna!.Seccion.Barras.Where(b => b.Posicion != "esquina").Select(b => b.Color)), "las caras no cambian");
});

Probar("CambiarRecubrimiento_MueveLasBarras", () =>
{
    // Tarea 5, paso 1: más recubrimiento, las barras más hacia adentro.
    SesionColumnas sesion = ConC12();
    BarraVista antes = sesion.Vista().Columna!.Seccion.Barras.First(b => b.Posicion == "esquina");
    VistaColumnas v = Mandar(sesion, new { tipo = "cambiarRecubrimiento", valor = 0.06 });
    BarraVista despues = v.Columna!.Seccion.Barras.First(b => b.Posicion == "esquina");
    Igual(6, v.Columna.Seccion.Recubrimiento, "recubrimiento de la sección, en cm");
    Igual(2, despues.X - antes.X, "la esquina se corre 2 cm en X");
    Igual(2, despues.Y - antes.Y, "y 2 cm en Y");
});

Probar("CambiarEspaciado_CambiaLasZonas", () =>
{
    VistaColumnas v = Mandar(ConC12(), new { tipo = "cambiarEstribos", tramosLibres = new { modo = "Uniforme", espaciadoUniforme = 0.15 } });
    List<ZonaVista> conEstribos = v.Columna!.ZonasEstribos.Where(z => z.Zona != "zapata").ToList();
    Cierto(conEstribos.Count > 0, "sin zonas");
    foreach (ZonaVista z in conEstribos) Igual(0.15, z.Cada!.Value, $"cada de la zona {z.Zona} del tramo {z.Indice}");
    Cierto(conEstribos.All(z => z.Cantidad > 0), "una zona quedó sin estribos");
});

Probar("PrimerCambio_MarcaLaColumnaLista", () =>
{
    SesionColumnas sesion = ConC12();
    Cierto(EstadoDe(sesion.Vista(), "C-12") == "sinConfigurar", "antes de cambiar nada no está lista");
    Cierto(sesion.OpcionesParaGenerar(false) is null, "sin listas no hay nada que generar");
    VistaColumnas v = Mandar(sesion, new { tipo = "cambiarRecubrimiento", valor = 0.05 });
    Cierto(EstadoDe(v, "C-12") == "lista", $"estado {EstadoDe(v, "C-12")}");
    Cierto(v.Generacion.Listas.SequenceEqual(new[] { "C-12" }), string.Join(",", v.Generacion.Listas));
    Igual(0.05, v.Opciones.Recubrimiento, "recubrimiento");
});

Probar("QuitarDeLista_LaVuelveSinConfigurar", () =>
{
    SesionColumnas sesion = ConC12();
    Mandar(sesion, new { tipo = "cambiarRecubrimiento", valor = 0.05 });
    VistaColumnas v = Mandar(sesion, new { tipo = "quitarDeLista", id = "C-12" });
    Cierto(EstadoDe(v, "C-12") == "sinConfigurar", $"estado {EstadoDe(v, "C-12")}");
    Cierto(v.Generacion.Listas.Count == 0, "sigue en la lista");
});

Probar("VariasColumnas_SoloCambianLasCompatibles", () =>
{
    var sesion = new SesionColumnas(new ConCopiaDeC12(), tipos);
    VistaColumnas v = Mandar(sesion, new { tipo = "elegirColumnas", ids = new[] { "C-12", "C-14", "C-1" } });
    Cierto(v.Compatibles.SequenceEqual(new[] { "C-12", "C-14" }), "compatibles: " + string.Join(",", v.Compatibles));
    Cierto(v.Descartadas.SequenceEqual(new[] { "C-1" }), "descartadas: " + string.Join(",", v.Descartadas));
    v = Mandar(sesion, new { tipo = "cambiarEstribos", tramosLibres = new { espaciadoUniforme = 0.15 } });
    Cierto(EstadoDe(v, "C-12") == "lista" && EstadoDe(v, "C-14") == "lista", "C-12 y C-14 tienen que quedar listas");
    Cierto(EstadoDe(v, "C-1") == "sinConfigurar", "C-1 no es compatible y no tiene que cambiar");
});

Probar("Generar_ArmaElMismoPlanQueLaVentanaVieja", () =>
{
    SesionColumnas sesion = ConC12();
    Mandar(sesion, new { tipo = "cambiarEstribos", tramosLibres = new { modo = "Uniforme", espaciadoUniforme = 0.15 } });
    ColumnaPlan real = sesion.OpcionesParaGenerar(false)!.Plan.Columnas.Single();

    // Lo que arma hoy la ventana vieja con 0.15 en el panel y «Aceptar y aplicar a toda la columna». El recubrimiento
    // va explícito: 4 cm (decisión 15), lo mismo que elige la vieja cuando C-12 trae 4 cm en Revit.
    ConfigTramo cfg = ValoresPorDefecto.Tramo(tipos);
    cfg.EspaciadoUniforme = 0.15;
    var esperado = new ColumnaPlan
    {
        Id = "C-12", Tramos = ComoAceptarTodaLaColumna(cfg, c12), Longitudinal = ValoresPorDefecto.Longitudinal(tipos), Recubrimiento = 0.04,
    };
    string Json(ColumnaPlan c) => JsonSerializer.Serialize(new { c.Id, c.Tramos, c.Longitudinal, c.Recubrimiento });
    Cierto(Json(esperado) == Json(real), $"esperado {Json(esperado)}\n         real     {Json(real)}");
});

Probar("SeleccionarEnRevit_ElementoDeArriba_EligeLaColumna", () =>
{
    // Hallazgo 12: en una planta de arriba se selecciona el elemento de ese piso, no el de abajo (el Id de la columna).
    var sesion = new SesionColumnas(new ConCopiaDeC12(), tipos);
    Cierto(sesion.ColumnasDe(new[] { "C-14-piso2", "C-14" }).SequenceEqual(new[] { "C-14" }), "dos pisos de la misma columna cuentan una");
    Cierto(sesion.ColumnasDe(new[] { "un muro" }).Count == 0, "lo que no es columna no cuenta");
    Cierto(sesion.ElementosEnRevit.Contains("C-14-piso2"), "«Todas» tiene que seleccionar también los pisos de arriba");
    VistaColumnas v = sesion.SincronizarDesdeRevit(new[] { "C-14-piso2" });
    Cierto(v.Elegidas.SequenceEqual(new[] { "C-14" }), "elegidas: " + string.Join(",", v.Elegidas));
    Cierto(v.Columna?.Marca == "C-14", "tiene que mostrar la sección de C-14");
});

Probar("ColumnaNueva_Recubrimiento4cm", () =>
{
    // Decisión 15: C-14 trae 2.5 cm en Revit, y arranca igual con 4 cm.
    var sesion = new SesionColumnas(new ConCopiaDeC12(), tipos);
    VistaColumnas v = Mandar(sesion, new { tipo = "elegirColumnas", ids = new[] { "C-14" } });
    Igual(0.04, v.Opciones.Recubrimiento, "en la página");
    Igual(0.04, sesion.OpcionesPara3D()!.Plan.Columnas[0].Recubrimiento!.Value, "en la 3D");
    Mandar(sesion, new { tipo = "cambiarEstribos", tramosLibres = new { espaciadoUniforme = 0.15 } });
    Igual(0.04, sesion.OpcionesParaGenerar(false)!.Plan.Columnas[0].Recubrimiento!.Value, "al generar");
});

Probar("RecubrimientosRevit_LosDeLaColumna", () =>
{
    // Decisión 15: C-14 trae 2.5 cm en la columna y 4 cm en sus tramos; el 7.5 cm de contra zapata no cuenta.
    var sesion = new SesionColumnas(new ConCopiaDeC12(), tipos);
    VistaColumnas v = Mandar(sesion, new { tipo = "elegirColumnas", ids = new[] { "C-14" } });
    Cierto(v.Columna!.RecubrimientosRevit.SequenceEqual(new[] { 0.025, 0.04 }), "en Revit: " + string.Join(", ", v.Columna.RecubrimientosRevit));
    v = Mandar(sesion, new { tipo = "cambiarRecubrimiento", valor = 0.025 });
    Igual(0.025, v.Opciones.Recubrimiento, "el clic en «2.5 cm» lo pone");
});

Probar("Catalogos_CqeingPrimero", () =>
{
    // Decisión 16: el orden de ProveedorTiposReal (las CQEING primero y por número), no por diámetro.
    var sesion = new SesionColumnas(new ProveedorContextoFalso(), new ConCqeingPrimero());
    CatalogosVista cat = sesion.Vista().Catalogos;
    Cierto(cat.BarrasLongitudinales.Take(2).Select(b => b.Nombre).SequenceEqual(new[] { "#3 CQEING", "#4 CQEING" }),
        "longitudinales: " + string.Join(", ", cat.BarrasLongitudinales.Select(b => b.Nombre)));
    Cierto(cat.BarrasEstribo.First().Nombre == "#3 CQEING", "estribo: " + string.Join(", ", cat.BarrasEstribo.Select(b => b.Nombre)));
    Cierto(cat.BarrasEstribo.All(b => b.Nombre != "#2 COL"), "la #2 COL no es apta para estribo (#64)");
});

Probar("Generar_DejaElPedidoYLuegoLaMarcaGenerada", () =>
{
    SesionColumnas sesion = ConC12();
    Mandar(sesion, new { tipo = "cambiarRecubrimiento", valor = 0.05 });
    VistaColumnas v = Mandar(sesion, new { tipo = "generar", borrarAjeno = true });
    Cierto(!v.Generacion.Pendiente, "no debería haber choques: " + string.Join(" | ", v.Generacion.Choques));
    Cierto(sesion.GeneracionPedida is { BorrarArmadoAjeno: true }, "no quedó pedida la generación");
    v = sesion.ResultadoGeneracion(new ResultadoArmado());
    Cierto(EstadoDe(v, "C-12") == "generada", $"estado {EstadoDe(v, "C-12")}");
    Cierto(v.Generacion.Resultado!.Detalles[0] == "Se armó la columna C-12.", v.Generacion.Resultado.Detalles[0]);
    Cierto(v.Generacion.Listas.Count == 0, "sigue en la lista");
    Cierto(v.Generacion.Resultado.Avisos.Count == 0, "sin avisos");
});

Probar("Generar_ConAvisos_NoLosMezclaConElExito", () =>
{
    // 29-oct: un aviso de traslapos salía en el mismo recuadro verde que «Se armó la columna».
    SesionColumnas sesion = ConC12();
    Mandar(sesion, new { tipo = "cambiarRecubrimiento", valor = 0.05 });
    Mandar(sesion, new { tipo = "generar", borrarAjeno = false });
    var resultado = new ResultadoArmado();
    resultado.Avisos.Add("Traslapos: las formas «CQ Recta 0-90» las usan barras de otras columnas.");
    VistaColumnas v = sesion.ResultadoGeneracion(resultado);
    ResultadoVista r = v.Generacion.Resultado!;
    Cierto(r.Avisos.SequenceEqual(resultado.Avisos), "los avisos van aparte");
    Cierto(r.Detalles.All(d => !d.StartsWith("Traslapos")), "no van entre los detalles del éxito");
    Cierto(r.Detalles[0] == "Se armó la columna C-12, pero con avisos: revísalos abajo.", r.Detalles[0]);
});

Probar("Generar_SinListas_Avisa", () =>
{
    SesionColumnas sesion = ConC12();
    VistaColumnas v = Mandar(sesion, new { tipo = "generar", borrarAjeno = false });
    Cierto(sesion.GeneracionPedida is null, "no hay nada que generar");
    Cierto(v.Aviso?.Contains("No hay columnas listas") == true, v.Aviso ?? "sin aviso");
});

Probar("MenorInvalido_SaleMarcadoConSuMotivo", () =>
{
    SesionColumnas sesion = ConC12();
    Mandar(sesion, new { tipo = "menor", accion = "agregar", forma = "Cerrado", eje = "Columna", desde = 0, hasta = 0 });
    VistaColumnas v = Mandar(sesion, new { tipo = "menor", accion = "agregar", forma = "GrapaC", eje = "Fila", desde = 4, hasta = 4 });
    MenorVista cerrado = v.Columna!.Seccion.Menores[0], fuera = v.Columna.Seccion.Menores[1];
    Cierto(!cerrado.Valida && cerrado.Motivo!.Contains("estribo cerrado"), $"cerrado angosto: {cerrado.Valida} {cerrado.Motivo}");
    Cierto(cerrado.Puntos.Count > 0, "el cerrado inválido se dibuja igual, en rojo");
    Cierto(!fuera.Valida && fuera.Motivo!.Contains("ya no existe"), $"grapa fuera de rango: {fuera.Valida} {fuera.Motivo}");
});

Probar("Grapa_TraeSuFormaYSusPuntas", () =>
{
    VistaColumnas v = Mandar(ConC12(), new { tipo = "menor", accion = "agregar", forma = "GrapaC", eje = "Fila", desde = 0, hasta = 0 });
    MenorVista grapa = v.Columna!.Seccion.Menores.Single();
    Cierto(grapa.Valida, grapa.Motivo ?? "");
    Cierto(grapa.Puntas!.Count == 2 && grapa.Puntos.Count > 10, "la grapa viene con sus dos puntas y su forma");
    Cierto(v.Opciones.Longitudinal.EstribosMenores.Single().AnguloInicio == 180, "los ángulos van en grados");
});

Probar("AplicarConfiguracionVieja_MarcaListaYTraduce", () =>
{
    var almacen = new AlmacenConfiguracionesEnMemoria();
    ConfigTramo cfg = ValoresPorDefecto.Tramo(tipos);
    cfg.Modo = ModoEstribo.Confinado;
    almacen.Guardar(new ConfiguracionGuardada { Nombre = "vieja", Tramo = cfg, Longitudinal = ValoresPorDefecto.Longitudinal(tipos) });

    SesionColumnas sesion = ConC12(almacen: almacen);
    VistaColumnas v = Mandar(sesion, new { tipo = "aplicarConfiguracion", nombre = "vieja" });
    Cierto(EstadoDe(v, "C-12") == "lista", $"estado {EstadoDe(v, "C-12")}");
    Cierto(v.Configuracion == "vieja", $"configuración {v.Configuracion}");
    Cierto(v.Opciones.Estribos.TramosLibres.Modo == ModoEstribo.Confinado, "tramos libres confinados");
    Igual(0.14, v.Opciones.Estribos.NudoEspaciado, "los nudos arrancan con el espaciado del centro");
    List<ConfigTramo?> tramos = sesion.OpcionesParaGenerar(false)!.Plan.Columnas[0].Tramos;
    foreach (int i in Indices(c12, TipoTramo.DentroDeViga))
        Cierto(tramos[i]!.Modo == ModoEstribo.Uniforme && Math.Abs(tramos[i]!.EspaciadoUniforme - 0.14) < 1e-9, $"nudo {i}");
});

Probar("GuardarConfiguracion_EscribeLosDosCampos", () =>
{
    var almacen = new AlmacenConfiguracionesEnMemoria();
    SesionColumnas sesion = ConC12(almacen: almacen);
    Mandar(sesion, new { tipo = "cambiarEstribos", nudoEspaciado = 0.11, tramosLibres = new { modo = "Confinado" } });
    VistaColumnas v = Mandar(sesion, new { tipo = "guardarConfiguracion", nombre = "nueva", comoNueva = true });
    ConfiguracionGuardada g = almacen.Listar().Single();
    Cierto(g.Estribos is not null && Math.Abs(g.Estribos.NudoEspaciado - 0.11) < 1e-9, "Estribos con el espaciado de los nudos");
    Cierto(g.Tramo.Modo == ModoEstribo.Confinado, "Tramo = los tramos libres, para la 1.4.1");
    Cierto(v.Catalogos.Configuraciones.SequenceEqual(new[] { "nueva" }), "la lista de configuraciones");
});

Probar("Ver3D_DejaElPedido", () =>
{
    SesionColumnas sesion = ConC12();
    Mandar(sesion, new { tipo = "ver3D", filtro = "Solo traslapos" });
    Cierto(sesion.Pedido3D is { Partes: ModoPartes.SoloTraslapos }, "el filtro de traslapos");
});

Probar("Ver3D_CadaCambioLoVuelveAPedir", () =>
{
    SesionColumnas sesion = ConC12();
    Mandar(sesion, new { tipo = "cambiarRecubrimiento", valor = 0.05 });
    Cierto(sesion.Pedido3D is null, "sin abrir la 3D no se pide nada");
    Mandar(sesion, new { tipo = "ver3D", filtro = "Todo" });
    Cierto(sesion.Abrir3D && sesion.Pedido3D is not null, "el menú abre la vista");
    sesion.Pedido3D = null;
    sesion.Abrir3D = false;
    Mandar(sesion, new { tipo = "cambiarRecubrimiento", valor = 0.045 });
    Cierto(sesion.Pedido3D is not null && !sesion.Abrir3D, "un cambio la redibuja sin volver a abrirla");
    Igual(0.045, sesion.OpcionesPara3D()!.Plan.Columnas[0].Recubrimiento!.Value, "la 3D usa lo de ahora");
});

Probar("Excepcion_SeMarcaEnElTramo", () =>
{
    int libre = Indices(c12, TipoTramo.Libre).Skip(1).First();
    VistaColumnas v = Mandar(ConC12(), new { tipo = "cambiarEstribos", excepcion = new { indice = libre, config = Uniforme(0.10) } });
    Cierto(v.Columna!.Tramos[libre].Excepcion, "el tramo tiene que venir marcado");
    Cierto(v.Columna.ZonasEstribos.Where(z => z.Indice == libre).All(z => Math.Abs(z.Cada!.Value - 0.10) < 1e-9), "las zonas del tramo");
    v = Mandar(ConC12(), new { tipo = "cambiarEstribos", excepcion = new { indice = libre, config = (ConfigTramo?)null } });
    Cierto(!v.Columna!.Tramos[libre].Excepcion, "sin excepción");
});

Probar("VistaC12_SeGuardaParaLaMaqueta", () =>
{
    string json = ConC12().Vista().AJson();
    Cierto(json.Contains("\"tipo\":\"ContraZapata\"") && json.Contains("\"barrasPorCaraX\":1"), "camelCase y enums por nombre");
    string carpeta = Path.Combine(CarpetaDelProyecto(), "Datos");
    Directory.CreateDirectory(carpeta);
    File.WriteAllText(Path.Combine(carpeta, "vista-c12.json"),
        JsonSerializer.Serialize(JsonDocument.Parse(json), new JsonSerializerOptions { WriteIndented = true }) + "\n");
});

static string CarpetaDelProyecto()
{
    for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "PruebasTableroColumnas.csproj"))) return d.FullName;
    throw new Exception("no se encontró la carpeta del proyecto de pruebas");
}

Console.WriteLine(fallas == 0 ? "Todas las pruebas pasaron." : $"{fallas} prueba(s) fallaron.");
return fallas == 0 ? 0 : 1;

/// <summary>El proveedor falso más C-14, una copia de C-12: dos columnas de la misma estructura (#63).</summary>
sealed class ConCopiaDeC12 : IProveedorContexto
{
    private readonly ProveedorContextoFalso _falso = new();

    public ColumnaContexto Leer(string idColumna)
    {
        if (idColumna != "C-14") return _falso.Leer(idColumna);
        ColumnaContexto c12 = _falso.Leer("C-12");
        return new ColumnaContexto
        {
            // 2.5 cm, como una columna mal cargada en Revit (decisión 15: igual arranca con 4 cm).
            Marca = "C-14", BaseX = c12.BaseX, BaseY = c12.BaseY, Recubrimiento = 0.025,
            ZInferior = c12.ZInferior, ZInferiorLongitudinal = c12.ZInferiorLongitudinal, ZSuperior = c12.ZSuperior,
            Tramos = c12.Tramos, Niveles = c12.Niveles,
        };
    }

    public IReadOnlyList<ColumnaResumen> ListarColumnas() =>
        _falso.ListarColumnas()
            // Dos pisos, como en Revit: el elemento de abajo es el Id (hallazgo 12).
            .Append(new ColumnaResumen("C-14", "C-14", false, 10, 0, "C-1", 0.40, 0.40, new[] { "C-14", "C-14-piso2" }))
            .ToList();

    public IReadOnlyList<Rejilla> ListarRejillas() => _falso.ListarRejillas();

    public void InvalidarCache() { }
}

/// <summary>Las barras como las da <c>ProveedorTiposReal</c>: las «#N CQEING» primero y por número, después las del modelo.</summary>
sealed class ConCqeingPrimero : IProveedorTipos
{
    private readonly ProveedorTiposFalso _falso = new();

    public IReadOnlyList<TipoBarra> ListarBarras() =>
        new[] { 3, 4 }
            .Select(n => CuadroVarillas.Cuadro.Single(v => v.Numero == n))
            .Select(v => new TipoBarra(CuadroVarillas.NombrePropio(v.Numero), v.DiametroMm)
            {
                DobladoEstriboMm = 4 * v.DiametroMm,
                DobladoGanchoMm = 6 * v.DiametroMm,
            })
            .Concat(_falso.ListarBarras())
            .ToList();

    public IReadOnlyList<TipoGancho> ListarGanchos() => _falso.ListarGanchos();
}
