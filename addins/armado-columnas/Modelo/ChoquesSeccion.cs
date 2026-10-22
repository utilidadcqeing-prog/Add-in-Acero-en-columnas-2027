namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// Qué piezas de la sección chocan (issue #131): el add-in avisa y deja
/// seguir, nunca corrige ni impide generar. Sin Revit: lo usan la vista
/// previa, "Editar estribos menores" y el aviso antes de generar. Ver
/// docs/planes/2026-10-22-avisos-de-choque-131-diseno.md.
/// Todas las medidas en METROS salvo los diámetros, en milímetros.
/// </summary>
public static class ChoquesSeccion
{
    /// <summary>Por debajo de esto (mm) dos piezas que "se tocan" no cuentan como choque.</summary>
    public const double ToleranciaMm = 1;

    /// <param name="Varilla">Índice de la varilla en el anillo: las piezas de una misma varilla no chocan entre sí.</param>
    /// <param name="ZInf">Desde qué altura existe la pieza; sin límite, toda la columna.</param>
    public sealed record PiezaRedonda(
        double X, double Y, double DiametroMm, int Varilla,
        double ZInf = double.NegativeInfinity, double ZSup = double.PositiveInfinity);

    /// <param name="Motivo">Qué pieza choca con cuál, para leer.</param>
    /// <param name="Marcas">Círculos a rodear en rojo en el dibujo (centro y radio).</param>
    public sealed record Choque(string Motivo, IReadOnlyList<(double X, double Y, double Radio)> Marcas);

    /// <summary>
    /// Distancia libre mínima (borde a borde) entre dos longitudinales: la de
    /// la NSR-10 para columnas, 1,5 diámetros de la más gruesa o 4 cm, la
    /// mayor. Decisión de Luciano (22-oct), A CONFIRMAR CON CARLOS: si la
    /// empresa usa otra regla, se cambia acá.
    /// </summary>
    public static double SeparacionMinimaMm(double diametroMm1, double diametroMm2) =>
        Math.Max(1.5 * Math.Max(diametroMm1, diametroMm2), 40);

    /// <summary>
    /// Cada par de piezas de varillas distintas que comparten altura y quedan
    /// más juntas que <see cref="SeparacionMinimaMm"/>: un choque por par.
    /// </summary>
    public static IReadOnlyList<Choque> BarrasJuntas(IReadOnlyList<PiezaRedonda> piezas, Func<int, string> nombreVarilla)
    {
        var choques = new List<Choque>();
        for (int i = 0; i < piezas.Count; i++)
            for (int j = i + 1; j < piezas.Count; j++)
            {
                PiezaRedonda a = piezas[i], b = piezas[j];
                if (a.Varilla == b.Varilla || a.ZSup <= b.ZInf || b.ZSup <= a.ZInf) continue;

                double libreMm = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) * 1000
                                 - (a.DiametroMm + b.DiametroMm) / 2;
                double minimoMm = SeparacionMinimaMm(a.DiametroMm, b.DiametroMm);
                if (libreMm >= minimoMm - ToleranciaMm) continue;

                choques.Add(new Choque(
                    string.Format(Es, "{0} y {1} quedan a {2:0.0} cm libres; se piden {3:0.0} cm.",
                        Mayuscula(nombreVarilla(a.Varilla)), nombreVarilla(b.Varilla), Math.Max(0, libreMm) / 10, minimoMm / 10),
                    new[] { Marca(a), Marca(b) }));
            }

        return choques;
    }

    /// <summary>
    /// Una grapa o un estribo menor (su eje, de <see cref="PlantaArmado.Menor"/>,
    /// con el grosor de su barra) que se mete en planta en una longitudinal
    /// que no es de las que abraza. Las longitudinales son continuas en
    /// altura, así que todo cruce en planta es un choque. Marca esa barra; si
    /// pisa varias, la primera.
    /// </summary>
    public static Choque? MenorSobreBarra(
        IReadOnlyList<(double X, double Y)> eje, double diametroMm, IReadOnlyList<PiezaRedonda> barras,
        ISet<int> abrazadas, string nombrePieza, Func<int, string> nombreVarilla)
    {
        foreach (PiezaRedonda barra in barras)
        {
            if (abrazadas.Contains(barra.Varilla)) continue;

            double distancia = double.PositiveInfinity;
            for (int i = 1; i < eje.Count; i++)
                distancia = Math.Min(distancia, DistanciaASegmento(barra.X, barra.Y, eje[i - 1], eje[i]));

            double metidaMm = (diametroMm + barra.DiametroMm) / 2 - distancia * 1000;
            if (metidaMm <= ToleranciaMm) continue;

            return new Choque(
                string.Format(Es, "{0} pasa encima de {1}, que no abraza (se meten {2:0.0} cm).",
                    Mayuscula(nombrePieza), nombreVarilla(barra.Varilla), metidaMm / 10),
                new[] { Marca(barra) });
        }

        return null;
    }

    /// <summary>
    /// Por qué un estribo cerrado no se puede doblar con su barra -- su lado
    /// corto, entre los ejes de su propia barra, más angosto que
    /// <see cref="EstriboMenor.AnchoMinimoCerradoMm"/> --, o null si se puede.
    /// Con la misma caja que se dibuja y que crea el generador, por fuera de
    /// las barras que abraza (#132). Antes vivía en VentanaEstribosMenores.
    /// </summary>
    /// <param name="donde">Cómo se nombra su lugar: "fila 2", "columnas 1 a 3".</param>
    public static string? CerradoImposible(EstriboMenor pieza, TipoBarra barra, IEnumerable<GanchoGrapa.Pieza> abrazadas, string donde)
    {
        if (pieza.Forma != FormaMenor.Cerrado) return null;

        double diametro = barra.DiametroMm / 1000;
        (double x1, double x2, double y1, double y2) = EstriboMenor.CajaQueAbraza(
            abrazadas, diametro, PlantaArmado.Doblez(barra, barra.DiametroMm) / 2);
        double anchoMm = (Math.Min(x2 - x1, y2 - y1) - diametro) * 1000;
        double minimoMm = EstriboMenor.AnchoMinimoCerradoMm(barra);
        if (anchoMm >= minimoMm) return null;

        return string.Format(Es,
            "El estribo cerrado de las {0} mide {1:0.0} cm de ancho (entre los ejes de su barra), y con «{2}» necesita al menos {3:0.0} cm para doblarse: " +
            "dibújalo entre barras más separadas o usa una barra más delgada.",
            donde, anchoMm / 10, barra.Nombre, minimoMm / 10);
    }

    private static double DistanciaASegmento(double px, double py, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, largo2 = dx * dx + dy * dy;
        double t = largo2 < 1e-18 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / largo2, 0, 1);
        double qx = a.X + t * dx - px, qy = a.Y + t * dy - py;
        return Math.Sqrt(qx * qx + qy * qy);
    }

    /// <summary>
    /// Cómo se nombra la barra <paramref name="indice"/> en los avisos:
    /// "la esquina inferior izquierda", o "la barra 2 de la cara inferior"
    /// (las caras de abajo y de arriba se cuentan de izquierda a derecha; las
    /// de los costados, de abajo hacia arriba; las esquinas no cuentan).
    /// </summary>
    public static string NombreBarra(IReadOnlyList<SeccionPlanta.BarraEnPlanta> barras, int indice)
    {
        double cx = (barras.Min(b => b.X) + barras.Max(b => b.X)) / 2;
        double cy = (barras.Min(b => b.Y) + barras.Max(b => b.Y)) / 2;
        SeccionPlanta.BarraEnPlanta barra = barras[indice];

        switch (barra.Posicion)
        {
            case PosicionBarra.Esquina:
                return $"la esquina {(barra.Y < cy ? "inferior" : "superior")} {(barra.X < cx ? "izquierda" : "derecha")}";
            case PosicionBarra.CaraX:
            {
                bool abajo = barra.Y < cy;
                int n = barras.Count(b => b.Posicion == PosicionBarra.CaraX && (b.Y < cy) == abajo && b.X < barra.X) + 1;
                return $"la barra {n} de la cara {(abajo ? "inferior" : "superior")}";
            }
            default:
            {
                bool izquierda = barra.X < cx;
                int n = barras.Count(b => b.Posicion == PosicionBarra.CaraY && (b.X < cx) == izquierda && b.Y < barra.Y) + 1;
                return $"la barra {n} de la cara {(izquierda ? "izquierda" : "derecha")}";
            }
        }
    }

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-CO");

    private static (double X, double Y, double Radio) Marca(PiezaRedonda p) => (p.X, p.Y, p.DiametroMm / 2000);

    private static string Mayuscula(string texto) => texto.Length == 0 ? texto : char.ToUpper(texto[0], Es) + texto[1..];
}
