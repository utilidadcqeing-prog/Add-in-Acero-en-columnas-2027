namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// El eje en planta de cada estribo y grapa, como lo deja Revit, para
/// dibujarlo sin Revit en la vista previa y en "Editar estribos menores"
/// (pedido de Luciano, 21-oct: "como van a aparecer en Revit").
/// docs/planes/2026-10-21-seccion-como-en-revit.md. Todo en metros; los arcos
/// salen como puntos cada pocos grados.
///
/// Las cuentas son las del generador y lo medido en Revit:
///  - Estribo cerrado (principal o menor), forma "CQ Estribo": esquinas con el
///    diámetro de doblado de la barra y los dos ganchos de 135° en la esquina
///    de arranque (abajo a la izquierda), hacia adentro. Las colas quedan a un
///    radio (al eje) de cada lado de la diagonal (medido: ~23,7 mm con #3).
///  - Grapa: gancho de 180° con el doblez de estribo de su barra, el centro del
///    doblez donde lo pone <see cref="GanchoGrapa.CentroDelDoblez"/> (la barra
///    adentro, tocando la cara interior en la punta, #130) y el tramo recto a un
///    radio del centro, del lado contrario al que abre.
/// </summary>
public static class PlantaArmado
{
    /// <summary>Cada cuántos grados se toma un punto de un arco.</summary>
    public const double PasoGrados = 7.5;

    private static readonly double Raiz2 = Math.Sqrt(2) / 2;

    /// <summary>
    /// El eje de un estribo cerrado con medidas por FUERA
    /// <paramref name="x1"/>..<paramref name="x2"/>, <paramref name="y1"/>..<paramref name="y2"/>,
    /// desde la punta de la cola del gancho del arranque hasta la del cierre.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> EstriboCerrado(
        double x1, double x2, double y1, double y2, double diametro, double doblez, double cola)
    {
        double r = doblez / 2 + diametro / 2;                       // radio al eje
        double ex1 = x1 + diametro / 2, ex2 = x2 - diametro / 2;    // el eje, medio diámetro adentro
        double ey1 = y1 + diametro / 2, ey2 = y2 - diametro / 2;
        r = Math.Max(0, Math.Min(r, Math.Min(ex2 - ex1, ey2 - ey1) / 2));

        (double X, double Y) abajoIzq = (ex1 + r, ey1 + r), abajoDer = (ex2 - r, ey1 + r);
        (double X, double Y) arribaDer = (ex2 - r, ey2 - r), arribaIzq = (ex1 + r, ey2 - r);

        // El gancho del cierre: baja por el lado izquierdo, da la vuelta a la
        // esquina 135° y sigue derecho hacia adentro, en diagonal.
        List<(double X, double Y)> cierre = Arco(abajoIzq, r, 180, 315);
        cierre.Add(Mas(cierre[^1], (Raiz2 * cola, Raiz2 * cola)));

        // El del arranque es el mismo reflejado en la diagonal de la esquina.
        List<(double X, double Y)> arranque = cierre
            .Select(p => (abajoIzq.X + (p.Y - abajoIzq.Y), abajoIzq.Y + (p.X - abajoIzq.X)))
            .Reverse()
            .ToList();

        var puntos = new List<(double X, double Y)>(arranque);
        puntos.AddRange(Arco(abajoDer, r, 270, 360));
        puntos.AddRange(Arco(arribaDer, r, 0, 90));
        puntos.AddRange(Arco(arribaIzq, r, 90, 180));
        puntos.AddRange(cierre);
        return puntos;
    }

    /// <summary>
    /// El eje de una grapa con los centros de sus dos dobleces ya ubicados.
    /// <paramref name="ladoInicio"/> / <paramref name="ladoFin"/>: +1 si el
    /// gancho abre a la izquierda de inicio→fin (visto desde arriba), −1 si a la
    /// derecha, como en <see cref="GanchoGrapa.Linea"/>.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> Grapa(
        (double X, double Y) centroInicio, (double X, double Y) centroFin, int ladoInicio, int ladoFin,
        double diametro, double doblez, double cola)
    {
        double r = doblez / 2 + diametro / 2;
        if (Math.Sqrt(Cuadrado(centroFin.X - centroInicio.X) + Cuadrado(centroFin.Y - centroInicio.Y)) <= 1e-9)
            return Array.Empty<(double, double)>();

        // Los dobleces y las colas van respecto del TRAMO RECTO, como los deja
        // Revit. En la C es paralelo a la línea de los centros; en la S cruza
        // en diagonal, tangente a los dos dobleces (#167): antes se dibujaban
        // los ganchos derechos y la recta en diagonal, con un quiebre que una
        // barra real no tiene.
        ((double X, double Y) a, (double X, double Y) b) =
            GanchoGrapa.Linea(centroInicio, centroFin, 0, r, ladoInicio, ladoFin);
        double largo = Math.Sqrt(Cuadrado(b.X - a.X) + Cuadrado(b.Y - a.Y));
        if (largo <= 1e-9) return Array.Empty<(double, double)>();
        (double X, double Y) u = ((b.X - a.X) / largo, (b.Y - a.Y) / largo);
        (double X, double Y) l = (-u.Y, u.X);

        // Del lado de la cola (hacia donde abre), por afuera (hacia la cara), al
        // lado del tramo recto: media vuelta alrededor del centro.
        List<(double X, double Y)> Gancho((double X, double Y) centro, int lado, (double X, double Y) haciaAfuera)
        {
            var arco = new List<(double X, double Y)>();
            for (double g = 0; g <= 180 + 1e-9; g += PasoGrados)
            {
                double c = Math.Cos(g * Math.PI / 180), s = Math.Sin(g * Math.PI / 180);
                arco.Add((centro.X + r * (c * lado * l.X + s * haciaAfuera.X), centro.Y + r * (c * lado * l.Y + s * haciaAfuera.Y)));
            }
            return arco;
        }

        // Inicio: punta de la cola → doblez → tramo recto. La cola va hacia adentro (+u).
        List<(double X, double Y)> inicio = Gancho(centroInicio, ladoInicio, (-u.X, -u.Y));
        var puntos = new List<(double X, double Y)> { Mas(inicio[0], (u.X * cola, u.Y * cola)) };
        puntos.AddRange(inicio);

        // Fin: tramo recto → doblez → cola hacia adentro (−u).
        List<(double X, double Y)> fin = Gancho(centroFin, ladoFin, u);
        fin.Reverse();
        puntos.AddRange(fin);
        puntos.Add(Mas(fin[^1], (-u.X * cola, -u.Y * cola)));
        return puntos;
    }

    /// <summary>
    /// El eje de un estribo menor como lo crea el generador.
    /// <paramref name="barrasDe"/> da, para cada fila o columna del rango, las
    /// dos barras que engancha: la de la izquierda (o la de abajo) y la de
    /// enfrente. La grapa abraza las dos de <see cref="EstriboMenor.Desde"/> y
    /// abre hacia el centro de la sección (al revés si está volteada; en la S,
    /// el gancho del fin al revés); el cerrado abraza por fuera todas las del
    /// rango (<see cref="EstriboMenor.CajaQueAbraza"/>).
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> Menor(
        EstriboMenor pieza, Func<int, (GanchoGrapa.Pieza Inicio, GanchoGrapa.Pieza Fin)> barrasDe,
        (double X, double Y) centroSeccion, double diametroMm, double doblez)
    {
        double diametro = diametroMm / 1000;

        if (pieza.Forma == FormaMenor.Cerrado)
        {
            IEnumerable<GanchoGrapa.Pieza> abrazadas = Enumerable.Range(pieza.Desde, pieza.Hasta - pieza.Desde + 1)
                .SelectMany(k => { (GanchoGrapa.Pieza a, GanchoGrapa.Pieza b) = barrasDe(k); return new[] { a, b }; });
            (double x1, double x2, double y1, double y2) = EstriboMenor.CajaQueAbraza(abrazadas, diametro, doblez / 2);
            return EstriboCerrado(x1, x2, y1, y2, diametro, doblez, Cola135(diametroMm));
        }

        (GanchoGrapa.Pieza inicio, GanchoGrapa.Pieza fin) = barrasDe(pieza.Desde);
        double largo = Math.Sqrt(Cuadrado(fin.X - inicio.X) + Cuadrado(fin.Y - inicio.Y));
        if (largo <= 1e-9) return Array.Empty<(double, double)>();
        (double X, double Y) u = ((fin.X - inicio.X) / largo, (fin.Y - inicio.Y) / largo);

        (int ladoInicio, int ladoFin) =
            GanchoGrapa.Lados((inicio.X, inicio.Y), (fin.X, fin.Y), centroSeccion, pieza.Forma, pieza.Volteado);

        return Grapa(
            CentroDelGancho(inicio.X, inicio.Y, inicio.Radio, doblez, u),
            CentroDelGancho(fin.X, fin.Y, fin.Radio, doblez, (-u.X, -u.Y)),
            ladoInicio, ladoFin, diametro, doblez, Cola180(diametroMm));
    }

    /// <summary>
    /// Dónde va el centro del doblez de una grapa que abraza la barra de
    /// (<paramref name="x"/>, <paramref name="y"/>): hacia la otra punta lo justo
    /// para que la barra toque la cara interior del doblez, como en el generador.
    /// </summary>
    public static (double X, double Y) CentroDelGancho(
        double x, double y, double radioBarra, double doblez, (double X, double Y) haciaAdentro) =>
        GanchoGrapa.CentroDelDoblez(new[] { new GanchoGrapa.Pieza(x, y, radioBarra, 0) }, doblez / 2, haciaAdentro);

    /// <summary>Cola recta del gancho de 135° del estribo: la E del cuadro, o 6 diámetros si la barra no está.</summary>
    public static double Cola135(double diametroMm) =>
        CuadroVarillas.PorDiametro(diametroMm)?.GanchoEstribo135?.Extension ?? 6 * diametroMm / 1000;

    /// <summary>Cola recta del gancho de 180° de la grapa: la E del cuadro, o 4 diámetros (mínimo 6,5 cm).</summary>
    public static double Cola180(double diametroMm) =>
        CuadroVarillas.PorDiametro(diametroMm)?.Gancho180Cuadro.Extension ?? Math.Max(0.065, 4 * diametroMm / 1000);

    /// <summary>El diámetro de doblado de estribo de una barra (m): el del tipo de barra, o 4 diámetros si no se conoce.</summary>
    public static double Doblez(TipoBarra? barra, double diametroMm) =>
        (barra?.DobladoEstriboMm > 0 ? barra.DobladoEstriboMm : 4 * diametroMm) / 1000;

    private static List<(double X, double Y)> Arco((double X, double Y) centro, double r, double desde, double hasta)
    {
        var puntos = new List<(double X, double Y)>();
        int pasos = Math.Max(1, (int)Math.Ceiling(Math.Abs(hasta - desde) / PasoGrados));
        for (int i = 0; i <= pasos; i++)
        {
            double g = (desde + (hasta - desde) * i / pasos) * Math.PI / 180;
            puntos.Add((centro.X + r * Math.Cos(g), centro.Y + r * Math.Sin(g)));
        }
        return puntos;
    }

    private static (double X, double Y) Mas((double X, double Y) a, (double X, double Y) b) => (a.X + b.X, a.Y + b.Y);

    private static double Cuadrado(double v) => v * v;
}
