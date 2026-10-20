namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// El tipo de empalme (<c>RebarSpliceType</c>) de un traslapo de barra
/// longitudinal: uno PROPIO del add-in por varilla del cuadro y por largo
/// ("CQ Traslapo #4 · 0,90 m").
///
/// Hace falta uno por largo porque Revit fija el solape en el tipo (como
/// múltiplo del diámetro) y desde la nueva metodología cada traslapo puede
/// medir distinto: el del cuadro es solo el mínimo, y el redondeo de las
/// varillas lo estira (docs/planes/2026-10-17-diseno-metodologia-traslapos.md).
///
/// Una instancia por corrida: cachea para no crear ni reconfigurar el mismo
/// tipo una vez por varilla.
/// </summary>
public sealed class TiposEmpalme
{
    public readonly record struct Resultado(ElementId? Tipo, string? Aviso);

    private readonly Document _doc;
    private readonly Dictionary<string, ElementId> _porNombre = new();
    private readonly Dictionary<(ElementId Barra, int Milimetros), Resultado> _cache = new();

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
    public Resultado Resolver(RebarBarType barra, double largoMetros)
    {
        var clave = (barra.Id, (int)Math.Round(largoMetros * 1000));
        if (_cache.TryGetValue(clave, out Resultado cacheado)) return cacheado;

        Resultado resultado;
        CuadroVarillas.Varilla? varilla = EstandarCQ.Buscar(barra);
        if (varilla is null)
        {
            resultado = new Resultado(null,
                $"la barra «{barra.Name}» no está en el cuadro de la empresa; sus varillas quedan enteras, sin traslapos.");
        }
        else
        {
            // Al milímetro cuando el largo no es redondo: un tipo fija UN largo
            // (ver NombreEmpalme).
            string nombre = NombreEmpalme.Propio(varilla.Numero, largoMetros);
            try
            {
                if (!_porNombre.TryGetValue(nombre, out ElementId? id))
                {
                    id = RebarSpliceTypeUtils.CreateRebarSpliceType(_doc, nombre).Id;
                    _porNombre[nombre] = id;
                }

                // Se vuelven a fijar en cada corrida, por si alguien los cambió
                // a mano: el nombre promete ese largo.
                RebarSpliceTypeUtils.SetLapLengthMultiplier(_doc, id, EstandarCQ.APies(largoMetros) / barra.BarNominalDiameter);
                RebarSpliceTypeUtils.SetShiftOption(_doc, id, RebarSpliceShiftOption.BarPlane);
                resultado = new Resultado(id, null);
            }
            catch (Exception ex)
            {
                resultado = new Resultado(null,
                    $"no se pudo preparar el tipo de empalme «{nombre}» ({GeometriaSeccion.Explicar(ex)}); " +
                    "ese traslapo no se aplica y la varilla queda más larga en ese punto.");
            }
        }

        _cache[clave] = resultado;
        return resultado;
    }
}
