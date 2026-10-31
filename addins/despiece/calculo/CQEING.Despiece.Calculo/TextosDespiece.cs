using System.Globalization;

namespace CQEING.Despiece.Calculo;

/// <summary>Los textos de las barras, en una sola línea y separados con "/" (Juan, 31-oct).</summary>
public static class TextosDespiece
{
    /// <summary>El número de barra, del nombre de su tipo ("#5 CQEING" da 5); 0 si el nombre no lo dice.</summary>
    public static int Numero(string tipo)
    {
        int i = tipo.IndexOf('#');
        if (i < 0)
            return 0;
        string digitos = new(tipo.Skip(i + 1).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digitos, out int n) ? n : 0;
    }

    public static string Nombre(string tipo)
    {
        int n = Numero(tipo);
        return n > 0 ? "#" + n : tipo;
    }

    /// <summary>
    /// <c>3 #5 L= 5.50/6.00</c>: tramo recto y largo total, en metros; lo que falta es del gancho (Juan, 31-oct). Sin
    /// <paramref name="rectoM"/>, o si a dos decimales es igual al total, solo el total: <c>3 #5 L= 6.00</c>.
    /// </summary>
    public static string Barra(string tipo, int cantidad, double largoM, double? rectoM = null)
    {
        string total = Metros(largoM);
        string largo = rectoM is { } recto && Metros(recto) != total ? $"{Metros(recto)}/{total}" : total;
        return $"{cantidad} {Nombre(tipo)} L= {largo}";
    }

    /// <summary><c>46 #3 L= 0.99 G= 0.08</c>: cantidad, largo total y gancho, en metros.</summary>
    public static string Estribo(string tipo, int cantidad, double largoM, double ganchoM) =>
        $"{Barra(tipo, cantidad, largoM)} G= {Metros(ganchoM)}";

    /// <summary>
    /// El largo que dicen las cotas de la barra: Revit mide por el eje y descuenta el doblez, las cotas miden por fuera.
    /// Cada gancho de 90° suma <c>2 (R + d/2) − R·π/2</c>, como <c>GanchoPropio.DiferenciaCotas</c> en acero de vigas.
    /// Sirve en cualquier unidad, si todo viene en la misma.
    /// </summary>
    public static double LargoPorCotas(double largoRevit, double radio, double diametro, int ganchosDe90) =>
        largoRevit + ganchosDe90 * (2 * (radio + diametro / 2) - radio * Math.PI / 2);

    private static string Metros(double valor) => valor.ToString("0.00", CultureInfo.InvariantCulture);
}
