using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

public sealed class AlmacenReglasTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    private string Ruta => Path.Combine(_carpeta, "reglas.json");

    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    [Fact]
    public void SinArchivo_SeUsanLasReglasGenerales()
    {
        ReglasCargadas cargadas = new AlmacenReglas(Ruta).Cargar();

        Assert.True(cargadas.Reglas.Equivale(Reglas.Generales));
        Assert.Null(cargadas.Problema);
    }

    [Fact]
    public void LoQueSeGuardaSeVuelveALeer()
    {
        Reglas cambiadas = Reglas.Generales with { Modulo = 600, Varilla = 12000 };
        new AlmacenReglas(Ruta).Guardar(cambiadas);

        ReglasCargadas cargadas = new AlmacenReglas(Ruta).Cargar();

        Assert.True(cargadas.Reglas.Equivale(cambiadas));
        Assert.Null(cargadas.Problema);
    }

    [Fact]
    public void GuardarCreaLaCarpetaSiNoExiste()
    {
        string ruta = Path.Combine(_carpeta, "sub", "reglas.json");

        new AlmacenReglas(ruta).Guardar(Reglas.Generales);

        Assert.True(File.Exists(ruta));
    }

    [Fact]
    public void UnArchivoDañado_NoRevientaYAvisa()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(Ruta, "{ no es json");

        ReglasCargadas cargadas = new AlmacenReglas(Ruta).Cargar();

        Assert.True(cargadas.Reglas.Equivale(Reglas.Generales));
        Assert.Equal("No se pudieron leer las reglas guardadas; se usan las de base.", cargadas.Problema);
    }

    [Fact]
    public void UnArchivoSinUnDiametro_LoCompletaConLaBase()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(Ruta, """
            { "version": 1, "reglas": { "modulo": 600, "varilla": 6000, "varillaLarga": 12000, "distanciaLibre": 25,
              "porDiametro": { "5": { "traslapo": 1000, "gancho90": { "b": 278 }, "gancho180": { "b": 238 },
                "estribo135": { "b": 189 }, "kgPorMetro": 1.552, "diametro": 15.9 } } } }
            """);

        Reglas reglas = new AlmacenReglas(Ruta).Cargar().Reglas;

        Assert.Equal(600, reglas.Modulo);
        Assert.Equal(1000, reglas.PorDiametro[5].Traslapo);
        Assert.Equal(Reglas.Generales.PorDiametro[8], reglas.PorDiametro[8]);
    }

    [Fact]
    public void UnDiametroSinUnGancho_LoCompletaConLaBase()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(Ruta, """
            { "version": 1, "reglas": { "modulo": 500, "varilla": 6000, "varillaLarga": 12000, "distanciaLibre": 25,
              "porDiametro": { "5": { "traslapo": 1000, "gancho90": { "b": 278 }, "kgPorMetro": 1.552, "diametro": 15.9 } } } }
            """);

        ReglaDiametro numero5 = new AlmacenReglas(Ruta).Cargar().Reglas.PorDiametro[5];

        Assert.Equal(1000, numero5.Traslapo);
        Assert.Equal(Reglas.Generales.PorDiametro[5].Estribo135, numero5.Estribo135);
        Assert.Equal(Reglas.Generales.PorDiametro[5].Gancho180, numero5.Gancho180);
    }

    [Fact]
    public void NoQuedaElArchivoTemporal()
    {
        new AlmacenReglas(Ruta).Guardar(Reglas.Generales);

        Assert.False(File.Exists(Ruta + ".tmp"));
    }

    [Fact]
    public void LaRutaPorDefectoEstaEnAppDataCqeingVigas()
    {
        Assert.EndsWith(Path.Combine("CQEING", "vigas", "reglas.json"), AlmacenReglas.RutaPorDefecto);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AlmacenReglas.RutaPorDefecto);
    }
}
