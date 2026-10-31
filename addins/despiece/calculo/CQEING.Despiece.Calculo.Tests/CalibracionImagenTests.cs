namespace CQEING.Despiece.Calculo.Tests;

public class CalibracionImagenTests
{
    private const int Ancho = 100, Alto = 80;

    /// <summary>Una imagen blanca BGRA con cruces magenta de 5 px centradas en los puntos dados.</summary>
    private static byte[] Imagen(params (int X, int Y)[] cruces)
    {
        var px = new byte[Ancho * Alto * 4];
        Array.Fill(px, (byte)255);
        foreach ((int cx, int cy) in cruces)
            for (int d = -2; d <= 2; d++)
            {
                Pintar(px, cx + d, cy);
                Pintar(px, cx, cy + d);
            }
        return px;
    }

    private static void Pintar(byte[] px, int x, int y)
    {
        int i = (y * Ancho + x) * 4;
        px[i] = 255; // azul
        px[i + 1] = 0; // verde
        px[i + 2] = 255; // rojo
    }

    private static readonly PuntoModelo MarcaA = new(0, 0), MarcaB = new(10, 10);

    [Fact]
    public void UbicaLasDosCrucesYCalculaLaEscala()
    {
        byte[] px = Imagen((10, 70), (90, 10));

        Calibracion c = CalibracionImagen.Calibrar(px, Ancho, Alto, MarcaA, MarcaB)!;

        Assert.Equal(8, c.EscalaX, 3);
        Assert.Equal(-6, c.EscalaY, 3);
        (double x, double y) = c.APixel(new PuntoModelo(5, 5));
        Assert.Equal(50, x, 3);
        Assert.Equal(40, y, 3);
    }

    [Fact]
    public void BorraLasCrucesDeLaImagen()
    {
        byte[] px = Imagen((10, 70), (90, 10));

        CalibracionImagen.Calibrar(px, Ancho, Alto, MarcaA, MarcaB);

        Assert.All(px, b => Assert.Equal(255, b));
    }

    [Fact]
    public void BorraCrucesGrandesAunqueLaImagenTengaMasResolucion()
    {
        var px = new byte[Ancho * Alto * 4];
        Array.Fill(px, (byte)255);
        foreach ((int cx, int cy) in new[] { (20, 60), (80, 20) })
            for (int d = -18; d <= 18; d++)
            {
                Pintar(px, cx + d, cy);
                Pintar(px, cx, cy + d);
            }

        CalibracionImagen.Calibrar(px, Ancho, Alto, MarcaA, MarcaB);

        Assert.All(px, b => Assert.Equal(255, b));
    }

    [Fact]
    public void ConUnaSolaCruzNoHayCalibracion()
    {
        Assert.Null(CalibracionImagen.Calibrar(Imagen((10, 70)), Ancho, Alto, MarcaA, MarcaB));
    }

    [Fact]
    public void SinCrucesNoHayCalibracion()
    {
        Assert.Null(CalibracionImagen.Calibrar(Imagen(), Ancho, Alto, MarcaA, MarcaB));
    }

    [Fact]
    public void UnaCajaDelModeloPasaAPixelesConArribaYAbajoEnOrden()
    {
        byte[] px = Imagen((10, 70), (90, 10));
        Calibracion c = CalibracionImagen.Calibrar(px, Ancho, Alto, MarcaA, MarcaB)!;

        CajaPixeles caja = c.ACaja(new PuntoModelo(0, 0), new PuntoModelo(10, 10));

        Assert.Equal(new CajaPixeles(10, 10, 80, 60), caja);
    }
}
