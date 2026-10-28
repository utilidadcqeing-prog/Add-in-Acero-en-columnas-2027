using System.Text.Json;
using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>Lo que hace C# con cada mensaje de la página (#83), sin Revit.</summary>
public sealed class SesionTableroTests : IDisposable
{
    private readonly string carpeta = Path.Combine(Path.GetTempPath(), "cqeing-sesion-" + Guid.NewGuid().ToString("N"));
    private ResultadoLectura lectura = new([ArmadorVistaTests.Portico1()], []);

    private SesionTablero Sesion() => new(() => lectura, new AlmacenReglas(Path.Combine(carpeta, "reglas.json")), "Proyecto de prueba");

    public void Dispose()
    {
        if (Directory.Exists(carpeta))
            Directory.Delete(carpeta, recursive: true);
    }

    /// <summary>La vista del único mensaje a la página que trae <paramref name="salidas"/>.</summary>
    private static JsonElement VistaDe(IReadOnlyList<Salida> salidas)
    {
        AJs js = Assert.Single(salidas.OfType<AJs>());
        JsonElement raiz = JsonDocument.Parse(js.Json).RootElement;
        Assert.Equal("vista", raiz.GetProperty("tipo").GetString());
        return raiz.GetProperty("vista");
    }

    [Fact]
    public void LaPreviaDevuelveElNumeroDelPedido()
    {
        SesionTablero s = Sesion();
        s.Manejar(new Listo());
        string antes = s.Vista().Json;

        AJs js = Assert.Single(s.Manejar(new Previsualizar("Pórtico 1", OpcionesTablero.PorDefecto with { MaximoFilas = 2 },
            new PedidoEdicion(7, "S1", AccionEdicion.Barra))).OfType<AJs>());

        JsonElement raiz = JsonDocument.Parse(js.Json).RootElement;
        Assert.Equal("previa", raiz.GetProperty("tipo").GetString());
        Assert.Equal(7, raiz.GetProperty("previa").GetProperty("n").GetInt32());
        // la vista previa no guarda las opciones que trae
        Assert.Equal(antes, s.Vista().Json);
    }

    [Fact]
    public void LaPreviaDeUnPorticoQueNoEstaNoContesta()
    {
        SesionTablero s = Sesion();
        s.Manejar(new Listo());

        Assert.Empty(s.Manejar(new Previsualizar("Otro", OpcionesTablero.PorDefecto, new PedidoEdicion(1, "S1", AccionEdicion.Barra))));
    }

    [Fact]
    public void SinPorticos_LaVistaVaVacia()
    {
        lectura = new ResultadoLectura([], []);

        JsonElement v = VistaDe(Sesion().Manejar(new Listo()));

        Assert.Equal(0, v.GetProperty("porticos").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, v.GetProperty("actual").ValueKind);
        Assert.Equal("Proyecto de prueba", v.GetProperty("proyecto").GetString());
    }

    /// <summary>
    /// El mismo eje se repite en cada nivel ("Pórtico 1" de Piso 1, Piso 2 y Cubierta): cada uno tiene su id y su nivel
    /// en la lista, y elegir uno no abre los otros (Juan, 16-oct: "los datos salen duplicados").
    /// </summary>
    [Fact]
    public void ElMismoEjeEnVariosNivelesTieneUnIdPorNivel()
    {
        PorticoLeido piso2 = ArmadorVistaTests.Portico1() with { Nivel = "Piso 2", Vigas = ["201", "202", "203", "204"] };
        PorticoLeido piso3 = ArmadorVistaTests.Portico1() with { Nivel = "Cubierta", Vigas = ["301", "302", "303", "304"] };
        PorticoLeido otro = ArmadorVistaTests.Portico1() with { Nombre = "Pórtico 2", Vigas = ["401"] };
        lectura = new ResultadoLectura([piso2, piso3, otro], []);
        SesionTablero s = Sesion();
        s.Manejar(new Listo());

        JsonElement v = VistaDe(s.Manejar(new ElegirPortico("Pórtico 1 · Cubierta")));

        JsonElement lista = v.GetProperty("porticos");
        Assert.Equal(["Pórtico 1 · Piso 2", "Pórtico 1 · Cubierta", "Pórtico 2"], lista.EnumerateArray().Select(p => p.GetProperty("id").GetString()));
        Assert.Equal(["Pórtico 1", "Pórtico 1", "Pórtico 2"], lista.EnumerateArray().Select(p => p.GetProperty("nombre").GetString()));
        Assert.EndsWith(" · Cubierta", lista[1].GetProperty("sub").GetString());
        Assert.DoesNotContain("Nivel", lista[2].GetProperty("sub").GetString());
        Assert.Equal("Pórtico 1 · Cubierta", v.GetProperty("actual").GetProperty("id").GetString());
        Assert.Equal("Pórtico 1 · Piso 2", s.PorticoDeViga("202"));
        ARevit a = Assert.Single(s.Manejar(new VerEnRevit("Pórtico 1 · Cubierta")).OfType<ARevit>());
        Assert.Equal(["301", "302", "303", "304"], a.Vigas);
    }

    /// <summary>
    /// "Seleccionar en Revit" con la barra del add-in (Juan, 16-oct): con vigas de varios pórticos, la lista muestra solo
    /// esos y abre el primero; "Ver todos" vuelve a la lista completa.
    /// </summary>
    [Fact]
    public void ElegirVigasDeVariosPorticosListaSoloEsos()
    {
        PorticoLeido piso2 = ArmadorVistaTests.Portico1() with { Nivel = "Piso 2", Vigas = ["201", "202"] };
        PorticoLeido piso3 = ArmadorVistaTests.Portico1() with { Nivel = "Cubierta", Vigas = ["301"] };
        PorticoLeido otro = ArmadorVistaTests.Portico1() with { Nombre = "Pórtico 2", Vigas = ["401"] };
        lectura = new ResultadoLectura([piso2, piso3, otro], []);
        SesionTablero s = Sesion();
        s.Manejar(new Listo());

        JsonElement v = VistaDe(s.ElegirVigas(["401", "999", "202"]));

        Assert.Equal(["Pórtico 1 · Piso 2", "Pórtico 2"], v.GetProperty("elegidos").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("Pórtico 1 · Piso 2", v.GetProperty("actual").GetProperty("id").GetString());
        Assert.Equal(["201", "202", "301", "401"], s.Vigas);

        JsonElement todos = VistaDe(s.Manejar(new VerTodos()));
        Assert.Equal(JsonValueKind.Null, todos.GetProperty("elegidos").ValueKind);
    }

    /// <summary>Ctrl+clic en la planta: con dos o más quedan como elegidos; con uno solo, la lista vuelve a ser completa.</summary>
    [Fact]
    public void ElegirVariosEnLaPlantaFiltraYAbreElPedido()
    {
        PorticoLeido otro = ArmadorVistaTests.Portico1() with { Nombre = "Pórtico 2", Vigas = ["401"] };
        lectura = new ResultadoLectura([ArmadorVistaTests.Portico1(), otro], []);
        SesionTablero s = Sesion();
        s.Manejar(new Listo());

        IReadOnlyList<Salida> salidas = s.Manejar(new ElegirVarios(["Pórtico 1", "Pórtico 2", "No existe"], "Pórtico 2"));
        JsonElement v = VistaDe(salidas);

        Assert.Equal(["Pórtico 1", "Pórtico 2"], v.GetProperty("elegidos").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("Pórtico 2", s.Actual);
        Assert.Equal(["101", "102", "103", "104", "401"], Assert.Single(salidas.OfType<ARevit>()).Vigas);

        JsonElement uno = VistaDe(s.Manejar(new ElegirVarios(["Pórtico 1"], "Pórtico 1")));
        Assert.Equal(JsonValueKind.Null, uno.GetProperty("elegidos").ValueKind);
        Assert.Equal("Pórtico 1", s.Actual);
    }

    [Fact]
    public void ElegirVigasFueraDeLosPorticosAvisaYNoCambia()
    {
        SesionTablero s = Sesion();
        s.Manejar(new Listo());

        AJs js = Assert.Single(s.ElegirVigas(["999"]).OfType<AJs>());

        Assert.Contains("\"tipo\":\"aviso\"", js.Json);
        Assert.Equal("Pórtico 1", s.Actual);
    }

    /// <summary>Abrir un pórtico que no está entre los elegidos (por ejemplo, En vivo) vuelve a la lista completa.</summary>
    [Fact]
    public void AbrirOtroPorticoQuitaElFiltro()
    {
        PorticoLeido otro = ArmadorVistaTests.Portico1() with { Nombre = "Pórtico 2", Vigas = ["401"] };
        lectura = new ResultadoLectura([ArmadorVistaTests.Portico1(), otro], []);
        SesionTablero s = Sesion();
        s.Manejar(new Listo());
        s.ElegirVigas(["101"]);

        JsonElement v = VistaDe(s.Manejar(new ElegirPortico("Pórtico 2")));

        Assert.Equal(JsonValueKind.Null, v.GetProperty("elegidos").ValueKind);
    }

    /// <summary>La maqueta abre con "En vivo con Revit" encendido.</summary>
    [Fact]
    public void ArrancaEnVivoComoLaMaqueta()
    {
        SesionTablero s = Sesion();

        JsonElement v = VistaDe(s.Manejar(new Listo()));

        Assert.True(s.EnVivo);
        Assert.True(v.GetProperty("enVivo").GetBoolean());
    }

    /// <summary>Para "En vivo" y para cambiar o cerrar el documento en Revit (tarea 5 del plan).</summary>
    [Fact]
    public void RecargarOtroDocumentoDaSuVista()
    {
        SesionTablero s = Sesion();
        s.Manejar(new Listo());
        s.Manejar(new CambiarEnVivo(true));
        Assert.True(s.EnVivo);
        Assert.Equal("Pórtico 1", s.Actual);

        lectura = new ResultadoLectura([], []);
        s.Recargar("Otro proyecto");
        JsonElement v = VistaDe([s.Vista()]);

        Assert.Null(s.Actual);
        Assert.Equal(JsonValueKind.Null, v.GetProperty("actual").ValueKind);
        Assert.Equal("Otro proyecto", v.GetProperty("proyecto").GetString());
    }

    [Fact]
    public void ListoAbreElPrimerPortico()
    {
        JsonElement v = VistaDe(Sesion().Manejar(new Listo()));

        Assert.Equal("Pórtico 1", v.GetProperty("actual").GetProperty("id").GetString());
        Assert.Equal("0.55", v.GetProperty("reglas").GetProperty("porDiametro")[0].GetProperty("t").GetString());
    }

    /// <summary>
    /// #942: una vigueta de 10 cm con las de fábrica de una viga (3 #5, recubrimiento 4) abría con "no caben"; arranca
    /// con 1 #4 arriba y abajo y recubrimiento 3, como la Vigueta T1 del plano de referencia.
    /// </summary>
    [Fact]
    public void UnaViguetaArrancaConUnaBarraYRecubrimiento3()
    {
        PorticoLeido p = ArmadorVistaTests.Portico1();
        lectura = new ResultadoLectura([p with { Elemento = Elemento.Vigueta, Tramos = p.Tramos.Select(t => t with { Ancho = 100, Alto = 350 }).ToList() }], []);

        JsonElement actual = VistaDe(Sesion().Manejar(new Listo())).GetProperty("actual");

        Assert.Equal("Ninguno", actual.GetProperty("superior").GetProperty("error").GetString());
        Assert.Equal([1], actual.GetProperty("superior").GetProperty("porFila").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal([1], actual.GetProperty("inferior").GetProperty("porFila").EnumerateArray().Select(x => x.GetInt32()));
        Assert.True(actual.GetProperty("sePuedeGenerar").GetBoolean());
    }

    [Fact]
    public void LasOpcionesDeArranque_DeVigaSonLasDeSiempre()
    {
        Assert.Same(OpcionesTablero.PorDefecto, OpcionesTablero.Para(Elemento.Viga));
        OpcionesTablero v = OpcionesTablero.Para(Elemento.Vigueta);
        Assert.Equal([new GrupoBarras(1, 4)], v.Superior);
        Assert.Equal([new GrupoBarras(1, 4)], v.Inferior);
        Assert.Equal(3, v.RecubrimientoCm);
    }

    [Fact]
    public void CalcularGuardaLasOpcionesDelPortico()
    {
        PorticoLeido otro = ArmadorVistaTests.Portico1() with { Nombre = "Pórtico 2" };
        lectura = new ResultadoLectura([ArmadorVistaTests.Portico1(), otro], []);
        SesionTablero s = Sesion();
        s.Manejar(new Listo());

        s.Manejar(new Calcular("Pórtico 1", OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(5, 5)] }));
        s.Manejar(new ElegirPortico("Pórtico 2"));
        JsonElement v = VistaDe(s.Manejar(new ElegirPortico("Pórtico 1")));

        Assert.Equal(5, v.GetProperty("actual").GetProperty("superior").GetProperty("porFila")[0].GetInt32());
    }

    [Fact]
    public void ElegirPorticoConEnVivoLoSeleccionaEnRevit()
    {
        SesionTablero s = Sesion();
        s.Manejar(new Listo());
        s.Manejar(new CambiarEnVivo(false));

        Assert.Empty(s.Manejar(new ElegirPortico("Pórtico 1")).OfType<ARevit>());
        s.Manejar(new CambiarEnVivo(true));
        ARevit a = Assert.Single(s.Manejar(new ElegirPortico("Pórtico 1")).OfType<ARevit>());

        Assert.Equal(AccionRevit.Seleccionar, a.Accion);
        Assert.Equal(["101", "102", "103", "104"], a.Vigas);
    }

    [Fact]
    public void LosBotonesDeRevitPidenSuAccion()
    {
        SesionTablero s = Sesion();
        s.Manejar(new Listo());

        Assert.Equal(AccionRevit.Mostrar, Assert.Single(s.Manejar(new VerEnRevit("Pórtico 1")).OfType<ARevit>()).Accion);
        Assert.Equal(AccionRevit.Abrir3d, Assert.Single(s.Manejar(new Abrir3d("Pórtico 1")).OfType<ARevit>()).Accion);
        Assert.Equal(AccionRevit.PedirSeleccion, Assert.Single(s.Manejar(new SeleccionarEnRevit()).OfType<ARevit>()).Accion);
        Assert.Empty(s.Manejar(new VerEnRevit("No existe")));
    }

    [Fact]
    public void GuardarReglasEscribeElArchivoYRecalcula()
    {
        SesionTablero s = Sesion();
        s.Manejar(new Listo());
        VistaReglas v = ArmadorVista.Reglas(new ReglasCargadas(Reglas.Generales, null)) with { Varilla12 = true };

        JsonElement vista = VistaDe(s.Manejar(new GuardarReglas(v)));

        Assert.True(File.Exists(Path.Combine(carpeta, "reglas.json")));
        Assert.True(vista.GetProperty("reglas").GetProperty("varilla12").GetBoolean());
    }

    [Fact]
    public void GuardarReglasInvalidasAvisa()
    {
        SesionTablero s = Sesion();
        s.Manejar(new Listo());
        VistaReglas v = ArmadorVista.Reglas(new ReglasCargadas(Reglas.Generales, null)) with { Multiplo = "0" };

        AJs aviso = Assert.Single(s.Manejar(new GuardarReglas(v)).OfType<AJs>());

        Assert.Equal(ProtocoloTablero.Aviso("El múltiplo de corte debe ser mayor que 0."), aviso.Json);
        Assert.False(File.Exists(Path.Combine(carpeta, "reglas.json")));
    }

    [Fact]
    public void RecargarConservaLasOpcionesPorNombre()
    {
        SesionTablero s = Sesion();
        s.Manejar(new Listo());
        s.Manejar(new Calcular("Pórtico 1", OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(5, 5)] }));

        s.Recargar();
        JsonElement v = VistaDe(s.Manejar(new ElegirPortico("Pórtico 1")));

        Assert.Equal(5, v.GetProperty("actual").GetProperty("superior").GetProperty("porFila")[0].GetInt32());
    }

    [Fact]
    public void RecargarSinElPorticoAbiertoAbreElPrimero()
    {
        SesionTablero s = Sesion();
        s.Manejar(new Listo());
        lectura = new ResultadoLectura([], []);

        s.Recargar();
        JsonElement v = VistaDe(s.Manejar(new CambiarEnVivo(false)));

        Assert.Equal(JsonValueKind.Null, v.GetProperty("actual").ValueKind);
    }

    [Fact]
    public void ElPorticoDeUnaVigaSaleDeLaLectura()
    {
        SesionTablero s = Sesion();
        s.Manejar(new Listo());

        Assert.Equal(("Pórtico 1", (string?)null), (s.PorticoDeViga("103"), s.PorticoDeViga("999")));
    }

    [Fact]
    public void UnMensajeDesconocidoNoHaceNada()
    {
        Assert.Empty(Sesion().Manejar(new Desconocido("otro")));
    }

    private SesionTablero ConUbicacion(IReadOnlyList<double>? nominales = null, Func<string, string?>? guardado = null,
        Func<Reglas, IReadOnlyDictionary<int, DobladoBarra>>? doblados = null)
    {
        lectura = new([PlanificadorAceroTests.Portico()], []);
        var s = new SesionTablero(() => lectura, new AlmacenReglas(Path.Combine(carpeta, "reglas.json")), "Proyecto de prueba",
            nominales is null ? null : () => nominales, guardado, doblados);
        s.Manejar(new Listo());
        return s;
    }

    // Choques entre barras (plan docs/planes/2026-10-22-choques-entre-barras-vigas.md, paso 3).

    private static List<string> TextosDeChoques(JsonElement actual) =>
        actual.GetProperty("choques").EnumerateArray().Select(c => c.GetProperty("texto").GetString()!).ToList();

    [Fact]
    public void LaVistaTraeLosChoquesDelPorticoAbiertoSinAviso()
    {
        // solo los puntos del alzado: el aviso en la lista sobraba (Juan, 22-oct)
        JsonElement actual = VistaDe([ConUbicacion().Vista()]).GetProperty("actual");

        JsonElement primero = actual.GetProperty("choques")[0];
        Assert.Equal(7, actual.GetProperty("choques").GetArrayLength());
        Assert.Equal(("BS1", 8), (primero.GetProperty("elemento").GetString(), primero.GetProperty("veces").GetInt32()));
        Assert.DoesNotContain(actual.GetProperty("avisos").EnumerateArray(), a => a.GetProperty("texto").GetString()!.Contains("choque"));
    }

    [Fact]
    public void SinBastonesLaMaquetaNoTieneChoques()
    {
        SesionTablero s = ConUbicacion();
        OpcionesTablero sinBastones = OpcionesTablero.PorDefecto with { Bastones = new ArmadoBastones(new GrupoBarras(0, 5), new GrupoBarras(0, 5)) };

        JsonElement actual = VistaDe(s.Manejar(new Calcular("Pórtico 1", sinBastones))).GetProperty("actual");

        Assert.Empty(TextosDeChoques(actual));
    }

    [Fact]
    public void SinUbicacionEnElModeloNoSeRevisanLosChoques()
    {
        SesionTablero s = Sesion();

        JsonElement actual = VistaDe(s.Manejar(new Listo())).GetProperty("actual");

        Assert.Equal(JsonValueKind.Null, actual.GetProperty("choques").ValueKind);
    }

    [Fact]
    public void LosArreglosDeUnChoqueVuelvenConSuNumeroYNoGuardanLasOpciones()
    {
        SesionTablero s = ConUbicacion();
        string antes = s.Vista().Json;

        AJs js = Assert.Single(s.Manejar(new PedirArreglos("Pórtico 1", OpcionesTablero.PorDefecto, 4, 1)).OfType<AJs>());

        JsonElement raiz = JsonDocument.Parse(js.Json).RootElement;
        Assert.Equal("arreglos", raiz.GetProperty("tipo").GetString());
        JsonElement arreglos = raiz.GetProperty("arreglos");
        Assert.Equal((4, 1), (arreglos.GetProperty("n").GetInt32(), arreglos.GetProperty("choque").GetInt32()));
        JsonElement acortar = arreglos.GetProperty("arreglos")[2];
        Assert.Equal("Acortar BS2: 2.00 → 1.00 m", acortar.GetProperty("texto").GetString());
        Assert.Equal("puntas", acortar.GetProperty("cambio").GetProperty("saltos")[0].GetProperty("tipo").GetString());
        Assert.Equal(antes, s.Vista().Json);
    }

    [Fact]
    public void LosArreglosDeUnPorticoQueNoEstaNoContestan()
    {
        Assert.Empty(ConUbicacion().Manejar(new PedirArreglos("Otro", OpcionesTablero.PorDefecto, 1, 0)));
    }

    // Avisar al generar (decisión 8 del plan de choques, paso 7).

    private static readonly OpcionesTablero SinBastones =
        OpcionesTablero.PorDefecto with { Bastones = new ArmadoBastones(new GrupoBarras(0, 5), new GrupoBarras(0, 5)) };

    [Fact]
    public void ConChoquesYSinForzarNoGeneraYContestaLaLista()
    {
        // la maqueta por defecto tiene los 7 choques de H6
        SesionTablero s = ConUbicacion();

        IReadOnlyList<Salida> salidas = s.Manejar(new Generar(["Pórtico 1"]));

        Assert.Empty(salidas.OfType<AGenerar>());
        JsonElement raiz = JsonDocument.Parse(Assert.Single(salidas.OfType<AJs>()).Json).RootElement;
        Assert.Equal("choques", raiz.GetProperty("tipo").GetString());
        JsonElement portico = Assert.Single(raiz.GetProperty("porticos").EnumerateArray());
        Assert.Equal(("Pórtico 1", 7), (portico.GetProperty("id").GetString(), portico.GetProperty("cantidad").GetInt32()));
    }

    [Fact]
    public void ConChoquesYForzandoGenera()
    {
        SesionTablero s = ConUbicacion();

        AGenerar g = Assert.Single(s.Manejar(new Generar(["Pórtico 1"], Forzar: true)).OfType<AGenerar>());

        Assert.Equal("Pórtico 1", Assert.Single(g.Porticos).Id);
    }

    [Fact]
    public void SinChoquesGeneraDirecto()
    {
        SesionTablero s = ConUbicacion();
        s.Manejar(new Calcular("Pórtico 1", SinBastones));

        IReadOnlyList<Salida> salidas = s.Manejar(new Generar(["Pórtico 1"]));

        Assert.Single(salidas.OfType<AGenerar>());
        Assert.Empty(salidas.OfType<AJs>());
    }

    [Fact]
    public void ConChoquesLaVistaPrevia3dNoSeQuita()
    {
        // no se generó nada: el 3D sigue mostrando la vista previa
        SesionTablero s = ConUbicacion();
        s.Manejar(new Abrir3d("Pórtico 1"));

        Assert.Empty(s.Manejar(new Generar(["Pórtico 1"])).OfType<APrevia3d>());
    }

    [Fact]
    public void LosChoquesUsanLosDobladosDelModelo()
    {
        // los de la vista previa 3D (H3): con el estribo doblado a 6 d la curva de la esquina cambia y el choque también
        Reglas? recibidas = null;
        SesionTablero s = ConUbicacion(doblados: r =>
        {
            recibidas = r;
            return new Dictionary<int, DobladoBarra> { [3] = new(9.5, 57, 57) };
        });

        List<string> conModelo = TextosDeChoques(VistaDe([s.Vista()]).GetProperty("actual"));

        Assert.NotNull(recibidas);
        Assert.NotEqual(TextosDeChoques(VistaDe([ConUbicacion().Vista()]).GetProperty("actual")), conModelo);
    }

    // Recordar el pórtico generado (plan docs/planes/2026-10-22-recordar-portico-generado-vigas.md).
    private const string EstadoPagina = """{"sup":[{"n":4,"d":"#5"}]}""";

    private static readonly OpcionesTablero CuatroArriba = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(4, 5)] };

    /// <summary>Solo la viga 102 del pórtico tiene algo guardado.</summary>
    private static string? GuardadoEnLa102(string viga) => viga == "102" ? ConfiguracionGuardada.Escribir(CuatroArriba, EstadoPagina) : null;

    private static int BarrasS1(SesionTablero s, IReadOnlyDictionary<string, string>? estados = null) =>
        Assert.Single(Assert.Single(s.Manejar(new Generar(["Pórtico 1"], estados, Forzar: true)).OfType<AGenerar>()).Porticos).Plan.Barras.Count(b => b.Pieza == "S1");

    [Fact]
    public void LoGuardadoEnLasVigasSeRecuperaAlLeerElModelo()
    {
        SesionTablero s = ConUbicacion(guardado: GuardadoEnLa102);

        Assert.Equal(EstadoPagina, VistaDe([s.Vista()]).GetProperty("actual").GetProperty("estado").GetString());
        Assert.Equal(4, BarrasS1(s));
    }

    [Fact]
    public void SinNadaGuardadoArrancaConLosValoresPorDefecto()
    {
        SesionTablero s = ConUbicacion(guardado: _ => null);

        Assert.Equal(JsonValueKind.Null, VistaDe([s.Vista()]).GetProperty("actual").GetProperty("estado").ValueKind);
        Assert.Equal(OpcionesTablero.PorDefecto.Superior[0].Cantidad, BarrasS1(s));
    }

    [Fact]
    public void LoCambiadoEnLaSesionMandaSobreLoGuardado()
    {
        SesionTablero s = ConUbicacion(guardado: GuardadoEnLa102);
        s.Manejar(new Calcular("Pórtico 1", OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(2, 5)] }));

        s.Recargar();

        Assert.Equal(2, BarrasS1(s));
    }

    [Fact]
    public void GenerarLlevaLaConfiguracionConElEstadoQueMandaLaPagina()
    {
        SesionTablero s = ConUbicacion(guardado: _ => null);
        s.Manejar(new Calcular("Pórtico 1", CuatroArriba));

        AGenerar g = Assert.Single(s.Manejar(new Generar(["Pórtico 1"], new Dictionary<string, string> { ["Pórtico 1"] = EstadoPagina }, Forzar: true)).OfType<AGenerar>());

        (OpcionesTablero o, string? estado) = ConfiguracionGuardada.Leer(Assert.Single(g.Porticos).Configuracion)!.Value;
        Assert.Equal((4, EstadoPagina), (o.Superior[0].Cantidad, estado));
        Assert.Equal(EstadoPagina, VistaDe([s.Vista()]).GetProperty("actual").GetProperty("estado").GetString());
    }

    [Fact]
    public void SinEstadoDeLaPagina_GenerarLlevaElRecuperado()
    {
        SesionTablero s = ConUbicacion(guardado: GuardadoEnLa102);

        AGenerar g = Assert.Single(s.Manejar(new Generar(["Pórtico 1"], Forzar: true)).OfType<AGenerar>());

        Assert.Equal(EstadoPagina, ConfiguracionGuardada.Leer(Assert.Single(g.Porticos).Configuracion)!.Value.Estado);
    }

    [Fact]
    public void GenerarUsaLasOpcionesGuardadasDelPortico()
    {
        SesionTablero s = ConUbicacion();
        s.Manejar(new Calcular("Pórtico 1", OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(4, 5)] }));

        AGenerar g = Assert.Single(s.Manejar(new Generar(["Pórtico 1"], Forzar: true)).OfType<AGenerar>());

        PorticoAGenerar p = Assert.Single(g.Porticos);
        Assert.Equal(("Pórtico 1", 4), (p.Id, p.Plan.Barras.Count(b => b.Pieza == "S1")));
        Assert.Equal(["101", "102", "103", "104"], p.Vigas);
        Assert.Empty(g.Saltados);
    }

    [Fact]
    public void El3dPideLaVistaPreviaDelAceroDelPortico()
    {
        SesionTablero s = ConUbicacion();
        s.Manejar(new Calcular("Pórtico 1", OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(4, 5)] }));

        IReadOnlyList<Salida> salidas = s.Manejar(new Abrir3d("Pórtico 1"));

        Assert.IsType<ARevit>(salidas[0]);
        PorticoAGenerar p = Assert.Single(salidas.OfType<APrevia3d>()).Portico!;
        Assert.Equal(("Pórtico 1", 4), (p.Id, p.Plan.Barras.Count(b => b.Pieza == "S1")));
    }

    [Fact]
    public void LaVistaPreviaSigueLosCambiosDeSuPorticoYSeBorraAlGenerar()
    {
        SesionTablero s = ConUbicacion();
        s.Manejar(new Abrir3d("Pórtico 1"));

        APrevia3d cambio = Assert.Single(s.Manejar(new Calcular("Pórtico 1", OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(4, 5)] })).OfType<APrevia3d>());
        Assert.Equal(4, cambio.Portico!.Plan.Barras.Count(b => b.Pieza == "S1"));

        Assert.Null(Assert.Single(s.Manejar(new Generar(["Pórtico 1"], Forzar: true)).OfType<APrevia3d>()).Portico);
        Assert.Empty(s.Manejar(new Calcular("Pórtico 1", OpcionesTablero.PorDefecto)).OfType<APrevia3d>());
    }

    [Fact]
    public void SinAbrirEl3dNoHayVistaPrevia()
    {
        SesionTablero s = ConUbicacion();

        Assert.Empty(s.Manejar(new Calcular("Pórtico 1", OpcionesTablero.PorDefecto)).OfType<APrevia3d>());
        Assert.Empty(s.Manejar(new Generar(["Pórtico 1"], Forzar: true)).OfType<APrevia3d>());
    }

    [Fact]
    public void GenerarSaltaLosQueNoSePuedenGenerar()
    {
        SesionTablero s = ConUbicacion();
        s.Manejar(new Calcular("Pórtico 1", OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(30, 8)] }));

        AGenerar g = Assert.Single(s.Manejar(new Generar(["Pórtico 1", "Pórtico 9"], Forzar: true)).OfType<AGenerar>());

        Assert.Empty(g.Porticos);
        Assert.Equal(["Pórtico 1: no se puede generar; revisa los avisos del tablero.", "Pórtico 9: ya no está en el modelo."], g.Saltados);
    }

    [Fact]
    public void GenerarSaltaElPorticoConUnDiametroSinTipoEnElModelo()
    {
        SesionTablero s = ConUbicacion([9.525, 12.7]);

        AGenerar g = Assert.Single(s.Manejar(new Generar(["Pórtico 1"], Forzar: true)).OfType<AGenerar>());

        Assert.Empty(g.Porticos);
        Assert.Equal(["Pórtico 1: el modelo no tiene un tipo de barra para el #5."], g.Saltados);
    }

    [Fact]
    public void LaVistaTraeLosDiametrosDelModelo()
    {
        JsonElement v = VistaDe([ConUbicacion([9.525, 12.7, 15.875, 16]).Vista()]);

        Assert.Equal([3, 4, 5], v.GetProperty("diametrosDisponibles").EnumerateArray().Select(x => x.GetInt32()));
    }

    // Sistema de viguetas (planta, 27-oct): al tomar una se toman todas las iguales, con el mismo acero.

    /// <summary>Tres viguetas iguales del Nivel 2 (en y = 0, 1 y 2 m) y la viga del eje 1, todas con ubicación.</summary>
    private SesionTablero ConViguetas()
    {
        PorticoLeido viga = PlanificadorAceroTests.Portico();
        PorticoLeido Vigueta(int n) => viga with
        {
            Nombre = $"Vigueta {n}",
            Elemento = Elemento.Vigueta,
            Vigas = [$"v{n}"],
            Ubicacion = viga.Ubicacion! with { OrigenY = n * 1000 },
        };
        lectura = new([Vigueta(1), Vigueta(2), Vigueta(3), viga with { Ubicacion = viga.Ubicacion! with { OrigenY = -3000 } }], []);
        SesionTablero s = Sesion();
        s.Manejar(new Listo());
        return s;
    }

    [Fact]
    public void LaVistaTraeLaPlantaYElSistemaDeCadaVigueta()
    {
        JsonElement v = VistaDe([ConViguetas().Vista()]);

        Assert.Equal(["30×50 · 1", "30×50 · 1", "30×50 · 1", ""], v.GetProperty("porticos").EnumerateArray().Select(p => p.GetProperty("grupo").GetString()));
        JsonElement nivel = Assert.Single(v.GetProperty("planta").GetProperty("niveles").EnumerateArray());
        Assert.Equal("Nivel 2", nivel.GetProperty("nombre").GetString());
        Assert.Equal(["vigueta", "vigueta", "vigueta", "viga"], nivel.GetProperty("elementos").EnumerateArray().Select(e => e.GetProperty("tipo").GetString()));
    }

    [Fact]
    public void CalcularUnaViguetaLeDaElMismoAceroATodoElSistema()
    {
        SesionTablero s = ConViguetas();

        s.Manejar(new Calcular("Vigueta 2", OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(4, 5)] }));
        AGenerar g = Assert.Single(s.Manejar(new Generar(["Vigueta 1", "Pórtico 1"], Forzar: true)).OfType<AGenerar>());

        Assert.Equal(["Vigueta 1", "Vigueta 2", "Vigueta 3", "Pórtico 1"], g.Porticos.Select(p => p.Id));
        Assert.Equal([4, 4, 4, 3], g.Porticos.Select(p => p.Plan.Barras.Count(b => b.Pieza == "S1")));
    }

    [Fact]
    public void GenerarElSistemaSinRepetirNiPerderElOrden()
    {
        SesionTablero s = ConViguetas();

        AGenerar g = Assert.Single(s.Manejar(new Generar(["Pórtico 1", "Vigueta 3", "Vigueta 1"], Forzar: true)).OfType<AGenerar>());

        Assert.Equal(["Pórtico 1", "Vigueta 1", "Vigueta 2", "Vigueta 3"], g.Porticos.Select(p => p.Id));
    }

    [Fact]
    public void ElEstadoDeLaPaginaDeUnaViguetaPasaALasDelSistemaQueNoTraenUno()
    {
        SesionTablero s = ConViguetas();
        s.Manejar(new ElegirPortico("Vigueta 2"));
        const string propio = """{"sup":[{"n":2,"d":"#4"}]}""";

        AGenerar g = Assert.Single(s.Manejar(new Generar(["Vigueta 2"],
            new Dictionary<string, string> { ["Vigueta 2"] = EstadoPagina, ["Vigueta 3"] = propio }, Forzar: true)).OfType<AGenerar>());

        Assert.Equal([EstadoPagina, EstadoPagina, propio],
            g.Porticos.Select(p => ConfiguracionGuardada.Leer(p.Configuracion)!.Value.Estado));
    }

    // Pórtico terminado (punto 5 de Santiago, 28-oct): la palomita junto al nombre y el verde en la planta.

    private static List<string?> Terminados(SesionTablero s) =>
        VistaDe([s.Vista()]).GetProperty("terminados").EnumerateArray().Select(t => t.GetString()).ToList();

    [Fact]
    public void MarcarTerminadoLoGuardaEnSusVigasYLoMuestra()
    {
        SesionTablero s = ConUbicacion();

        IReadOnlyList<Salida> salidas = s.Manejar(new MarcarTerminado("Pórtico 1", true));

        ATerminado marca = Assert.Single(salidas.OfType<ATerminado>());
        Assert.True(marca.Terminado);
        Assert.Equal(PlanificadorAceroTests.Portico().Vigas, marca.Vigas);
        Assert.Equal(["Pórtico 1"], VistaDe(salidas).GetProperty("terminados").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public void DesmarcarTerminadoLoQuita()
    {
        SesionTablero s = ConUbicacion();
        s.Manejar(new MarcarTerminado("Pórtico 1", true));

        Assert.False(Assert.Single(s.Manejar(new MarcarTerminado("Pórtico 1", false)).OfType<ATerminado>()).Terminado);
        Assert.Empty(Terminados(s));
    }

    [Fact]
    public void MarcarUnPorticoQueNoEstaNoTocaRevit()
    {
        Assert.Empty(ConUbicacion().Manejar(new MarcarTerminado("Otro", true)).OfType<ATerminado>());
    }

    [Fact]
    public void MarcarUnaViguetaMarcaTodoSuSistema()
    {
        SesionTablero s = ConViguetas();

        ATerminado marca = Assert.Single(s.Manejar(new MarcarTerminado("Vigueta 2", true)).OfType<ATerminado>());

        Assert.Equal(["v1", "v2", "v3"], marca.Vigas);
        Assert.Equal(["Vigueta 1", "Vigueta 2", "Vigueta 3"], Terminados(s));
    }

    [Fact]
    public void LoMarcadoEnLasVigasSeLeeAlRecargar()
    {
        lectura = new([ArmadorVistaTests.Portico1()], []);
        var marcadas = new HashSet<string>();
        var s = new SesionTablero(() => lectura, new AlmacenReglas(Path.Combine(carpeta, "reglas.json")), "Proyecto de prueba",
            terminadaViga: marcadas.Contains);
        s.Manejar(new Listo());
        Assert.Empty(Terminados(s));

        marcadas.Add(ArmadorVistaTests.Portico1().Vigas[0]);
        s.Recargar();

        Assert.Equal(["Pórtico 1"], Terminados(s));
    }

    [Fact]
    public void SinLectorDeDiametros_SonLosDeLasReglas()
    {
        JsonElement v = VistaDe([ConUbicacion().Vista()]);

        Assert.Equal(Reglas.Generales.PorDiametro.Keys.Order(), v.GetProperty("diametrosDisponibles").EnumerateArray().Select(x => x.GetInt32()));
    }
}
