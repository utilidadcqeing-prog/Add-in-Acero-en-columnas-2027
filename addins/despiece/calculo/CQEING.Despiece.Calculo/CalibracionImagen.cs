namespace CQEING.Despiece.Calculo;

/// <summary>Un punto del modelo en las coordenadas de la vista (a la derecha y hacia arriba), en pies.</summary>
public record PuntoModelo(double X, double Y);

/// <summary>Una caja en píxeles de la imagen.</summary>
public record CajaPixeles(double X, double Y, double Ancho, double Alto);

/// <summary>Cómo pasar de coordenadas de la vista a píxeles de la imagen exportada.</summary>
public record Calibracion(double EscalaX, double EscalaY, double OrigenX, double OrigenY)
{
    public (double X, double Y) APixel(PuntoModelo p) => (OrigenX + p.X * EscalaX, OrigenY + p.Y * EscalaY);

    public CajaPixeles ACaja(PuntoModelo a, PuntoModelo b)
    {
        (double x1, double y1) = APixel(a);
        (double x2, double y2) = APixel(b);
        return new CajaPixeles(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }
}

/// <summary>
/// Revit no dice qué parte del modelo cubre una imagen exportada: los círculos de los ejes la agrandan (plan de #221).
/// Por eso la vista temporal lleva dos cruces magenta en puntos conocidos: se ubican en la imagen, de ahí sale la escala
/// y se borran.
/// </summary>
public static class CalibracionImagen
{
    /// <summary>Lo mínimo que separa las dos cruces, en píxeles: menos que esto es una sola.</summary>
    private const int SeparacionMinima = 20;

    /// <summary>Lo que se borra de más alrededor de una cruz, en píxeles: sus bordes rosados.</summary>
    private const int MargenBorrado = 4;

    /// <param name="bgra">La imagen, 4 bytes por píxel (azul, verde, rojo, alfa). Se le borran las cruces.</param>
    /// <param name="marcaA">La cruz de abajo a la izquierda.</param>
    /// <param name="marcaB">La cruz de arriba a la derecha.</param>
    /// <returns>Null si no están las dos cruces.</returns>
    public static Calibracion? Calibrar(byte[] bgra, int ancho, int alto, PuntoModelo marcaA, PuntoModelo marcaB)
    {
        List<(int X, int Y)> magenta = Magenta(bgra, ancho, alto);
        if (magenta.Count == 0)
            return null;
        int minX = magenta.Min(p => p.X), maxX = magenta.Max(p => p.X), minY = magenta.Min(p => p.Y), maxY = magenta.Max(p => p.Y);
        if (maxX - minX < SeparacionMinima || maxY - minY < SeparacionMinima)
            return null;

        // Cada píxel es de la cruz cuya esquina tiene más cerca: A abajo a la izquierda, B arriba a la derecha.
        var a = magenta.Where(p => Distancia(p, minX, maxY) <= Distancia(p, maxX, minY)).ToList();
        var b = magenta.Except(a).ToList();
        if (a.Count == 0 || b.Count == 0)
            return null;
        (double ax, double ay) = Centro(a);
        (double bx, double by) = Centro(b);
        Borrar(bgra, ancho, alto, ax, ay, Radio(a, ax, ay));
        Borrar(bgra, ancho, alto, bx, by, Radio(b, bx, by));

        double escalaX = (bx - ax) / (marcaB.X - marcaA.X);
        double escalaY = (by - ay) / (marcaB.Y - marcaA.Y);
        return new Calibracion(escalaX, escalaY, ax - marcaA.X * escalaX, ay - marcaA.Y * escalaY);
    }

    private static List<(int X, int Y)> Magenta(byte[] bgra, int ancho, int alto)
    {
        var puntos = new List<(int, int)>();
        for (int y = 0; y < alto; y++)
            for (int x = 0; x < ancho; x++)
            {
                int i = (y * ancho + x) * 4;
                if (bgra[i] > 180 && bgra[i + 1] < 90 && bgra[i + 2] > 180)
                    puntos.Add((x, y));
            }
        return puntos;
    }

    private static double Distancia((int X, int Y) p, int x, int y) => Math.Pow(p.X - x, 2) + Math.Pow(p.Y - y, 2);

    private static (double X, double Y) Centro(List<(int X, int Y)> puntos) => (puntos.Average(p => p.X), puntos.Average(p => p.Y));

    /// <summary>El tamaño de la cruz depende de la resolución de la imagen: se mide, no se supone.</summary>
    private static int Radio(List<(int X, int Y)> puntos, double cx, double cy) =>
        (int)Math.Ceiling(puntos.Max(p => Math.Max(Math.Abs(p.X - cx), Math.Abs(p.Y - cy)))) + MargenBorrado;

    /// <summary>También los bordes rosados de la cruz: lo que tiene más rojo y azul que verde, cerca de su centro.</summary>
    private static void Borrar(byte[] bgra, int ancho, int alto, double cx, double cy, int radio)
    {
        for (int y = Math.Max(0, (int)cy - radio); y <= Math.Min(alto - 1, (int)cy + radio); y++)
            for (int x = Math.Max(0, (int)cx - radio); x <= Math.Min(ancho - 1, (int)cx + radio); x++)
            {
                int i = (y * ancho + x) * 4;
                if (bgra[i] - bgra[i + 1] > 20 && bgra[i + 2] - bgra[i + 1] > 20)
                    bgra[i] = bgra[i + 1] = bgra[i + 2] = 255;
            }
    }
}
