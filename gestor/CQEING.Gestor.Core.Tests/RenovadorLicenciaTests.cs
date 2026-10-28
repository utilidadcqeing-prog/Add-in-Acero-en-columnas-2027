using CQEING.Compartido;
using CQEING.Gestor.Core.Tests.Apoyo;
using static CQEING.Gestor.Core.Tests.Apoyo.FirmaPrueba;

namespace CQEING.Gestor.Core.Tests;

public class RenovadorLicenciaTests : IDisposable
{
    private static readonly DateTime Ahora = new(2026, 10, 28, 17, 0, 0, DateTimeKind.Utc);

    private readonly CarpetaTemporal _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private string ArchivoLicencia => _tmp.Combinar(Licencia.NombreArchivo);

    private RenovadorLicencia Renovador(HandlerFalso handler, string? codigo = Codigo) =>
        new(new ServicioLicencias(handler.Cliente(), "https://servicio.test/exec"), _tmp.Ruta, () => Ahora, codigo, Publica);

    private static HandlerFalso Responde(string json) => new(_ => Respuestas.Texto(json));

    private static HandlerFalso Aprueba(string licencia) => Responde($$"""{"estado":"aprobado","licencia":{{licencia}}}""");

    private void Guardar(string licencia) => File.WriteAllText(ArchivoLicencia, licencia);

    [Fact]
    public async Task Aprobada_guarda_la_licencia_y_queda_vigente()
    {
        string licencia = LicenciaFirmada(Ahora);

        InfoLicencia info = await Renovador(Aprueba(licencia)).RenovarAsync("Santiago Bula", forzar: false, "1.4.0");

        Assert.True(info.Vigente);
        Assert.Equal(EstadoSolicitud.Aprobada, info.UltimaRespuesta);
        Assert.Equal("Santiago Bula", info.Nombre);
        Assert.Equal(Ahora.AddDays(3), info.VenceUtc);
        Assert.Equal(Codigo, info.CodigoEquipo);
        Assert.Equal(licencia, File.ReadAllText(ArchivoLicencia));
        Assert.Empty(Directory.GetFiles(_tmp.Ruta, "*.tmp"));
    }

    [Fact]
    public async Task Sin_conexion_conserva_la_licencia_vigente()
    {
        string licencia = LicenciaFirmada(Ahora.AddDays(-1));
        Guardar(licencia);

        InfoLicencia info = await Renovador(new HandlerFalso(_ => throw new HttpRequestException("sin red"))).RenovarAsync(null, false, "1.4.0");

        Assert.True(info.Vigente);
        Assert.Equal(EstadoSolicitud.SinConexion, info.UltimaRespuesta);
        Assert.Equal(licencia, File.ReadAllText(ArchivoLicencia));
    }

    [Fact]
    public async Task Aprobada_de_otro_equipo_no_reemplaza_la_buena()
    {
        string buena = LicenciaFirmada(Ahora.AddDays(-1));
        Guardar(buena);

        InfoLicencia info = await Renovador(Aprueba(LicenciaFirmada(Ahora, codigo: "1111-2222-3333-4444"))).RenovarAsync(null, false, "1.4.0");

        Assert.True(info.Vigente);
        Assert.Equal(buena, File.ReadAllText(ArchivoLicencia));
    }

    [Fact]
    public async Task Aprobada_mal_firmada_no_reemplaza_la_buena()
    {
        string buena = LicenciaFirmada(Ahora.AddDays(-1));
        Guardar(buena);
        string falsa = Archivo(FirmaPrueba.Datos(Ahora, Ahora.AddYears(5)), firma: Firmar("otra cosa"));

        await Renovador(Aprueba(falsa)).RenovarAsync(null, false, "1.4.0");

        Assert.Equal(buena, File.ReadAllText(ArchivoLicencia));
    }

    [Fact]
    public async Task Revocada_borra_la_licencia()
    {
        Guardar(LicenciaFirmada(Ahora.AddDays(-1)));

        InfoLicencia info = await Renovador(Responde("""{"estado":"revocado"}""")).RenovarAsync(null, false, "1.4.0");

        Assert.False(info.Vigente);
        Assert.Equal(EstadoSolicitud.Revocada, info.UltimaRespuesta);
        Assert.False(File.Exists(ArchivoLicencia));
    }

    [Fact]
    public async Task Pendiente_no_toca_nada()
    {
        InfoLicencia info = await Renovador(Responde("""{"estado":"pendiente"}""")).RenovarAsync("Yina Londoño", false, "1.4.0");

        Assert.False(info.Vigente);
        Assert.Equal(EstadoSolicitud.Pendiente, info.UltimaRespuesta);
        Assert.False(File.Exists(ArchivoLicencia));
    }

    [Fact]
    public async Task Emitida_hace_menos_de_12_horas_no_consulta()
    {
        Guardar(LicenciaFirmada(Ahora.AddHours(-11)));
        HandlerFalso handler = Responde("""{"estado":"revocado"}""");

        InfoLicencia info = await Renovador(handler).RenovarAsync(null, forzar: false, "1.4.0");

        Assert.Empty(handler.Pedidos);
        Assert.True(info.Vigente);
        Assert.Null(info.UltimaRespuesta);
    }

    [Fact]
    public async Task Emitida_hace_mas_de_12_horas_consulta()
    {
        Guardar(LicenciaFirmada(Ahora.AddHours(-13)));
        HandlerFalso handler = Aprueba(LicenciaFirmada(Ahora));

        await Renovador(handler).RenovarAsync(null, forzar: false, "1.4.0");

        Assert.Single(handler.Pedidos);
    }

    [Fact]
    public async Task Forzada_consulta_igual()
    {
        Guardar(LicenciaFirmada(Ahora.AddHours(-1)));
        HandlerFalso handler = Responde("""{"estado":"pendiente"}""");

        await Renovador(handler).RenovarAsync(null, forzar: true, "1.4.0");

        Assert.Single(handler.Pedidos);
    }

    [Fact]
    public async Task Con_nombre_lo_guarda_y_sin_nombre_usa_el_guardado()
    {
        await Renovador(Responde("""{"estado":"pendiente"}""")).RenovarAsync("  Yina Londoño ", true, "1.4.0");
        HandlerFalso segundo = Responde("""{"estado":"pendiente"}""");

        RenovadorLicencia renovador = Renovador(segundo);
        await renovador.RenovarAsync(null, true, "1.4.0");

        Assert.Equal("Yina Londoño", renovador.NombreGuardado);
        Assert.Contains("nombre=Yina%20Londo%C3%B1o", segundo.Pedidos.Single().RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Sin_codigo_de_equipo_no_consulta()
    {
        HandlerFalso handler = Responde("""{"estado":"pendiente"}""");

        InfoLicencia info = await Renovador(handler, codigo: null).RenovarAsync("Yina", true, "1.4.0");

        Assert.Empty(handler.Pedidos);
        Assert.False(info.Vigente);
        Assert.Null(info.CodigoEquipo);
    }

    [Fact]
    public async Task Anota_la_ultima_vista()
    {
        await Renovador(Responde("""{"estado":"pendiente"}""")).RenovarAsync(null, false, "1.4.0");

        Assert.Equal(Ahora, Licencia.UltimaVista(_tmp.Ruta));
    }

    [Fact]
    public void Estado_sin_licencia_lleva_el_codigo_en_el_motivo()
    {
        InfoLicencia info = Renovador(Responde("{}")).Estado();

        Assert.False(info.Vigente);
        Assert.Null(info.UltimaRespuesta);
        Assert.EndsWith($"Código del equipo: {Codigo}", info.Motivo);
    }
}
