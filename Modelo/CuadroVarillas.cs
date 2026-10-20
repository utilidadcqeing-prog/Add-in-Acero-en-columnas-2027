namespace practicacq.Modelo;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// El "Cuadro de ganchos y traslapos de varillas" de la empresa, en metros, sin
/// ninguna dependencia de la API de Revit.
///
/// Vive separado de <see cref="EstandarCQ"/> (que sí referencia Revit, porque
/// busca por <c>RebarBarType</c>) para que la ventana pueda elegir el tipo de
/// barra por defecto POR DIÁMETRO sin arrastrar Revit a <c>Modelo/</c>/<c>Vistas/</c>
/// -- la misma frontera que ya respetan <see cref="TipoGancho"/> e
/// <see cref="IProveedorTipos"/>. <c>tools/PreviewUI</c> excluye
/// <c>EstandarCQ.cs</c> justamente por eso, y necesita igual la tabla.
///
/// Las barras se identifican por diámetro nominal y no por nombre, porque en la
/// plantilla conviven nombres imperiales ("#3 COL") y métricos ("6 mm").
/// </summary>
public static class CuadroVarillas
{
    /// <param name="GanchoEstribo">
    /// Longitud del gancho de estribo de 135° (la "B" del cuadro "Dimensiones
    /// mínimas para ganchos estándar"). Para los estribos manda ESE cuadro y
    /// no la columna ESTRIBOS del cuadro de ganchos y traslapos, que para la
    /// #2 y la #3 dice otra cosa (0,113 y 0,131) -- confirmado por Luciano el
    /// 8-oct, issue #65. El cuadro de dimensiones mínimas solo llega hasta la
    /// #5; de la #6 a la #8 se conserva el valor del otro cuadro.
    /// </param>
    /// <param name="Traslapo">
    /// El de la columna "VIGAS" del cuadro, que aplica también a columnas
    /// (confirmado por Luciano con el cuadro nuevo, 8-oct). <c>null</c> = el
    /// cuadro no da traslapo para esa barra en vigas y columnas: es el caso de
    /// la #2, que solo lo tiene en muros (0,40 m). El cuadro trae además
    /// columnas "LOSAS" y "MUROS", hoy con los mismos valores que "VIGAS" para
    /// las barras #3 a #8; no se cargan porque nada las usa todavía.
    /// </param>
    public record Varilla(
        int Numero,
        double DiametroMm,
        double GanchoEstribo,
        double Gancho90,
        double Gancho180,
        double? Traslapo)
    {
        /// <summary>
        /// Estribo de 135° del cuadro 2 ("Dimensiones mínimas para ganchos
        /// estándar", #65). null de la #6 a la #8: el cuadro no les da estribo.
        /// </summary>
        public Gancho? GanchoEstribo135 { get; init; }

        /// <summary>Gancho de 90° del cuadro 2.</summary>
        public required Gancho Gancho90Cuadro { get; init; }

        /// <summary>Gancho de 180° del cuadro 2 (el de las grapas).</summary>
        public required Gancho Gancho180Cuadro { get; init; }
    }

    /// <summary>
    /// Un gancho del cuadro 2, en metros: la extensión recta (E) y el diámetro
    /// mínimo de doblado (D). Se guardan los valores del cuadro y no la regla
    /// que los genera (6 d, 4 d...): el cuadro manda (#64).
    /// </summary>
    public sealed record Gancho(double Extension, double DiametroDoblado);

    /// <summary>
    /// Dos barras del cuadro que se lleven menos que esto son la misma: los
    /// diámetros nominales están separados por 3 mm o más, y lo que llega del
    /// documento puede venir con redondeos de conversión.
    /// </summary>
    public const double ToleranciaMm = 0.6;

    public static readonly IReadOnlyList<Varilla> Cuadro = new[]
    {
        // Cuadro 2 (#65), E / D en metros.
        new Varilla(2,  6.4, 0.076, 0.112, 0.096, null)
            { GanchoEstribo135 = new(0.038, 0.026), Gancho90Cuadro = new(0.077, 0.038), Gancho180Cuadro = new(0.065, 0.038) },
        new Varilla(3,  9.5, 0.113, 0.166, 0.142, 0.55)
            { GanchoEstribo135 = new(0.057, 0.038), Gancho90Cuadro = new(0.114, 0.057), Gancho180Cuadro = new(0.065, 0.057) },
        new Varilla(4, 12.7, 0.151, 0.222, 0.190, 0.75)
            { GanchoEstribo135 = new(0.076, 0.051), Gancho90Cuadro = new(0.152, 0.076), Gancho180Cuadro = new(0.065, 0.076) },
        new Varilla(5, 15.9, 0.189, 0.278, 0.238, 0.90)
            { GanchoEstribo135 = new(0.095, 0.064), Gancho90Cuadro = new(0.191, 0.095), Gancho180Cuadro = new(0.065, 0.095) },
        new Varilla(6, 19.1, 0.272, 0.334, 0.286, 1.10)
            { Gancho90Cuadro = new(0.229, 0.115), Gancho180Cuadro = new(0.076, 0.115) },
        new Varilla(7, 22.2, 0.316, 0.388, 0.333, 1.35)
            { Gancho90Cuadro = new(0.266, 0.133), Gancho180Cuadro = new(0.089, 0.133) },
        new Varilla(8, 25.4, 0.362, 0.444, 0.381, 1.70)
            { Gancho90Cuadro = new(0.305, 0.152), Gancho180Cuadro = new(0.102, 0.152) },
    };

    /// <summary>La varilla del cuadro con ese diámetro, o null si no es ninguno de los normalizados.</summary>
    public static Varilla? PorDiametro(double diametroMm) =>
        Cuadro.FirstOrDefault(v => System.Math.Abs(v.DiametroMm - diametroMm) < ToleranciaMm);

    /// <summary>El diámetro nominal de la varilla número N, o null si ese número no está en el cuadro.</summary>
    public static double? DiametroDe(int numero) =>
        Cuadro.FirstOrDefault(v => v.Numero == numero)?.DiametroMm;

    /// <summary>
    /// Nombre del tipo de barra propio del add-in para la varilla número N
    /// ("#4 CQEING"). Se busca por NOMBRE y no por diámetro porque por diámetro
    /// una barra métrica pasa por nuestra: la "16 B 400 S" de una plantilla
    /// europea queda a 0,1 mm de la #5 (19-oct). Ver
    /// <c>docs/planes/2026-10-19-varillas-propias-cqeing.md</c>.
    /// </summary>
    public static string NombrePropio(int numero) => $"#{numero} CQEING";

    /// <summary>La varilla del cuadro cuyo tipo propio se llama así, o null si el nombre no es uno de los nuestros.</summary>
    public static Varilla? PorNombrePropio(string nombre) =>
        Cuadro.FirstOrDefault(v => NombrePropio(v.Numero) == nombre);

    /// <summary>
    /// Si una barra sirve para estribos (y para cajas y grapas, que usan la
    /// misma barra) según el cuadro -- #64, "Filtro de la barra de estribo":
    /// se comparan su doblado de estribo con la D del estribo de 135° y su
    /// doblado de gancho estándar con la D del de 180° (el de las grapas).
    /// Hasta 1 mm (el cuadro está redondeado al milímetro) es apta; hasta un
    /// diámetro de barra, apta con aviso; más, no apta.
    /// </summary>
    public static (AptitudEstribo Aptitud, string? Motivo) EvaluarParaEstribo(TipoBarra barra)
    {
        Varilla? varilla = PorNombrePropio(barra.Nombre) ?? PorDiametro(barra.DiametroMm);
        if (varilla is null)
            return (AptitudEstribo.NoApta, $"«{barra.Nombre}» no está en el cuadro de la empresa.");
        if (varilla.GanchoEstribo135 is not { } estribo)
            return (AptitudEstribo.NoApta, $"«{barra.Nombre}»: el cuadro no da estribo de 135° para la #{varilla.Numero}.");

        var diferencias = new[]
        {
            (Que: "doblado de estribo", Mide: barra.DobladoEstriboMm, Pide: estribo.DiametroDoblado * 1000),
            (Que: "doblado de gancho estándar", Mide: barra.DobladoGanchoMm, Pide: varilla.Gancho180Cuadro.DiametroDoblado * 1000),
        };

        var peor = diferencias.MaxBy(d => System.Math.Abs(d.Mide - d.Pide));
        double diferencia = System.Math.Abs(peor.Mide - peor.Pide);
        if (diferencia <= 1) return (AptitudEstribo.Apta, null);

        var es = System.Globalization.CultureInfo.GetCultureInfo("es-CO");
        string motivo = string.Format(es, "«{0}»: {1} de {2:0.0} cm; el cuadro pide {3:0.0} cm.",
            barra.Nombre, peor.Que, peor.Mide / 10, peor.Pide / 10);
        return (diferencia <= barra.DiametroMm ? AptitudEstribo.AptaConAviso : AptitudEstribo.NoApta, motivo);
    }
}
