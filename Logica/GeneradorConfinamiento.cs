namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

public static class GeneradorConfinamiento
{
    /// <summary>
    /// Gira la columneta para alinearla con el eje del muro. Si tu familia queda
    /// atravesada (la cara ancha perpendicular al muro en lugar de paralela),
    /// cambia este valor a Math.PI / 2.
    /// </summary>
    private const double GiroExtraColumneta = 0.0;

    private const double Tolerancia = 1e-6;

    /// <summary>
    /// Dos muros que se encuentran en esquina comparten el vértice, así que cada
    /// uno pediría su propia columneta en el mismo sitio. Las que caigan a menos
    /// de esta distancia de una ya colocada se omiten.
    /// </summary>
    private static readonly double ToleranciaEsquina =
        UnitUtils.ConvertToInternalUnits(0.15, UnitTypeId.Meters);

    public static ResultadoGeneracion Generar(Document doc, OpcionesGeneracion op)
    {
        var res = new ResultadoGeneracion();

        // Los muros de la lista son la fuente de la verdad: se rehace todo desde
        // cero para que cambiar las opciones cambie el resultado y para que un
        // muro que se deja de seleccionar pierda lo que se le generó antes.
        res.Eliminados = BorrarGenerados(doc);

        if (op.GenerarColumnetas && op.TipoColumneta != null && !op.TipoColumneta.IsActive)
            op.TipoColumneta.Activate();

        bool algunaVigueta = op.GenerarViguetasSuperiores || op.GenerarViguetasInferiores;
        if (algunaVigueta && op.TipoVigueta != null && !op.TipoVigueta.IsActive)
            op.TipoVigueta.Activate();

        // Compartido entre todos los muros para no duplicar columnetas en las esquinas.
        var puntosOcupados = new List<XYZ>();

        foreach (Wall muro in op.Muros)
        {
            if (muro.Location is not LocationCurve loc || loc.Curve is null)
            {
                res.Avisos.Add($"El muro {muro.Id.Value} no tiene curva de ubicación; se omite.");
                continue;
            }

            Curve curva = loc.Curve;

            if (doc.GetElement(muro.LevelId) is not Level nivelBase)
            {
                res.Avisos.Add($"El muro {muro.Id.Value} no tiene nivel asociado; se omite.");
                continue;
            }

            if (!ObtenerAlturas(doc, muro, nivelBase, out double zBase, out double zTope))
            {
                res.Avisos.Add($"No se pudo determinar la altura del muro {muro.Id.Value}; se omite.");
                continue;
            }

            if (zTope - zBase < Tolerancia)
            {
                res.Avisos.Add($"El muro {muro.Id.Value} tiene altura nula o negativa; se omite.");
                continue;
            }

            if (op.GenerarColumnetas && op.TipoColumneta != null)
                res.Columnetas += ColocarColumnetas(
                    doc, op, muro, curva, nivelBase, zBase, zTope, puntosOcupados, res);

            if (op.GenerarViguetasInferiores && op.TipoVigueta != null)
            {
                if (ColocarVigueta(doc, op.TipoVigueta, muro, curva, nivelBase, zBase))
                    res.ViguetasInferiores++;
            }

            if (op.GenerarViguetasSuperiores && op.TipoVigueta != null)
            {
                double zVigueta = op.AplicarDesfaseVertical ? zTope - op.DesfaseVertical : zTope;

                if (zVigueta < zBase - Tolerancia)
                {
                    res.Avisos.Add(
                        $"En el muro {muro.Id.Value} el desfase supera la altura del muro; " +
                        "la vigueta superior se omite.");
                }
                else if (ColocarVigueta(doc, op.TipoVigueta, muro, curva, nivelBase, zVigueta))
                {
                    res.ViguetasSuperiores++;
                }
            }
        }

        return res;
    }

    /// <summary>
    /// Borra todas las columnetas y viguetas que este add-in creó en el
    /// documento. Solo alcanza a los elementos que llevan nuestra marca: lo que
    /// hayas modelado a mano no se toca.
    /// </summary>
    private static int BorrarGenerados(Document doc)
    {
        // Fuerza el registro del esquema antes de filtrar por él.
        MarcaConfinamiento.ObtenerEsquema();

        ICollection<ElementId> ids = new FilteredElementCollector(doc)
            .WherePasses(MarcaConfinamiento.Filtro())
            .WhereElementIsNotElementType()
            .ToElementIds();

        if (ids.Count == 0) return 0;

        doc.Delete(ids);
        return ids.Count;
    }

    /// <summary>
    /// Calcula la cota de arranque y de tope del muro respetando su restricción
    /// superior si la tiene, y si no, su altura no conectada.
    /// </summary>
    private static bool ObtenerAlturas(
        Document doc, Wall muro, Level nivelBase, out double zBase, out double zTope)
    {
        zBase = 0;
        zTope = 0;

        double desfaseBase = muro.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0;
        zBase = nivelBase.Elevation + desfaseBase;

        ElementId idNivelSup = muro.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId()
                               ?? ElementId.InvalidElementId;

        if (idNivelSup != ElementId.InvalidElementId && doc.GetElement(idNivelSup) is Level nivelSup)
        {
            double desfaseSup = muro.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET)?.AsDouble() ?? 0;
            zTope = nivelSup.Elevation + desfaseSup;
            return true;
        }

        Parameter? altura = muro.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
        if (altura is null) return false;

        zTope = zBase + altura.AsDouble();
        return true;
    }

    /// <summary>
    /// Reparte columnetas a lo largo del muro: una en cada extremo y las
    /// intermedias necesarias, equiespaciadas, sin superar la separación máxima.
    /// </summary>
    private static int ColocarColumnetas(
        Document doc, OpcionesGeneracion op, Wall muro, Curve curva,
        Level nivelBase, double zBase, double zTope,
        List<XYZ> puntosOcupados, ResultadoGeneracion res)
    {
        double longitud = curva.Length;
        if (longitud < Tolerancia) return 0;

        int tramos = Math.Max(1, (int)Math.Ceiling(longitud / op.SeparacionMaxima - Tolerancia));
        int colocadas = 0;

        for (int i = 0; i <= tramos; i++)
        {
            double t = (double)i / tramos;
            XYZ punto = curva.Evaluate(t, true);
            XYZ tangente = curva.ComputeDerivatives(t, true).BasisX;
            XYZ base_ = new XYZ(punto.X, punto.Y, zBase);

            if (YaOcupado(puntosOcupados, base_))
            {
                res.ColumnetasOmitidas++;
                continue;
            }

            puntosOcupados.Add(base_);

            var columneta = doc.Create.NewFamilyInstance(
                base_,
                op.TipoColumneta,
                nivelBase,
                StructuralType.Column);

            FijarAlturaColumneta(columneta, nivelBase, zBase, zTope);
            AlinearConMuro(doc, columneta, punto, tangente);
            MarcaConfinamiento.Marcar(columneta, muro.Id);

            colocadas++;
        }

        return colocadas;
    }

    /// <summary>
    /// Compara en 3D, no solo en planta: dos muros en niveles distintos que se
    /// crucen en planta sí deben tener cada uno su columneta.
    /// </summary>
    private static bool YaOcupado(List<XYZ> puntos, XYZ candidato)
    {
        foreach (XYZ p in puntos)
        {
            if (p.DistanceTo(candidato) < ToleranciaEsquina) return true;
        }
        return false;
    }

    private static void FijarAlturaColumneta(
        FamilyInstance columneta, Level nivelBase, double zBase, double zTope)
    {
        columneta.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.Set(nivelBase.Id);
        columneta.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.Set(nivelBase.Id);
        columneta.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)
            ?.Set(zBase - nivelBase.Elevation);
        columneta.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)
            ?.Set(zTope - nivelBase.Elevation);
    }

    private static void AlinearConMuro(Document doc, FamilyInstance columneta, XYZ punto, XYZ tangente)
    {
        if (tangente.GetLength() < Tolerancia) return;

        XYZ dir = tangente.Normalize();
        double angulo = Math.Atan2(dir.Y, dir.X) + GiroExtraColumneta;
        if (Math.Abs(angulo) < Tolerancia) return;

        var eje = Line.CreateBound(punto, punto + XYZ.BasisZ);
        ElementTransformUtils.RotateElement(doc, columneta.Id, eje, angulo);
    }

    /// <summary>
    /// Coloca una vigueta siguiendo el eje del muro a la cota indicada.
    /// </summary>
    private static bool ColocarVigueta(
        Document doc, FamilySymbol tipo, Wall muro, Curve curva, Level nivelBase, double z)
    {
        if (curva.Length < Tolerancia) return false;

        double zActual = curva.GetEndPoint(0).Z;
        Curve curvaVigueta = curva.CreateTransformed(
            Transform.CreateTranslation(new XYZ(0, 0, z - zActual)));

        var vigueta = doc.Create.NewFamilyInstance(
            curvaVigueta, tipo, nivelBase, StructuralType.Beam);

        // Sin esto Revit aplica las extensiones por defecto del tipo y las
        // viguetas dejan de coincidir con la longitud del muro.
        vigueta.get_Parameter(BuiltInParameter.START_EXTENSION)?.Set(0.0);
        vigueta.get_Parameter(BuiltInParameter.END_EXTENSION)?.Set(0.0);

        MarcaConfinamiento.Marcar(vigueta, muro.Id);

        return true;
    }
}
