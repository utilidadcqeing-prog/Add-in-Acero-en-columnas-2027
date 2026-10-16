using System.Text.Json;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>Los mensajes entre la página del tablero y C# (#83): JSON en camelCase, con <c>tipo</c> como discriminador.</summary>
public class ProtocoloTableroTests
{
    private static string OpcionesJson(OpcionesTablero o) => JsonSerializer.Serialize(o, ProtocoloTablero.Json);

    [Fact]
    public void LeeCadaMensajeDeLaPagina()
    {
        Assert.IsType<Listo>(ProtocoloTablero.Leer("""{"tipo":"listo"}"""));
        Assert.Equal(new ElegirPortico("P1"), ProtocoloTablero.Leer("""{"tipo":"elegirPortico","id":"P1"}"""));
        Assert.IsType<SeleccionarEnRevit>(ProtocoloTablero.Leer("""{"tipo":"seleccionarEnRevit"}"""));
        Assert.Equal(new VerEnRevit("P1"), ProtocoloTablero.Leer("""{"tipo":"verEnRevit","id":"P1"}"""));
        Assert.Equal(new Abrir3d("P1"), ProtocoloTablero.Leer("""{"tipo":"abrir3d","id":"P1"}"""));
        Assert.Equal(new CambiarEnVivo(false), ProtocoloTablero.Leer("""{"tipo":"cambiarEnVivo","activo":false}"""));
        Assert.Equal(new Desconocido("otro"), ProtocoloTablero.Leer("""{"tipo":"otro"}"""));
    }

    [Fact]
    public void CalcularTraeLasOpcionesDeLaPagina()
    {
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(5, 5), new GrupoBarras(2, 4)], Cara = Cara.Final };

        var m = Assert.IsType<Calcular>(ProtocoloTablero.Leer($$"""{"tipo":"calcular","id":"P1","opciones":{{OpcionesJson(o)}}}"""));

        Assert.Equal("P1", m.Id);
        Assert.Equal([new GrupoBarras(5, 5), new GrupoBarras(2, 4)], m.Opciones.Superior);
        Assert.Equal((Cara.Final, 4.0, 3), (m.Opciones.Cara!.Value, m.Opciones.RecubrimientoCm, m.Opciones.MaximoFilas));
        Assert.Equal(new ConfigEstribos().Gancho, m.Opciones.Estribos.Gancho);
    }

    [Fact]
    public void LosEnumsVanComoTexto()
    {
        string json = OpcionesJson(OpcionesTablero.PorDefecto with { Cara = Cara.Final });

        Assert.Contains("\"cara\":\"Final\"", json);
        Assert.Contains("\"gancho\":\"De135\"", json);
    }

    [Fact]
    public void GuardarReglasTraeLosTextosDeLaPantalla()
    {
        VistaReglas v = ArmadorVista.Reglas(new ReglasCargadas(Reglas.Generales, null));
        string json = $$"""{"tipo":"guardarReglas","reglas":{{JsonSerializer.Serialize(v, ProtocoloTablero.Json)}}}""";

        var m = Assert.IsType<GuardarReglas>(ProtocoloTablero.Leer(json));

        Assert.Equal(v.PorDiametro, m.Reglas.PorDiametro);
        Assert.Equal("0.50", m.Reglas.Multiplo);
    }

    [Fact]
    public void UnJsonRotoEsDesconocido()
    {
        Assert.Equal(new Desconocido(""), ProtocoloTablero.Leer("{"));
        Assert.Equal(new Desconocido(""), ProtocoloTablero.Leer("""{"id":"P1"}"""));
    }

    [Fact]
    public void UnCalcularSinBarrasEsDesconocido()
    {
        Assert.Equal(new Desconocido("calcular"), ProtocoloTablero.Leer("""{"tipo":"calcular","id":"P1","opciones":{"superior":[]}}"""));
    }

    [Fact]
    public void LaVistaSaleEnCamelCase()
    {
        var v = new VistaTablero("Proyecto", [], null, ArmadorVista.Reglas(new ReglasCargadas(Reglas.Generales, null)), true);

        string json = ProtocoloTablero.Vista(v);

        Assert.StartsWith("""{"tipo":"vista","vista":{""", json);
        Assert.Contains("\"proyecto\":\"Proyecto\"", json);
        Assert.Contains("\"porticos\":[]", json);
        Assert.Contains("\"enVivo\":true", json);
    }

    [Fact]
    public void ElAvisoLlevaSuTexto()
    {
        Assert.Equal("""{"tipo":"aviso","texto":"Hola \"tú\""}""", ProtocoloTablero.Aviso("Hola \"tú\""));
    }
}
