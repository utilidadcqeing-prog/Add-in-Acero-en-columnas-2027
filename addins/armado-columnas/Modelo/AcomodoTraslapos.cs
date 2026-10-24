namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Hacia qué lado queda la pieza corrida del traslapo de cada varilla, para
/// que no choque con las vecinas.
///
/// En cada traslapo Revit corre una de las dos piezas un diámetro a lo largo
/// de la cara, y la deja corrida en TODO su largo; lo único que se puede
/// elegir es cuál de los dos grupos de piezas queda en la posición pedida (el
/// otro queda un diámetro para un lado o para el otro; ver
/// <c>BarraContinua.Alinear</c>). Medido el 21-oct en la columna 387983: la
/// pieza corrida de la #8 de esquina y la de la #6 vecina iban una hacia la
/// otra y quedaban a 5,5 mm de centro a centro entre 1,2 y 6,0 m.
///
/// Sin Revit: recibe las dos maneras de dejar cada varilla, con las piezas ya
/// medidas, y elige la combinación con menos choque.
/// </summary>
public static class AcomodoTraslapos
{
    /// <summary>Una pieza de varilla: eje en planta, radio y alturas que cubre. Mismas unidades en todo.</summary>
    public readonly record struct Pieza(double X, double Y, double Radio, double ZInf, double ZSup);

    /// <summary>
    /// Las maneras posibles de dejar una varilla, cada una con dónde queda cada
    /// pieza. La primera es la de siempre. Una varilla sin traslapos, o donde
    /// las otras se saldrían de las esquinas, tiene una sola.
    /// </summary>
    public sealed record Varilla(IReadOnlyList<IReadOnlyList<Pieza>> Opciones)
    {
        /// <summary>La de siempre y, si existe, la otra (el otro grupo en la posición pedida).</summary>
        public Varilla(IReadOnlyList<Pieza> comun, IReadOnlyList<Pieza>? volteada)
            : this(volteada is null ? new[] { comun } : new[] { comun, volteada })
        {
        }
    }

    /// <summary>Una de las cuatro maneras de dejar una varilla de dos piezas (ver <see cref="Cuatro"/>).</summary>
    /// <param name="DeArribaAAbajo">Si la varilla se dibuja de arriba hacia abajo.</param>
    /// <param name="CorridaLaDeArriba">Si la pieza corrida es la de arriba (si no, la de abajo).</param>
    /// <param name="Signo">+1 si la corrida queda hacia el lado positivo del eje; −1 si hacia el negativo.</param>
    public sealed record Manera(IReadOnlyList<Pieza> Piezas, bool DeArribaAAbajo, bool CorridaLaDeArriba, int Signo);

    /// <summary>
    /// Las maneras de dejar una varilla de DOS piezas (un traslapo) que quedan
    /// dentro de las esquinas: la pieza corrida puede ser la de abajo o la de
    /// arriba, y quedar hacia un lado o hacia el otro del eje en que corre.
    ///
    /// Medido el 21-oct en la columna 387983 (con normal X y con normal Y):
    /// Revit corre +1 diámetro, hacia el lado positivo del eje, la pieza por
    /// donde EMPIEZA el dibujo de la varilla -- la de abajo si se dibuja de
    /// abajo hacia arriba, la de arriba si se dibuja al revés. La que no se
    /// corre puede ir en la posición pedida o un diámetro hacia el otro lado
    /// (la otra queda entonces en la posición pedida). Las cuatro combinaciones
    /// salen eligiendo el sentido del dibujo y cuál grupo va en la posición
    /// pedida. La primera de la lista es la de siempre: dibujo de abajo hacia
    /// arriba, la de abajo corrida hacia +.
    /// </summary>
    /// <param name="eje">Versor del eje en que se corre la pieza (el de la normal de la barra).</param>
    /// <param name="limites">Donde puede quedar el eje de una pieza: XMin, XMax, YMin, YMax.</param>
    public static IReadOnlyList<Manera> Cuatro(
        double x, double y, double radio, (double X, double Y) eje, double diametro,
        (double ZInf, double ZSup) deAbajo, (double ZInf, double ZSup) deArriba,
        (double XMin, double XMax, double YMin, double YMax) limites)
    {
        const double holgura = 1e-9;
        bool Dentro(double px, double py) =>
            px >= limites.XMin - holgura && px <= limites.XMax + holgura &&
            py >= limites.YMin - holgura && py <= limites.YMax + holgura;

        var maneras = new List<Manera>();
        foreach ((bool arriba, int signo) in new[] { (false, 1), (true, -1), (true, 1), (false, -1) })
        {
            double cx = x + signo * diametro * eje.X, cy = y + signo * diametro * eje.Y;
            if (!Dentro(cx, cy)) continue;

            var piezaAbajo = arriba ? new Pieza(x, y, radio, deAbajo.ZInf, deAbajo.ZSup) : new Pieza(cx, cy, radio, deAbajo.ZInf, deAbajo.ZSup);
            var piezaArriba = arriba ? new Pieza(cx, cy, radio, deArriba.ZInf, deArriba.ZSup) : new Pieza(x, y, radio, deArriba.ZInf, deArriba.ZSup);

            // Revit corre hacia + la pieza donde empieza el dibujo: para que la
            // corrida sea la de abajo hacia +, o la de arriba hacia −, se dibuja
            // de abajo hacia arriba; en los otros dos casos, al revés.
            bool deArribaAAbajo = arriba == (signo > 0);
            maneras.Add(new Manera(new[] { piezaAbajo, piezaArriba }, deArribaAAbajo, arriba, signo));
        }

        return maneras;
    }

    /// <summary>
    /// Cuánto se meten las piezas de dos varillas una en la otra: por cada par
    /// de piezas que comparten altura, lo que se meten en planta por el largo
    /// en que lo comparten. Cero si no se tocan.
    /// </summary>
    public static double Choque(IReadOnlyList<Pieza> a, IReadOnlyList<Pieza> b)
    {
        double total = 0;
        foreach (Pieza p in a)
        foreach (Pieza q in b)
        {
            double comun = Math.Min(p.ZSup, q.ZSup) - Math.Max(p.ZInf, q.ZInf);
            if (comun <= 0) continue;
            double distancia = Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
            double metida = p.Radio + q.Radio - distancia;
            if (metida > 0) total += metida * comun;
        }

        return total;
    }

    /// <summary>
    /// Lo más que se mete una pieza de <paramref name="a"/> en una de
    /// <paramref name="b"/>, entre las que comparten altura. Cero si no se tocan.
    /// </summary>
    public static double MaximaMetida(IReadOnlyList<Pieza> a, IReadOnlyList<Pieza> b)
    {
        double maxima = 0;
        foreach (Pieza p in a)
        foreach (Pieza q in b)
        {
            if (Math.Min(p.ZSup, q.ZSup) - Math.Max(p.ZInf, q.ZInf) <= 0) continue;
            double distancia = Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
            maxima = Math.Max(maxima, p.Radio + q.Radio - distancia);
        }

        return maxima;
    }

    /// <summary>
    /// Qué manera de cada varilla (índice en <see cref="Varilla.Opciones"/>)
    /// deja el choque total lo más bajo posible, entre TODAS las combinaciones
    /// (Luciano, 21-oct: "la más óptima, moviendo las que sean necesarias").
    /// Entre las que chocan lo mismo, la que deja más varillas como siempre
    /// (manera 0).
    ///
    /// Se recorren las combinaciones varilla por varilla, en el orden del
    /// anillo, y se abandona un camino apenas lo que ya choca (más lo mínimo
    /// que van a chocar con los obstáculos las que faltan) no puede mejorar la
    /// mejor encontrada. Los choques de a pares se calculan una sola vez. Con
    /// pocas varillas y pocas maneras por varilla es instantáneo; aun así hay
    /// un tope de pasos y, si se llega, queda la mejor encontrada hasta ahí
    /// (<see cref="UltimaFueCompleta"/> en false).
    /// </summary>
    /// <param name="obstaculos">
    /// Lo que no se mueve y las piezas tampoco pueden tocar: los ganchos del
    /// estribo, en trocitos (cada uno un círculo con las alturas que cubre).
    /// </param>
    public static int[] Elegir(IReadOnlyList<Varilla> varillas, IReadOnlyList<Pieza>? obstaculos = null)
    {
        const double igual = 1e-12;
        const long topeDePasos = 50_000_000;
        int n = varillas.Count;
        int[] cuantas = varillas.Select(v => v.Opciones.Count).ToArray();

        // Lo que cada manera choca con los obstáculos no depende de las otras.
        double[][] contraObstaculos = varillas
            .Select(v => v.Opciones.Select(o => obstaculos is null ? 0 : Choque(o, obstaculos)).ToArray())
            .ToArray();

        // entre[j][i][kj, ki], para i < j.
        var entre = new double[n][][,];
        for (int j = 0; j < n; j++)
        {
            entre[j] = new double[j][,];
            for (int i = 0; i < j; i++)
            {
                var m = new double[cuantas[j], cuantas[i]];
                for (int kj = 0; kj < cuantas[j]; kj++)
                for (int ki = 0; ki < cuantas[i]; ki++)
                    m[kj, ki] = Choque(varillas[j].Opciones[kj], varillas[i].Opciones[ki]);
                entre[j][i] = m;
            }
        }

        // Lo mínimo que les falta chocar con los obstáculos a las varillas de
        // j en adelante: una cota que nunca se pasa.
        var minimoQueFalta = new double[n + 1];
        for (int j = n - 1; j >= 0; j--)
            minimoQueFalta[j] = minimoQueFalta[j + 1] + (cuantas[j] == 0 ? 0 : contraObstaculos[j].Min());

        var actual = new int[n];
        var mejor = new int[n];
        double mejorChoque = 0;
        for (int j = 0; j < n; j++)
        {
            mejorChoque += cuantas[j] == 0 ? 0 : contraObstaculos[j][0];
            for (int i = 0; i < j; i++)
                if (cuantas[i] > 0 && cuantas[j] > 0) mejorChoque += entre[j][i][0, 0];
        }

        int mejorCambios = 0;
        long pasos = 0;
        bool completa = true;

        void Probar(int j, double choque, int cambios)
        {
            if (!completa) return;
            if (j == n)
            {
                if (choque < mejorChoque - igual || (choque <= mejorChoque + igual && cambios < mejorCambios))
                {
                    mejorChoque = choque;
                    mejorCambios = cambios;
                    Array.Copy(actual, mejor, n);
                }

                return;
            }

            if (cuantas[j] == 0)
            {
                actual[j] = 0;
                Probar(j + 1, choque, cambios);
                return;
            }

            for (int k = 0; k < cuantas[j]; k++)
            {
                if (++pasos > topeDePasos) { completa = false; return; }

                double conEsta = choque + contraObstaculos[j][k];
                for (int i = 0; i < j; i++)
                    if (cuantas[i] > 0) conEsta += entre[j][i][k, actual[i]];

                int conCambios = cambios + (k == 0 ? 0 : 1);
                double cota = conEsta + minimoQueFalta[j + 1];
                if (cota > mejorChoque + igual) continue;
                if (cota >= mejorChoque - igual && conCambios >= mejorCambios) continue;

                actual[j] = k;
                Probar(j + 1, conEsta, conCambios);
            }
        }

        Probar(0, 0, 0);
        UltimaFueCompleta = completa;
        return mejor;
    }

    /// <summary>
    /// Si la última llamada a <see cref="Elegir"/> recorrió todas las
    /// combinaciones (false: llegó al tope y devolvió la mejor que tenía).
    /// </summary>
    public static bool UltimaFueCompleta { get; private set; } = true;

    /// <summary>
    /// Una varilla de la sección, como la ve <see cref="PlanearSeccion"/>.
    /// Mismas unidades en todo (el generador pasa pies; las ventanas, metros).
    /// </summary>
    /// <param name="Normal">Versor en planta de la normal de la barra (X, Y o diagonal).</param>
    /// <param name="Entera">Alturas de la varilla entera, para cuando no lleva traslapo.</param>
    /// <param name="Abajo">Alturas de la pieza de abajo si lleva UN traslapo (dos piezas); null si no.</param>
    /// <param name="Arriba">Alturas de la pieza de arriba, con el mismo criterio.</param>
    public sealed record PedidoSeccion(
        double X, double Y, double Radio, double Diametro, bool Esquina, bool ConGancho,
        (double X, double Y) Normal, (double X, double Y) CentroSeccion, (double ZInf, double ZSup) Entera,
        (double ZInf, double ZSup)? Abajo, (double ZInf, double ZSup)? Arriba,
        (double XMin, double XMax, double YMin, double YMax) Limites);

    /// <summary>
    /// Hacia dónde se corre la pieza del traslapo de cada varilla de la
    /// sección para que no choque con las vecinas ni con
    /// <paramref name="obstaculos"/> (los ganchos del estribo): las cuatro
    /// maneras (<see cref="Cuatro"/>) por cada eje que se prueba y la mejor
    /// combinación (<see cref="Elegir"/>). Antes vivía dentro de
    /// GeneradorLongitudinal; sale acá para que la ventana muestre lo mismo
    /// que se genera (#131). <c>Manera</c> = null si la varilla no lleva un
    /// traslapo (va como siempre); <c>Eje</c> = el eje elegido (la normal si
    /// no hay manera).
    /// </summary>
    public static IReadOnlyList<(Manera? Manera, (double X, double Y) Eje)> PlanearSeccion(
        IReadOnlyList<PedidoSeccion> pedidos, IReadOnlyList<Pieza>? obstaculos)
    {
        var maneras = new List<List<(Manera Manera, (double X, double Y) Eje)>?>();
        foreach (PedidoSeccion p in pedidos)
        {
            if (p.Abajo is not { } abajo || p.Arriba is not { } arriba)
            {
                maneras.Add(null);
                continue;
            }

            // Una esquina apoya en dos caras, así que su pieza corrida puede ir
            // a lo largo de cualquiera de las dos: se prueban los dos ejes
            // (primero el de siempre). Medido el 21-oct en la columna 387983:
            // con eje X en las dos de arriba, la corrida de la esquina iba a lo
            // largo de la cara de arriba, contra la vecina (se metían 8,8 y
            // 12 mm), mientras que abajo, con eje Y, la cara quedaba limpia.
            //
            // Además, la diagonal hacia adentro: en la esquina donde cierra el
            // estribo, las colas de los dos ganchos entran en diagonal, una a
            // cada lado de la barra, y lo único libre es correrla entre ellas
            // (Luciano, 21-oct: "que se mueva diagonalmente, pero pegada al
            // gancho").
            //
            // Medido el 21-oct en la columna 387983 con normales diagonales:
            // Revit corre la pieza como si la normal tuviera siempre la Y
            // positiva -- con (0,707, 0,707) la corrió hacia (+, +), pero con
            // (−0,707, −0,707) también hacia (+, +), y con (0,707, −0,707)
            // hacia (−, +). Si se le pasa al revés, la pieza sale para el otro
            // lado y Alinear tiene que mover la cadena en diagonal, cosa que
            // Revit no respeta (en la esquina de arriba a la derecha la corrió
            // 36 mm en Y en vez de 18 y 18, y la barra quedó atravesando el
            // estribo). Por eso la diagonal se pasa con la Y positiva: Cuatro
            // prueba los dos sentidos igual, y así lo que se planea es lo que
            // Revit hace. Los ejes X e Y ya van positivos.
            //
            // Si la barra lleva gancho, no se prueba la diagonal: el gancho
            // dobla perpendicular a la normal y con la diagonal no miraría al
            // centro.
            (double X, double Y) diagonal = Versor(Math.Sign(p.CentroSeccion.X - p.X), Math.Sign(p.CentroSeccion.Y - p.Y));
            if (diagonal.Y < 0) diagonal = (-diagonal.X, -diagonal.Y);
            bool normalEnX = Math.Abs(p.Normal.X - 1) < 1e-9 && Math.Abs(p.Normal.Y) < 1e-9;
            (double X, double Y) otroEje = normalEnX ? (0, 1) : (1, 0);
            (double X, double Y)[] ejes = !p.Esquina ? new[] { p.Normal }
                : p.ConGancho ? new[] { p.Normal, otroEje }
                : new[] { p.Normal, otroEje, diagonal };

            var deEstaVarilla = new List<(Manera, (double X, double Y))>();
            foreach ((double X, double Y) eje in ejes)
                foreach (Manera m in Cuatro(p.X, p.Y, p.Radio, eje, p.Diametro, abajo, arriba, p.Limites))
                    deEstaVarilla.Add((m, eje));
            maneras.Add(deEstaVarilla);
        }

        int[] elegidas = Elegir(maneras.Select((m, i) => m is { Count: > 0 }
            ? new Varilla(m.Select(x => x.Manera.Piezas).ToList())
            : new Varilla(new[] { new[] { new Pieza(pedidos[i].X, pedidos[i].Y, pedidos[i].Radio, pedidos[i].Entera.ZInf, pedidos[i].Entera.ZSup) } })).ToList(),
            obstaculos);

        return maneras.Select((m, i) => m is { Count: > 0 }
                ? ((Manera?)m[elegidas[i]].Manera, m[elegidas[i]].Eje)
                : (null, pedidos[i].Normal))
            .ToList();
    }

    private static (double X, double Y) Versor(double x, double y)
    {
        double largo = Math.Sqrt(x * x + y * y);
        return largo < 1e-12 ? (0, 0) : (x / largo, y / largo);
    }
}
