using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CQEING.Despiece.Calculo;
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

    /// <summary>La vista larga y la sección a 1:15 (Juan, 30-oct).</summary>
    private const int EscalaLongitudinal = 15;
    private const int EscalaSeccion = 15;

    // Las separaciones de la vista larga se midieron a 1:50, en pies del modelo; el texto ocupa lo que dice la escala,
    // así que se reducen en proporción para que la vista no quede llena de aire.
    private const double Proporcion = EscalaLongitudinal / 50.0;
    private const double SeparacionViga = 0.14 * M;
    // A 1:15 la caja de la nota baja 0.09 m desde su punto (medido en V 3, 30-oct); el resto es aire sobre la barra.
    private const double AltoTexto = 0.11 * M;
    private const double EspacioCotas = 0.15 * M;
    private const double MinimoGancho = 0.05 * M;
    private const double MinimoTraslapo = 0.01 * M;
    private const double MedioAnchoEtiqueta = 0.65 * M * Proporcion;
    private const double HuecoFilas = 0.15 * M * Proporcion;
    private const double HuecoEnFila = 0.20 * M * Proporcion;
    private const double DesfaseTraslapo = 0.08 * M;
    private const double MismaZona = 0.10 * M;
    // La caja de la nota mide 0.12 m de alto a 1:15 (medido en V 3, 30-oct).
    private const double FilaEstribos = 0.13 * M;
    private const double BajaNota = 0.09 * M;
    private const double SubeNota = 0.03 * M;
    private const double AireNota = 0.01 * M;
    private const double AnchoNotaGirada = 0.10 * M;

    // Bajo la sección, de arriba abajo: las etiquetas (hasta 0.29 m), la cota del ancho y el recubrimiento. Así las flechas
    // no cruzan las cotas. A la izquierda, la cota del alto y su recubrimiento.
    private const double CotaAnchoSeccion = 0.42 * M;
    private const double TextoAnchoSeccion = 0.46 * M;
    private const double CotaAltoSeccion = 0.15 * M;
    private const double TextoAltoSeccion = 0.30 * M;

    // La vista es solo de la viga (Juan, 30-oct): sin columnas, ni ocultas con otro dibujo. El recorte va justo a su alto;
    // las barras sacadas se ven por fuera de él. Donde había una columna, Revit deja la viga cortada: es su geometría real.
    private const double HolguraRecorte = 0.005 * M;
    private const double ColaEje = 0.3 * M * Proporcion;
    private const double CabezaEje = 0.8 * M * Proporcion;
    private const double CotaLuces = 0.35 * M * Proporcion;
    private const double CotaTotal = 0.75 * M * Proporcion;
    // Debajo de las cotas de ejes: la cadena de columnas y luces libres y, al final, el largo de la viga.
    private const double CotaColumnas = 1.15 * M * Proporcion;
    private const double CotaViga = 1.55 * M * Proporcion;
    private const double AlturaPlano = 0.03 * M;

    /// <summary>Un bastón va más adentro que la capa exterior por más de esto.</summary>
    private const double ToleranciaBaston = 0.005 * M;

    /// <summary>Aire entre un estribo con su texto y el siguiente que se apila debajo.</summary>
    private const double HuecoEstribos = 0.08 * M;

    /// <summary>
    /// Textos en negro en vez del color de su diámetro (Juan, 31-oct). Es un dato de esta pasada, no del modelo: lo lee
    /// <see cref="Pintar"/> para no repasarlo por cada una de las notas. Solo vale dentro de <see cref="Generar"/> y <see cref="Crear"/>.
    /// </summary>
    [ThreadStatic]
    private static bool textosNegros;

    public static PreviaGenerada Generar(Document doc, VigaContinua viga, string carpeta, bool negros)
    {
        Registro.Paso("previa: empieza " + viga.Elemento.Id);
        using var t = new Transaction(doc, "Vista previa de despiece");
        t.Start();
        textosNegros = negros;
        try
        {
            (View longitudinal, View seccion) = Construir(doc, viga, TiposPropios.Crear(doc, false));
            Registro.Paso("previa: exportando la longitudinal");
            string a = ExportadorImagen.Exportar(doc, longitudinal, carpeta, ExportadorImagen.AnchoLarga);
            Registro.Paso("previa: exportando la sección");
            string b = ExportadorImagen.Exportar(doc, seccion, carpeta);
            return new PreviaGenerada(a, b);
        }
        finally
        {
            textosNegros = false;
            Registro.Paso("previa: deshaciendo");
            t.RollBack();
            Registro.Paso("previa: deshecho");
        }
    }

    /// <summary>
    /// Deja las dos vistas en el navegador de Revit (#222), iguales a la vista previa. No coloca nada en las hojas. Si ya
    /// existe alguna, no crea nada: reemplazarla podría borrar lo que se haya cambiado a mano.
    /// </summary>
    public static VistasCreadas Crear(Document doc, VigaContinua viga, string nombre, bool negros)
    {
        string nombreSeccion = "Sección " + nombre;
        bool yaExisten = new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Any(v => v.Name == nombre || v.Name == nombreSeccion);
        if (yaExisten)
            return new VistasCreadas(nombre, nombreSeccion, true);

        using var t = new Transaction(doc, "Crear vistas de despiece");
        t.Start();
        textosNegros = negros;
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
            textosNegros = false;
            if (t.HasStarted() && !t.HasEnded())
                t.RollBack();
        }
    }

    /// <summary>Una de las vistas que dejó <see cref="Crear"/> para este nombre: la larga o la sección. Null si no existe.</summary>
    public static View? VistaCreada(Document doc, string nombre, bool seccion = false)
    {
        string buscado = seccion ? "Sección " + nombre : nombre;
        return new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Cast<View>().FirstOrDefault(v => !v.IsTemplate && v.Name == buscado);
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
    private sealed record Sacada(Rebar Barra, Element Detalle, XYZ Posicion, int Cantidad, double Izquierda, double Derecha, double Abajo, double Arriba)
    {
        public double Alto => Arriba - Abajo;

        private double GanchoArriba => Barra.get_BoundingBox(null).Max.Z - Mayor(Barra).GetEndPoint(0).Z;

        private double GanchoAbajo => Mayor(Barra).GetEndPoint(0).Z - Barra.get_BoundingBox(null).Min.Z;

        /// <summary>
        /// Del borde inferior del detalle a su tramo recto. Revit pone la medida del tramo al lado contrario del gancho: con
        /// el gancho arriba o sin gancho va debajo; con el gancho abajo, encima (visto en V 3).
        /// </summary>
        public double Linea => GanchoAbajo > MinimoGancho ? GanchoAbajo : GanchoArriba > MinimoGancho ? Alto - GanchoArriba : Alto;

        /// <summary>Lo que ocupa la medida del tramo por encima de él: solo si el gancho va abajo.</summary>
        public double CotaEncima => GanchoAbajo > MinimoGancho ? Alto - GanchoAbajo : 0;
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
        var creadas = new List<(Rebar Barra, Element Detalle, XYZ Posicion, int Cantidad)>();
        // Una misma pieza puede ser varios conjuntos (S1: 3 #5 y dos sueltas #4). Las de igual diámetro y largo se dibujan
        // una vez, con su cantidad total: para figurar el acero son la misma barra. Las hechas a mano no se juntan.
        foreach (IGrouping<string, Rebar> iguales in longitudinales.GroupBy(b => MarcaVigas.Pieza(b) is { } pieza
            ? $"{pieza}|{b.GetTypeId()}|{Math.Round(LectorDespiece.LargoBarra(b) * 304.8)}"
            : b.Id.ToString()))
        {
            Rebar b = iguales.First();
            XYZ c = Centro(b);
            XYZ p = new XYZ(c.X, c.Y, max.Z + 11 * M) + haciaElQueMira * (enPlano - c).DotProduct(haciaElQueMira);
            Registro.Paso($"longitudinal: barra {b.Id}");
            Element d = RebarBendingDetail.Create(doc, vista.Id, b.Id, 0, tipos.BarraSinMedidas, p, 0);
            Pintar(vista, d.Id, b, 2);
            creadas.Add((b, d, p, iguales.Sum(x => x.Quantity)));
        }
        doc.Regenerate();
        List<Sacada> sacadas = creadas.Select(c =>
        {
            BoundingBoxXYZ m = c.Detalle.get_BoundingBox(vista);
            double a = m.Min.DotProduct(derecha), b = m.Max.DotProduct(derecha);
            return new Sacada(c.Barra, c.Detalle, c.Posicion, c.Cantidad, Math.Min(a, b), Math.Max(a, b), m.Min.Z - c.Posicion.Z, m.Max.Z - c.Posicion.Z);
        }).ToList();

        double mitad = (min.Z + max.Z) / 2;
        // Arriba o abajo según la marca del add-in de vigas: S e I son las líneas de arriba y de abajo, BS y BI sus bastones.
        // Sin marca (modelada a mano), según la altura de su tramo recto, no de la caja de toda la barra: un gancho hacia
        // el centro de la viga corre el centro de la caja y mandaba la barra de arriba abajo (visto en V 3 y V A, 30-oct).
        bool EstaArriba(Sacada s) => MarcaVigas.Serie(s.Barra) switch
        {
            "S" or "BS" => true,
            "I" or "BI" => false,
            _ => Mayor(s.Barra).GetEndPoint(0).Z > mitad,
        };
        // Con marca del add-in de vigas, los bastones son BS y BI; sin marca, una barra recta que va más adentro que la capa
        // exterior de su lado.
        List<double> alturas = sacadas.Select(s => Mayor(s.Barra).GetEndPoint(0).Z).ToList();
        bool EsBaston(Sacada s) => MarcaVigas.EsBaston(s.Barra) || (MarcaVigas.Serie(s.Barra) is null && Curvas(s.Barra, false).Count == 1
            && ClasificadorBastones.EsBaston(Mayor(s.Barra).GetEndPoint(0).Z, alturas, ToleranciaBaston));
        List<Sacada> deArriba = sacadas.Where(EstaArriba).ToList(), deAbajo = sacadas.Except(deArriba).ToList();
        // Los bastones en medio, entre la viga y las corridas (Juan, 31-oct); después una fila por diámetro, la más gruesa primero.
        double Lineas(List<Sacada> lado, string serie, double desde, bool haciaArriba)
        {
            double z = Acomodar(doc, vista, lado.Where(EsBaston).ToList(), desde, haciaArriba, tipos);
            foreach (IGrouping<int, Sacada> diametro in lado.Where(s => MarcaVigas.Serie(s.Barra) == serie)
                .GroupBy(s => LectorDespiece.Numero(doc.GetElement(s.Barra.GetTypeId()).Name)).OrderByDescending(g => g.Key))
                z = AcomodarLinea(doc, vista, diametro.ToList(), derecha, z, haciaArriba, tipos);
            return Acomodar(doc, vista, lado.Where(s => MarcaVigas.Serie(s.Barra) != serie && !EsBaston(s)).ToList(), z, haciaArriba, tipos);
        }
        int filasEstribos = EtiquetarEstribos(doc, vista, barras, derecha, enPlano, max.Z, tipos);
        double tope = Lineas(deArriba, "S", max.Z + SeparacionViga + FilaEstribos * Math.Max(filasEstribos - 1, 0), true);
        double fondo = Lineas(deAbajo, "I", min.Z - SeparacionViga, false);

        Recortar(vista, centro, largo / 2 + 1.0 * M, largo / 2 + 1.0 * M, alto / 2 + HolguraRecorte, alto / 2 + HolguraRecorte);
        vista.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Levels), true);
        doc.Regenerate();
        AjustarEjes(doc, vista, Math.Max(tope, max.Z) + CabezaEje, Math.Min(fondo, min.Z) - CotaViga - ColaEje);
        Cotar(doc, vista, centro, derecha, largo, min, max, Math.Min(fondo, min.Z), tipos);
        CotarColumnas(doc, vista, centro, derecha, min, max, Math.Min(fondo, min.Z), tipos);
        vista.DisplayStyle = DisplayStyle.FlatColors;
        return vista;
    }

    /// <summary>
    /// La cadena columna, luz libre, columna… y, más abajo, el largo completo de la viga (Juan, 31-oct). Las medidas se
    /// toman contra planos de referencia puestos en las caras de las columnas, solo en esta vista. Si Revit no acepta las
    /// cotas, se deshace lo hecho y la vista queda sin ellas: no vale tumbar la vista previa por esto.
    /// </summary>
    private static void CotarColumnas(Document doc, View vista, XYZ centro, XYZ derecha, XYZ min, XYZ max, double fondo, TiposPropios tipos)
    {
        double a = Math.Min(min.DotProduct(derecha), max.DotProduct(derecha)), b = Math.Max(min.DotProduct(derecha), max.DotProduct(derecha));
        double holgura = 0.05 * M;
        var caja = new Outline(new XYZ(min.X - holgura, min.Y - holgura, min.Z - holgura), new XYZ(max.X + holgura, max.Y + holgura, max.Z + holgura));
        List<(double Desde, double Hasta)> columnas = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns)
            .WhereElementIsNotElementType().WherePasses(new BoundingBoxIntersectsFilter(caja)).Select(c => c.get_BoundingBox(null)).Where(c => c is not null)
            .Select(c => (Math.Min(c.Min.DotProduct(derecha), c.Max.DotProduct(derecha)), Math.Max(c.Min.DotProduct(derecha), c.Max.DotProduct(derecha)))).ToList();
        IReadOnlyList<TramoViga> tramos = TramosViga.Calcular(a, b, columnas);
        Registro.Paso($"cotas: {columnas.Count} columnas, {tramos.Count} tramos");

        XYZ En(double s, double z) => new XYZ(centro.X, centro.Y, z) + derecha * (s - centro.DotProduct(derecha));
        using var prueba = new SubTransaction(doc);
        prueba.Start();
        try
        {
            List<double> marcas = [tramos[0].Desde, .. tramos.Select(t => t.Hasta)];
            // Entran apenas en el recorte, que va justo al alto de la viga: un plano fuera de él deja su cota sin mostrar. En
            // blanco, porque ocultar su categoría ocultaría también las cotas (medido el 31-oct).
            var blanco = new OverrideGraphicSettings();
            blanco.SetProjectionLineColor(new Color(255, 255, 255));
            List<Reference> planos = marcas.Select(s =>
            {
                ReferencePlane plano = doc.Create.NewReferencePlane(En(s, min.Z + AlturaPlano), En(s, fondo - CotaViga - AlturaPlano), vista.ViewDirection, vista);
                vista.SetElementOverrides(plano.Id, blanco);
                return plano.GetReference();
            }).ToList();
            doc.Regenerate();

            void Cota(IEnumerable<Reference> referencias, double z)
            {
                var lista = new ReferenceArray();
                foreach (Reference r in referencias)
                    lista.Append(r);
                Dimension cota = doc.Create.NewDimension(vista, Line.CreateBound(En(marcas[0], z), En(marcas[^1], z)), lista);
                if (tipos.CotaCorta is { } tipo)
                    cota.DimensionType = tipo;
            }
            Cota(planos, fondo - CotaColumnas);
            Cota([planos[0], planos[^1]], fondo - CotaViga);
            prueba.Commit();
        }
        catch (Exception e)
        {
            Registro.Paso("cotas de columnas: no se pudieron crear: " + e.Message);
            prueba.RollBack();
        }
    }

    /// <summary>
    /// Una etiqueta sobre la viga por cada conjunto de estribos, centrada en su zona (#224). Si no cabe junto a la anterior
    /// sube a otra fila, para que no se encimen. Devuelve cuántas filas usó. La grapa de una zona trae la misma cantidad y
    /// separación que su estribo y va 2 cm al lado (viga 388501): con la misma etiqueta en el mismo sitio, se escribe una vez.
    /// </summary>
    private static int EtiquetarEstribos(Document doc, View vista, List<Rebar> barras, XYZ derecha, XYZ enPlano, double techo, TiposPropios tipos)
    {
        var finDeFila = new List<double>();
        var escritas = new List<(string Texto, double Centro)>();
        foreach ((Rebar b, double centro) in barras.Where(b => !EsLongitudinal(b, derecha))
            .Select(b => (b, CentroDelConjunto(b).DotProduct(derecha))).OrderBy(z => z.Item2))
        {
            string texto = LectorDespiece.EtiquetaEstribos(doc.GetElement(b.GetTypeId()).Name, b.Quantity, LectorDespiece.Separacion(b));
            if (escritas.Any(e => e.Texto == texto && Math.Abs(e.Centro - centro) < MismaZona))
                continue;
            escritas.Add((texto, centro));
            int fila = finDeFila.FindIndex(fin => centro - MedioAnchoEtiqueta > fin + HuecoEnFila);
            if (fila < 0)
            {
                fila = finDeFila.Count;
                finDeFila.Add(0);
            }
            finDeFila[fila] = centro + MedioAnchoEtiqueta;
            XYZ punto = enPlano + derecha * (centro - enPlano.DotProduct(derecha));
            TextNote nota = TextNote.Create(doc, vista.Id, new XYZ(punto.X, punto.Y, techo + AltoTexto + FilaEstribos * fila), texto, tipos.Nota(HorizontalTextAlignment.Center));
            Pintar(vista, nota.Id, b, null);
        }
        return finDeFila.Count;
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
        // El reparto vive en AcomodadorFilas, con sus pruebas de que nada se encima. Aquí solo importa a qué fila va cada una.
        IEnumerable<PiezaFila> piezas = sacadas.Select((s, i) =>
        {
            double medio = (s.Izquierda + s.Derecha) / 2;
            return new PiezaFila(i, Math.Min(s.Izquierda, medio - MedioAnchoEtiqueta), Math.Max(s.Derecha, medio + MedioAnchoEtiqueta), 0);
        });
        List<List<(Sacada, double)>> filas = AcomodadorFilas.Acomodar(piezas, 0, true, HuecoEnFila, 0).Piezas.GroupBy(u => u.Fila).OrderBy(g => g.Key)
            .Select(g => g.Select(u => (sacadas[u.Pieza.Indice], 0.0)).ToList()).ToList();
        return ColocarFilas(doc, vista, filas, desde, haciaArriba, tipos);
    }

    /// <summary>
    /// Todas las piezas de una misma línea de acero (S1, S2… arriba; I1, I2… abajo) en una sola fila, como en el plano de
    /// Santiago: las que se traslapan quedan en la misma fila y una se corre <see cref="DesfaseTraslapo"/> para que se vea
    /// el traslapo (#223). Los bastones van entre esta fila y la viga.
    /// </summary>
    private static double AcomodarLinea(Document doc, View vista, List<Sacada> sacadas, XYZ derecha, double desde, bool haciaArriba, TiposPropios tipos)
    {
        if (sacadas.Count == 0)
            return desde;
        var fila = new List<(Sacada, double)>();
        var traslapos = new List<Traslapo>();
        (double Desde, double Hasta)? anterior = null;
        double hasta = double.MinValue;
        int escalon = 0;
        foreach (Sacada s in sacadas.OrderBy(s => s.Izquierda))
        {
            escalon = s.Izquierda < hasta ? escalon + 1 : 0;
            hasta = Math.Max(hasta, s.Derecha);
            fila.Add((s, escalon * DesfaseTraslapo));

            // El traslapo sale de los tramos rectos reales de las dos piezas, no de lo que ocupan sus detalles.
            Curve mayor = Mayor(s.Barra);
            double a = mayor.GetEndPoint(0).DotProduct(derecha), e = mayor.GetEndPoint(1).DotProduct(derecha);
            (double Desde, double Hasta) tramo = (Math.Min(a, e), Math.Max(a, e));
            if (anterior is { } previo && Math.Min(previo.Hasta, tramo.Hasta) - Math.Max(previo.Desde, tramo.Desde) > MinimoTraslapo)
            {
                double inicio = Math.Max(previo.Desde, tramo.Desde), fin = Math.Min(previo.Hasta, tramo.Hasta);
                traslapos.Add(new Traslapo(s, (inicio + fin) / 2, fin - inicio));
            }
            anterior = tramo;
        }
        return ColocarFilas(doc, vista, [fila], desde, haciaArriba, tipos, derecha, traslapos);
    }

    /// <summary>Un traslapo de una fila: su centro a lo largo de la vista y su largo, en pies.</summary>
    private sealed record Traslapo(Sacada Pieza, double Centro, double Largo);

    /// <summary>Coloca las filas de la viga hacia afuera. Cada pieza puede ir corrida un desfase; la etiqueta va a la altura de la más alta de su fila.</summary>
    private static double ColocarFilas(Document doc, View vista, List<List<(Sacada S, double Desfase)>> filas, double desde, bool haciaArriba, TiposPropios tipos, XYZ? derecha = null, List<Traslapo>? traslapos = null)
    {
        double z = desde;
        foreach (List<(Sacada S, double Desfase)> fila in filas)
        {
            // Todo se mide desde el tramo recto: la etiqueta va justo sobre él, no sobre la punta del gancho (Juan, 30-oct:
            // "quedan muy lejos"). Encima queda libre el espacio de las cotas de traslapo (#223).
            double bajoLinea = fila.Max(f => f.S.Linea);
            double etiqueta = fila.Max(f => f.S.CotaEncima + f.Desfase) + AltoTexto;
            // Lo más junto posible (Juan, 31-oct): solo se deja sitio de más si un traslapo puede subir sobre su etiqueta.
            double sobreLinea = Math.Max(fila.Max(f => f.S.Alto - f.S.Linea + f.Desfase), etiqueta) + (traslapos is { Count: > 0 } ? EspacioCotas : AireNota);
            double altoFila = bajoLinea + sobreLinea;
            double linea = (haciaArriba ? z : z - altoFila) + bajoLinea;
            foreach ((Sacada s, double desfase) in fila)
            {
                RebarBendingDetail.SetPosition(s.Detalle, new XYZ(s.Posicion.X, s.Posicion.Y, linea - s.Linea - s.Abajo + desfase));
                TextNote nota = TextNote.Create(doc, vista.Id, new XYZ(s.Posicion.X, s.Posicion.Y, linea + s.CotaEncima + desfase + AltoTexto), LectorDespiece.Etiqueta(doc.GetElement(s.Barra.GetTypeId()).Name, s.Cantidad, LectorDespiece.LargoBarra(s.Barra), Curvas(s.Barra, true).Sum(c => c.Length)), tipos.Nota(HorizontalTextAlignment.Center));
                Pintar(vista, nota.Id, s.Barra, null);
                MedirGanchos(doc, vista, s, linea + desfase, tipos);
            }
            // La medida de cada traslapo, sobre él (#223). Es texto: un detalle de plegado no da referencias para una cota
            // de Revit (probado el 30-oct). Si le cae encima a una etiqueta de la fila, sube al espacio libre.
            foreach (Traslapo t in derecha is null ? [] : traslapos ?? [])
            {
                bool choca = fila.Any(f => Math.Abs(f.S.Posicion.DotProduct(derecha!) - t.Centro) < MedioAnchoEtiqueta);
                XYZ punto = t.Pieza.Posicion + derecha! * (t.Centro - t.Pieza.Posicion.DotProduct(derecha!));
                double alto = linea + etiqueta + (choca ? AltoTexto : 0);
                TextNote nota = TextNote.Create(doc, vista.Id, new XYZ(punto.X, punto.Y, alto), (t.Largo * 0.3048).ToString("0.00", CultureInfo.InvariantCulture), tipos.Nota(HorizontalTextAlignment.Center));
                Pintar(vista, nota.Id, t.Pieza.Barra, null);
            }
            z = haciaArriba ? z + altoFila + HuecoFilas : z - altoFila - HuecoFilas;
        }
        return z;
    }

    /// <summary>
    /// La medida de cada gancho, girada y por fuera de su pata. Es texto porque Revit no deja mostrar la del gancho sin la
    /// del tramo recto, y esa ya va en la etiqueta (decisión 9 del plan del 31-oct).
    /// </summary>
    private static void MedirGanchos(Document doc, View vista, Sacada s, double linea, TiposPropios tipos)
    {
        IList<Curve> curvas = Curvas(s.Barra, false);
        if (curvas.Count < 3)
            return;
        Curve mayor = Mayor(s.Barra);
        XYZ derecha = vista.RightDirection;
        double medio = (mayor.GetEndPoint(0) + mayor.GetEndPoint(1)).DotProduct(derecha) / 2, alturaTramo = mayor.GetEndPoint(0).Z;
        double d = ((RebarBarType)doc.GetElement(s.Barra.GetTypeId())).BarNominalDiameter;

        void Medir(Curve pata, Curve doblez)
        {
            double radio = doblez is Arc arco ? arco.Radius : 0;
            double largo = pata.Length + radio + d / 2;
            XYZ centroPata = (pata.GetEndPoint(0) + pata.GetEndPoint(1)) / 2;
            double x = centroPata.DotProduct(derecha) > medio ? s.Derecha + AireNota : s.Izquierda - AnchoNotaGirada;
            double z = linea + (centroPata.Z > alturaTramo ? largo : -largo) / 2;
            TextNoteOptions girada = tipos.Nota(HorizontalTextAlignment.Center);
            girada.Rotation = Math.PI / 2;
            XYZ punto = new XYZ(s.Posicion.X, s.Posicion.Y, z) + derecha * (x - s.Posicion.DotProduct(derecha));
            TextNote nota = TextNote.Create(doc, vista.Id, punto, (largo * 0.3048).ToString("0.00", CultureInfo.InvariantCulture), girada);
            Pintar(vista, nota.Id, s.Barra, null);
        }

        if (s.Barra.GetHookTypeId(0) != ElementId.InvalidElementId)
            Medir(curvas[0], curvas[1]);
        if (s.Barra.GetHookTypeId(1) != ElementId.InvalidElementId)
            Medir(curvas[^1], curvas[^2]);
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
        Recortar(vista, corte, ancho / 2 + 0.55 * M, ancho / 2 + 1.05 * M, alto / 2 + 0.7 * M, alto / 2 + 0.7 * M);
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

        // La vista tiene fondo para alcanzar un estribo: se veían barras que terminan detrás del corte, sin etiqueta y como si
        // fueran del corte (viga 388501: 10 puntos #4 y 5 etiquetas). Solo se deja lo que el corte atraviesa.
        List<ElementId> detras = barras.Where(b => EsLongitudinal(b, dir)).Where(b =>
        {
            Curve mayor = Mayor(b);
            double pa = mayor.GetEndPoint(0).DotProduct(dir), pe = mayor.GetEndPoint(1).DotProduct(dir);
            return aLoLargo < Math.Min(pa, pe) || aLoLargo > Math.Max(pa, pe);
        }).Select(b => b.Id).Where(id => doc.GetElement(id).CanBeHidden(vista)).ToList();
        if (detras.Count > 0)
            vista.HideElements(detras);

        // Una etiqueta con flecha por cada barra que pasa por el corte. El bastón no se etiqueta (Santiago, 30-oct).
        var cortadas = new List<(Rebar Barra, XYZ Punto)>();
        foreach (Rebar b in barras.Where(b => EsLongitudinal(b, dir) && !MarcaVigas.EsBaston(b)))
        {
            for (int i = 0; i < b.Quantity; i++)
            {
                // Con la curva transformada: el add-in de vigas corre barras dentro del conjunto (MoveBarInSet), y la
                // posición del conjunto no lo sabe; la flecha caía 2 cm al lado de la barra (Sección V 3, 30-oct).
                Curve mayor = b.GetTransformedCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, i).OrderByDescending(c => c.Length).First();
                XYZ a = mayor.GetEndPoint(0), e = mayor.GetEndPoint(1);
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

        // El estribo aparte, solo con sus medidas. Con este tipo de detalle sale parado, como en la sección. El cerrado es
        // el de más curvas: la grapa de la misma zona también cruza el corte.
        Rebar? estribo = new FilteredElementCollector(doc, vista.Id).OfCategory(BuiltInCategory.OST_Rebar).OfType<Rebar>()
            .Where(b => viga.Vigas.Contains(b.GetHostId()) && !EsLongitudinal(b, dir))
            .OrderByDescending(b => Curvas(b, false).Count).FirstOrDefault();
        if (estribo is not null)
        {
            XYZ centroDetalle = corte + derecha * 0.7 * M;
            Element d = RebarBendingDetail.Create(doc, vista.Id, estribo.Id, 0, tipos.Barra, centroDetalle, 0);
            Pintar(vista, d.Id, estribo, 3);
            EtiquetarGancho(doc, vista, estribo, centroDetalle, medio.Z, tipos);
            double abajo = ResumirEstribos(doc, vista, barras.Where(b => !EsLongitudinal(b, dir)).ToList(), estribo, d, centroDetalle, tipos);
            Recortar(vista, corte, ancho / 2 + 0.55 * M, ancho / 2 + 1.05 * M, Math.Max(alto / 2 + 0.7 * M, medio.Z - abajo + 0.1 * M), alto / 2 + 0.7 * M);
        }
        Acotar(doc, vista, viga, estribo, corte, derecha, medio.Z, tipos);
        return vista;
    }

    /// <summary>
    /// Cada forma de estribo de la viga, también las menores (grapas), con su detalle y debajo su cantidad total, su largo y
    /// su gancho: <c>46 #3 L= 0.99 G= 0.08</c> (Juan, 31-oct). El principal ya está dibujado; los demás se apilan debajo.
    /// Devuelve hasta qué altura bajó.
    /// </summary>
    private static double ResumirEstribos(Document doc, View vista, List<Rebar> estribos, Rebar principal, Element detallePrincipal, XYZ centroDetalle, TiposPropios tipos)
    {
        string Clave(Rebar b) => $"{b.GetTypeId()}|{b.GetShapeId()}|{Math.Round(LectorDespiece.LargoBarra(b) * 304.8)}";
        List<IGrouping<string, Rebar>> formas = estribos.GroupBy(Clave).OrderByDescending(g => g.Key == Clave(principal) ? int.MaxValue : Curvas(g.First(), false).Count).ToList();
        double tope = double.NaN, abajo = centroDetalle.Z;
        // Revit solo dibuja el detalle de una barra que se vea en la vista (medido el 31-oct: lanza ArgumentException con viewId).
        var enLaVista = new HashSet<ElementId>(new FilteredElementCollector(doc, vista.Id).OfCategory(BuiltInCategory.OST_Rebar).ToElementIds());
        foreach (IGrouping<string, Rebar> forma in formas)
        {
            bool esPrincipal = forma.Key == Clave(principal);
            Rebar? visible = forma.FirstOrDefault(x => enLaVista.Contains(x.Id));
            Rebar b = esPrincipal ? principal : visible ?? forma.First();
            Element? detalle = esPrincipal ? detallePrincipal : null;
            if (!esPrincipal && visible is not null)
            {
                detalle = RebarBendingDetail.Create(doc, vista.Id, b.Id, 0, tipos.Barra, centroDetalle, 0);
                Pintar(vista, detalle.Id, b, 3);
            }
            double piso = double.IsNaN(tope) ? centroDetalle.Z : tope;
            if (detalle is not null)
            {
                doc.Regenerate();
                BoundingBoxXYZ m = detalle.get_BoundingBox(vista);
                double dz = double.IsNaN(tope) ? 0 : tope - m.Max.Z;
                if (dz != 0)
                    RebarBendingDetail.SetPosition(detalle, new XYZ(centroDetalle.X, centroDetalle.Y, centroDetalle.Z + dz));
                piso = m.Min.Z + dz;
            }
            else
            {
                // Ninguna de esta forma pasa por el corte: sin dibujo, pero con su cantidad y sus medidas.
                Registro.Paso($"estribos: sin detalle para {b.Id}, ninguna pasa por el corte");
            }
            string tipo = doc.GetElement(b.GetTypeId()).Name;
            TextNote nota = TextNote.Create(doc, vista.Id, new XYZ(centroDetalle.X, centroDetalle.Y, piso - AireNota),
                TextosDespiece.Estribo(tipo, forma.Sum(x => x.Quantity), LectorDespiece.LargoBarra(b) * 0.3048, Gancho(b) * 0.3048), tipos.Nota(HorizontalTextAlignment.Center));
            Pintar(vista, nota.Id, b, null);
            abajo = piso - AireNota - FilaEstribos;
            tope = abajo - HuecoEstribos;
        }
        return abajo;
    }

    /// <summary>El largo del gancho que da Revit para esta barra (el más largo de sus dos puntas), en pies; 0 si no tiene.</summary>
    private static double Gancho(Rebar b)
    {
        if (b.Document.GetElement(b.GetTypeId()) is not RebarBarType tipo)
            return 0;
        return new[] { 0, 1 }.Select(b.GetHookTypeId).Where(id => id != ElementId.InvalidElementId).Select(tipo.GetHookLength).DefaultIfEmpty(0).Max();
    }

    /// <summary>
    /// El largo de la pata del gancho dentro del estribo aparte, como <c>0.08</c> en la captura de Santiago (#225): el
    /// detalle de plegado no lo muestra con el gancho de 180°. Va donde el estribo tiene espacio, al otro lado de las patas.
    /// </summary>
    private static void EtiquetarGancho(Document doc, View vista, Rebar estribo, XYZ centroDetalle, double centroZ, TiposPropios tipos)
    {
        IList<Curve> curvas = Curvas(estribo, false);
        if (curvas.Count == Curvas(estribo, true).Count)
            return;
        double centroEstribo = (curvas.Min(c => c.GetEndPoint(0).Z) + curvas.Max(c => c.GetEndPoint(0).Z)) / 2;
        List<double> puntas = new[] { curvas[0], curvas[^1] }.SelectMany(c => new[] { c.GetEndPoint(0).Z, c.GetEndPoint(1).Z }).ToList();
        bool patasAbajo = puntas.Average() < centroEstribo;
        double z = centroZ + (patasAbajo ? puntas.Max() - centroEstribo + BajaNota + AireNota : puntas.Min() - centroEstribo - SubeNota - AireNota);
        string texto = (Gancho(estribo) * 0.3048).ToString("0.00", CultureInfo.InvariantCulture);
        TextNote nota = TextNote.Create(doc, vista.Id, new XYZ(centroDetalle.X, centroDetalle.Y, z), texto, tipos.Nota(HorizontalTextAlignment.Center));
        Pintar(vista, nota.Id, estribo, null);
    }

    /// <summary>
    /// Ancho y alto con cotas de Revit, ligadas a las caras de la viga. El recubrimiento va en texto, como <c>0.04 · 0.17 ·
    /// 0.04</c>: Revit ignora la referencia del estribo en una cota (probado el 30-oct, la cadena salió de un solo tramo).
    /// </summary>
    private static void Acotar(Document doc, View vista, VigaContinua viga, Rebar? estribo, XYZ corte, XYZ derecha, double centroZ, TiposPropios tipos)
    {
        // Las referencias dejan de valer al crear una cota (la segunda fallaba con InternalException, 30-oct): se leen cada vez.
        List<PlanarFace> Lados() => Caras(doc.GetElement(viga.Vigas[0]), vista).Where(c => Math.Abs(c.FaceNormal.DotProduct(derecha)) > 0.99).OrderBy(c => c.Origin.DotProduct(derecha)).ToList();
        List<PlanarFace> Tapas() => Caras(doc.GetElement(viga.Vigas[0]), vista).Where(c => Math.Abs(c.FaceNormal.Z) > 0.99).OrderBy(c => c.Origin.Z).ToList();
        List<PlanarFace> lados = Lados(), tapas = Tapas();
        if (lados.Count < 2 || tapas.Count < 2)
            return;
        PlanarFace izquierda = lados[0], dere = lados[^1], abajo = tapas[0], arriba = tapas[^1];
        // Sobre el plano del corte: el origen de una cara puede estar a metros de él, y ahí Revit no crea la cota (30-oct).
        XYZ enCorte = corte + derecha * (izquierda.Origin - corte).DotProduct(derecha);
        XYZ esquina = new(enCorte.X, enCorte.Y, abajo.Origin.Z);
        double ancho = (dere.Origin - izquierda.Origin).DotProduct(derecha), alto = arriba.Origin.Z - abajo.Origin.Z;
        double cara = izquierda.Origin.DotProduct(derecha), zAbajo = abajo.Origin.Z, zArriba = arriba.Origin.Z;

        void Cota(Reference a, Reference b, XYZ desde, XYZ hacia)
        {
            var referencias = new ReferenceArray();
            referencias.Append(a);
            referencias.Append(b);
            Dimension cota = doc.Create.NewDimension(vista, Line.CreateBound(desde, hacia), referencias);
            if (tipos.Cota is { } tipo)
                cota.DimensionType = tipo;
            doc.Regenerate();
        }
        XYZ bajo = esquina - XYZ.BasisZ * CotaAnchoSeccion;
        Cota(izquierda.Reference, dere.Reference, bajo, bajo + derecha * ancho);
        XYZ alLado = esquina - derecha * CotaAltoSeccion;
        tapas = Tapas();
        Cota(tapas[0].Reference, tapas[^1].Reference, alLado, alLado + XYZ.BasisZ * alto);

        if (estribo is null)
            return;
        // Por la cara exterior del estribo: su eje más medio diámetro, igual que la medida del detalle.
        double d = ((RebarBarType)doc.GetElement(estribo.GetTypeId())).BarNominalDiameter;
        // Por sus esquinas: el tramo donde arranca el gancho se pasa del lado contrario (6 mm en la viga 388501, y daba 0.18 · 0.03).
        IList<Curve> tramos = Curvas(estribo, true);
        List<Curve> esquinas = tramos.Where(c => c is Arc).ToList();
        List<XYZ> puntos = (esquinas.Count > 0 ? esquinas : tramos).SelectMany(c => new[] { c.GetEndPoint(0), c.GetEndPoint(1) }).ToList();
        double desdeEje = puntos.Min(p => p.DotProduct(derecha)) - d / 2, hastaEje = puntos.Max(p => p.DotProduct(derecha)) + d / 2;
        double bajoEje = puntos.Min(p => p.Z) - d / 2, altoEje = puntos.Max(p => p.Z) + d / 2;
        XYZ medioAncho = esquina + derecha * (ancho / 2);
        TextNote.Create(doc, vista.Id, medioAncho - XYZ.BasisZ * TextoAnchoSeccion,
            Cadena(desdeEje - cara, hastaEje - desdeEje, cara + ancho - hastaEje), tipos.Nota(HorizontalTextAlignment.Center));
        TextNoteOptions vertical = tipos.Nota(HorizontalTextAlignment.Center);
        vertical.Rotation = Math.PI / 2;
        TextNote.Create(doc, vista.Id, new XYZ(esquina.X, esquina.Y, centroZ) - derecha * TextoAltoSeccion,
            Cadena(bajoEje - zAbajo, altoEje - bajoEje, zArriba - altoEje), vertical);
    }

    private static string Cadena(params double[] pies) => string.Join(" · ", pies.Select(p => (p * 0.3048).ToString("0.00", CultureInfo.InvariantCulture)));

    /// <summary>Las caras planas de la viga con referencia, para acotarlas en esta vista.</summary>
    private static List<PlanarFace> Caras(Element viga, View vista)
    {
        var caras = new List<PlanarFace>();
        foreach (GeometryObject g in viga.get_Geometry(new Options { ComputeReferences = true, View = vista }))
        {
            IEnumerable<Solid> solidos = g is GeometryInstance i ? i.GetInstanceGeometry().OfType<Solid>() : g is Solid s ? [s] : [];
            foreach (Solid solido in solidos.Where(x => x.Volume > 0))
                caras.AddRange(solido.Faces.OfType<PlanarFace>());
        }
        return caras;
    }

    private static IList<Curve> Curvas(Rebar b, bool sinGanchos) => b.GetCenterlineCurves(false, sinGanchos, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);

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
        if (textosNegros && barra.Document.GetElement(id) is TextNote)
            return;
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

    /// <summary>El punto medio entre la primera y la última barra del conjunto.</summary>
    private static XYZ CentroDelConjunto(Rebar b)
    {
        XYZ punto = Mayor(b).GetEndPoint(0);
        RebarShapeDrivenAccessor acceso = b.GetShapeDrivenAccessor();
        return (acceso.GetBarPositionTransform(0).OfPoint(punto) + acceso.GetBarPositionTransform(b.Quantity - 1).OfPoint(punto)) / 2;
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

    private TiposPropios(RebarBendingDetailType barra, RebarBendingDetailType barraSinMedidas, DimensionType? cota, DimensionType? cotaCorta, ElementId notaId)
    {
        Barra = barra;
        BarraSinMedidas = barraSinMedidas;
        Cota = cota;
        CotaCorta = cotaCorta;
        this.notaId = notaId;
    }

    public RebarBendingDetailType Barra { get; }

    /// <summary>Para las barras longitudinales: su largo va en la etiqueta y no se repite en el dibujo.</summary>
    public RebarBendingDetailType BarraSinMedidas { get; }

    public DimensionType? Cota { get; }

    /// <summary>En metros y con las líneas de referencia cortas, para que no crucen las barras que hay entre la cota y la viga.</summary>
    public DimensionType? CotaCorta { get; }

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
        DimensionType? corta = null;
        if (metros is not null)
        {
            corta = EnMetros(doc, metros, Nombre("CQ Despiece - metros") + " corta");
            // 1 = línea de referencia fija, medida desde la línea de cota; 0 = hasta el elemento.
            corta.get_Parameter(BuiltInParameter.DIM_WITNS_LINE_CNTRL).Set(1);
            corta.get_Parameter(BuiltInParameter.DIM_WITNS_LINE_EXTENSION_BELOW).Set(0.0024 / 0.3048);
        }
        string nombreSinMedidas = Nombre("CQ Despiece sin etiqueta") + " sin medidas";
        var sinMedidas = todos.FirstOrDefault(t => t.Name == nombreSinMedidas) ?? (RebarBendingDetailType)barra.Duplicate(nombreSinMedidas);
        sinMedidas.SegmentLengthDimensionsEnabled = false;
        return new TiposPropios(barra, sinMedidas, metros, corta, nota.Id);
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
