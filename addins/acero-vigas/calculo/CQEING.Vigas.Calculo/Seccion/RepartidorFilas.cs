using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Seccion;

/// <summary>
/// Reparte las barras de cada cara de la sección en filas (#88) y dice dónde va cada una en el corte. Sigue la
/// maqueta (paso 1 "Barras" y corte 1-1) y el plan docs/planes/2026-10-16-capas-y-filas-vigas-88.md.
/// </summary>
public static class RepartidorFilas
{
    public static ResultadoSeccion Repartir(EntradaSeccion entrada)
    {
        CaraSeccion arriba = RepartirCara(entrada, Posicion.Superior, entrada.Superior, entrada.Recubrimiento.Arriba);
        CaraSeccion abajo = RepartirCara(entrada, Posicion.Inferior, entrada.Inferior, entrada.Recubrimiento.Abajo);
        if (SePisan(entrada, arriba, abajo))
        {
            arriba = ConErrorDeAlto(arriba);
            abajo = ConErrorDeAlto(abajo);
        }
        return new ResultadoSeccion(arriba, abajo);
    }

    private static CaraSeccion RepartirCara(EntradaSeccion e, Posicion cara, IReadOnlyList<GrupoBarras> grupos, double recubrimiento)
    {
        double estribo = Diametro(e, e.DiametroEstribo);
        double libre = e.Reglas.DistanciaLibre;
        double mayor = grupos.Max(g => Diametro(e, g.Diametro));
        double disponible = e.Ancho - 2 * e.Recubrimiento.Lados - 2 * estribo;
        int caben = Math.Max(0, (int)Math.Floor((disponible + libre) / (mayor + libre)));
        int maximo = caben * e.MaximoFilas;

        List<int> numeros = grupos.SelectMany(g => Enumerable.Repeat(g.Diametro, g.Cantidad)).ToList();
        ErrorCara error = numeros.Count < 2 ? ErrorCara.MenosDeDosBarras
            : caben == 0 || numeros.Count > maximo ? ErrorCara.NoCaben
            : ErrorCara.Ninguno;
        if (caben == 0)
            return new CaraSeccion(cara, 0, [], error, 0, recubrimiento + estribo + Diametro(e, grupos[0].Diametro) / 2);

        List<List<int>> porFila = EnFilas(grupos, caben);
        if (error == ErrorCara.Ninguno && grupos.Any(g => g.Fila is not null))
        {
            error = grupos.Any(g => g.Fila is < 1 || g.Fila > e.MaximoFilas) || porFila.Count > e.MaximoFilas
                || porFila.Any(f => f.Count > caben) ? ErrorCara.NoCaben
                : porFila[0].Count < 2 ? ErrorCara.MenosDeDosBarras
                : ErrorCara.Ninguno;
        }
        // una fila que quedó vacía no se dibuja
        List<List<int>> enFilas = porFila.Where(f => f.Count > 0).ToList();
        // las esquinas son del primer grupo de la fila 1
        double esquina = Diametro(e, enFilas[0][0]);
        double alEje = recubrimiento + estribo + esquina / 2;

        // Puestos de una fila llena, de esquina a esquina y a la misma distancia.
        double izquierda = e.Recubrimiento.Lados + estribo + esquina / 2;
        double[] puestosLlenos = Puestos(izquierda, e.Ancho - izquierda, caben, e.Ancho);

        var filas = new List<IReadOnlyList<BarraEnSeccion>>();
        double borde = recubrimiento + estribo; // de la cara al borde de la fila actual
        foreach (List<int> enFila in enFilas)
        {
            // La fila 1 reparte sus barras de esquina a esquina; las demás usan los puestos de la fila 1 llena. Una
            // fila fija con más barras de las que caben (error NoCaben) se dibuja con sus puestos, para que se vea.
            double[] puestos = filas.Count == 0 || enFila.Count > caben ? Puestos(izquierda, e.Ancho - izquierda, enFila.Count, e.Ancho)
                : puestosLlenos;
            int?[] acomodo = Acomodar(enFila, puestos.Length);
            double bordeFila = borde;
            filas.Add(acomodo
                .Select((numero, i) => (numero, i))
                .Where(x => x.numero is not null)
                .Select(x =>
                {
                    double distancia = bordeFila + Diametro(e, x.numero!.Value) / 2;
                    double y = cara == Posicion.Inferior ? distancia : e.Alto - distancia;
                    return new BarraEnSeccion(x.numero.Value, puestos[x.i], y);
                })
                .ToList());
            borde += enFila.Max(n => Diametro(e, n)) + e.SeparacionFilas;
        }
        return new CaraSeccion(cara, caben, filas, error, maximo, alEje);
    }

    /// <summary>
    /// Los diámetros de cada fila (decisión 11 del plan de #84): primero los grupos con fila fija, en su fila; luego los
    /// Auto, en orden, llenan los puestos libres desde la fila 1, hasta <paramref name="caben"/> por fila. Sin filas fijas
    /// es el reparto de siempre. Puede devolver filas vacías (una fila fija más abajo que las demás).
    /// </summary>
    private static List<List<int>> EnFilas(IReadOnlyList<GrupoBarras> grupos, int caben)
    {
        var filas = new List<List<int>>();
        List<int> Fila(int i)
        {
            while (filas.Count <= i)
                filas.Add([]);
            return filas[i];
        }
        foreach (GrupoBarras g in grupos.Where(g => g.Fila is >= 1))
            Fila(g.Fila!.Value - 1).AddRange(Enumerable.Repeat(g.Diametro, g.Cantidad));
        int actual = 0;
        foreach (int numero in grupos.Where(g => g.Fila is not >= 1).SelectMany(g => Enumerable.Repeat(g.Diametro, g.Cantidad)))
        {
            while (Fila(actual).Count >= caben)
                actual++;
            Fila(actual).Add(numero);
        }
        return filas;
    }

    /// <summary><paramref name="cantidad"/> puestos de <paramref name="desde"/> a <paramref name="hasta"/> a la misma distancia; uno solo va al centro.</summary>
    internal static double[] Puestos(double desde, double hasta, int cantidad, double ancho) =>
        cantidad == 1 ? [ancho / 2]
            : Enumerable.Range(0, cantidad).Select(i => desde + i * (hasta - desde) / (cantidad - 1)).ToArray();

    /// <summary>
    /// Decisión 4 del plan: cada grupo, en el orden en que se escribió, ocupa pares simétricos de las esquinas hacia
    /// adentro; si le sobra una barra, va al centro (si está libre) o al primer puesto libre.
    /// </summary>
    private static int?[] Acomodar(IReadOnlyList<int> numeros, int puestos)
    {
        var acomodo = new int?[puestos];
        foreach (var grupo in Corridas(numeros))
        {
            int quedan = grupo.Cantidad;
            while (quedan >= 2 && SiguientePar(acomodo) is var (a, b))
            {
                acomodo[a] = acomodo[b] = grupo.Numero;
                quedan -= 2;
            }
            for (; quedan > 0; quedan--)
            {
                int centro = puestos / 2;
                int puesto = puestos % 2 == 1 && acomodo[centro] is null ? centro : Array.FindIndex(acomodo, p => p is null);
                acomodo[puesto] = grupo.Numero;
            }
        }
        return acomodo;
    }

    private static (int, int)? SiguientePar(int?[] acomodo)
    {
        for (int i = 0, j = acomodo.Length - 1; i < j; i++, j--)
            if (acomodo[i] is null && acomodo[j] is null)
                return (i, j);
        return null;
    }

    /// <summary>Barras seguidas del mismo diámetro, en orden.</summary>
    private static IEnumerable<(int Numero, int Cantidad)> Corridas(IReadOnlyList<int> numeros)
    {
        for (int i = 0; i < numeros.Count;)
        {
            int j = i;
            while (j < numeros.Count && numeros[j] == numeros[i])
                j++;
            yield return (numeros[i], j - i);
            i = j;
        }
    }

    /// <summary>Las filas de arriba y las de abajo no dejan entre sí la distancia libre.</summary>
    private static bool SePisan(EntradaSeccion e, CaraSeccion arriba, CaraSeccion abajo)
    {
        var barrasArriba = arriba.Filas.SelectMany(f => f).ToList();
        var barrasAbajo = abajo.Filas.SelectMany(f => f).ToList();
        if (barrasArriba.Count == 0 || barrasAbajo.Count == 0)
            return false;
        double techoAbajo = barrasAbajo.Max(b => b.Y + Diametro(e, b.Diametro) / 2);
        double pisoArriba = barrasArriba.Min(b => b.Y - Diametro(e, b.Diametro) / 2);
        return pisoArriba - techoAbajo < e.Reglas.DistanciaLibre;
    }

    private static CaraSeccion ConErrorDeAlto(CaraSeccion c) =>
        c.Error == ErrorCara.Ninguno ? c with { Error = ErrorCara.NoCabenEnElAlto } : c;

    internal static double Diametro(EntradaSeccion e, int numero) => e.Reglas.PorDiametro[numero].Diametro;
}
