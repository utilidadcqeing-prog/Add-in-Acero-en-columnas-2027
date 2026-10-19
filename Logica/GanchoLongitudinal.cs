namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// El gancho de 90° de la barra longitudinal (anclaje en la zapata y remate):
/// uno PROPIO del add-in, «CQ Gancho 90°», ajustado para que cada punta sume
/// al largo de corte exactamente el <see cref="CuadroVarillas.Varilla.Gancho90"/>
/// del cuadro de la empresa.
///
/// Hace falta porque la pestaña «Traslapos» redondea los largos de corte con
/// el gancho del cuadro, y el de 90° del modelo («Estándar - 90°») suma menos:
/// 0,188 m por punta en una #4 contra 0,222 m del cuadro (medido el 17-oct,
/// docs/planes/2026-10-17-diagnostico-traslapos-variables.md). Ese gancho no
/// se toca porque lo usan otras barras del modelo (vigas, por ejemplo).
///
/// El ajuste se mide, no se supone: por cada barra se crea una barra temporal
/// de 1 m con el gancho en las dos puntas, se lee su largo y se corrige el
/// largo del gancho para esa barra (<c>SetAutoCalcHookLengths(false)</c> +
/// <c>SetHookLength</c>, el mismo método que el #64). La barra temporal se borra.
///
/// Una instancia por corrida; debe usarse dentro de una transacción abierta.
/// </summary>
public sealed class GanchoLongitudinal
{
    public const string Nombre = "CQ Gancho 90°";

    /// <summary>
    /// Medio centímetro por punta: <c>TotalLength</c> sale redondeado al
    /// centímetro (redondeo de armaduras del proyecto), así que la medición
    /// de una punta no distingue menos que eso. Con 1 mm el ajuste saltaba
    /// entre 0,220 y 0,225 sin llegar nunca a 0,222 y avisaba de más
    /// (retest del 19-oct, docs/planes/2026-10-17-diagnostico-traslapos-variables.md).
    /// </summary>
    private const double ToleranciaMetros = 0.005;

    private const double LargoDePruebaMetros = 1.0;
    private const int Intentos = 3;

    public readonly record struct Resultado(RebarHookType? Gancho, string? Aviso);

    private readonly Document _doc;
    private readonly Dictionary<ElementId, Resultado> _porBarra = new();
    private RebarHookType? _gancho;
    private string? _avisoGancho;
    private bool _buscado;

    /// <summary>TEMPORAL -- igual que <see cref="BarraContinua.Traza"/>: una línea por medición.</summary>
    public Action<string>? Traza { get; set; }

    public GanchoLongitudinal(Document doc) => _doc = doc;

    /// <summary>
    /// El gancho propio, ya ajustado para esa barra, o null con el motivo si
    /// no se pudo (quien llama usa entonces el gancho del modelo). La barra
    /// temporal se crea en <paramref name="host"/>, a partir de <paramref name="punto"/>.
    /// </summary>
    public Resultado Resolver(RebarBarType barra, Element host, XYZ punto)
    {
        if (_porBarra.TryGetValue(barra.Id, out Resultado cacheado)) return cacheado;

        Resultado resultado = Ajustar(barra, host, punto);
        _porBarra[barra.Id] = resultado;
        return resultado;
    }

    private Resultado Ajustar(RebarBarType barra, Element host, XYZ punto)
    {
        CuadroVarillas.Varilla? varilla = EstandarCQ.Buscar(barra);
        if (varilla is null)
            return new Resultado(null, $"la barra «{barra.Name}» no está en el cuadro de la empresa: no hay largo de gancho que igualar.");

        RebarHookType? gancho = Gancho();
        if (gancho is null) return new Resultado(null, _avisoGancho);

        double objetivo = EstandarCQ.APies(varilla.Gancho90);
        try
        {
            for (int intento = 1; intento <= Intentos; intento++)
            {
                double? aporte = MedirAporte(barra, gancho, host, punto);
                if (aporte is null)
                    return new Resultado(null, $"Revit no pudo crear la barra de medición del gancho «{Nombre}» para «{barra.Name}».");

                double diferencia = objetivo - aporte.Value;
                Traza?.Invoke(
                    $"  gancho «{Nombre}» en «{barra.Name}», intento {intento}: suma {Metros(aporte.Value):0.0000} m por punta, " +
                    $"el cuadro pide {varilla.Gancho90:0.000} m");
                if (Math.Abs(diferencia) <= EstandarCQ.APies(ToleranciaMetros)) return new Resultado(gancho, null);
                if (intento == Intentos) break;

                double largo = barra.GetHookLength(gancho.Id);
                barra.SetAutoCalcHookLengths(gancho.Id, false);
                barra.SetHookLength(gancho.Id, largo + diferencia);
                _doc.Regenerate();
            }

            return new Resultado(gancho,
                $"el gancho «{Nombre}» de la barra «{barra.Name}» no quedó exactamente con el largo del cuadro " +
                $"({varilla.Gancho90:0.000} m por punta); los largos de varilla en Revit pueden diferir unos milímetros de la pestaña «Traslapos».");
        }
        catch (Exception ex)
        {
            try { _doc.Regenerate(); } catch { /* best-effort */ }
            return new Resultado(null, $"no se pudo ajustar el gancho «{Nombre}» para «{barra.Name}» ({GeometriaSeccion.Explicar(ex)}).");
        }
    }

    /// <summary>Busca o crea el gancho propio, una sola vez por corrida.</summary>
    private RebarHookType? Gancho()
    {
        if (_buscado) return _gancho;
        _buscado = true;

        try
        {
            _gancho = new FilteredElementCollector(_doc)
                .OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                .FirstOrDefault(g => g.Name == Nombre);

            if (_gancho is null)
            {
                _gancho = RebarHookType.Create(_doc, Math.PI / 2, 12);
                _gancho.Name = Nombre;
            }

            // Se vuelve a fijar en cada corrida, por si alguien lo cambió a mano.
            if (_gancho.Style != RebarStyle.Standard) _gancho.Style = RebarStyle.Standard;

            // El ángulo no se puede cambiar: uno alterado a mano no se usa.
            if (Math.Abs(_gancho.HookAngle - Math.PI / 2) > 1e-3)
            {
                _avisoGancho = $"el gancho «{Nombre}» del modelo no es de 90°; se usa el gancho del modelo.";
                _gancho = null;
            }
        }
        catch (Exception ex)
        {
            _avisoGancho = $"no se pudo crear el gancho «{Nombre}» ({GeometriaSeccion.Explicar(ex)}).";
            _gancho = null;
        }

        return _gancho;
    }

    /// <summary>Cuánto suma cada gancho al largo de la barra, medido sobre una barra temporal (pies).</summary>
    private double? MedirAporte(RebarBarType barra, RebarHookType gancho, Element host, XYZ punto)
    {
        double recta = EstandarCQ.APies(LargoDePruebaMetros);
        using var terminaciones = new BarTerminationsData(_doc)
        {
            HookTypeIdAtStart = gancho.Id,
            HookTypeIdAtEnd = gancho.Id,
        };

        Rebar? prueba = Rebar.CreateFromCurves(
            _doc, RebarStyle.Standard, barra, host, XYZ.BasisY,
            new List<Curve> { Line.CreateBound(punto, punto + XYZ.BasisZ * recta) },
            terminaciones, useExistingShapeIfPossible: true, createNewShape: true);
        if (prueba is null) return null;

        try
        {
            _doc.Regenerate();
            return (prueba.TotalLength - recta) / 2;
        }
        finally
        {
            _doc.Delete(prueba.Id);
        }
    }

    private static double Metros(double pies) => UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Meters);
}
