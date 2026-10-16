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
    public void SinPorticos_LaVistaVaVacia()
    {
        lectura = new ResultadoLectura([], []);

        JsonElement v = VistaDe(Sesion().Manejar(new Listo()));

        Assert.Equal(0, v.GetProperty("porticos").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, v.GetProperty("actual").ValueKind);
        Assert.Equal("Proyecto de prueba", v.GetProperty("proyecto").GetString());
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
}
