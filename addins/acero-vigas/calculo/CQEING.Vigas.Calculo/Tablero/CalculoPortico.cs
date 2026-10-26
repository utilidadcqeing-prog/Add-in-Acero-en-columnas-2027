using CQEING.Vigas.Calculo.Estribos;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Vigas;

namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>
/// Todo lo que se calcula de un pórtico con lo que eligió el usuario: barras, bastones, sección y estribos.
/// <paramref name="PorTramo"/> es la configuración de estribos de los tramos marcados (null = la general en todos).
/// <paramref name="SePuedeGenerar"/>: hay solución en las dos capas, la sección no tiene errores y ningún aviso lo impide.
/// </summary>
public sealed record PorticoCalculado(
    ResultadoVigas Vigas,
    ResultadoEstribos Estribos,
    EntradaSeccion Seccion,
    IReadOnlyDictionary<string, ConfigEstribos>? PorTramo,
    IReadOnlyList<Aviso> Avisos,
    bool SePuedeGenerar);

/// <summary>
/// El cálculo de un pórtico que comparten la vista del tablero (<see cref="ArmadorVista"/>) y la generación en Revit
/// (#85): lo que se genera es exactamente lo que se ve.
/// </summary>
public static class CalculoPortico
{
    /// <summary>Avisos que impiden generar el acero en Revit.</summary>
    private static readonly TipoAviso[] Bloquean = [TipoAviso.SinTraslapoMinimo, TipoAviso.SinSolucion, TipoAviso.DiametroSinReglas];

    /// <summary>Null si algún diámetro elegido no está en las reglas (ver <see cref="DiametrosSinReglas"/>).</summary>
    public static PorticoCalculado? Calcular(PorticoLeido p, OpcionesTablero o, Reglas r)
    {
        if (DiametrosSinReglas(o, r).Count > 0)
            return null;

        ResultadoVigas rv = MotorVigas.Calcular(EntradaDe(p, o, r));
        EntradaSeccion seccion = Seccion(p, o, r);
        IReadOnlyDictionary<string, ConfigEstribos>? porTramo = o.EstribosMarcados is null
            ? null
            : o.TramosMarcados.Distinct().ToDictionary(t => t, _ => o.EstribosMarcados);
        ResultadoEstribos re = MotorEstribos.Calcular(new EntradaEstribos(p.Portico, seccion, o.Estribos, porTramo));
        List<Aviso> avisos = [.. rv.Avisos, .. re.Avisos];

        bool seccionSinError = re.Seccion.Superior.Error == ErrorCara.Ninguno && re.Seccion.Inferior.Error == ErrorCara.Ninguno;
        bool sePuede = rv.Superior is not null && rv.Inferior is not null && seccionSinError && !avisos.Any(a => Bloquean.Contains(a.Tipo));
        return new PorticoCalculado(rv, re, seccion, porTramo, avisos, sePuede);
    }

    /// <summary>
    /// Los diámetros elegidos (barras, bastones y estribos) que no están en las reglas, sin repetir y de menor a mayor.
    /// Con alguno, el reparto de la sección reventaría: no se calcula.
    /// </summary>
    public static IReadOnlyList<int> DiametrosSinReglas(OpcionesTablero o, Reglas r) =>
        o.Superior.Concat(o.Inferior).Select(g => g.Diametro)
            .Concat(new[] { o.Bastones.Superior, o.Bastones.Inferior }.OfType<GrupoBarras>().Select(g => g.Diametro))
            .Append(o.Estribos.Diametro)
            .Concat(o.EstribosMarcados is null ? [] : [o.EstribosMarcados.Diametro])
            .Where(d => !r.PorDiametro.ContainsKey(d)).Distinct().Order().ToList();

    /// <summary>Lo que recibe el motor para el pórtico <paramref name="p"/> con lo que eligió el usuario (también la vista previa, #84).</summary>
    internal static EntradaVigas EntradaDe(PorticoLeido p, OpcionesTablero o, Reglas r) =>
        new(p.Portico, o.Tipo ?? p.Tipo, o.Superior, o.Inferior, r, o.Cara, Cambios: o.Cambios, Bastones: o.Bastones,
            EstriboEnVoladizo: r.PorDiametro.TryGetValue(o.Estribos.Diametro, out ReglaDiametro? estribo) ? (int)Math.Ceiling(estribo.Diametro) : 0);

    /// <summary>La sección del pórtico con los recubrimientos (cm en la página, mm en el motor) y las filas del paso 1.</summary>
    internal static EntradaSeccion Seccion(PorticoLeido p, OpcionesTablero o, Reglas r)
    {
        var rec = new Recubrimientos((o.RecArribaCm ?? o.RecubrimientoCm) * 10, (o.RecAbajoCm ?? o.RecubrimientoCm) * 10,
            (o.RecLadosCm ?? o.RecubrimientoCm) * 10);
        return new EntradaSeccion(p.Tramos[0].Ancho, p.Tramos[0].Alto, rec, o.Estribos.Diametro, o.Superior, o.Inferior, r,
            (o.SeparacionFilasCm ?? 0) * 10, o.MaximoFilas, PermiteUnaBarra: (o.Elemento ?? p.Elemento) == Elemento.Vigueta);
    }
}
