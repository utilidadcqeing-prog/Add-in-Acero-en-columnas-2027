namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// Qué tipo de empalme (<c>RebarSpliceType</c>) le toca a una barra
/// longitudinal -- issue #41.
///
/// Por defecto son tipos PROPIOS del add-in, uno por varilla del cuadro de la
/// empresa ("CQ Traslapo #4"), para que el solape salga de
/// <see cref="CuadroVarillas.Varilla.Traslapo"/> y no de lo que traiga cargado
/// cada modelo. Revit define el solape como un múltiplo del diámetro, y el
/// cuadro no es un múltiplo fijo (#4 son 59 diámetros, #8 son 67), por eso hace
/// falta un tipo por varilla. Si el usuario eligió un tipo del modelo, se usa
/// ese tal cual, sin tocarlo.
///
/// Una instancia por corrida: cachea para no crear ni reconfigurar el mismo
/// tipo una vez por varilla.
/// </summary>
public sealed class TiposEmpalme
{
    public const string PrefijoPropio = "CQ Traslapo #";

    public readonly record struct Resultado(ElementId? Tipo, string? Aviso);

    private readonly Document _doc;
    private readonly Dictionary<string, ElementId> _porNombre = new();
    private readonly Dictionary<(ElementId Barra, string Nombre), Resultado> _cache = new();

    public TiposEmpalme(Document doc)
    {
        _doc = doc;
        foreach (ElementId id in RebarSpliceTypeUtils.GetAllRebarSpliceTypes(doc))
        {
            string? nombre = doc.GetElement(id)?.Name;
            if (!string.IsNullOrEmpty(nombre)) _porNombre[nombre] = id;
        }
    }

    /// <summary>Debe llamarse dentro de una transacción abierta: puede crear un tipo.</summary>
    public Resultado Resolver(RebarBarType barra, string? nombreElegido)
    {
        var clave = (barra.Id, nombreElegido ?? "");
        if (_cache.TryGetValue(clave, out Resultado cacheado)) return cacheado;

        Resultado resultado;
        if (string.IsNullOrEmpty(nombreElegido))
        {
            resultado = ResolverPropio(barra);
        }
        else if (_porNombre.TryGetValue(nombreElegido, out ElementId? delModelo))
        {
            resultado = new Resultado(delModelo, null);
        }
        else
        {
            Resultado propio = ResolverPropio(barra);
            string aviso = $"el tipo de empalme «{nombreElegido}» no existe en este modelo; se usa el por defecto del add-in.";
            resultado = new Resultado(propio.Tipo, propio.Aviso is null ? aviso : $"{aviso} {propio.Aviso}");
        }

        _cache[clave] = resultado;
        return resultado;
    }

    private Resultado ResolverPropio(RebarBarType barra)
    {
        CuadroVarillas.Varilla? varilla = EstandarCQ.Buscar(barra);
        if (varilla is null)
            return new Resultado(null,
                $"la barra «{barra.Name}» no está en el cuadro de la empresa, así que no hay solape que aplicar: " +
                "sus varillas quedan enteras, sin traslapos. Para traslaparlas, elija un tipo de empalme del modelo.");

        if (varilla.Traslapo is not { } traslapo)
            return new Resultado(null,
                $"el cuadro de la empresa no da traslapo para la barra «{barra.Name}» (#{varilla.Numero}) en columnas: " +
                "sus varillas quedan enteras, sin traslapos. Para traslaparlas, elija un tipo de empalme del modelo.");

        string nombre = $"{PrefijoPropio}{varilla.Numero}";
        try
        {
            if (!_porNombre.TryGetValue(nombre, out ElementId? id))
            {
                id = RebarSpliceTypeUtils.CreateRebarSpliceType(_doc, nombre).Id;
                _porNombre[nombre] = id;
            }

            // Se vuelven a fijar en cada corrida, por si alguien los cambió a
            // mano: el nombre promete el solape del cuadro.
            double multiplicador = EstandarCQ.APies(traslapo) / barra.BarNominalDiameter;
            RebarSpliceTypeUtils.SetLapLengthMultiplier(_doc, id, multiplicador);
            RebarSpliceTypeUtils.SetShiftOption(_doc, id, RebarSpliceShiftOption.BarPlane);
            return new Resultado(id, null);
        }
        catch (Exception ex)
        {
            return new Resultado(null,
                $"no se pudo preparar el tipo de empalme «{nombre}» ({GeometriaSeccion.Explicar(ex)}); " +
                $"las varillas «{barra.Name}» quedan enteras, sin traslapos.");
        }
    }
}
