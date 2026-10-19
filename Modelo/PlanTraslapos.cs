namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Dónde van los traslapos de la barra longitudinal de una columna y cuánto
/// mide cada varilla -- docs/planes/2026-10-17-diseno-metodologia-traslapos.md.
///
/// Sin Revit, en metros: lo usan la ventana (para dibujar A y B mientras se
/// configura, pestaña «Traslapos») y el generador (para crear exactamente
/// eso). Que sea el mismo cálculo es lo que garantiza que lo que se ve es lo
/// que se genera.
///
/// Reglas obligatorias (spec, sección 2): ningún traslapo toca un nudo; A
/// traslapa en los tramos libres pares y B en los impares (salvo que A no
/// tenga ninguno: entonces va en los de B, sin pisarlos); cada largo de corte
/// es múltiplo exacto del redondeo; ninguna varilla supera el largo
/// comercial; ningún traslapo baja del solape del cuadro. Entre los planes
/// que cumplen, gana el de menos traslapos, después el de menos exceso de
/// solape, después el más centrado y por último el de más abajo.
///
/// La búsqueda es exhaustiva (spec, sección 5): si existe un plan válido, lo
/// encuentra.
/// </summary>
public static class PlanTraslapos
{
    /// <summary>Pasos de redondeo que ofrece la ventana, en metros.</summary>
    public static readonly IReadOnlyList<double> Redondeos = new[] { 0.10, 0.20, 0.25, 0.50, 1.00 };

    public const double RedondeoPorDefecto = 0.50;

    /// <summary>La barra comercial más larga: la que se sugiere cuando con 6 m no hay solución.</summary>
    private const double LargoComercialMayor = 12.0;

    /// <summary>
    /// Lo que el cálculo necesita de la columna. <paramref name="ConGanchos"/>:
    /// la varilla lleva el gancho de 90° del cuadro en las dos puntas (hoy, solo
    /// cuando arranca sobre zapata).
    /// </summary>
    public sealed record Columna(IReadOnlyList<Tramo> Tramos, double ZInicio, double ZFin, bool ConGanchos)
    {
        /// <summary>
        /// Desde lo que lee la ventana. Hay zapata si el contexto la nombra o
        /// si la barra arranca más abajo que el estribo (el proveedor falso no
        /// nombra zapatas pero sí baja el arranque, ver C-12).
        /// </summary>
        public static Columna De(ColumnaContexto c) => new(
            c.Tramos, c.ZInferiorLongitudinal, c.ZSuperior,
            c.IdZapata is not null || c.ZInferiorLongitudinal < c.ZInferior - 1e-6);
    }

    public sealed record Opciones(double LargoComercial, double Redondeo, bool SolapeMayorDeLosDos);

    /// <summary>
    /// Una varilla. <see cref="ZDesde"/>/<see cref="ZHasta"/> son las cotas de
    /// su parte recta; <see cref="LargoCorte"/> incluye los ganchos de la
    /// primera y la última.
    /// </summary>
    public sealed record Pieza(double ZDesde, double ZHasta, double LargoCorte);

    public sealed record Traslapo(double ZInferior, double ZSuperior)
    {
        public double Largo => ZSuperior - ZInferior;
        public double Centro => (ZInferior + ZSuperior) / 2;
    }

    /// <param name="EnTramosDelOtro">El tipo no tenía tramos propios y traslapa en los del otro (spec, regla 2).</param>
    /// <param name="Motivo">Por qué no hay solución, en lenguaje llano; null si la hay.</param>
    public sealed record PlanTipo(
        string Nombre, IReadOnlyList<Pieza> Piezas, IReadOnlyList<Traslapo> Traslapos,
        double? SolapeMinimo, bool EnTramosDelOtro, string? Motivo)
    {
        public bool TieneSolucion => Motivo is null;
    }

    public sealed record Resultado(PlanTipo A, PlanTipo B)
    {
        public bool TieneSolucion => A.TieneSolucion && B.TieneSolucion;
    }

    public static Resultado Calcular(Columna columna, double diametroAMm, double diametroBMm, Opciones opciones)
    {
        Resultado resultado = CalcularCon(columna, diametroAMm, diametroBMm, opciones);
        if (resultado.TieneSolucion || opciones.LargoComercial >= LargoComercialMayor - 1e-9) return resultado;

        // Spec, sección 6: si con 6 m no hay forma, se dice si con 12 sí la hay.
        // No se cambia solo: la elección sigue siendo del usuario.
        Resultado con12 = CalcularCon(columna, diametroAMm, diametroBMm, opciones with { LargoComercial = LargoComercialMayor });
        if (!con12.TieneSolucion) return resultado;

        static PlanTipo Sugerir(PlanTipo p) =>
            p.TieneSolucion ? p : p with { Motivo = $"{p.Motivo} Con barras de 12 m sí se puede." };
        return new Resultado(Sugerir(resultado.A), Sugerir(resultado.B));
    }

    private static Resultado CalcularCon(Columna columna, double diametroAMm, double diametroBMm, Opciones opciones)
    {
        // Un dato de columna roto (remate por debajo del arranque) no puede
        // tirar una excepción dentro de Revit: sale sin solución, con el motivo.
        if (columna.ZFin - columna.ZInicio < 0.01)
        {
            string motivo = $"la columna remata en {columna.ZFin:0.00} m, por debajo de donde arranca la barra " +
                            $"({columna.ZInicio:0.00} m): hay que revisar el modelo.";
            return new Resultado(Fallido("A", null, false, motivo), Fallido("B", null, false, motivo));
        }

        CuadroVarillas.Varilla? varillaA = CuadroVarillas.PorDiametro(diametroAMm);
        CuadroVarillas.Varilla? varillaB = CuadroVarillas.PorDiametro(diametroBMm);

        double? solapeA = varillaA?.Traslapo, solapeB = varillaB?.Traslapo;
        if (opciones.SolapeMayorDeLosDos && solapeA is double sa && solapeB is double sb)
            solapeA = solapeB = Math.Max(sa, sb);

        // Ordinal de los tramos libres de TODA la columna, de abajo hacia
        // arriba, desde 1: el mismo que usaba el generador antes de esto.
        List<Tramo> libres = columna.Tramos
            .Where(t => t.Tipo != TipoTramo.DentroDeViga)
            .OrderBy(t => t.ZInferior)
            .ToList();
        List<Rango> deA = libres.Where((_, i) => (i + 1) % 2 == 0).Select(Rango.De).ToList();
        List<Rango> deB = libres.Where((_, i) => (i + 1) % 2 == 1).Select(Rango.De).ToList();

        // El tramo libre 1 es siempre de B, así que solo A puede quedarse sin
        // tramos propios (columna de un piso). Entonces se calcula primero B y
        // A va en lo que B dejó libre (spec, regla 2).
        if (deA.Count == 0 && deB.Count > 0)
        {
            PlanTipo b = Resolver("B", columna, varillaB, diametroBMm, solapeB, opciones, deB, enTramosDelOtro: false);
            PlanTipo a = b.TieneSolucion
                ? Resolver("A", columna, varillaA, diametroAMm, solapeA, opciones, Restar(deB, b.Traslapos), enTramosDelOtro: true)
                : Fallido("A", solapeA, true, $"el tipo A no tiene tramos propios y va en los del B, que no tiene solución. {b.Motivo}");
            return new Resultado(a, b);
        }

        return new Resultado(
            Resolver("A", columna, varillaA, diametroAMm, solapeA, opciones, deA, enTramosDelOtro: false),
            Resolver("B", columna, varillaB, diametroBMm, solapeB, opciones, deB, enTramosDelOtro: false));
    }

    /// <summary>Un tramo (o un pedazo de tramo) donde puede ir un traslapo; <see cref="Centro"/> es el del tramo entero.</summary>
    private readonly record struct Rango(double Inf, double Sup, double Centro)
    {
        public static Rango De(Tramo t) => new(t.ZInferior, t.ZSuperior, (t.ZInferior + t.ZSuperior) / 2);
    }

    /// <summary>Los rangos menos lo que ocupan los traslapos del otro tipo.</summary>
    private static List<Rango> Restar(List<Rango> rangos, IReadOnlyList<Traslapo> ocupados)
    {
        var libres = new List<Rango>();
        foreach (Rango r in rangos)
        {
            double desde = r.Inf;
            foreach (Traslapo t in ocupados.Where(t => t.ZSuperior > r.Inf && t.ZInferior < r.Sup).OrderBy(t => t.ZInferior))
            {
                if (t.ZInferior > desde) libres.Add(r with { Inf = desde, Sup = t.ZInferior });
                desde = Math.Max(desde, t.ZSuperior);
            }

            if (r.Sup > desde) libres.Add(r with { Inf = desde });
        }

        return libres;
    }

    private static PlanTipo Resolver(
        string nombre, Columna columna, CuadroVarillas.Varilla? varilla, double diametroMm, double? solape,
        Opciones opciones, List<Rango> rangos, bool enTramosDelOtro)
    {
        if (varilla is null)
            return Fallido(nombre, null, enTramosDelOtro,
                $"la barra del tipo {nombre} (Ø {diametroMm:0.#} mm) no está en el cuadro de la empresa: " +
                "no se conoce su gancho ni su solape.");

        var calculo = new Busqueda(columna, columna.ConGanchos ? varilla.Gancho90 : 0, solape, opciones, rangos);
        return calculo.Resolver() is { } plan
            ? new PlanTipo(nombre, plan.Piezas, plan.Traslapos, solape, enTramosDelOtro, null)
            : Fallido(nombre, solape, enTramosDelOtro, calculo.Motivo(nombre, varilla, enTramosDelOtro));
    }

    private static PlanTipo Fallido(string nombre, double? solape, bool enTramosDelOtro, string motivo) =>
        new(nombre, Array.Empty<Pieza>(), Array.Empty<Traslapo>(), solape, enTramosDelOtro, motivo);

    /// <summary>
    /// La búsqueda de un tipo, en milímetros enteros medidos a lo largo de la
    /// varilla estirada ("u"): 0 es la punta del gancho de abajo y
    /// <see cref="_h"/> la del gancho de arriba.
    ///
    /// Programación dinámica sobre el punto donde arranca cada varilla. Un
    /// estado es "acá arranca una varilla, y el traslapo que la une a la de
    /// abajo ocupó la ventana tal". Desde un estado se prueba cada largo de
    /// varilla (múltiplo del redondeo hasta el comercial): o llega justo a la
    /// punta de arriba (fin), o termina dentro de una ventana más alta y ahí
    /// se prueba cada arranque posible de la varilla siguiente. Los arranques
    /// van en pasos de 1 cm, más los que dejan a la última varilla con un
    /// largo redondo (absorben los milímetros sueltos de la altura real).
    ///
    /// El costo se suma por traslapo y se compara en orden (cantidad, exceso,
    /// descentrado, altura), así que el mínimo es el plan preferido del spec.
    /// Como cada estado solo lleva a estados más altos, alcanza con recorrerlos
    /// de abajo hacia arriba una vez.
    /// </summary>
    private sealed class Busqueda
    {
        /// <summary>Los traslapos se ubican en pasos de 1 cm.</summary>
        private const int Rejilla = 10;

        private readonly double _zInicio, _zFin;
        private readonly Opciones _opciones;
        private readonly double? _solapeMetros;
        private readonly int _gancho, _h, _paso, _pasosMax;
        private readonly int? _solape;
        private readonly List<Ventana> _ventanas;
        private readonly Estado[] _rejilla;   // u múltiplo de Rejilla: índice u / Rejilla
        private readonly Estado[] _especial;  // u = _h - k * _paso fuera de la rejilla: índice k
        private int _alcanceMaximo;

        private readonly record struct Ventana(int Inf, int Sup, int Centro2);

        /// <summary>Una forma de llegar a un arranque: el traslapo que lo une a la varilla de abajo.</summary>
        private struct Camino
        {
            public bool Alcanzado;
            public (long Traslapos, long Exceso, long Descentrado, long Altura) Costo;
            public int Ventana;      // la del traslapo que llega a este arranque; -1 en el arranque de abajo
            public int Previo;       // dónde arranca la varilla de abajo; -1 en el arranque de abajo
            public bool PrevioHolgado; // por cuál de los dos caminos se llegó a ese arranque de abajo
            public int Fin;          // dónde termina la varilla de abajo (= arriba del traslapo)
        }

        /// <summary>
        /// Cada arranque guarda DOS caminos: el más barato y el que deja el
        /// traslapo de abajo más abajo (el "holgado"). Hace falta porque en una
        /// misma ventana puede haber más de un traslapo (spec, sección 5: sin
        /// límite por tramo), y el siguiente no puede empezar antes de que
        /// termine el anterior. El holgado es el que menos restringe lo que
        /// sigue, así que con él la búsqueda nunca pierde una solución; el más
        /// barato mantiene el orden de preferencias.
        /// </summary>
        private struct Estado
        {
            public Camino Mejor;
            public Camino Holgado;
        }

        public Busqueda(Columna columna, double ganchoMetros, double? solape, Opciones opciones, List<Rango> rangos)
        {
            _zInicio = columna.ZInicio;
            _zFin = columna.ZFin;
            _opciones = opciones;
            _solapeMetros = solape;
            _gancho = Mm(ganchoMetros);
            _h = Mm(_zFin - _zInicio) + 2 * _gancho;
            _paso = Math.Max(Rejilla, Mm(opciones.Redondeo));
            _pasosMax = Mm(opciones.LargoComercial) / _paso;
            _solape = solape is double s ? Mm(s) : null;
            _ventanas = rangos
                .Select(r => new Ventana(U(Math.Max(r.Inf, _zInicio)), U(Math.Min(r.Sup, _zFin)), 2 * U(r.Centro)))
                .Where(v => v.Sup > v.Inf)
                .OrderBy(v => v.Inf)
                .ToList();
            _rejilla = new Estado[_h / Rejilla + 1];
            _especial = new Estado[_pasosMax + 1];
        }

        private static int Mm(double metros) => (int)Math.Round(metros * 1000);
        private int U(double z) => Mm(z - _zInicio) + _gancho;
        private double Z(int u) => _zInicio + (u - _gancho) / 1000.0;

        private ref Estado Celda(int u) =>
            ref (u % Rejilla == 0 ? ref _rejilla[u / Rejilla] : ref _especial[(_h - u) / _paso]);

        /// <summary>Todos los arranques posibles, de abajo hacia arriba.</summary>
        private IEnumerable<int> Posiciones()
        {
            var especiales = new List<int>();
            for (int k = _pasosMax; k >= 1; k--)
            {
                int u = _h - k * _paso;
                if (u > 0 && u % Rejilla != 0) especiales.Add(u);
            }

            int e = 0;
            for (int g = 0; g < _h; g += Rejilla)
            {
                while (e < especiales.Count && especiales[e] < g) yield return especiales[e++];
                yield return g;
            }

            while (e < especiales.Count) yield return especiales[e++];
        }

        public (List<Pieza> Piezas, List<Traslapo> Traslapos)? Resolver()
        {
            if (_pasosMax <= 0) return null;

            var inicio = new Camino { Alcanzado = true, Ventana = -1, Previo = -1 };
            _rejilla[0] = new Estado { Mejor = inicio, Holgado = inicio };
            int mejorUltima = -1;
            bool mejorUltimaHolgado = false;
            (long, long, long, long) costoMejor = default;

            foreach (int a in Posiciones())
            {
                Estado estado = Celda(a);
                if (!estado.Mejor.Alcanzado) continue;
                _alcanceMaximo = Math.Max(_alcanceMaximo, a);

                // Un arranque fuera de la rejilla solo existe para que la
                // última varilla tenga un largo redondo: desde ahí solo se termina.
                bool soloTerminar = a % Rejilla != 0;

                // Los dos caminos, salvo que sean el mismo.
                bool mismoCamino = estado.Holgado.Previo == estado.Mejor.Previo &&
                                   estado.Holgado.Fin == estado.Mejor.Fin &&
                                   estado.Holgado.PrevioHolgado == estado.Mejor.PrevioHolgado;
                foreach (bool holgado in mismoCamino ? new[] { false } : new[] { false, true })
                {
                    Camino actual = holgado ? estado.Holgado : estado.Mejor;
                    for (int k = 1; k <= _pasosMax; k++)
                    {
                        int b = a + k * _paso;
                        if (b > _h) break;
                        if (b == _h)
                        {
                            if (mejorUltima < 0 || actual.Costo.CompareTo(costoMejor) < 0)
                            {
                                mejorUltima = a;
                                mejorUltimaHolgado = holgado;
                                costoMejor = actual.Costo;
                            }

                            break;
                        }

                        if (soloTerminar || _solape is not int solape) continue;

                        int w = VentanaQueContiene(b, actual.Ventana);
                        if (w < 0) continue;
                        Ventana v = _ventanas[w];

                        // El arranque siguiente: por encima del actual (si no,
                        // las dos varillas nacerían en el mismo punto), por
                        // encima del traslapo de abajo (en la misma ventana
                        // podría pisarlo) y dejando al menos el solape mínimo hasta b.
                        int desde = Math.Max(Math.Max(v.Inf, a + 1), actual.Fin);
                        int hasta = b - solape;
                        if (hasta < desde) continue;

                        for (int ap = (desde + Rejilla - 1) / Rejilla * Rejilla; ap <= hasta; ap += Rejilla)
                            Relajar(ap, actual, a, holgado, b, w, solape, v);

                        if (_h % Rejilla != 0)
                            for (int k2 = 1; k2 <= _pasosMax; k2++)
                            {
                                int ap = _h - k2 * _paso;
                                if (ap >= desde && ap <= hasta) Relajar(ap, actual, a, holgado, b, w, solape, v);
                            }
                    }
                }
            }

            return mejorUltima < 0 ? null : Reconstruir(mejorUltima, mejorUltimaHolgado);
        }

        /// <summary>La primera ventana que contiene u, desde la del traslapo de abajo (puede ser la misma).</summary>
        private int VentanaQueContiene(int u, int desdeVentana)
        {
            for (int w = Math.Max(desdeVentana, 0); w < _ventanas.Count; w++)
                if (u >= _ventanas[w].Inf && u <= _ventanas[w].Sup) return w;
            return -1;
        }

        private void Relajar(int ap, Camino desde, int a, bool desdeHolgado, int b, int w, int solape, Ventana v)
        {
            var camino = new Camino
            {
                Alcanzado = true,
                Costo = (
                    desde.Costo.Traslapos + 1,
                    desde.Costo.Exceso + (b - ap - solape),
                    desde.Costo.Descentrado + Math.Abs(ap + b - v.Centro2),
                    desde.Costo.Altura + ap + b),
                Ventana = w,
                Previo = a,
                PrevioHolgado = desdeHolgado,
                Fin = b,
            };

            ref Estado destino = ref Celda(ap);
            if (!destino.Mejor.Alcanzado || camino.Costo.CompareTo(destino.Mejor.Costo) < 0)
                destino.Mejor = camino;
            if (!destino.Holgado.Alcanzado || camino.Fin < destino.Holgado.Fin ||
                (camino.Fin == destino.Holgado.Fin && camino.Costo.CompareTo(destino.Holgado.Costo) < 0))
                destino.Holgado = camino;
        }

        private (List<Pieza>, List<Traslapo>) Reconstruir(int arranqueUltima, bool holgado)
        {
            var piezas = new List<Pieza>();
            var traslapos = new List<Traslapo>();
            int desde = arranqueUltima, hasta = _h;
            while (true)
            {
                piezas.Add(new Pieza(Math.Max(Z(desde), _zInicio), Math.Min(Z(hasta), _zFin), (hasta - desde) / 1000.0));
                Camino c = holgado ? Celda(desde).Holgado : Celda(desde).Mejor;
                if (c.Previo < 0) break;
                traslapos.Add(new Traslapo(Z(desde), Z(c.Fin)));
                hasta = c.Fin;
                desde = c.Previo;
                holgado = c.PrevioHolgado;
            }

            piezas.Reverse();
            traslapos.Reverse();
            return (piezas, traslapos);
        }

        public string Motivo(string nombre, CuadroVarillas.Varilla varilla, bool enTramosDelOtro)
        {
            string largoCorte = $"{_h / 1000.0:0.00} m";
            string barras = $"varillas de hasta {_opciones.LargoComercial:0.#} m (en múltiplos de {_opciones.Redondeo:0.00} m)";

            if (_solapeMetros is null)
                return $"tipo {nombre}: el cuadro de la empresa no da traslapo en columnas para la #{varilla.Numero}, " +
                       $"y su largo de corte ({largoCorte}) no entra justo en una sola de las {barras}.";

            string donde = enTramosDelOtro ? "un hueco libre de los tramos del otro tipo"
                : $"un tramo libre {(nombre == "A" ? "par" : "impar")}";

            if (_ventanas.Count == 0)
                return $"tipo {nombre}: no hay ningún {donde.Replace("un ", "")} donde traslapar, y su largo de corte " +
                       $"({largoCorte}) no entra justo en una sola de las {barras}.";

            string desde = _alcanceMaximo == 0 ? "desde el arranque" : $"desde la cota {Z(_alcanceMaximo):0.00} m";
            return $"tipo {nombre}: {desde}, ninguna de las {barras} llega a {donde} con lugar para un traslapo " +
                   $"de {_solapeMetros:0.00} m sin tocar un nudo.";
        }
    }
}
