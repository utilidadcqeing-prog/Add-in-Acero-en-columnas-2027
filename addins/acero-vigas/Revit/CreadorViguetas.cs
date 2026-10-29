using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CQEING.Vigas.Calculo.Viguetas;

namespace CQEING.Vigas.Revit;

/// <summary>
/// Crea y quita en Revit las viguetas de un panel (#952). Cada operación es una transacción: Ctrl+Z la deshace entera.
/// </summary>
internal static class CreadorViguetas
{
    /// <summary>
    /// Crea las viguetas vivas de <paramref name="propuesta"/> con <paramref name="tipo"/>, de eje a eje de viga, en
    /// <paramref name="nivel"/> y a la cota de las vigas del piso. Antes quita las que el add-in ya había creado en el
    /// panel (<paramref name="anteriores"/>), así repetir no las duplica. Devuelve cuántas creó.
    /// </summary>
    public static int Crear(Document doc, Level nivel, FamilySymbol tipo, PropuestaViguetas propuesta, IEnumerable<ElementId> anteriores)
    {
        double z = LectorPiso.CotaLineas(doc, nivel);
        double minimo = doc.Application.ShortCurveTolerance;
        using var t = new Transaction(doc, "CQEING: crear viguetas");
        t.Start();
        Borrar(doc, anteriores);
        if (!tipo.IsActive)
            tipo.Activate();
        int creadas = 0;
        foreach (Vigueta v in propuesta.Vivas)
        {
            XYZ a = Pies(propuesta.Punto(v.Cota, v.EjeDesde), z), b = Pies(propuesta.Punto(v.Cota, v.EjeHasta), z);
            if (a.DistanceTo(b) <= minimo)
                continue;
            FamilyInstance f = doc.Create.NewFamilyInstance(Line.CreateBound(a, b), tipo, nivel, StructuralType.Beam);
            f.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(LectorPiso.Marca);
            creadas++;
        }
        t.Commit();
        return creadas;
    }

    /// <summary>Quita las viguetas que creó el add-in (solo esas: se comprueba la marca). Devuelve cuántas quitó.</summary>
    public static int Quitar(Document doc, IEnumerable<ElementId> ids)
    {
        using var t = new Transaction(doc, "CQEING: quitar viguetas");
        t.Start();
        int quitadas = Borrar(doc, ids);
        t.Commit();
        return quitadas;
    }

    private static int Borrar(Document doc, IEnumerable<ElementId> ids)
    {
        List<ElementId> nuestras = ids.Where(id => doc.GetElement(id) is Element e && LectorPiso.EsCreada(e)).ToList();
        if (nuestras.Count > 0)
            doc.Delete(nuestras);
        return nuestras.Count;
    }

    private static XYZ Pies(Calculo.Porticos.Punto p, double z) =>
        new(UnitUtils.ConvertToInternalUnits(p.X, UnitTypeId.Millimeters), UnitUtils.ConvertToInternalUnits(p.Y, UnitTypeId.Millimeters), z);
}
