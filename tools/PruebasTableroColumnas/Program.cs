using System.Text.Json;
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

Console.WriteLine(fallas == 0 ? "Todas las pruebas pasaron." : $"{fallas} prueba(s) fallaron.");
return fallas == 0 ? 0 : 1;
