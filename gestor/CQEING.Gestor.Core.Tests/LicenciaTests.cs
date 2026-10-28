using System.Security.Cryptography;
using System.Text.Json;
using CQEING.Compartido;
using CQEING.Gestor.Core.Tests.Apoyo;
using static CQEING.Gestor.Core.Tests.Apoyo.FirmaPrueba;

namespace CQEING.Gestor.Core.Tests;

public class LicenciaTests
{
    private static readonly DateTime Emitida = new(2026, 10, 28, 17, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Vence = Emitida.AddDays(3);
    private static readonly DateTime Ahora = Emitida.AddHours(5);

    private static string Datos(string codigo = Codigo, string nombre = "Santiago Bula", string prefijo = "cqeing-licencia-v1") =>
        FirmaPrueba.Datos(Emitida, Vence, codigo, nombre, prefijo);

    private static EstadoLicencia Verificar(string? contenido, DateTime? ahora = null, DateTime? ultimaVista = null, string? codigo = Codigo) =>
        Licencia.Verificar(contenido, codigo, ahora ?? Ahora, ultimaVista, Publica);

    [Fact]
    public void Licencia_bien_firmada_es_vigente()
    {
        EstadoLicencia estado = Verificar(Archivo(Datos()));

        Assert.Equal(ResultadoLicencia.Vigente, estado.Resultado);
        Assert.True(estado.EsVigente);
        Assert.Equal("Santiago Bula", estado.Nombre);
        Assert.Equal(Emitida, estado.EmitidaUtc);
        Assert.Equal(Vence, estado.VenceUtc);
    }

    [Fact]
    public void La_firma_hecha_por_Apps_Script_se_verifica()
    {
        using JsonDocument prueba = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Datos", "firma-apps-script.json")));
        string clavePublica = prueba.RootElement.GetProperty("clavePublica").GetString()!;
        string datos = prueba.RootElement.GetProperty("datos").GetString()!;
        string firma = prueba.RootElement.GetProperty("firma").GetString()!;

        EstadoLicencia estado = Licencia.Verificar(
            Archivo(datos, firma), "0000-0000-0000-0000", new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc), null, clavePublica);

        Assert.Equal(ResultadoLicencia.Vigente, estado.Resultado);
        Assert.Equal("Yina Londoño", estado.Nombre);
    }

    [Fact]
    public void Nombre_con_tilde_y_enie_es_vigente()
    {
        Assert.Equal("Yina Londoño", Verificar(Archivo(Datos(nombre: "Yina Londoño"))).Nombre);
    }

    [Fact]
    public void La_clave_publica_de_produccion_es_la_de_la_hoja()
    {
        using JsonDocument prueba = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Datos", "firma-apps-script.json")));

        Assert.Equal(
            prueba.RootElement.GetProperty("clavePublica").GetString()!.Trim().ReplaceLineEndings("\n"),
            Licencia.ClavePublica.Trim().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Datos_editados_sin_volver_a_firmar_es_invalida()
    {
        string firma = Firmar(Datos());
        string editados = Datos().Replace(Iso(Vence), Iso(Vence.AddYears(1)));

        Assert.Equal(ResultadoLicencia.Invalida, Verificar(Archivo(editados, firma)).Resultado);
    }

    [Fact]
    public void Firmada_con_otra_clave_es_invalida()
    {
        using RSA otra = RSA.Create(2048);

        Assert.Equal(ResultadoLicencia.Invalida, Verificar(Archivo(Datos(), Firmar(Datos(), otra))).Resultado);
    }

    [Fact]
    public void Codigo_de_otro_equipo_es_OtroEquipo()
    {
        Assert.Equal(ResultadoLicencia.OtroEquipo, Verificar(Archivo(Datos(codigo: "1111-2222-3333-4444"))).Resultado);
    }

    [Fact]
    public void Pasado_el_vencimiento_es_Vencida()
    {
        Assert.Equal(ResultadoLicencia.Vencida, Verificar(Archivo(Datos()), ahora: Vence.AddMinutes(1)).Resultado);
    }

    [Fact]
    public void Reloj_dos_horas_antes_de_la_ultima_vista_es_RelojAtrasado()
    {
        DateTime vista = Emitida.AddDays(2);

        Assert.Equal(ResultadoLicencia.RelojAtrasado, Verificar(Archivo(Datos()), ahora: vista.AddHours(-2), ultimaVista: vista).Resultado);
    }

    [Fact]
    public void Reloj_dos_horas_antes_de_la_emision_es_RelojAtrasado()
    {
        Assert.Equal(ResultadoLicencia.RelojAtrasado, Verificar(Archivo(Datos()), ahora: Emitida.AddHours(-2)).Resultado);
    }

    [Fact]
    public void Media_hora_de_diferencia_de_reloj_se_tolera()
    {
        Assert.Equal(ResultadoLicencia.Vigente, Verificar(Archivo(Datos()), ahora: Emitida.AddMinutes(-30)).Resultado);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"datos\":\"cqe")]
    [InlineData("no es json")]
    [InlineData("{\"datos\":\"x\"}")]
    [InlineData("{\"datos\":\"x\",\"firma\":\"esto no es base64\"}")]
    public void Contenido_vacio_cortado_o_ilegible_es_Invalida(string contenido)
    {
        Assert.Equal(ResultadoLicencia.Invalida, Verificar(contenido).Resultado);
    }

    [Fact]
    public void Contenido_nulo_es_NoExiste()
    {
        Assert.Equal(ResultadoLicencia.NoExiste, Verificar(null).Resultado);
    }

    [Fact]
    public void Version_desconocida_es_invalida()
    {
        Assert.Equal(ResultadoLicencia.Invalida, Verificar(Archivo(Datos(prefijo: "cqeing-licencia-v2"))).Resultado);
    }

    [Fact]
    public void Datos_con_partes_de_mas_son_invalidos()
    {
        Assert.Equal(ResultadoLicencia.Invalida, Verificar(Archivo(Datos() + "|extra")).Resultado);
    }

    [Fact]
    public void Sin_codigo_de_equipo_es_Invalida()
    {
        Assert.Equal(ResultadoLicencia.Invalida, Verificar(Archivo(Datos()), codigo: null).Resultado);
    }

    [Fact]
    public void Clave_publica_ilegible_es_Invalida()
    {
        Assert.Equal(ResultadoLicencia.Invalida, Licencia.Verificar(Archivo(Datos()), Codigo, Ahora, null, "no es una clave").Resultado);
    }

    [Fact]
    public void El_motivo_termina_con_el_codigo_del_equipo()
    {
        Assert.EndsWith($"Código del equipo: {Codigo}", Verificar(null).Motivo(Codigo));
    }

    [Fact]
    public void Leer_una_carpeta_que_no_existe_es_NoExiste()
    {
        using var carpeta = new CarpetaTemporal();

        Assert.Equal(ResultadoLicencia.NoExiste, Licencia.Leer(carpeta.Combinar("no-existe"), Codigo, Ahora, Publica).Resultado);
    }

    [Fact]
    public void Leer_el_archivo_guardado_es_vigente()
    {
        using var carpeta = new CarpetaTemporal();
        File.WriteAllText(carpeta.Combinar(Licencia.NombreArchivo), Archivo(Datos()));

        Assert.Equal(ResultadoLicencia.Vigente, Licencia.Leer(carpeta.Ruta, Codigo, Ahora, Publica).Resultado);
    }

    [Fact]
    public void Leer_usa_la_ultima_vista_anotada()
    {
        using var carpeta = new CarpetaTemporal();
        File.WriteAllText(carpeta.Combinar(Licencia.NombreArchivo), Archivo(Datos()));
        Licencia.AnotarVista(carpeta.Ruta, Emitida.AddDays(2));

        Assert.Equal(ResultadoLicencia.RelojAtrasado, Licencia.Leer(carpeta.Ruta, Codigo, Ahora, Publica).Resultado);
    }

    [Fact]
    public void AnotarVista_nunca_retrocede()
    {
        using var carpeta = new CarpetaTemporal();

        Licencia.AnotarVista(carpeta.Ruta, Emitida.AddHours(10));
        Licencia.AnotarVista(carpeta.Ruta, Emitida.AddHours(8));

        Assert.Equal(Emitida.AddHours(10), Licencia.UltimaVista(carpeta.Ruta));
    }

    [Fact]
    public void AnotarVista_en_una_carpeta_que_no_existe_la_crea()
    {
        using var carpeta = new CarpetaTemporal();

        Licencia.AnotarVista(carpeta.Combinar("CQEING"), Emitida);

        Assert.Equal(Emitida, Licencia.UltimaVista(carpeta.Combinar("CQEING")));
    }

    [Fact]
    public void Ultima_vista_ilegible_se_ignora()
    {
        using var carpeta = new CarpetaTemporal();
        File.WriteAllText(carpeta.Combinar(Licencia.NombreArchivoVista), "basura");

        Assert.Null(Licencia.UltimaVista(carpeta.Ruta));
    }
}
