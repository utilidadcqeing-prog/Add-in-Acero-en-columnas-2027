using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tests;

public sealed class EdicionReglasTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    private string Ruta => Path.Combine(_carpeta, "reglas.json");

    private EdicionReglas Nueva() => new(new AlmacenReglas(Ruta));

    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    [Fact]
    public void AlAbrir_EstanBloqueadasYSinCambios()
    {
        EdicionReglas edicion = Nueva();

        Assert.True(edicion.Bloqueadas);
        Assert.False(edicion.HayCambiosSinGuardar);
        Assert.Null(edicion.ProblemaAlCargar);
    }

    [Fact]
    public void BloqueadasNoSePuedenCambiar()
    {
        EdicionReglas edicion = Nueva();

        Assert.Throws<InvalidOperationException>(() => edicion.Cambiar(edicion.Borrador with { Modulo = 600 }));
        Assert.Throws<InvalidOperationException>(() => edicion.CambiarDiametro(5, edicion.Borrador.PorDiametro[5]));
    }

    [Fact]
    public void DesbloqueadasSeCambianYHayCambiosSinGuardar()
    {
        EdicionReglas edicion = Nueva();

        edicion.Desbloquear();
        edicion.Cambiar(edicion.Borrador with { Modulo = 600 });

        Assert.True(edicion.HayCambiosSinGuardar);
        Assert.Equal(600, edicion.Borrador.Modulo);
        Assert.Equal(500, edicion.Guardadas.Modulo);
    }

    [Fact]
    public void SiSeVuelveAlValorOriginal_NoHayCambios()
    {
        EdicionReglas edicion = Nueva();
        edicion.Desbloquear();

        edicion.Cambiar(edicion.Borrador with { Modulo = 600 });
        edicion.Cambiar(edicion.Borrador with { Modulo = 500 });

        Assert.False(edicion.HayCambiosSinGuardar);
    }

    [Fact]
    public void GuardarYBloquear_GuardaParaTodosLosProyectos()
    {
        EdicionReglas edicion = Nueva();
        edicion.Desbloquear();
        edicion.Cambiar(edicion.Borrador with { Modulo = 600 });

        Assert.Empty(edicion.GuardarYBloquear());

        Assert.True(edicion.Bloqueadas);
        Assert.False(edicion.HayCambiosSinGuardar);
        EdicionReglas otra = Nueva();
        Assert.Equal(600, otra.Guardadas.Modulo);
        Assert.True(otra.Bloqueadas);
    }

    [Fact]
    public void GuardarConUnValorInvalido_NoGuardaYSigueDesbloqueada()
    {
        EdicionReglas edicion = Nueva();
        edicion.Desbloquear();
        edicion.Cambiar(edicion.Borrador with { Modulo = 0 });

        Assert.Equal(["El múltiplo de corte debe ser mayor que 0."], edicion.GuardarYBloquear());

        Assert.False(edicion.Bloqueadas);
        Assert.True(edicion.HayCambiosSinGuardar);
        Assert.False(File.Exists(Ruta));
    }

    [Fact]
    public void SiNoSePuedeEscribirElArchivo_DevuelveMensajeYSigueDesbloqueada()
    {
        new AlmacenReglas(Ruta).Guardar(Reglas.Generales);
        EdicionReglas edicion = Nueva();
        edicion.Desbloquear();
        edicion.Cambiar(edicion.Borrador with { Modulo = 600 });

        IReadOnlyList<string> errores;
        using (new FileStream(Ruta, FileMode.Open, FileAccess.Read, FileShare.None))
            errores = edicion.GuardarYBloquear();

        Assert.Equal(["No se pudieron guardar las reglas en el PC."], errores);
        Assert.False(edicion.Bloqueadas);
        Assert.Equal(500, edicion.Guardadas.Modulo);
    }

    [Fact]
    public void Descartar_VuelveALoGuardadoYBloquea()
    {
        EdicionReglas edicion = Nueva();
        edicion.Desbloquear();
        edicion.Cambiar(edicion.Borrador with { Modulo = 600 });

        edicion.Descartar();

        Assert.True(edicion.Bloqueadas);
        Assert.False(edicion.HayCambiosSinGuardar);
        Assert.Equal(500, edicion.Borrador.Modulo);
    }

    [Fact]
    public void CambiarDiametro_SoloTocaEseDiametro()
    {
        EdicionReglas edicion = Nueva();
        edicion.Desbloquear();

        edicion.CambiarDiametro(5, edicion.Borrador.PorDiametro[5] with { Traslapo = 1000 });

        Assert.Equal(1000, edicion.Borrador.PorDiametro[5].Traslapo);
        Assert.Equal(900, edicion.Guardadas.PorDiametro[5].Traslapo);
        Assert.Equal(Reglas.Generales.PorDiametro[4], edicion.Borrador.PorDiametro[4]);
    }

    [Fact]
    public void ConArchivoDañado_ElProblemaLlegaALaEdicion()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(Ruta, "{ no es json");

        Assert.NotNull(Nueva().ProblemaAlCargar);
    }
}
