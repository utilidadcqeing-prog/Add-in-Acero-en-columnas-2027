using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Despiece.Revit;

/// <summary>Las dos imágenes de la vista previa.</summary>
internal sealed record PreviaGenerada(string Longitudinal, string Seccion);

/// <summary>Las vistas del navegador de Revit. <paramref name="YaExistian"/>: no se creó nada porque ya estaban.</summary>
internal sealed record VistasCreadas(string Longitudinal, string Seccion, bool YaExistian);

/// <summary>
/// La vista previa real (#221): crea en Revit la vista longitudinal y la sección de una viga como lo hace Santiago, las
/// exporta y deshace todo. Los pasos y sus motivos están en "Cómo se hace" de #217. No usa nombres de parámetros ni de
/// formas, que cambian con el idioma de Revit.
/// </summary>
internal static class GeneradorPrevia
{
    private const double M = 1 / 0.3048;

    /// <summary>Como en el plano E-01: la vista larga a 1:50, para que los textos se lean; la sección a 1:25.</summary>
    private const int EscalaLongitudinal = 50;
    private const int EscalaSeccion = 25;

    // Medidas de la vista larga, en pies del modelo: a 1:50 el texto ocupa el doble que a 1:25.
    private const double SeparacionViga = 0.6 * M;
    private const double AltoEtiqueta = 0.40 * M;
    private const double MedioAnchoEtiqueta = 0.65 * M;
    private const double HuecoFilas = 0.15 * M;
    private const double HuecoEnFila = 0.20 * M;

    // La vista es solo de la viga (Juan, 30-oct): sin columnas, ni ocultas con otro dibujo. El recorte va justo a su alto;
    // las barras sacadas se ven por fuera de él. Donde había una columna, Revit deja la viga cortada: es su geometría real.
    private const double HolguraRecorte = 0.005 * M;
    private const double ColaEje = 0.3 * M;
    private const double CabezaEje = 0.8 * M;
    private const double CotaLuces = 0.35 * M;
    private const double CotaTotal = 0.75 * M;

    public static PreviaGenerada Generar(Document doc, VigaContinua viga, string carpeta)
    {
        Registro.Paso("previa: empieza " + viga.Elemento.Id);
        using var t = new Transaction(doc, "Vista previa de despiece");
        t.Start();
        try
        {
            (View longitudinal, View seccion) = Construir(doc, viga, TiposPropios.Crear(doc, false));
            Registro.Paso("previa: exportando la longitudinal");
            string a = ExportadorImagen.Exportar(doc, longitudinal, carpeta);
            Registro.Paso("previa: exportando la sección");
            string b = ExportadorImagen.Exportar(doc, seccion, carpeta);
            return new PreviaGenerada(a, b);
        }
        finally
        {
            Registro.Paso("previa: deshaciendo");
            t.RollBack();
            Registro.Paso("previa: deshecho");
        }
    }

    /// <summary>
    /// Deja las dos vistas en el navegador de Revit (#222), iguales a la vista previa. No coloca nada en las hojas. Si ya
    /// existe alguna, no crea nada: reemplazarla podría borrar lo que se haya cambiado a mano.
    /// </summary>
    public static VistasCreadas Crear(Document doc, VigaContinua viga, string nombre)
    {
        string nombreSeccion = "Sección " + nombre;
        bool yaExisten = new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Any(v => v.Name == nombre || v.Name == nombreSeccion);
        if (yaExisten)
            return new VistasCreadas(nombre, nombreSeccion, true);

        using var t = new Transaction(doc, "Crear vistas de despiece");
        t.Start();
        try
        {
            (View longitudinal, View seccion) = Construir(doc, viga, TiposPropios.Crear(doc, true));
            longitudinal.Name = nombre;
            seccion.Name = nombreSeccion;
            if (t.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit no aceptó las vistas.");
            return new VistasCreadas(nombre, nombreSeccion, false);
        }
        finally
        {
            if (t.HasStarted() && !t.HasEnded())
                t.RollBack();
        }
    }

    private static (View Longitudinal, View Seccion) Construir(Document doc, VigaContinua viga, TiposPropios tipos)
    {
        ViewFamilyType tipoSeccion = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .First(v => v.ViewFamily == ViewFamily.Section);
        List<Rebar> barras = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
            .Where(b => viga.Vigas.Contains(b.GetHostId())).ToList();
        Registro.Paso($"vistas: {barras.Count} barras; vista longitudinal");
        View longitudinal = Longitudinal(doc, tipoSeccion, viga, barras, tipos);
        Registro.Paso("vistas: sección");
        View seccion = Seccion(doc, tipoSeccion, viga, barras, tipos);
        doc.Regenerate();
        return (longitudinal, seccion);
    }

    private static (XYZ Min, XYZ Max) Caja(Document doc, VigaContinua viga)
    {
        List<BoundingBoxXYZ> cajas = viga.Vigas.Select(id => doc.GetElement(id).get_BoundingBox(null)).ToList();
        return (new XYZ(cajas.Min(c => c.Min.X), cajas.Min(c => c.Min.Y), cajas.Min(c => c.Min.Z)),
            new XYZ(cajas.Max(c => c.Max.X), cajas.Max(c => c.Max.Y), cajas.Max(c => c.Max.Z)));
    }

    /// <summary>Una barra sacada y lo que ocupa en la vista, medido en Revit.</summary>
    private sealed record Sacada(Rebar Barra, Element Detalle, XYZ Posicion, double Izquierda, double Derecha, double Abajo, double Arriba)
    {
        public double Alto => Arriba - Abajo;
    }

    private static View Longitudinal(Document doc, ViewFamilyType tipo, VigaContinua viga, List<Rebar> barras, TiposPropios tipos)
    {
        (XYZ min, XYZ max) = Caja(doc, viga);
        XYZ derecha = SentidoDeLectura(doc, viga);
        // Al crear la sección, la derecha de la vista sale contraria al BasisX pedido (medido el 30-oct: con +X salió C-B-A).
        XYZ basisX = derecha.Negate();
        XYZ haciaElQueMira = basisX.CrossProduct(XYZ.BasisZ);
        XYZ centro = (min + max) / 2;
        double largo = Math.Abs((max - min).DotProduct(derecha));
        double ancho = Math.Abs((max - min).DotProduct(haciaElQueMira));
        double alto = max.Z - min.Z;
        XYZ enPlano = centro + haciaElQueMira * (ancho / 2 + 0.3 * M);

        Transform t = Transform.Identity;
        t.BasisX = basisX;
        t.BasisY = XYZ.BasisZ;
        t.BasisZ = haciaElQueMira;
        t.Origin = enPlano;
        var caja = new BoundingBoxXYZ
        {
            Transform = t,
            Min = new XYZ(-largo / 2 - 1.0 * M, -alto / 2 - 12.0 * M, -ancho - 0.7 * M),
            Max = new XYZ(largo / 2 + 1.0 * M, alto / 2 + 12.0 * M, 0),
        };
        ViewSection vista = Preparar(doc, tipo, caja, "V", EscalaLongitudinal);
        Registro.Paso("longitudinal: limpiando");
        Limpiar(doc, vista, viga, barras);
        Registro.Paso("longitudinal: coloreando");
        Colorear(doc, vista, barras);
        doc.Regenerate();

        // Se sacan todas lejos de la viga, se mide cuánto ocupa cada una y se acomodan en filas sin encimarse.
        Registro.Paso("longitudinal: sacando barras");
        List<Rebar> longitudinales = barras.Where(b => EsLongitudinal(b, derecha)).ToList();
        var creadas = new List<(Rebar Barra, Element Detalle, XYZ Posicion)>();
        foreach (Rebar b in longitudinales)
        {
            XYZ c = Centro(b);
            XYZ p = new XYZ(c.X, c.Y, max.Z + 11 * M) + haciaElQueMira * (enPlano - c).DotProduct(haciaElQueMira);
            Registro.Paso($"longitudinal: barra {b.Id}");
            Element d = RebarBendingDetail.Create(doc, vista.Id, b.Id, 0, tipos.Barra, p, 0);
            Pintar(vista, d.Id, b, 2);
            creadas.Add((b, d, p));
        }
        doc.Regenerate();
        List<Sacada> sacadas = creadas.Select(c =>
        {
            BoundingBoxXYZ m = c.Detalle.get_BoundingBox(vista);
            double a = m.Min.DotProduct(derecha), b = m.Max.DotProduct(derecha);
            return new Sacada(c.Barra, c.Detalle, c.Posicion, Math.Min(a, b), Math.Max(a, b), m.Min.Z - c.Posicion.Z, m.Max.Z - c.Posicion.Z);
        }).ToList();

        double mitad = (min.Z + max.Z) / 2;
        double tope = Acomodar(doc, vista, sacadas.Where(s => Centro(s.Barra).Z > mitad).ToList(), max.Z + SeparacionViga, true, tipos);
        double fondo = Acomodar(doc, vista, sacadas.Where(s => Centro(s.Barra).Z <= mitad).ToList(), min.Z - SeparacionViga, false, tipos);

        Recortar(vista, centro, largo / 2 + 1.0 * M, largo / 2 + 1.0 * M, alto / 2 + HolguraRecorte, alto / 2 + HolguraRecorte);
        vista.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Levels), true);
        doc.Regenerate();
        AjustarEjes(doc, vista, Math.Max(tope, max.Z) + CabezaEje, Math.Min(fondo, min.Z) - CotaTotal - ColaEje);
        Cotar(doc, vista, centro, derecha, largo, min, max, Math.Min(fondo, min.Z), tipos);
        vista.DisplayStyle = DisplayStyle.FlatColors;
        return vista;
    }

    /// <summary>Lleva los círculos de los ejes por encima de las filas de barras, para que no choquen, solo en esta vista.</summary>
    private static void AjustarEjes(Document doc, View vista, double arriba, double abajo)
    {
        foreach (Grid g in new FilteredElementCollector(doc, vista.Id).OfClass(typeof(Grid)).Cast<Grid>())
        {
            Curve actual = g.GetCurvesInView(DatumExtentType.ViewSpecific, vista)[0];
            XYZ a = actual.GetEndPoint(0), e = actual.GetEndPoint(1);
            g.SetDatumExtentType(DatumEnds.End0, vista, DatumExtentType.ViewSpecific);
            g.SetDatumExtentType(DatumEnds.End1, vista, DatumExtentType.ViewSpecific);
            bool subeElPrimero = a.Z > e.Z;
            g.SetCurveInView(DatumExtentType.ViewSpecific, vista, Line.CreateBound(new XYZ(a.X, a.Y, subeElPrimero ? arriba : abajo), new XYZ(e.X, e.Y, subeElPrimero ? abajo : arriba)));
        }
    }

    /// <summary>Las luces entre ejes y el total, debajo de todo. Son cotas de Revit, ligadas a los ejes (#222). Con menos de dos ejes no hay qué acotar.</summary>
    private static void Cotar(Document doc, View vista, XYZ centro, XYZ derecha, double largo, XYZ min, XYZ max, double fondo, TiposPropios tipos)
    {
        double desde = Math.Min(min.DotProduct(derecha), max.DotProduct(derecha)), hasta = Math.Max(min.DotProduct(derecha), max.DotProduct(derecha));
        List<Grid> ejes = new FilteredElementCollector(doc, vista.Id).OfClass(typeof(Grid)).Cast<Grid>()
            .Where(g => g.Curve is Line l && Math.Abs(l.Direction.DotProduct(derecha)) < 0.01)
            .Where(g => g.Curve.GetEndPoint(0).DotProduct(derecha) >= desde - 0.5 * M && g.Curve.GetEndPoint(0).DotProduct(derecha) <= hasta + 0.5 * M)
            .OrderBy(g => g.Curve.GetEndPoint(0).DotProduct(derecha)).ToList();
        if (ejes.Count < 2)
            return;

        void Cota(IEnumerable<Grid> entre, double z)
        {
            var referencias = new ReferenceArray();
            foreach (Grid g in entre)
                referencias.Append(new Reference(g));
            XYZ medio = new(centro.X, centro.Y, z);
            Dimension cota = doc.Create.NewDimension(vista, Line.CreateBound(medio - derecha * (largo / 2), medio + derecha * (largo / 2)), referencias);
            if (tipos.Cota is { } tipo)
                cota.DimensionType = tipo;
        }

        Cota(ejes, fondo - CotaLuces);
        if (ejes.Count > 2)
            Cota([ejes[0], ejes[^1]], fondo - CotaTotal);
    }

    /// <summary>
    /// Pone cada barra en la primera fila donde quepa, de la viga hacia afuera, con su etiqueta encima. Devuelve hasta qué
    /// altura llegó la última fila.
    /// </summary>
    private static double Acomodar(Document doc, View vista, List<Sacada> sacadas, double desde, bool haciaArriba, TiposPropios tipos)
    {
        var finDeFila = new List<double>();
        var filas = new List<List<Sacada>>();
        foreach (Sacada s in sacadas.OrderBy(s => s.Izquierda))
        {
            double medio = (s.Izquierda + s.Derecha) / 2;
            double izquierda = Math.Min(s.Izquierda, medio - MedioAnchoEtiqueta), derecha = Math.Max(s.Derecha, medio + MedioAnchoEtiqueta);
            int fila = finDeFila.FindIndex(f => izquierda > f + HuecoEnFila);
            if (fila < 0)
            {
                finDeFila.Add(derecha);
                filas.Add([s]);
            }
            else
            {
                finDeFila[fila] = derecha;
                filas[fila].Add(s);
            }
        }

        double z = desde;
        foreach (List<Sacada> fila in filas)
        {
            double altoFila = fila.Max(s => s.Alto) + AltoEtiqueta;
            double piso = haciaArriba ? z : z - altoFila;
            foreach (Sacada s in fila)
            {
                RebarBendingDetail.SetPosition(s.Detalle, new XYZ(s.Posicion.X, s.Posicion.Y, piso - s.Abajo));
                TextNote nota = TextNote.Create(doc, vista.Id, new XYZ(s.Posicion.X, s.Posicion.Y, piso + s.Alto + AltoEtiqueta), Etiqueta(doc, s.Barra), tipos.Nota(HorizontalTextAlignment.Center));
                Pintar(vista, nota.Id, s.Barra, null);
            }
            z = haciaArriba ? z + altoFila + HuecoFilas : z - altoFila - HuecoFilas;
        }
        return z;
    }

    private static View Seccion(Document doc, ViewFamilyType tipo, VigaContinua viga, List<Rebar> barras, TiposPropios tipos)
    {
        (XYZ min, XYZ max) = Caja(doc, viga);
        XYZ dir = viga.Direccion;
        XYZ base_ = dir.CrossProduct(XYZ.BasisZ).Negate();
        double ancho = Math.Abs((max - min).DotProduct(base_));
        double alto = max.Z - min.Z;
        XYZ medio = (min + max) / 2;
        double aLoLargo = PuntoDeCorte(doc, viga, barras, dir);
        XYZ corte = medio + dir * (aLoLargo - medio.DotProduct(dir));

        Transform t = Transform.Identity;
        t.BasisX = base_;
        t.BasisY = XYZ.BasisZ;
        t.BasisZ = base_.CrossProduct(XYZ.BasisZ);
        t.Origin = corte;
        var caja = new BoundingBoxXYZ { Transform = t, Min = new XYZ(-1, -1, -0.3 * M), Max = new XYZ(1, 1, 0) };
        ViewSection vista = Preparar(doc, tipo, caja, "S", EscalaSeccion);
        // Revit puede voltear la vista: lo que manda es su derecha real.
        XYZ derecha = vista.RightDirection;
        // Justa: la sección a la izquierda y el estribo a su lado (captura de Santiago).
        Recortar(vista, corte, ancho / 2 + 0.55 * M, ancho / 2 + 1.05 * M, alto / 2 + 0.5 * M, alto / 2 + 0.5 * M);
        Limpiar(doc, vista, viga, barras);
        Colorear(doc, vista, barras);
        vista.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Levels), true);
        vista.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Grids), true);
        var gris = new OverrideGraphicSettings();
        gris.SetCutBackgroundPatternId(Solido(doc));
        gris.SetCutBackgroundPatternColor(new Color(205, 205, 205));
        foreach (ElementId id in viga.Vigas)
            vista.SetElementOverrides(id, gris);
        vista.DisplayStyle = DisplayStyle.FlatColors;
        doc.Regenerate();

        // Una etiqueta con flecha por cada barra que pasa por el corte. El bastón no se etiqueta (Santiago, 30-oct).
        var cortadas = new List<(Rebar Barra, XYZ Punto)>();
        foreach (Rebar b in barras.Where(b => EsLongitudinal(b, dir) && !MarcaVigas.EsBaston(b)))
        {
            Curve mayor = Mayor(b);
            RebarShapeDrivenAccessor acceso = b.GetShapeDrivenAccessor();
            for (int i = 0; i < b.Quantity; i++)
            {
                Transform movida = acceso.GetBarPositionTransform(i);
                XYZ a = movida.OfPoint(mayor.GetEndPoint(0)), e = movida.OfPoint(mayor.GetEndPoint(1));
                double pa = a.DotProduct(dir), pe = e.DotProduct(dir);
                if (aLoLargo >= Math.Min(pa, pe) && aLoLargo <= Math.Max(pa, pe))
                    cortadas.Add((b, a + dir * (aLoLargo - pa)));
            }
        }
        void Etiquetar(List<(Rebar Barra, XYZ Punto)> grupo, double dz)
        {
            List<(Rebar Barra, XYZ Punto)> orden = grupo.OrderBy(c => c.Punto.DotProduct(derecha)).ToList();
            for (int i = 0; i < orden.Count; i++)
            {
                XYZ p = corte + XYZ.BasisZ * dz + derecha * ((i - (orden.Count - 1) / 2.0) * 0.28 * M - 0.08 * M);
                (Rebar barra, XYZ punto) = orden[i];
                TextNote nota = TextNote.Create(doc, vista.Id, p, "1 " + NombreBarra(doc, barra), tipos.Nota(HorizontalTextAlignment.Left));
                nota.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L).End = punto;
                Pintar(vista, nota.Id, barra, null);
            }
        }
        Etiquetar(cortadas.Where(c => c.Punto.Z > medio.Z).ToList(), alto / 2 + 0.38 * M);
        Etiquetar(cortadas.Where(c => c.Punto.Z <= medio.Z).ToList(), -alto / 2 - 0.2 * M);

        // El estribo aparte, solo con sus medidas. Con este tipo de detalle sale parado, como en la sección.
        Rebar? estribo = new FilteredElementCollector(doc, vista.Id).OfCategory(BuiltInCategory.OST_Rebar).OfType<Rebar>()
            .FirstOrDefault(b => viga.Vigas.Contains(b.GetHostId()) && !EsLongitudinal(b, dir));
        if (estribo is not null)
        {
            Element d = RebarBendingDetail.Create(doc, vista.Id, estribo.Id, 0, tipos.Barra, corte + derecha * 0.7 * M, 0);
            Pintar(vista, d.Id, estribo, 3);
        }
        return vista;
    }

    /// <summary>
    /// Dónde cortar la primera viga: lo más cerca de su centro sin caer en un traslapo, donde las barras salen dobles y la
    /// sección dejaría de parecerse a la del plano. Se evitan los extremos, que están sobre los apoyos.
    /// </summary>
    private static double PuntoDeCorte(Document doc, VigaContinua viga, List<Rebar> barras, XYZ dir)
    {
        Curve eje = ((LocationCurve)doc.GetElement(viga.Vigas[0]).Location).Curve;
        double centro = eje.Evaluate(0.5, true).DotProduct(dir);
        List<(double Desde, double Hasta, int Cantidad)> corridas = barras.Where(b => EsLongitudinal(b, dir) && !MarcaVigas.EsBaston(b)).Select(b =>
        {
            Curve mayor = Mayor(b);
            double a = mayor.GetEndPoint(0).DotProduct(dir), e = mayor.GetEndPoint(1).DotProduct(dir);
            return (Math.Min(a, e), Math.Max(a, e), b.Quantity);
        }).ToList();
        return Enumerable.Range(10, 81).Select(i => eje.Evaluate(i / 100.0, true).DotProduct(dir))
            .OrderBy(p => corridas.Where(c => p >= c.Desde && p <= c.Hasta).Sum(c => c.Cantidad))
            .ThenBy(p => Math.Abs(p - centro))
            .First();
    }

    private static ViewSection Preparar(Document doc, ViewFamilyType tipo, BoundingBoxXYZ caja, string prefijo, int escala)
    {
        ViewSection vista = ViewSection.CreateSection(doc, tipo.Id, caja);
        vista.Name = $"CQEING temporal {prefijo} {Guid.NewGuid():N}";
        vista.ViewTemplateId = ElementId.InvalidElementId;
        vista.Scale = escala;
        vista.DetailLevel = ViewDetailLevel.Medium;
        vista.DisplayStyle = DisplayStyle.Wireframe;
        vista.CropBoxActive = true;
        vista.CropBoxVisible = false;
        doc.Regenerate();
        return vista;
    }

    /// <summary>
    /// Recorta alrededor de un punto del modelo. Se pasa a las coordenadas del recorte porque Revit, al crear la sección,
    /// le pone el origen en una esquina y no en el que se le pidió.
    /// </summary>
    private static void Recortar(View vista, XYZ punto, double izquierda, double derecha, double abajo, double arriba)
    {
        BoundingBoxXYZ recorte = vista.CropBox;
        XYZ p = recorte.Transform.Inverse.OfPoint(punto);
        recorte.Min = new XYZ(p.X - izquierda, p.Y - abajo, recorte.Min.Z);
        recorte.Max = new XYZ(p.X + derecha, p.Y + arriba, recorte.Max.Z);
        vista.CropBox = recorte;
    }

    /// <summary>Solo la viga y su acero (#217): sin columnas, rellenos ni dibujos que imiten otra cosa.</summary>
    private static void Limpiar(Document doc, View vista, VigaContinua viga, List<Rebar> barras)
    {
        var deja = new HashSet<ElementId>(barras.Select(b => b.Id).Concat(viga.Vigas));
        List<ElementId> ocultar = new FilteredElementCollector(doc, vista.Id).WhereElementIsNotElementType()
            .Where(e => e.Category is { CategoryType: CategoryType.Model } && !deja.Contains(e.Id) && e.CanBeHidden(vista))
            .Select(e => e.Id).ToList();
        if (ocultar.Count > 0)
            vista.HideElements(ocultar);
        vista.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Sections), true);
        vista.SetCategoryHidden(new ElementId(BuiltInCategory.OST_SectionBox), true);
    }

    private static void Colorear(Document doc, View vista, List<Rebar> barras)
    {
        foreach (Rebar b in barras)
        {
            b.SetUnobscuredInView(vista, true);
            Pintar(vista, b.Id, b, null);
        }
    }

    private static void Pintar(View vista, ElementId id, Rebar barra, int? grosor)
    {
        (byte r, byte g, byte a) = ColoresDiametro.Rgb(LectorDespiece.Numero(barra.Document.GetElement(barra.GetTypeId()).Name));
        var color = new Color(r, g, a);
        var o = new OverrideGraphicSettings();
        o.SetProjectionLineColor(color);
        o.SetCutLineColor(color);
        o.SetSurfaceForegroundPatternId(Solido(barra.Document));
        o.SetSurfaceForegroundPatternColor(color);
        o.SetCutForegroundPatternId(Solido(barra.Document));
        o.SetCutForegroundPatternColor(color);
        if (grosor is { } g2)
            o.SetProjectionLineWeight(g2);
        vista.SetElementOverrides(id, o);
    }

    private static ElementId Solido(Document doc) => new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
        .First(f => f.GetFillPattern().IsSolidFill).Id;

    private static string Etiqueta(Document doc, Rebar b) => LectorDespiece.Etiqueta(doc.GetElement(b.GetTypeId()).Name, b.Quantity, LectorDespiece.LargoBarra(b));

    private static string NombreBarra(Document doc, Rebar b)
    {
        string tipo = doc.GetElement(b.GetTypeId()).Name;
        int n = LectorDespiece.Numero(tipo);
        return n > 0 ? "#" + n : tipo;
    }

    private static XYZ Centro(Rebar b)
    {
        BoundingBoxXYZ c = b.get_BoundingBox(null);
        return (c.Min + c.Max) / 2;
    }

    private static Curve Mayor(Rebar b) => b.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0).OrderByDescending(c => c.Length).First();

    /// <summary>Longitudinal si su tramo más largo va a lo largo de la viga. No se mira el nombre de la forma, que depende del proyecto.</summary>
    private static bool EsLongitudinal(Rebar b, XYZ dir)
    {
        Curve mayor = Mayor(b);
        XYZ d = (mayor.GetEndPoint(1) - mayor.GetEndPoint(0)).Normalize();
        return Math.Abs(d.DotProduct(dir)) > 0.99;
    }

    /// <summary>La dirección para que los ejes se lean en orden, de izquierda a derecha (A-B-C, 1-2-3).</summary>
    private static XYZ SentidoDeLectura(Document doc, VigaContinua viga)
    {
        XYZ dir = viga.Direccion;
        List<(double Pos, string Nombre)> cruzan = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
            .Where(g => g.Curve is Line l && Math.Abs(l.Direction.DotProduct(dir)) < 0.01)
            .Select(g => (g.Curve.GetEndPoint(0).DotProduct(dir), g.Name)).OrderBy(c => c.Item1).ToList();
        if (cruzan.Count >= 2 && string.CompareOrdinal(cruzan[0].Nombre, cruzan[^1].Nombre) > 0)
            return dir.Negate();
        return dir;
    }
}

/// <summary>Los tipos propios del despiece. Nunca se editan los del proyecto: se duplican (#217).</summary>
internal sealed class TiposPropios
{
    private readonly ElementId notaId;

    private TiposPropios(RebarBendingDetailType barra, DimensionType? cota, ElementId notaId)
    {
        Barra = barra;
        Cota = cota;
        this.notaId = notaId;
    }

    public RebarBendingDetailType Barra { get; }

    public DimensionType? Cota { get; }

    public TextNoteOptions Nota(HorizontalTextAlignment alineacion) => new(notaId) { HorizontalAlignment = alineacion };

    /// <summary>
    /// <paramref name="definitivos"/>: para vistas que se quedan en el proyecto, con nombre fijo y sin repetirlos si ya
    /// están. Si no, con nombre único, porque la vista previa los deshace.
    /// </summary>
    public static TiposPropios Crear(Document doc, bool definitivos)
    {
        string sufijo = Guid.NewGuid().ToString("N");
        string Nombre(string fijo) => definitivos ? fijo : "CQEING temporal " + sufijo;
        List<RebarBendingDetailType> todos = new FilteredElementCollector(doc).OfClass(typeof(RebarBendingDetailType)).Cast<RebarBendingDetailType>().ToList();
        // El que no trae etiqueta: la del proyecto dice "#5 CQEING - 34", y borrarla después borra también el detalle.
        RebarBendingDetailType original = todos.FirstOrDefault(t => !TieneEtiqueta(doc, t)) ?? todos.First();
        string nombreBarra = Nombre("CQ Despiece sin etiqueta");
        var barra = todos.FirstOrDefault(t => t.Name == nombreBarra) ?? (RebarBendingDetailType)original.Duplicate(nombreBarra);
        DimensionType? metros = null;
        foreach (Parameter p in barra.Parameters)
        {
            if (p.IsReadOnly || p.StorageType != StorageType.ElementId)
                continue;
            if (doc.GetElement(p.AsElementId()) is DimensionType { StyleType: DimensionStyleType.Linear } lineal)
            {
                Registro.Paso("tipos: cota en metros " + p.Definition.Name);
                metros ??= EnMetros(doc, lineal, Nombre("CQ Despiece - metros"));
                p.Set(metros.Id);
            }
        }
        Registro.Paso("tipos: nota de texto");
        List<TextNoteType> notas = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().ToList();
        string nombreNota = Nombre("CQ Despiece etiqueta");
        TextNoteType? nota = notas.FirstOrDefault(n => n.Name == nombreNota);
        if (nota is null)
        {
            nota = (TextNoteType)notas[0].Duplicate(nombreNota);
            nota.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(0.0025 / 0.3048);
            nota.get_Parameter(BuiltInParameter.TEXT_BACKGROUND).Set(1);
        }
        return new TiposPropios(barra, metros, nota.Id);
    }

    private static bool TieneEtiqueta(Document doc, RebarBendingDetailType tipo) => tipo.Parameters.Cast<Parameter>()
        .Any(p => p.StorageType == StorageType.ElementId && doc.GetElement(p.AsElementId())?.Category?.Id.Value == (long)BuiltInCategory.OST_RebarTags);

    private static DimensionType EnMetros(Document doc, DimensionType original, string nombre)
    {
        if (original.Name == nombre)
            return original;
        if (new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().FirstOrDefault(d => d.Name == nombre) is { } existente)
            return existente;
        var copia = (DimensionType)original.Duplicate(nombre);
        copia.SetUnitsFormatOptions(new FormatOptions(UnitTypeId.Meters) { UseDefault = false, Accuracy = 0.01, SuppressTrailingZeros = false });
        return copia;
    }
}
