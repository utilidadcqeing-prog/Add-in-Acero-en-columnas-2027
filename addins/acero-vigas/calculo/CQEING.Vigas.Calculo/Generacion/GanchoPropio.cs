using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Generacion;

/// <summary>
/// Los ganchos propios del add-in (#85, decisión 8b del plan docs/planes/2026-10-19-generar-acero-vigas-85.md, Juan
/// 2026-10-19): un tipo de gancho de Revit por ángulo y diámetro ("CQEING 90° #5"), con la extensión recta que hace que la
/// barra mida en Revit lo mismo que en el tablero. Son tipos nuevos: los del proyecto no se tocan.
/// <para>
/// Cómo cuenta Revit (medido en la primera prueba): la línea que se le pasa termina en la cara exterior del doblez; la
/// parte recta llega hasta <c>R + d/2</c> antes, sigue el arco de radio <c>R</c> (al eje de la barra, con
/// <c>R = D/2 + d/2</c> y <c>D</c> el diámetro de doblado del gancho) y luego la extensión recta. Para que el gancho sume
/// <c>B</c> al largo: <c>extensión = B + R + d/2 − R·θ</c>. A 90° la pata por fuera mide <c>B</c>:
/// <c>extensión = B − R − d/2</c>, y el largo de Revit queda <see cref="DiferenciaCotas"/> por debajo de la suma de las cotas.
/// </para>
/// </summary>
public static class GanchoPropio
{
    /// <param name="b">Largo B del cuadro de Carlos, en mm.</param>
    /// <param name="diametro">Diámetro de la barra en Revit, en mm.</param>
    /// <param name="doblado">Diámetro de doblado del gancho en el tipo de barra de Revit, en mm.</param>
    /// <param name="grados">Ángulo del gancho.</param>
    /// <returns>La extensión recta en mm; nunca menos de un diámetro.</returns>
    public static double Extension(double b, double diametro, double doblado, int grados)
    {
        double radio = doblado / 2 + diametro / 2;
        double extension = grados == 90 ? b - radio - diametro / 2 : b + radio + diametro / 2 - radio * grados * Math.PI / 180;
        return Math.Max(diametro, extension);
    }

    /// <summary>
    /// Cuánto más suman las cotas de un gancho de 90° que el largo por el eje que da Revit, en mm: las dos cotas llegan a
    /// la esquina exterior y el eje va por el arco. <c>2 (R + d/2) − R·π/2</c>; 33 mm en la barra de 16 con doblado de 64.
    /// </summary>
    public static double DiferenciaCotas(double diametro, double doblado)
    {
        double radio = doblado / 2 + diametro / 2;
        return 2 * (radio + diametro / 2) - radio * Math.PI / 2;
    }

    /// <summary>
    /// El ángulo del gancho de un estribo y su cola: la extensión recta E del cuadro de Carlos, del bloque de estribos de
    /// 135° o del de ganchos de 180° (H2 del plan de 22-oct). Null si el cuadro no la trae (el de 90° y el 135° del #6 al
    /// #8): quien lo usa pone 6 diámetros.
    /// </summary>
    public static (int Grados, int? Cola) DeEstribo(GanchoEstribo g, ReglaDiametro r) => g switch
    {
        GanchoEstribo.De135 => (135, r.Estribo135.E),
        GanchoEstribo.De180 => (180, r.Gancho180.E),
        _ => (90, null),
    };

    /// <summary>El nombre del tipo: <c>"CQEING 90° #5"</c>, o <c>"CQEING Estribo 135° #3"</c>.</summary>
    public static string Nombre(int grados, int numero, bool estribo = false) =>
        estribo ? $"CQEING Estribo {grados}° #{numero}" : $"CQEING {grados}° #{numero}";
}
