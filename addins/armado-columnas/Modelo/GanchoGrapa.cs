namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Dónde va el gancho de 180° de una grapa para que ABRACE la barra
/// longitudinal: la barra queda adentro del doblez, tocando su cara interior
/// en la punta (#130, docs/planes/2026-10-21-diagnostico-grapas-130.md). Antes
/// (20-oct) el arco se ponía pasando por el eje de la barra, y en planta la
/// atravesaba (medido con el MCP en la columna 387983).
///
/// Todo en planta y en las mismas unidades; sin Revit, para poder probarlo.
/// </summary>
public static class GanchoGrapa
{
    /// <summary>Una pieza de barra longitudinal a la altura de la grapa: su eje en planta, su radio y hasta qué altura sigue.</summary>
    public readonly record struct Pieza(double X, double Y, double Radio, double ZSuperior);

    /// <summary>
    /// Qué piezas abraza el gancho. En un traslapo hay dos, una al lado de la
    /// otra: si entran juntas en el doblez se abrazan las dos; si no, solo la
    /// que sigue más arriba, y <c>Caben</c> sale en false para avisar
    /// (decisión de Luciano, 21-oct).
    /// </summary>
    public static (IReadOnlyList<Pieza> Abrazadas, bool Caben) Elegir(IReadOnlyList<Pieza> piezas, double radioInterior)
    {
        if (piezas.Count == 0) throw new ArgumentException("no hay ninguna pieza que abrazar", nameof(piezas));
        if (Entran(piezas, radioInterior)) return (piezas, true);

        Pieza una = piezas.OrderByDescending(p => p.ZSuperior).First();
        return (new[] { una }, piezas.Count == 1 && una.Radio <= radioInterior);
    }

    /// <summary>
    /// Si las piezas entran juntas en un doblez de ese radio interior: el
    /// círculo que las encierra, centrado entre ellas, no puede ser más grande.
    /// </summary>
    public static bool Entran(IReadOnlyList<Pieza> piezas, double radioInterior)
    {
        (double cx, double cy) = Centro(piezas);
        return piezas.All(p => Math.Sqrt(Cuadrado(p.X - cx) + Cuadrado(p.Y - cy)) + p.Radio <= radioInterior + 1e-9);
    }

    /// <summary>
    /// El centro del doblez: de costado, en el medio de las piezas abrazadas;
    /// a lo largo de la grapa, lo más hacia adentro posible sin que ninguna
    /// pieza se salga del doblez -- o sea, las piezas tocan la cara interior
    /// en la punta del gancho, como una grapa real.
    /// </summary>
    /// <param name="haciaAdentro">Versor a lo largo de la grapa, desde esta punta hacia la otra.</param>
    public static (double X, double Y) CentroDelDoblez(
        IReadOnlyList<Pieza> abrazadas, double radioInterior, (double X, double Y) haciaAdentro)
    {
        (double tx, double ty) = Centro(abrazadas);
        (double ux, double uy) = haciaAdentro;

        // Cada pieza, respecto del medio: "a" a lo largo (hacia adentro) y "b"
        // de costado. Con el centro corrido "c" hacia adentro, la pieza queda a
        // sqrt((a - c)² + b²) del centro, y tiene que ser <= radioInterior - radio.
        double c = abrazadas.Min(p =>
        {
            double a = (p.X - tx) * ux + (p.Y - ty) * uy;
            double b = -(p.X - tx) * uy + (p.Y - ty) * ux;
            double libre = Math.Max(0, radioInterior - p.Radio);
            return a + Math.Sqrt(Math.Max(0, libre * libre - b * b));
        });

        return (tx + ux * c, ty + uy * c);
    }

    /// <summary>
    /// Hacia qué lado abre cada gancho de una grapa que va de
    /// <paramref name="desde"/> a <paramref name="hasta"/> (las barras que
    /// engancha): +1 a la izquierda (girando 90° antihorario desde
    /// desde→hasta, visto desde arriba), −1 a la derecha. La C abre hacia el
    /// centro de la sección; "Voltear" invierte las dos puntas y la S, la del
    /// fin.
    ///
    /// Se calcula UNA vez, con la línea entre las barras, y lo usan la vista
    /// previa, la línea que se le pide a Revit y la orientación de los ganchos
    /// (#167): antes el generador lo volvía a decidir con la línea ya
    /// inclinada de la S, que en la grapa del medio pasa justo por el centro,
    /// y el redondeo daba vuelta los dos ganchos. Si el centro cae sobre la
    /// línea (grapa del medio) se toma siempre la izquierda.
    /// </summary>
    public static (int Inicio, int Fin) Lados(
        (double X, double Y) desde, (double X, double Y) hasta, (double X, double Y) centroSeccion,
        FormaMenor forma, bool volteado)
    {
        double largo = Math.Sqrt(Cuadrado(hasta.X - desde.X) + Cuadrado(hasta.Y - desde.Y));
        if (largo <= 1e-12) throw new ArgumentException("los dos ganchos caen en el mismo punto");

        (double ux, double uy) = ((hasta.X - desde.X) / largo, (hasta.Y - desde.Y) / largo);
        double haciaElCentro = -uy * (centroSeccion.X - desde.X) + ux * (centroSeccion.Y - desde.Y);
        int ladoCentro = haciaElCentro >= -1e-6 * largo ? 1 : -1;

        int inicio = volteado ? -ladoCentro : ladoCentro;
        int fin = (forma == FormaMenor.GrapaS) != volteado ? -ladoCentro : ladoCentro;
        return (inicio, fin);
    }

    /// <summary>
    /// La línea que hay que pedirle a Revit para que los dos ganchos queden
    /// con su centro donde se quiere. <paramref name="inicio"/> y
    /// <paramref name="fin"/> son lo MEDIDO en una grapa de prueba con el
    /// gancho de cada punta: dónde queda el centro del gancho respecto de la
    /// punta de la línea pedida, a lo largo de la grapa (hacia adentro) y de
    /// costado (hacia donde abre). Pueden diferir si cada punta lleva otro
    /// ángulo.
    /// </summary>
    /// <param name="ladoInicio">
    /// +1 si el gancho del inicio abre hacia la izquierda de la grapa (girando
    /// 90° antihorario desde inicio→fin, visto desde arriba); −1 si abre a la derecha.
    /// </param>
    /// <param name="ladoFin">Ídem para el gancho del fin. Distinto de <paramref name="ladoInicio"/> en una S.</param>
    public static ((double X, double Y) Desde, (double X, double Y) Hasta) Linea(
        (double X, double Y) centroInicio, (double X, double Y) centroFin,
        (double Axial, double Lateral) inicio, (double Axial, double Lateral) fin, int ladoInicio, int ladoFin)
    {
        // Lo medido es respecto de la línea pedida, y en una S (o si los dos
        // centros no están a la misma altura de la sección) esa línea no es
        // paralela a la que une los centros: se ajusta su dirección hasta que
        // no cambie.
        (double X, double Y) desde = centroInicio, hasta = centroFin;
        for (int i = 0; i < 20; i++)
        {
            double largo = Math.Sqrt(Cuadrado(hasta.X - desde.X) + Cuadrado(hasta.Y - desde.Y));
            if (largo <= 1e-9) throw new ArgumentException("los dos ganchos caen en el mismo punto");

            (double ux, double uy) = ((hasta.X - desde.X) / largo, (hasta.Y - desde.Y) / largo);
            (double lx, double ly) = (-uy, ux);
            (double X, double Y) nuevoDesde =
                (centroInicio.X - ux * inicio.Axial - lx * ladoInicio * inicio.Lateral,
                 centroInicio.Y - uy * inicio.Axial - ly * ladoInicio * inicio.Lateral);
            (double X, double Y) nuevoHasta =
                (centroFin.X + ux * fin.Axial - lx * ladoFin * fin.Lateral,
                 centroFin.Y + uy * fin.Axial - ly * ladoFin * fin.Lateral);

            bool quieto = Math.Abs(nuevoDesde.X - desde.X) + Math.Abs(nuevoDesde.Y - desde.Y) +
                          Math.Abs(nuevoHasta.X - hasta.X) + Math.Abs(nuevoHasta.Y - hasta.Y) < 1e-12;
            (desde, hasta) = (nuevoDesde, nuevoHasta);
            if (quieto) break;
        }

        return (desde, hasta);
    }

    private static (double X, double Y) Centro(IReadOnlyList<Pieza> piezas) =>
        (piezas.Average(p => p.X), piezas.Average(p => p.Y));

    private static double Cuadrado(double v) => v * v;
}
