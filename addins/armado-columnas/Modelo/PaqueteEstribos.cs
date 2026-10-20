namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// El paquete de una posición de estribo: el principal en el centro y sus
/// estribos menores apilados justo encima y justo debajo, tocándose, lo más
/// cerca posible del principal -- docs/planes/2026-10-20-diseno-estribos-menores-visual-y-64.md,
/// sección 2.
///
/// Sin Revit: lo usan el panel (grapas tapadas) y los dos generadores (el
/// reparto del principal y la altura de cada menor), así lo que se calcula en
/// un lado es lo mismo que en el otro.
///
/// Todas las medidas de salida en metros, medidas desde el PUNTO MEDIO del
/// paquete (positivo hacia arriba): la separación entre estribos se mide de
/// punto medio a punto medio (Ing. Carlos Quintero).
/// </summary>
public static class PaqueteEstribos
{
    /// <param name="Nivel">1 = el primero debajo del principal, 2 = el primero encima, 3 = el segundo debajo...</param>
    /// <param name="Desplazamiento">Eje de la pieza respecto del punto medio del paquete, en metros.</param>
    public sealed record NivelPieza(EstriboMenor Pieza, int Nivel, double Desplazamiento);

    /// <param name="DesplazamientoPrincipal">Eje del estribo principal respecto del punto medio, en metros.</param>
    /// <param name="Debajo">Del punto medio a la cara de abajo de la pieza más baja.</param>
    /// <param name="Encima">Del punto medio a la cara de arriba de la más alta.</param>
    public sealed record Paquete(double DesplazamientoPrincipal, double Debajo, double Encima, IReadOnlyList<NivelPieza> Piezas)
    {
        public double Alto => Debajo + Encima;
    }

    /// <summary>
    /// Una grapa (C o S) dentro del rango de un cerrado del mismo eje: gana la
    /// caja y la grapa no se genera (regla confirmada por Luciano).
    /// </summary>
    public static bool EstaTapada(EstriboMenor grapa, IEnumerable<EstriboMenor> piezas) =>
        grapa.Forma != FormaMenor.Cerrado &&
        piezas.Any(c => c.Forma == FormaMenor.Cerrado && c.Eje == grapa.Eje && c.Desde <= grapa.Desde && grapa.Desde <= c.Hasta);

    /// <summary>
    /// Dos piezas no pueden ir a la misma altura si en planta se cruzan o
    /// tocan una misma barra: las de distinto eje siempre se cruzan, y las del
    /// mismo eje cuando sus rangos se superponen (compartir un índice cuenta).
    /// </summary>
    public static bool Chocan(EstriboMenor a, EstriboMenor b) =>
        a.Eje != b.Eje || (a.Desde <= b.Hasta && b.Desde <= a.Hasta);

    /// <summary>
    /// Arma el paquete con la menor cantidad de niveles. Dentro de un eje, que
    /// dos piezas choquen es que sus rangos se crucen, así que repartirlas por
    /// <see cref="EstriboMenor.Desde"/> en el primer nivel libre es óptimo
    /// (coloreo de intervalos); entre ejes distintos todo choca. Los cerrados
    /// se reparten antes que las grapas y sus niveles van más cerca del
    /// principal (decisión sin un motivo de obra detrás: si la empresa tiene
    /// una costumbre, manda esa).
    /// </summary>
    public static Paquete Calcular(IReadOnlyList<EstriboMenor> piezas, double diametroPrincipalMm, Func<EstriboMenor, double> diametroMm)
    {
        List<EstriboMenor> vigentes = piezas.Where(p => !EstaTapada(p, piezas)).ToList();

        var niveles = new List<(EjeCaja Eje, List<EstriboMenor> Piezas)>();
        foreach (EstriboMenor pieza in vigentes
                     .OrderBy(p => p.Forma == FormaMenor.Cerrado ? 0 : 1)
                     .ThenBy(p => p.Desde))
        {
            int libre = niveles.FindIndex(n => n.Eje == pieza.Eje && n.Piezas.All(o => !Chocan(o, pieza)));
            if (libre < 0) niveles.Add((pieza.Eje, new List<EstriboMenor> { pieza }));
            else niveles[libre].Piezas.Add(pieza);
        }

        // Orden fijo, para que el resultado sea siempre el mismo: los que
        // tienen un cerrado, después Fila antes que Columna, después por dónde arrancan.
        niveles = niveles
            .OrderBy(n => n.Piezas.Any(p => p.Forma == FormaMenor.Cerrado) ? 0 : 1)
            .ThenBy(n => n.Eje == EjeCaja.Fila ? 0 : 1)
            .ThenBy(n => n.Piezas.Min(p => p.Desde))
            .ToList();

        // En milímetros, con el eje del principal en 0. Abajo y arriba se van
        // turnando: el nivel 1 debajo, el 2 encima, el 3 debajo del 1...
        var ejes = new List<(EstriboMenor Pieza, int Nivel, double Eje)>();
        (double Eje, double Diametro) abajo = (0, diametroPrincipalMm), arriba = (0, diametroPrincipalMm);
        for (int i = 0; i < niveles.Count; i++)
        {
            double d = niveles[i].Piezas.Max(diametroMm);
            bool debajo = i % 2 == 0;
            (double Eje, double Diametro) vecino = debajo ? abajo : arriba;
            double eje = debajo ? vecino.Eje - (vecino.Diametro + d) / 2 : vecino.Eje + (vecino.Diametro + d) / 2;
            if (debajo) abajo = (eje, d); else arriba = (eje, d);

            ejes.AddRange(niveles[i].Piezas.Select(p => (p, i + 1, eje)));
        }

        double caraAbajo = abajo.Eje - abajo.Diametro / 2;
        double caraArriba = arriba.Eje + arriba.Diametro / 2;
        double medio = (caraAbajo + caraArriba) / 2;

        return new Paquete(
            -medio / 1000,
            (medio - caraAbajo) / 1000,
            (caraArriba - medio) / 1000,
            ejes.Select(e => new NivelPieza(e.Pieza, e.Nivel, (e.Eje - medio) / 1000)).ToList());
    }

    /// <summary>
    /// Las alturas de los estribos que de verdad tiene un grupo de Revit, desde
    /// <paramref name="zInicio"/> hacia arriba. Con <paramref name="incluirUltimo"/>
    /// en false el grupo tiene la última posición pero sin estribo (es el
    /// primero del grupo siguiente): no se cuenta, o los menores verían dos
    /// estribos a 0 cm (20-oct).
    /// </summary>
    public static IEnumerable<double> PosicionesDelGrupo(int posiciones, double zInicio, double separacion, bool incluirUltimo)
    {
        int cantidad = incluirUltimo || posiciones <= 1 ? posiciones : posiciones - 1;
        for (int i = 0; i < cantidad; i++)
            yield return zInicio + i * separacion;
    }
}
