namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// Los tipos de barra propios del add-in, "#2 CQEING" a "#8 CQEING", uno por
/// varilla del <see cref="CuadroVarillas"/>. Hacen falta porque la lista de
/// barras del configurador muestra lo que trae cada modelo, y un modelo con
/// plantilla europea trae "10 B 400 S", "16 B 500 S"… y ninguna de las
/// nuestras (19-oct). Ver <c>docs/planes/2026-10-19-varillas-propias-cqeing.md</c>.
///
/// Se crean de cero con <c>RebarBarType.Create</c> y no duplicando una barra
/// del modelo: duplicar arrastraría lo que esa barra tenga de su plantilla.
/// </summary>
public static class VarillasPropias
{
    /// <summary>Las varillas del cuadro cuyo tipo propio todavía no existe en el documento.</summary>
    public static List<CuadroVarillas.Varilla> Faltantes(Document doc)
    {
        HashSet<string> nombres = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType))
            .Select(t => t.Name)
            .ToHashSet();

        return CuadroVarillas.Cuadro.Where(v => !nombres.Contains(CuadroVarillas.NombrePropio(v.Numero))).ToList();
    }

    /// <summary>
    /// Crea los tipos que falten, en una sola transacción. Devuelve null si
    /// salió bien, o el motivo si no (la transacción se deshace entera: o
    /// quedan todas o ninguna).
    /// </summary>
    public static string? Crear(Document doc, IEnumerable<CuadroVarillas.Varilla> faltantes)
    {
        using var transaccion = new Transaction(doc, "CQEING - agregar varillas propias");
        transaccion.Start();
        try
        {
            foreach (CuadroVarillas.Varilla v in faltantes)
            {
                RebarBarType tipo = RebarBarType.Create(doc);
                tipo.Name = CuadroVarillas.NombrePropio(v.Numero);
                tipo.SetBarTypeDiameters(Diametros(tipo, v));

                // SetBarTypeDiameters valida pero no garantiza redondeos: se
                // relee para no dejar una "#5 CQEING" que no mida 15,9 mm.
                double medido = EstandarCQ.DiametroMm(tipo);
                if (Math.Abs(medido - v.DiametroMm) >= CuadroVarillas.ToleranciaMm)
                    throw new InvalidOperationException($"«{tipo.Name}» quedó con {medido:0.#} mm en vez de {v.DiametroMm:0.#} mm");
            }

            if (CompletarPeso(doc) is string avisoPeso) throw new InvalidOperationException(avisoPeso);
            transaccion.Commit();
            return null;
        }
        catch (Exception ex)
        {
            if (transaccion.HasStarted() && !transaccion.HasEnded()) transaccion.RollBack();
            return ex.Message;
        }
    }

    /// <summary>
    /// Le pone a cada varilla propia el peso por metro del cuadro
    /// (<see cref="CuadroVarillas.Varilla.KgPorMetro"/>). <c>RebarBarType.Create</c>
    /// la entrega con 0, y entonces la masa de las barras salía 0 en las
    /// cantidades (28-oct: «#3 CQEING» con 0 kg/m, «#3» de la plantilla con
    /// 0,56). Corrige también las que ya estaban creadas en el modelo. Hay que
    /// llamarla dentro de una transacción. Devuelve un aviso si alguna no se
    /// pudo, o null.
    ///
    /// Se llenan dos cosas: cualquier parámetro del tipo con dato «masa por
    /// unidad de longitud» que se pueda editar (en las plantillas del equipo,
    /// «kg/m»: es lo que leen las tablas y el cuadro de cantidades, y el único
    /// peso que hay en Revit 2026), y en 2027 además el peso propio de la
    /// barra (<c>BarMassPerUnitLength</c>, que no existe en la API de 2026).
    /// </summary>
    public static string? CompletarPeso(Document doc)
    {
        Dictionary<string, RebarBarType> tipos = new FilteredElementCollector(doc)
            .OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
            .GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First());

        string? aviso = null;
        foreach (CuadroVarillas.Varilla v in CuadroVarillas.Cuadro)
        {
            if (!tipos.TryGetValue(CuadroVarillas.NombrePropio(v.Numero), out RebarBarType? tipo)) continue;
            try
            {
                double objetivo = UnitUtils.ConvertToInternalUnits(v.KgPorMetro, UnitTypeId.KilogramsPerMeter);
                foreach (Parameter p in tipo.Parameters)
                {
                    if (p.IsReadOnly || p.StorageType != StorageType.Double) continue;
                    if (p.Definition.GetDataType() != SpecTypeId.MassPerUnitLength) continue;
                    if (Math.Abs(p.AsDouble() - objetivo) > 1e-9) p.Set(objetivo);
                }
#if !REVIT2026
                if (Math.Abs(tipo.BarMassPerUnitLength - objetivo) > 1e-9) tipo.BarMassPerUnitLength = objetivo;
#endif
            }
            catch (Exception ex)
            {
                aviso = $"no se pudo poner el peso por metro de «{tipo.Name}» ({v.KgPorMetro:0.###} kg/m): {ex.Message}";
            }
        }
        return aviso;
    }

    /// <summary>
    /// Diámetros de doblado del cuadro de la empresa: 4 d para estribos y 6 d
    /// para ganchos estándar y dobleces (ver
    /// <c>docs/planes/2026-10-08-diagnostico-formas-ganchos-propios-64.md</c>).
    /// Los LARGOS de gancho no se fijan acá: los ajusta el generador barra por
    /// barra contra el cuadro, igual que con las barras de la plantilla.
    /// </summary>
    private static BarTypeDiameterOptions Diametros(RebarBarType tipo, CuadroVarillas.Varilla v)
    {
        double d = EstandarCQ.APies(v.DiametroMm / 1000);

        return new BarTypeDiameterOptions(tipo)
        {
            BarNominalDiameter = d,
            BarModelDiameter = d,
            StirrupTieBendDiameter = 4 * d,
            StandardHookBendDiameter = 6 * d,
            StandardBendDiameter = 6 * d,
        };
    }
}
