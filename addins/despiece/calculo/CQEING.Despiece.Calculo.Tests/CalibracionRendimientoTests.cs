using System.Diagnostics;
using Xunit.Abstractions;

namespace CQEING.Despiece.Calculo.Tests;

/// <summary>
/// Cuánto tarda calibrar una planta del tamaño real (3000 px de ancho). No mide a Revit: la exportación solo se puede
/// medir con Revit abierto. El tope es holgado a propósito, para avisar de un retroceso grande y no fallar por una máquina
/// ocupada. Se corre con <c>dotnet test --filter Categoria=Rendimiento --logger "console;verbosity=detailed"</c>.
/// </summary>
[Trait("Categoria", "Rendimiento")]
public class CalibracionRendimientoTests(ITestOutputHelper salida)
{
    private const int Repeticiones = 15;
    private const int TopeMilisegundos = 500;
    private static readonly PuntoModelo MarcaA = new(0, 0), MarcaB = new(10, 10);

    /// <summary>Una planta blanca con líneas oscuras en la proporción dada y cruces magenta de 40 px en dos esquinas.</summary>
    private static byte[] Planta(int ancho, int alto, double oscuro)
    {
        var px = new byte[ancho * alto * 4];
        Array.Fill(px, (byte)255);
        var azar = new Random(1);
        for (int i = 0; i < px.Length; i += 4)
            if (azar.NextDouble() < oscuro)
                px[i] = px[i + 1] = px[i + 2] = 40;
        foreach ((int cx, int cy) in new[] { (100, alto - 100), (ancho - 100, 100) })
            for (int d = -20; d <= 20; d++)
            {
                Magenta(px, ancho, cx + d, cy);
                Magenta(px, ancho, cx, cy + d);
            }
        return px;
    }

    private static void Magenta(byte[] px, int ancho, int x, int y)
    {
        int i = (y * ancho + x) * 4;
        px[i] = 255;
        px[i + 1] = 0;
        px[i + 2] = 255;
    }

    [Theory]
    [InlineData(3000, 1600, 0.05)]
    [InlineData(3000, 3000, 0.05)]
    [InlineData(2000, 1067, 0.05)]
    [InlineData(1500, 800, 0.05)]
    public void CalibrarUnaPlanta(int ancho, int alto, double oscuro)
    {
        byte[] original = Planta(ancho, alto, oscuro);
        var tiempos = new List<double>();
        for (int i = 0; i < Repeticiones; i++)
        {
            var px = (byte[])original.Clone();
            var reloj = Stopwatch.StartNew();
            Calibracion? c = CalibracionImagen.Calibrar(px, ancho, alto, MarcaA, MarcaB);
            reloj.Stop();
            Assert.NotNull(c);
            tiempos.Add(reloj.Elapsed.TotalMilliseconds);
        }
        tiempos.Sort();
        double mediana = tiempos[tiempos.Count / 2];
        salida.WriteLine($"{ancho}x{alto}: mediana {mediana:0.0} ms, mínimo {tiempos[0]:0.0} ms, máximo {tiempos[^1]:0.0} ms");
        Assert.True(mediana < TopeMilisegundos, $"La calibración tardó {mediana:0} ms; el tope es {TopeMilisegundos} ms.");
    }
}
