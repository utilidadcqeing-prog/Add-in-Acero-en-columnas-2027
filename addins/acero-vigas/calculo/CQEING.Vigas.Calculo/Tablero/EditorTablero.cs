using System.Globalization;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>
/// La vista previa de la edición (#84): qué pasaría con Subir / Bajar, con un traslapo escrito o con un bastón escrito,
/// y el texto que muestra el panel. Usa los <c>Previsualizar*</c> del motor; la página no calcula nada (decisiones 1
/// a 4 del plan docs/planes/2026-10-17-edicion-vigas-84.md).
/// </summary>
public static class EditorTablero
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const string NoEsta = "Ese elemento ya no está en el pórtico.";

    public static PreviaVista Previsualizar(PorticoLeido p, OpcionesTablero o, Reglas r, PedidoEdicion pedido)
    {
        EntradaVigas e = ArmadorVista.EntradaDe(p, o, r);
        IReadOnlyList<OpcionEdicion> opciones = pedido.Accion switch
        {
            AccionEdicion.Barra => Barra(e, pedido.Elemento),
            AccionEdicion.Traslapo => [Traslapo(e, pedido.Elemento, pedido.Valor)],
            AccionEdicion.Baston => [Baston(e, pedido.Elemento, pedido.Valor)],
            _ => [],
        };
        return new PreviaVista(pedido.N, pedido.Elemento, opciones, []);
    }

    /// <summary>Subir y Bajar: un módulo más y uno menos (decisión 2), con cómo quedan las vecinas.</summary>
    private static IReadOnlyList<OpcionEdicion> Barra(EntradaVigas e, string id)
    {
        ResultadoVigas actual = MotorVigas.Calcular(e);
        Barra? barra = actual.Barras.FirstOrDefault(b => b.Id == id);
        if (barra is null)
            return [new OpcionEdicion("subir", 0, false, NoEsta), new OpcionEdicion("bajar", 0, false, NoEsta)];
        int modulo = e.Reglas.Modulo;
        return [Opcion("subir", barra.Largo + modulo), Opcion("bajar", barra.Largo - modulo)];

        OpcionEdicion Opcion(string accion, int largo)
        {
            Previsualizacion pv = MotorVigas.PrevisualizarBarra(e, id, largo);
            string texto = pv.SePuede
                ? Efecto(actual, pv.Resultado!, id)
                : pv.Motivo switch
                {
                    MotivoBloqueo.NoSaleDeLaVarilla => $"No sale de una varilla de {Varilla(e.Reglas, barra.Largo)} m.",
                    MotivoBloqueo.SinSolucion or MotivoBloqueo.TraslapoBajoElMinimo when accion == "bajar" =>
                        "Bajarla dejaría un traslapo por debajo del mínimo.",
                    _ => Texto(pv.Motivo),
                };
            return new OpcionEdicion(accion, M(largo), pv.SePuede, texto);
        }
    }

    /// <summary>"S1 pasa a 3.50 m." y una frase por cada otra barra cuyo largo cambia.</summary>
    private static string Efecto(ResultadoVigas antes, ResultadoVigas despues, string id) =>
        string.Join(" ", despues.Barras
            .Where(b => b.Id == id || antes.Barras.FirstOrDefault(a => a.Id == b.Id)?.Largo != b.Largo)
            .OrderBy(b => b.Id != id)
            .Select(b => $"{b.Id} pasa a {F2(b.Largo)} m."));

    /// <summary>Un traslapo escrito (decisión 3): solo crece, y crece la barra de la derecha al múltiplo.</summary>
    private static OpcionEdicion Traslapo(EntradaVigas e, string id, double? valor)
    {
        ResultadoVigas actual = MotorVigas.Calcular(e);
        Traslapo? t = actual.Traslapos.FirstOrDefault(x => x.Id == id);
        if (t is null)
            return new OpcionEdicion("aplicar", 0, false, NoEsta);
        if (valor is not { } v)
            return new OpcionEdicion("aplicar", 0, false, "Escribe un largo en metros.");

        int largo = Mm(v), modulo = e.Reglas.Modulo;
        Previsualizacion pv = MotorVigas.PrevisualizarTraslapo(e, id, largo);
        string texto;
        if (pv.SePuede)
        {
            ResultadoVigas r = pv.Resultado!;
            texto = $"{t.BarraDerecha} pasa a {F2(r.Barras.First(b => b.Id == t.BarraDerecha).Largo)} m (múltiplo de {F2(modulo)}) "
                + $"y el traslapo queda en {F2(r.Traslapos.First(x => x.Id == id).Largo)} m.";
        }
        else
        {
            Barra derecha = actual.Barras.First(b => b.Id == t.BarraDerecha);
            // lo que crecería la barra de la derecha: el largo pedido redondeado hacia arriba al módulo, como en AplicadorCambios
            int crece = largo <= t.Largo ? 0 : (largo - t.Largo + modulo - 1) / modulo * modulo;
            texto = pv.Motivo switch
            {
                MotivoBloqueo.TraslapoBajoElMinimo => $"No puede ser menor que el mínimo ({F2(t.Minimo)} m).",
                MotivoBloqueo.SinCambio => $"Escribe un largo mayor que el actual ({F2(t.Largo)} m).",
                MotivoBloqueo.NoSaleDeLaVarilla =>
                    $"{t.BarraDerecha} pasaría a {F2(derecha.Largo + crece)} m y no sale de una varilla de {Varilla(e.Reglas, derecha.Largo)} m.",
                _ => Texto(pv.Motivo),
            };
        }
        return new OpcionEdicion("aplicar", M(largo), pv.SePuede, texto);
    }

    /// <summary>Un bastón escrito (decisión 4): centrado en su eje y sin redondear.</summary>
    private static OpcionEdicion Baston(EntradaVigas e, string id, double? valor)
    {
        if (valor is not { } v)
            return new OpcionEdicion("aplicar", 0, false, "Escribe un largo en metros.");
        int largo = Mm(v);
        Previsualizacion pv = MotorVigas.PrevisualizarBaston(e, id, largo);
        string texto = pv.Motivo switch
        {
            MotivoBloqueo.Ninguno => pv.Resultado!.Bastones.First(b => b.Id == id).LargoRecto < largo
                ? "Llega al borde de la viga y se corta en la punta. No se redondea."
                : $"Queda {F2(largo / 2.0)} m a cada lado de su eje. No se redondea.",
            MotivoBloqueo.SinCambio => "Ese ya es su largo.",
            _ => Texto(pv.Motivo),
        };
        return new OpcionEdicion("aplicar", M(largo), pv.SePuede, texto);
    }

    private static string Texto(MotivoBloqueo motivo) => motivo switch
    {
        MotivoBloqueo.NoExiste => NoEsta,
        MotivoBloqueo.SinCambio => "No cambia nada.",
        MotivoBloqueo.TraslapoBajoElMinimo => "Un traslapo quedaría por debajo del mínimo.",
        MotivoBloqueo.NoSaleDeLaVarilla => "No sale de la varilla.",
        MotivoBloqueo.EntraAlNudo => "El traslapo se metería en el nudo.",
        MotivoBloqueo.SinSolucion => "Con ese largo, las barras vecinas no cumplen las reglas.",
        MotivoBloqueo.LargoNoValido => "El largo debe ser mayor que 0.",
        _ => "",
    };

    /// <summary>La varilla de la que no puede pasar una barra que hoy mide <paramref name="largoActual"/>, en m ("6" o "12").</summary>
    private static string Varilla(Reglas r, int largoActual) =>
        ((largoActual > r.Varilla ? r.VarillaLarga : r.Varilla) / 1000.0).ToString("0.##", Inv);

    private static int Mm(double m) => (int)Math.Round(m * 1000);

    private static double M(int mm) => mm / 1000.0;

    private static string F2(double mm) => (mm / 1000).ToString("0.00", Inv);
}
