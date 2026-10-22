using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// La configuración de un pórtico que se guarda en sus vigas al generar (plan docs/planes/2026-10-22-recordar-portico-generado-vigas.md):
/// las opciones de C# y el estado de la página, tal cual.
/// </summary>
public class ConfiguracionGuardadaTests
{
    private static readonly OpcionesTablero Opciones = OpcionesTablero.PorDefecto with
    {
        Superior = [new GrupoBarras(4, 5)],
        Estribos = new ConfigEstribos(Gancho: GanchoEstribo.De180,
            Adicionales: [new EstriboAdicional(TipoEstribo.Abierto, [new PuntoBarra(Posicion.Superior, 1, 2), new PuntoBarra(Posicion.Inferior, 1, 2)], FormaGrapa.S)]),
        Cambios = new CambiosManuales(Ganchos: new Dictionary<string, TipoGancho> { ["S.inicio"] = TipoGancho.De180 }),
    };

    private const string Estado = """{"sup":[{"n":4,"d":"#5"}],"est":{"estG":"180°"}}""";

    [Fact]
    public void LoQueSeEscribeSeLeeIgual()
    {
        (OpcionesTablero opciones, string? estado) = ConfiguracionGuardada.Leer(ConfiguracionGuardada.Escribir(Opciones, Estado))!.Value;

        Assert.Equal(Estado, estado);
        Assert.Equal(4, opciones.Superior[0].Cantidad);
        Assert.Equal(GanchoEstribo.De180, opciones.Estribos!.Gancho);
        Assert.Equal(FormaGrapa.S, Assert.Single(opciones.Estribos.Adicionales!).Forma);
        Assert.Equal(TipoGancho.De180, opciones.Cambios!.Ganchos!["S.inicio"]);
    }

    [Fact]
    public void SinEstadoDeLaPaginaSeLeeSoloLasOpciones()
    {
        Assert.Null(ConfiguracionGuardada.Leer(ConfiguracionGuardada.Escribir(Opciones, null))!.Value.Estado);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("""{"version":99,"opciones":{}}""")]
    [InlineData("""{"version":1}""")]
    public void LoQueNoSeEntiendeSeIgnora(string guardado)
    {
        Assert.Null(ConfiguracionGuardada.Leer(guardado));
    }
}
