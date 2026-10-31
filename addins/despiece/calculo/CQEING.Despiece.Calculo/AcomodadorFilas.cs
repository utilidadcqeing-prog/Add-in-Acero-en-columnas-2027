namespace CQEING.Despiece.Calculo;

/// <summary>Lo que ocupa una barra con su texto. <paramref name="Indice"/> dice de qué barra se trata.</summary>
public sealed record PiezaFila(int Indice, double Izquierda, double Derecha, double Alto, double AltoBarra = 0, double Separacion = 0)
{
    /// <summary>La barra abajo y, encima, su texto centrado. Si el texto es más ancho que la barra, manda el texto.</summary>
    public static PiezaFila ConTexto(int indice, double izquierda, double derecha, double altoBarra, double anchoTexto, double altoTexto, double separacion)
    {
        double medio = (izquierda + derecha) / 2;
        return new PiezaFila(indice, Math.Min(izquierda, medio - anchoTexto / 2), Math.Max(derecha, medio + anchoTexto / 2), altoBarra + separacion + altoTexto, altoBarra, separacion);
    }
}

/// <summary>Una pieza ya ubicada: su fila y la altura de su borde de abajo.</summary>
public sealed record PiezaUbicada(PiezaFila Pieza, int Fila, double Piso)
{
    public double BaseTexto => Piso + Pieza.AltoBarra + Pieza.Separacion;

    public double TopeTexto => Piso + Pieza.Alto;
}

/// <summary>El resultado: dónde quedó cada pieza y entre qué alturas. <paramref name="Hasta"/> es el borde más lejano en el sentido en que se acomodó.</summary>
public sealed record Acomodo(IReadOnlyList<PiezaUbicada> Piezas, double Desde, double Hasta);

/// <summary>Reparte barras con su texto en filas, sin que nada se encime.</summary>
public static class AcomodadorFilas
{
    /// <summary>Cada pieza va en la primera fila donde quepa, desde <paramref name="desde"/> hacia afuera.</summary>
    public static Acomodo Acomodar(IEnumerable<PiezaFila> piezas, double desde, bool haciaArriba, double huecoEnFila, double huecoFilas)
    {
        var finDeFila = new List<double>();
        var filas = new List<List<PiezaFila>>();
        foreach (PiezaFila p in piezas.OrderBy(p => p.Izquierda))
        {
            int fila = finDeFila.FindIndex(fin => p.Izquierda >= fin + huecoEnFila);
            if (fila < 0)
            {
                finDeFila.Add(p.Derecha);
                filas.Add([p]);
            }
            else
            {
                finDeFila[fila] = p.Derecha;
                filas[fila].Add(p);
            }
        }

        var ubicadas = new List<PiezaUbicada>();
        double z = desde;
        for (int i = 0; i < filas.Count; i++)
        {
            double alto = filas[i].Max(p => p.Alto);
            double piso = haciaArriba ? z : z - alto;
            ubicadas.AddRange(filas[i].Select(p => new PiezaUbicada(p, i, piso)));
            z = haciaArriba ? z + alto : z - alto;
            if (i < filas.Count - 1)
                z = haciaArriba ? z + huecoFilas : z - huecoFilas;
        }
        return new Acomodo(ubicadas, desde, z);
    }

    /// <summary>Las filas, apiladas y con su centro en <paramref name="centro"/>. <see cref="Acomodo.Desde"/> es el borde de abajo.</summary>
    public static Acomodo Centrar(IEnumerable<PiezaFila> piezas, double centro, double huecoEnFila, double huecoFilas)
    {
        Acomodo a = Acomodar(piezas, 0, true, huecoEnFila, huecoFilas);
        double abajo = centro - a.Hasta / 2;
        return new Acomodo(a.Piezas.Select(u => u with { Piso = u.Piso + abajo }).ToList(), abajo, abajo + a.Hasta);
    }

    /// <summary>Apila hacia abajo desde <paramref name="tope"/> y devuelve el borde de abajo de cada uno.</summary>
    public static IReadOnlyList<double> Apilar(IEnumerable<double> altos, double tope, double hueco)
    {
        var pisos = new List<double>();
        double z = tope;
        foreach (double alto in altos)
        {
            pisos.Add(z - alto);
            z -= alto + hueco;
        }
        return pisos;
    }
}
