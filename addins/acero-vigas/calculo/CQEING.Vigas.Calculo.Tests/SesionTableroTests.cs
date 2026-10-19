using System.Text.Json;
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

    private SesionTablero ConUbicacion(IReadOnlyList<double>? nominales = null)
    {
        lectura = new([PlanificadorAceroTests.Portico()], []);
        var s = new SesionTablero(() => lectura, new AlmacenReglas(Path.Combine(carpeta, "reglas.json")), "Proyecto de prueba",
            nominales is null ? null : () => nominales);
        s.Manejar(new Listo());
        return s;
    }

    [Fact]
    public void GenerarUsaLasOpcionesGuardadasDelPortico()
    {
        SesionTablero s = ConUbicacion();
        s.Manejar(new Calcular("Pórtico 1", OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(4, 5)] }));

        AGenerar g = Assert.Single(s.Manejar(new Generar(["Pórtico 1"])).OfType<AGenerar>());

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

        Assert.Null(Assert.Single(s.Manejar(new Generar(["Pórtico 1"])).OfType<APrevia3d>()).Portico);
        Assert.Empty(s.Manejar(new Calcular("Pórtico 1", OpcionesTablero.PorDefecto)).OfType<APrevia3d>());
    }

    [Fact]
    public void SinAbrirEl3dNoHayVistaPrevia()
    {
        SesionTablero s = ConUbicacion();

        Assert.Empty(s.Manejar(new Calcular("Pórtico 1", OpcionesTablero.PorDefecto)).OfType<APrevia3d>());
        Assert.Empty(s.Manejar(new Generar(["Pórtico 1"])).OfType<APrevia3d>());
    }

    [Fact]
    public void GenerarSaltaLosQueNoSePuedenGenerar()
    {
        SesionTablero s = ConUbicacion();
        s.Manejar(new Calcular("Pórtico 1", OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(30, 8)] }));

        AGenerar g = Assert.Single(s.Manejar(new Generar(["Pórtico 1", "Pórtico 9"])).OfType<AGenerar>());

        Assert.Empty(g.Porticos);
        Assert.Equal(["Pórtico 1: no se puede generar; revisa los avisos del tablero.", "Pórtico 9: ya no está en el modelo."], g.Saltados);
    }

    [Fact]
    public void GenerarSaltaElPorticoConUnDiametroSinTipoEnElModelo()
    {
        SesionTablero s = ConUbicacion([9.525, 12.7]);

        AGenerar g = Assert.Single(s.Manejar(new Generar(["Pórtico 1"])).OfType<AGenerar>());

        Assert.Empty(g.Porticos);
        Assert.Equal(["Pórtico 1: el modelo no tiene un tipo de barra para el #5."], g.Saltados);
    }

    [Fact]
    public void LaVistaTraeLosDiametrosDelModelo()
    {
        JsonElement v = VistaDe([ConUbicacion([9.525, 12.7, 15.875, 16]).Vista()]);

        Assert.Equal([3, 4, 5], v.GetProperty("diametrosDisponibles").EnumerateArray().Select(x => x.GetInt32()));
    }

    [Fact]
    public void SinLectorDeDiametros_SonLosDeLasReglas()
    {
        JsonElement v = VistaDe([ConUbicacion().Vista()]);

        Assert.Equal(Reglas.Generales.PorDiametro.Keys.Order(), v.GetProperty("diametrosDisponibles").EnumerateArray().Select(x => x.GetInt32()));
    }
}
