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
        Assert.IsType<VerTodos>(ProtocoloTablero.Leer("""{"tipo":"verTodos"}"""));
        Assert.Equal(new Desconocido("otro"), ProtocoloTablero.Leer("""{"tipo":"otro"}"""));
    }

    [Fact]
    public void LeeElegirVariosDeLaPlanta()
    {
        ElegirVarios m = Assert.IsType<ElegirVarios>(ProtocoloTablero.Leer("""{"tipo":"elegirVarios","ids":["P1","P2"],"abrir":"P2"}"""));

        Assert.Equal(["P1", "P2"], m.Ids);
        Assert.Equal("P2", m.Abrir);
        Assert.Equal(new Desconocido("elegirVarios"), ProtocoloTablero.Leer("""{"tipo":"elegirVarios","abrir":"P2"}"""));
    }

    [Fact]
    public void LeeMarcarTerminado()
    {
        Assert.Equal(new MarcarTerminado("P1", true), ProtocoloTablero.Leer("""{"tipo":"marcarTerminado","id":"P1","terminado":true}"""));
        Assert.Equal(new MarcarTerminado("P1", false), ProtocoloTablero.Leer("""{"tipo":"marcarTerminado","id":"P1","terminado":false}"""));
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
    public void LasOpcionesTraenElCorte()
    {
        string opciones = OpcionesJson(OpcionesTablero.PorDefecto).TrimEnd('}') + ""","corte":{"x":2200,"profundidad":150,"mirando":"Izquierda"}}""";

        var m = Assert.IsType<Calcular>(ProtocoloTablero.Leer($$"""{"tipo":"calcular","id":"P1","opciones":{{opciones}}}"""));

        Assert.Equal(new OpcionesCorte(2200, 150, MiraCorte.Izquierda), m.Opciones.Corte);
    }

    [Fact]
    public void SinCorteEsLaVistaTipo()
    {
        var m = Assert.IsType<Calcular>(ProtocoloTablero.Leer($$"""{"tipo":"calcular","id":"P1","opciones":{{OpcionesJson(OpcionesTablero.PorDefecto)}}}"""));

        Assert.Null(m.Opciones.Corte);
    }

    [Fact]
    public void ElCorteSinProfundidadUsaLaDeBase()
    {
        string opciones = OpcionesJson(OpcionesTablero.PorDefecto).TrimEnd('}') + ""","corte":{"x":null}}""";

        var m = Assert.IsType<Calcular>(ProtocoloTablero.Leer($$"""{"tipo":"calcular","id":"P1","opciones":{{opciones}}}"""));

        Assert.Equal(new OpcionesCorte(), m.Opciones.Corte);
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
    public void LeePrevisualizar()
    {
        string json = $$$"""{"tipo":"previsualizar","id":"P1","opciones":{{{OpcionesJson(OpcionesTablero.PorDefecto)}}},"pedido":{"n":7,"elemento":"S1–S2","accion":"Traslapo","valor":1.2}}""";

        var m = Assert.IsType<Previsualizar>(ProtocoloTablero.Leer(json));

        Assert.Equal("P1", m.Id);
        Assert.Equal(new PedidoEdicion(7, "S1–S2", AccionEdicion.Traslapo, 1.2), m.Pedido);
    }

    [Fact]
    public void LasOpcionesTraenLosCambiosComoLosMandaLaPagina()
    {
        // tablero.js manda también movidos y ganchos (fases B y C): mientras C# no los conoce, se ignoran
        string opciones = OpcionesJson(OpcionesTablero.PorDefecto).Replace("\"cambios\":null",
            "\"cambios\":{\"barras\":{\"S1\":3500},\"traslapos\":{\"I2–I3\":1000},\"movidos\":{},\"ganchos\":{}}");

        var m = Assert.IsType<Calcular>(ProtocoloTablero.Leer($$"""{"tipo":"calcular","id":"P1","opciones":{{opciones}}}"""));

        Assert.Equal(3500, m.Opciones.Cambios!.Barras!["S1"]);
        Assert.Equal(1000, m.Opciones.Cambios.Traslapos!["I2–I3"]);
    }

    [Fact]
    public void LeeElPedidoDeArreglosDeUnChoque()
    {
        string json = $$"""{"tipo":"arreglos","id":"P1","n":4,"choque":2,"opciones":{{OpcionesJson(OpcionesTablero.PorDefecto)}}}""";

        var m = Assert.IsType<PedirArreglos>(ProtocoloTablero.Leer(json));

        Assert.Equal(("P1", 4, 2), (m.Id, m.N, m.Choque));
    }

    [Fact]
    public void GenerarLeeForzar()
    {
        Assert.Equal(new Generar(["P1"]).Forzar, Assert.IsType<Generar>(ProtocoloTablero.Leer("""{"tipo":"generar","ids":["P1"]}""")).Forzar);
        Assert.True(Assert.IsType<Generar>(ProtocoloTablero.Leer("""{"tipo":"generar","ids":["P1"],"forzar":true}""")).Forzar);
    }

    [Fact]
    public void EscribeLosChoquesAlGenerar()
    {
        string json = ProtocoloTablero.Choques([new PorticoConChoques("Pórtico 1", 7)]);

        Assert.Equal("""{"tipo":"choques","porticos":[{"id":"Pórtico 1","cantidad":7}]}""", json);
    }

    [Fact]
    public void ArreglosSinChoqueEsDesconocido()
    {
        string json = $$"""{"tipo":"arreglos","id":"P1","n":4,"opciones":{{OpcionesJson(OpcionesTablero.PorDefecto)}}}""";

        Assert.Equal(new Desconocido("arreglos"), ProtocoloTablero.Leer(json));
    }

    [Fact]
    public void PrevisualizarSinPedidoEsDesconocido()
    {
        string json = $$"""{"tipo":"previsualizar","id":"P1","opciones":{{OpcionesJson(OpcionesTablero.PorDefecto)}}}""";

        Assert.Equal(new Desconocido("previsualizar"), ProtocoloTablero.Leer(json));
    }

    [Fact]
    public void EscribePrevia()
    {
        var p = new PreviaVista(7, "S1", [new OpcionEdicion("subir", 3.5, true, "S1 pasa a 3.50 m.")], []);

        string json = ProtocoloTablero.Previa(p);

        Assert.Equal("""{"tipo":"previa","previa":{"n":7,"elemento":"S1","opciones":[{"accion":"subir","valor":3.5,"sePuede":true,"texto":"S1 pasa a 3.50 m."}],"saltos":[]}}""", json);
    }

    [Fact]
    public void ElAvisoLlevaSuTexto()
    {
        Assert.Equal("""{"tipo":"aviso","texto":"Hola \"tú\""}""", ProtocoloTablero.Aviso("Hola \"tú\""));
    }

    [Fact]
    public void CalcularTraeLosEstribosAdicionales()
    {
        // Lo que manda la página al agregar un adicional en el corte (hallazgo E8); "nombre" es solo para la lista y se ignora.
        const string json = """
            {"tipo":"calcular","id":"Pórtico 1","opciones":{"superior":[{"cantidad":3,"diametro":5}],"inferior":[{"cantidad":3,"diametro":5}],
             "recubrimientoCm":4,"maximoFilas":3,
             "estribos":{"diametro":3,"gancho":"De135","largoZona":800,"separacionZona":100,"separacionCentro":200,
               "adicionales":[{"tipo":"Abierto","barras":[{"cara":"Superior","fila":1,"numero":2,"nombre":"arriba 2"},{"cara":"Inferior","fila":1,"numero":2}]}]}}}
            """;

        Calcular c = Assert.IsType<Calcular>(ProtocoloTablero.Leer(json));

        EstriboAdicional a = Assert.Single(c.Opciones.Estribos.Adicionales!);
        Assert.Equal(TipoEstribo.Abierto, a.Tipo);
        Assert.Equal([new PuntoBarra(Posicion.Superior, 1, 2), new PuntoBarra(Posicion.Inferior, 1, 2)], a.Barras);
        Assert.Equal(FormaGrapa.C, a.Forma);
    }

    [Fact]
    public void CalcularTraeElGancho180YLaGrapaEnS()
    {
        const string json = """
            {"tipo":"calcular","id":"Pórtico 1","opciones":{"superior":[{"cantidad":3,"diametro":5}],"inferior":[{"cantidad":3,"diametro":5}],
             "recubrimientoCm":4,"maximoFilas":3,
             "estribos":{"diametro":3,"gancho":"De180","largoZona":800,"separacionZona":100,"separacionCentro":200,
               "adicionales":[{"tipo":"Abierto","forma":"S","barras":[{"cara":"Superior","fila":1,"numero":2},{"cara":"Inferior","fila":1,"numero":2}]}]}}}
            """;

        Calcular c = Assert.IsType<Calcular>(ProtocoloTablero.Leer(json));

        Assert.Equal(GanchoEstribo.De180, c.Opciones.Estribos.Gancho);
        Assert.Equal(FormaGrapa.S, Assert.Single(c.Opciones.Estribos.Adicionales!).Forma);
    }

    [Fact]
    public void LeeGenerar()
    {
        MensajeTablero m = ProtocoloTablero.Leer("""{"tipo":"generar","ids":["Pórtico 1","Pórtico 2"]}""");

        Assert.Equal(["Pórtico 1", "Pórtico 2"], Assert.IsType<Generar>(m).Ids);
        Assert.Null(((Generar)m).Estados);
    }

    [Fact]
    public void GenerarTraeElEstadoDeLaPaginaDeCadaPortico()
    {
        MensajeTablero m = ProtocoloTablero.Leer("""{"tipo":"generar","ids":["Pórtico 1"],"estados":{"Pórtico 1":"{\"rec\":\"5\"}"}}""");

        Assert.Equal("""{"rec":"5"}""", Assert.IsType<Generar>(m).Estados!["Pórtico 1"]);
        Assert.IsType<Desconocido>(ProtocoloTablero.Leer("""{"tipo":"generar"}"""));
    }

    [Fact]
    public void LasDiferenciasDeLargo_UnaLineaPorPorticoDiametroYDiferencia()
    {
        // Lo que dio el modelo real (hallazgo del plan): S1, S4 e I1 con el gancho del proyecto, 0.18 m más cortas.
        IReadOnlyList<string> t = ProtocoloTablero.Textos([
            new DiferenciaLargo("Pórtico A", "S1", 5, 4317, 4500),
            new DiferenciaLargo("Pórtico A", "S1", 5, 4317, 4500),
            new DiferenciaLargo("Pórtico A", "S4", 5, 4317, 4500),
            new DiferenciaLargo("Pórtico A", "I1", 5, 3317, 3500),
            new DiferenciaLargo("Pórtico A", "S1", 4, 6310, 6280),
        ]);

        Assert.Equal([
            "Pórtico A, barras #5 (S1, S4 e I1): en Revit miden 0.18 m menos que en el tablero. El gancho del tipo de barra #5 en Revit no coincide con el cuadro de Reglas.",
            "Pórtico A, barras #4 (S1): en Revit miden 0.03 m más que en el tablero. El gancho del tipo de barra #4 en Revit no coincide con el cuadro de Reglas.",
        ], t);
    }

    [Fact]
    public void ElResumenTraeSuTexto()
    {
        var r = new ResumenGeneracion(2, 48, 7, 112, ["Pórtico 3: el modelo no tiene un tipo de barra para el #9."],
            [new DiferenciaLargo("Pórtico 2", "S1", 5, 6320, 6280)]);

        JsonElement raiz = JsonDocument.Parse(ProtocoloTablero.Resumen(r)).RootElement;

        Assert.Equal("generado", raiz.GetProperty("tipo").GetString());
        Assert.Equal(
            "Se generó el acero de 2 pórticos: 48 barras, 7 bastones y 112 estribos.\n" +
            "Pórtico 3: el modelo no tiene un tipo de barra para el #9.\n" +
            "Pórtico 2, barras #5 (S1): en Revit miden 0.04 m más que en el tablero. El gancho del tipo de barra #5 en Revit no coincide con el cuadro de Reglas.",
            raiz.GetProperty("texto").GetString());
        Assert.Equal("Se generó el acero de 1 pórtico: 3 barras, 0 bastones y 1 estribo.",
            JsonDocument.Parse(ProtocoloTablero.Resumen(new ResumenGeneracion(1, 3, 0, 1, [], []))).RootElement.GetProperty("texto").GetString());
        Assert.Equal("No se generó ningún pórtico.\nPórtico 3: no se puede generar; revisa los avisos del tablero.",
            JsonDocument.Parse(ProtocoloTablero.Resumen(new ResumenGeneracion(0, 0, 0, 0, ["Pórtico 3: no se puede generar; revisa los avisos del tablero."], [])))
                .RootElement.GetProperty("texto").GetString());
    }
}
