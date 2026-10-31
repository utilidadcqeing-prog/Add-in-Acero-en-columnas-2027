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

    /// <summary><c>3 #5 / L= 6.00</c>, con el largo en metros.</summary>
    public static string Barra(string tipo, int cantidad, double largoM) =>
        string.Format(CultureInfo.InvariantCulture, "{0} {1} / L= {2:0.00}", cantidad, Nombre(tipo), largoM);

    /// <summary><c>46 #3 / L= 0.99 / G= 0.08</c>: cantidad, largo total y gancho, en metros.</summary>
    public static string Estribo(string tipo, int cantidad, double largoM, double ganchoM) =>
        string.Format(CultureInfo.InvariantCulture, "{0} / G= {1:0.00}", Barra(tipo, cantidad, largoM), ganchoM);
}
