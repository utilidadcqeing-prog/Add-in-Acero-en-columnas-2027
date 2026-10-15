using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class ManifiestoTests
{
    [Fact]
    public void Escribe_los_nombres_de_campo_de_la_spec_y_lee_lo_mismo()
    {
        Manifiesto original = Datos.Manifiesto(paquetes: [Datos.Paquete("2026", new string('a', 64)), Datos.Paquete("2027", new string('b', 64))]);

        string json = SerializadorManifiesto.Escribir(original);
        Manifiesto leido = SerializadorManifiesto.Leer(json);

        foreach (string campo in new[] { "esquema", "id", "nombre", "descripcion", "version", "publicadoEn", "notas", "heredados", "paquetes", "revit", "framework", "archivo", "sha256" })
            Assert.Contains($"\"{campo}\"", json);
        Assert.Contains("Imágenes", SerializadorManifiesto.Escribir(Datos.Manifiesto(id: "imagenes")));
        Assert.Equal(original.Id, leido.Id);
        Assert.Equal(original.Version, leido.Version);
        Assert.Equal(original.PublicadoEn, leido.PublicadoEn);
        Assert.Equal(original.Heredados, leido.Heredados);
        Assert.Equal(original.Paquetes, leido.Paquetes);
        Assert.Equal("2027", leido.PaqueteParaRevit("2027")!.Revit);
        Assert.Null(leido.PaqueteParaRevit("2025"));
    }

    [Fact]
    public void Esquema_mas_nuevo_pide_actualizar_el_gestor()
    {
        string json = SerializadorManifiesto.Escribir(Datos.Manifiesto() with { Esquema = 2 });
        var error = Assert.Throws<GestorException>(() => SerializadorManifiesto.Leer(json));
        Assert.Equal(TipoError.ManifiestoInvalido, error.Tipo);
        Assert.Contains("Actualice el gestor", error.Message);
    }

    [Fact]
    public void Sin_heredados_queda_lista_vacia()
    {
        string json = SerializadorManifiesto.Escribir(Datos.Manifiesto()).Replace("\"heredados\"", "\"otroCampo\"");
        Assert.Empty(SerializadorManifiesto.Leer(json).Heredados);
    }

    [Theory]
    [InlineData("{ no es json")]
    [InlineData("null")]
    public void Json_invalido_es_ManifiestoInvalido(string json) =>
        Assert.Equal(TipoError.ManifiestoInvalido, Assert.Throws<GestorException>(() => SerializadorManifiesto.Leer(json)).Tipo);

    [Theory]
    [InlineData("../fuera.zip")]
    [InlineData("C:\\Windows\\x.zip")]
    [InlineData("sin-extension")]
    public void Archivo_con_ruta_es_invalido(string archivo)
    {
        var m = Datos.Manifiesto(paquetes: [Datos.Paquete("2026", new string('a', 64)) with { Archivo = archivo }]);
        Assert.Throws<GestorException>(() => SerializadorManifiesto.Leer(SerializadorManifiesto.Escribir(m)));
    }

    [Fact]
    public void Sha256_mal_formado_es_invalido()
    {
        var m = Datos.Manifiesto(paquetes: [Datos.Paquete("2026", "ABC")]);
        Assert.Throws<GestorException>(() => SerializadorManifiesto.Leer(SerializadorManifiesto.Escribir(m)));
    }

    [Theory]
    [InlineData("..\\otro.addin")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("practicacq.dll")]
    public void Heredado_con_ruta_es_invalido(string heredado)
    {
        var m = Datos.Manifiesto(heredados: [heredado]);
        Assert.Throws<GestorException>(() => SerializadorManifiesto.Leer(SerializadorManifiesto.Escribir(m)));
    }

    [Fact]
    public void Sha256Archivo_da_hex_en_minusculas()
    {
        using var tmp = new CarpetaTemporal();
        string ruta = tmp.Combinar("a.txt");
        File.WriteAllText(ruta, "abc");
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Sha256Archivo.Calcular(ruta));
    }
}
