namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// Fase 2: de un id de columna a la lista ordenada de tramos.
///
/// Es el cimiento del confinamiento, porque antes de repartir estribos hay que
/// saber dónde empieza y termina de verdad cada tramo. Y «de verdad» significa
/// leído de la geometría:
///
///  - La altura sale del sólido, no del parámetro «Longitud del sistema», que
///    no existe o miente en cuanto la columna está mal modelada.
///  - El corte de un tramo lo pone la cara inferior de la viga, no la cota del
///    nivel. Entre una y otra suele haber medio metro.
///  - Un nivel sin viga no corta nada: no es nivel de confinamiento. Los niveles
///    solo sirven para rotular la elevación.
///  - Una viga cuenta si TOCA la columna. Una viga que pasa cerca, no.
///
/// Ninguna columna mal modelada tumba el lote: lo que no se pudo determinar sale
/// como aviso dentro del propio contexto.
/// </summary>
public sealed class LectorContexto : IProveedorContexto
{
    /// <summary>Un centímetro en pies. Por debajo de esto un tramo no es un tramo.</summary>
    private static readonly double TramoMinimo = APies(0.01);

    private const double Tolerancia = 1e-6;

    private readonly Document _doc;

    public LectorContexto(Document doc) => _doc = doc;

    /// <summary>
    /// Lista las columnas estructurales del documento para poblar la selección
    /// de L2. El id de cada una viaja como string (ver el porqué en
    /// <see cref="IProveedorContexto"/>) — es simplemente <c>ElementId.Value</c>
    /// convertido a texto.
    /// </summary>
    public IReadOnlyList<ColumnaResumen> ListarColumnas() =>
        new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Select(c => new ColumnaResumen(IdTexto(c.Id), Marca(c)))
            .ToList();

    public ColumnaContexto Leer(string idColumna)
    {
        var avisos = new List<string>();

        if (!long.TryParse(idColumna, out long valor))
            throw new InvalidOperationException($"«{idColumna}» no es un id de columna válido.");

        if (_doc.GetElement(new ElementId(valor)) is not FamilyInstance columna)
            throw new InvalidOperationException($"El elemento {idColumna} no es una columna.");

        Solid solido = SolidoMayor(columna.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }))
            ?? throw new InvalidOperationException(
                $"No se pudo leer la geometría de la columna {Marca(columna)}.");

        (double zPie, double zTope) = ExtensionZ(solido);

        // La zapata manda sobre el arranque: el acero de la columna no entra en ella.
        double zZapata = TopeDeZapata(solido, avisos);
        bool sobreZapata = zZapata > zPie + TramoMinimo;
        double zArranque = sobreZapata ? zZapata : zPie;

        if (zTope - zArranque <= TramoMinimo)
            throw new InvalidOperationException(
                $"La columna {Marca(columna)} no deja altura libre sobre la cimentación.");

        List<(double Inf, double Sup)> nudos = BandasDeViga(solido, zArranque, zTope);

        if (nudos.Count == 0)
            avisos.Add(
                "Ninguna viga toca esta columna: queda un solo tramo libre de punta a punta. " +
                "Si esperabas nudos, revisa que las vigas lleguen a tocarla.");

        return new ColumnaContexto
        {
            Marca = Marca(columna),
            BaseX = AMetros(LadoX(solido)),
            BaseY = AMetros(LadoY(solido)),
            Recubrimiento = AMetros(Recubrimiento(columna, avisos)),
            ZInferior = AMetros(zArranque),
            ZSuperior = AMetros(zTope),
            Tramos = ArmarTramos(zArranque, zTope, nudos, sobreZapata),
            Niveles = Rotulos(zArranque, zTope),
            Avisos = avisos
        };
    }

    // ---------------------------------------------------------------- tramos

    /// <summary>
    /// Cose las bandas de viga con lo que queda libre entre ellas, de abajo hacia
    /// arriba y sin dejar huecos. El primero se marca contra zapata si la columna
    /// arranca sobre una, porque ese borde no admite el mismo tratamiento que un
    /// extremo libre.
    /// </summary>
    private static List<Tramo> ArmarTramos(
        double zArranque,
        double zTope,
        List<(double Inf, double Sup)> nudos,
        bool sobreZapata)
    {
        var tramos = new List<Tramo>();
        double cursor = zArranque;

        void Agregar(double inf, double sup, TipoTramo tipo)
        {
            if (sup - inf <= TramoMinimo) return;

            tramos.Add(new Tramo
            {
                Indice = tramos.Count,
                ZInferior = AMetros(inf),
                ZSuperior = AMetros(sup),
                // Solo el primero, y solo si es un tramo libre: si la columna
                // arrancara dentro de una viga, saber eso importa más.
                Tipo = tramos.Count == 0 && sobreZapata && tipo == TipoTramo.Libre
                    ? TipoTramo.ContraZapata
                    : tipo
            });
        }

        foreach ((double inf, double sup) in nudos)
        {
            Agregar(cursor, inf, TipoTramo.Libre);
            Agregar(Math.Max(inf, cursor), sup, TipoTramo.DentroDeViga);
            cursor = Math.Max(cursor, sup);
        }

        Agregar(cursor, zTope, TipoTramo.Libre);

        return tramos;
    }

    /// <summary>
    /// Las franjas de altura que las vigas le quitan a la columna, fusionadas y
    /// ordenadas. Se fusionan porque en un nudo suelen concurrir dos o cuatro
    /// vigas al mismo canto, y eso es un solo sólido para la ventana, no cuatro.
    /// </summary>
    private List<(double Inf, double Sup)> BandasDeViga(Solid columna, double zArranque, double zTope)
    {
        var crudas = new List<(double Inf, double Sup)>();

        foreach (Element viga in Tocan(columna, BuiltInCategory.OST_StructuralFraming))
        {
            Solid? solidoViga = SolidoMayor(
                viga.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }));

            if (solidoViga is null) continue;

            // La banda es la intersección real de los dos sólidos, no el alto de la
            // viga: una viga que apenas muerde la esquina no se come todo el canto,
            // y una inclinada toca en un rango distinto al de su bounding box.
            if (Interseccion(columna, solidoViga) is not Solid corte) continue;

            (double inf, double sup) = ExtensionZ(corte);

            inf = Math.Max(inf, zArranque);
            sup = Math.Min(sup, zTope);

            if (sup - inf > TramoMinimo) crudas.Add((inf, sup));
        }

        return Fusionar(crudas);
    }

    private static List<(double Inf, double Sup)> Fusionar(List<(double Inf, double Sup)> bandas)
    {
        var fusionadas = new List<(double Inf, double Sup)>();

        foreach ((double inf, double sup) in bandas.OrderBy(b => b.Inf))
        {
            if (fusionadas.Count > 0 && inf <= fusionadas[^1].Sup + TramoMinimo)
            {
                (double antInf, double antSup) = fusionadas[^1];
                fusionadas[^1] = (antInf, Math.Max(antSup, sup));
            }
            else
            {
                fusionadas.Add((inf, sup));
            }
        }

        return fusionadas;
    }

    /// <summary>
    /// Cota superior de la zapata sobre la que arranca la columna, o
    /// <see cref="double.MinValue"/> si no hay ninguna.
    /// </summary>
    private double TopeDeZapata(Solid columna, List<string> avisos)
    {
        double tope = double.MinValue;

        foreach (Element zapata in Tocan(columna, BuiltInCategory.OST_StructuralFoundation))
        {
            Solid? solidoZapata = SolidoMayor(
                zapata.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }));

            if (solidoZapata is null) continue;
            if (Interseccion(columna, solidoZapata) is not Solid corte) continue;

            tope = Math.Max(tope, ExtensionZ(corte).Sup);
        }

        if (tope > double.MinValue)
            avisos.Add($"Arranca sobre cimentación: el acero empieza en {AMetros(tope):0.00} m.");

        return tope;
    }

    /// <summary>
    /// Elementos de esa categoría que se cruzan con el sólido de la columna.
    /// Es intersección real, no proximidad: una viga que pasa al lado sin llegar
    /// a tocarla no forma nudo.
    /// </summary>
    private IEnumerable<Element> Tocan(Solid columna, BuiltInCategory categoria) =>
        new FilteredElementCollector(_doc)
            .OfCategory(categoria)
            .WhereElementIsNotElementType()
            .WherePasses(new ElementIntersectsSolidFilter(columna))
            .ToElements();

    // ------------------------------------------------------------- geometría

    /// <summary>
    /// Cotas mínima y máxima del sólido, tomadas de sus aristas.
    ///
    /// No se usa <c>Solid.GetBoundingBox</c> porque esa caja viene en el sistema
    /// local del sólido, y una columna girada en planta devolvería cotas que no
    /// son las del modelo.
    /// </summary>
    private static (double Inf, double Sup) ExtensionZ(Solid solido)
    {
        double inf = double.MaxValue;
        double sup = double.MinValue;

        foreach (Edge arista in solido.Edges)
        {
            foreach (XYZ p in arista.Tessellate())
            {
                inf = Math.Min(inf, p.Z);
                sup = Math.Max(sup, p.Z);
            }
        }

        if (inf > sup)
            throw new InvalidOperationException("El sólido no tiene aristas legibles.");

        return (inf, sup);
    }

    private static double LadoX(Solid solido) => Extension(solido, p => p.X);

    private static double LadoY(Solid solido) => Extension(solido, p => p.Y);

    /// <summary>
    /// Ancho del sólido en una dirección global. Para una columna rectangular
    /// alineada con los ejes es su sección; para una girada da el sobre que la
    /// contiene, y por eso la ventana solo admite secciones rectangulares.
    /// </summary>
    private static double Extension(Solid solido, Func<XYZ, double> eje)
    {
        double min = double.MaxValue;
        double max = double.MinValue;

        foreach (Edge arista in solido.Edges)
        {
            foreach (XYZ p in arista.Tessellate())
            {
                min = Math.Min(min, eje(p));
                max = Math.Max(max, eje(p));
            }
        }

        return max - min;
    }

    private static Solid? Interseccion(Solid a, Solid b)
    {
        try
        {
            Solid corte = BooleanOperationsUtils.ExecuteBooleanOperation(
                a, b, BooleanOperationsType.Intersect);

            return corte.Volume > Tolerancia ? corte : null;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Sólidos que Revit no sabe cruzar (mallas sucias, caras degeneradas).
            // Se ignora ese elemento en vez de tumbar la lectura entera.
            return null;
        }
    }

    private static Solid? SolidoMayor(GeometryElement? ge)
    {
        if (ge is null) return null;

        Solid? mejor = null;
        foreach (GeometryObject go in ge)
        {
            Solid? candidato = go switch
            {
                Solid s when s.Volume > Tolerancia => s,
                GeometryInstance gi => SolidoMayor(gi.GetInstanceGeometry()),
                _ => null
            };

            if (candidato is not null && (mejor is null || candidato.Volume > mejor.Volume))
                mejor = candidato;
        }

        return mejor;
    }

    // ---------------------------------------------------------------- lectura

    /// <summary>
    /// Recubrimiento lateral en pies. Aquí solo se LEE: corregirlo es trabajo del
    /// generador, que sí abre transacción. Si la columna no lo tiene, se avisa y
    /// se devuelve 0 para que el defecto se vea en la ventana en vez de colarse
    /// como un 4 cm inventado.
    /// </summary>
    private double Recubrimiento(FamilyInstance columna, List<string> avisos)
    {
        Parameter? p = columna.get_Parameter(BuiltInParameter.CLEAR_COVER_OTHER);

        if (p is not null && _doc.GetElement(p.AsElementId()) is RebarCoverType tipo)
            return tipo.CoverDistance;

        avisos.Add("No tiene recubrimiento lateral asignado. Se corregirá al generar.");
        return 0;
    }

    /// <summary>
    /// Niveles que caen dentro del alto de la columna, más el inmediato de abajo
    /// y el de arriba: la elevación se lee mejor con un rótulo a cada punta.
    /// </summary>
    private List<NivelRotulo> Rotulos(double zArranque, double zTope)
    {
        List<Level> niveles = new FilteredElementCollector(_doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(n => n.Elevation)
            .ToList();

        var dentro = niveles
            .Where(n => n.Elevation >= zArranque - Tolerancia && n.Elevation <= zTope + Tolerancia)
            .ToList();

        Level? abajo = niveles.LastOrDefault(n => n.Elevation < zArranque - Tolerancia);
        Level? arriba = niveles.FirstOrDefault(n => n.Elevation > zTope + Tolerancia);

        if (abajo is not null) dentro.Insert(0, abajo);
        if (arriba is not null) dentro.Add(arriba);

        return dentro
            .Select(n => new NivelRotulo { Nombre = n.Name, Cota = AMetros(n.Elevation) })
            .ToList();
    }

    private static string Marca(FamilyInstance columna) =>
        columna.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() is { Length: > 0 } marca
            ? marca
            : $"[{columna.Id}]";

    /// <summary>Simétrico de <c>new ElementId(long.Parse(id))</c> del lado de lectura.</summary>
    private static string IdTexto(ElementId id) =>
        id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static double AMetros(double pies) =>
        UnitUtils.ConvertFromInternalUnits(pies, UnitTypeId.Meters);

    private static double APies(double metros) =>
        UnitUtils.ConvertToInternalUnits(metros, UnitTypeId.Meters);
}
