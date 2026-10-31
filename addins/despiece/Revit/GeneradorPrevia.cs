using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CQEING.Despiece.Calculo;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Despiece.Revit;

/// <summary>Las dos imágenes de la vista previa.</summary>
internal sealed record PreviaGenerada(string Longitudinal, string Seccion);

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
    private const double SeparacionViga = 0.30 * M;
    private const double SeparacionTexto = 0.03 * M;
    private const double HuecoFilas = 0.12 * M;
    private const double HuecoEnFila = 0.20 * M;
    private const double SeparacionCotas = 0.32 * M;

    /// <summary>Un bastón va más adentro que la capa exterior por más de esto.</summary>
    private const double ToleranciaBaston = 0.005 * M;

    public static PreviaGenerada Generar(Document doc, VigaContinua viga, string carpeta, bool textosNegros)
    {
        Registro.Paso("previa: empieza " + viga.Elemento.Id);
        using var t = new Transaction(doc, "Vista previa de despiece");
        t.Start();
        try
        {
            return Crear(doc, viga, carpeta, textosNegros);
        }
        finally
        {
            Registro.Paso("previa: deshaciendo");
            t.RollBack();
            Registro.Paso("previa: deshecho");
        }
    }

    /// <summary>Crea y exporta las dos vistas. Quien llama abre la transacción y la deshace.</summary>
    private static PreviaGenerada Crear(Document doc, VigaContinua viga, string carpeta, bool textosNegros)
    {
        ViewFamilyType tipoSeccion = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .First(v => v.ViewFamily == ViewFamily.Section);
        List<Rebar> barras = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
            .Where(b => viga.Vigas.Contains(b.GetHostId())).ToList();
        Registro.Paso($"previa: {barras.Count} barras; creando tipos propios");
        var tipos = TiposPropios.Crear(doc);

        Registro.Paso("previa: vista longitudinal");
        View longitudinal = Longitudinal(doc, tipoSeccion, viga, barras, tipos, textosNegros);
        Registro.Paso("previa: sección");
        View seccion = Seccion(doc, tipoSeccion, viga, barras, tipos, textosNegros);
        doc.Regenerate();
        Registro.Paso("previa: exportando la longitudinal");
        string a = ExportadorImagen.Exportar(doc, longitudinal, carpeta);
        Registro.Paso("previa: exportando la sección");
        string b = ExportadorImagen.Exportar(doc, seccion, carpeta);
        return new PreviaGenerada(a, b);
    }

    private static (XYZ Min, XYZ Max) Caja(Document doc, VigaContinua viga)
    {
        List<BoundingBoxXYZ> cajas = viga.Vigas.Select(id => doc.GetElement(id).get_BoundingBox(null)).ToList();
        return (new XYZ(cajas.Min(c => c.Min.X), cajas.Min(c => c.Min.Y), cajas.Min(c => c.Min.Z)),
            new XYZ(cajas.Max(c => c.Max.X), cajas.Max(c => c.Max.Y), cajas.Max(c => c.Max.Z)));
    }

    /// <summary>Una barra sacada con su texto y lo que ocupa cada uno en la vista, medido en Revit.</summary>
    private sealed record Sacada(Rebar Barra, Element Detalle, TextNote Nota, XYZ Posicion, Medida MedidaDetalle, Medida MedidaNota)
    {
        public double Medio => (MedidaDetalle.Izquierda + MedidaDetalle.Derecha) / 2;
    }

    /// <summary>La caja de un elemento en la vista: a lo largo de <c>derecha</c> y en altura.</summary>
    private readonly record struct Medida(double Izquierda, double Derecha, double Abajo, double Arriba)
    {
        public double Ancho => Derecha - Izquierda;

        public double Alto => Arriba - Abajo;
    }

    private static Medida Medir(Element e, View vista, XYZ derecha)
    {
        BoundingBoxXYZ m = e.get_BoundingBox(vista);
        double a = m.Min.DotProduct(derecha), b = m.Max.DotProduct(derecha);
        return new Medida(Math.Min(a, b), Math.Max(a, b), m.Min.Z, m.Max.Z);
    }

    private static View Longitudinal(Document doc, ViewFamilyType tipo, VigaContinua viga, List<Rebar> barras, TiposPropios tipos, bool textosNegros)
    {
        (XYZ min, XYZ max) = Caja(doc, viga);
        XYZ derecha = SentidoDeLectura(doc, viga);
        XYZ haciaElQueMira = derecha.CrossProduct(XYZ.BasisZ);
        XYZ centro = (min + max) / 2;
        double largo = Math.Abs((max - min).DotProduct(derecha));
        double ancho = Math.Abs((max - min).DotProduct(haciaElQueMira));
        double alto = max.Z - min.Z;
        XYZ enPlano = centro + haciaElQueMira * (ancho / 2 + 0.3 * M);

        Transform t = Transform.Identity;
        t.BasisX = derecha;
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
        var creadas = new List<(Rebar Barra, Element Detalle, TextNote Nota, XYZ Posicion)>();
        foreach (Rebar b in longitudinales)
        {
            XYZ c = Centro(b);
            XYZ p = new XYZ(c.X, c.Y, max.Z + 11 * M) + haciaElQueMira * (enPlano - c).DotProduct(haciaElQueMira);
            Registro.Paso($"longitudinal: barra {b.Id}");
            Element d = RebarBendingDetail.Create(doc, vista.Id, b.Id, 0, tipos.Barra, p, 0);
            Pintar(vista, d.Id, b, 2);
            TextNote nota = TextNote.Create(doc, vista.Id, p, Etiqueta(doc, b), tipos.Nota(HorizontalTextAlignment.Center));
            if (!textosNegros)
                Pintar(vista, nota.Id, b, null);
            creadas.Add((b, d, nota, p));
        }
        doc.Regenerate();
        List<Sacada> sacadas = creadas.Select(c => new Sacada(c.Barra, c.Detalle, c.Nota, c.Posicion, Medir(c.Detalle, vista, derecha), Medir(c.Nota, vista, derecha))).ToList();

        // Los bastones, dentro de la viga y en el medio de su alto; el resto, arriba y abajo. Las filas de afuera se alejan si los bastones no caben.
        HashSet<ElementId> bastones = Bastones(longitudinales);
        double mitad = (min.Z + max.Z) / 2;
        Acomodo medio = AcomodadorFilas.Centrar(Piezas(sacadas, s => bastones.Contains(s.Barra.Id)), mitad, HuecoEnFila, HuecoFilas);
        Colocar(sacadas, medio, derecha);

        double techo = Math.Max(max.Z, medio.Hasta), piso = Math.Min(min.Z, medio.Desde);
        double fondoCotas = Cotas(doc, vista, viga, tipos, derecha, enPlano, min, max, piso);
        Acomodo arriba = AcomodadorFilas.Acomodar(Piezas(sacadas, s => !bastones.Contains(s.Barra.Id) && Altura(s.Barra) > mitad), techo + SeparacionViga, true, HuecoEnFila, HuecoFilas);
        Acomodo abajo = AcomodadorFilas.Acomodar(Piezas(sacadas, s => !bastones.Contains(s.Barra.Id) && Altura(s.Barra) <= mitad), fondoCotas - SeparacionViga, false, HuecoEnFila, HuecoFilas);
        Colocar(sacadas, arriba, derecha);
        Colocar(sacadas, abajo, derecha);

        // El recorte, justo a lo que quedó: así la imagen no se llena de vacío y todo se ve más grande.
        Recortar(vista, centro, largo / 2 + 1.0 * M, largo / 2 + 1.0 * M, centro.Z - Math.Min(abajo.Hasta, fondoCotas) + 0.3 * M, Math.Max(arriba.Hasta, techo) - centro.Z + 0.3 * M);
        vista.DisplayStyle = DisplayStyle.FlatColors;
        return vista;

        List<PiezaFila> Piezas(List<Sacada> todas, Func<Sacada, bool> cuales) => todas.Select((s, i) => (s, i)).Where(x => cuales(x.s))
            .Select(x => PiezaFila.ConTexto(x.i, x.s.MedidaDetalle.Izquierda, x.s.MedidaDetalle.Derecha, x.s.MedidaDetalle.Alto, x.s.MedidaNota.Ancho, x.s.MedidaNota.Alto, SeparacionTexto)).ToList();
    }

    /// <summary>Mueve cada barra y su texto a donde dijo el acomodo. El texto queda centrado sobre su barra.</summary>
    private static void Colocar(List<Sacada> sacadas, Acomodo acomodo, XYZ derecha)
    {
        foreach (PiezaUbicada u in acomodo.Piezas)
        {
            Sacada s = sacadas[u.Pieza.Indice];
            RebarBendingDetail.SetPosition(s.Detalle, new XYZ(s.Posicion.X, s.Posicion.Y, s.Posicion.Z + u.Piso - s.MedidaDetalle.Abajo));
            XYZ arriba = new(s.Posicion.X, s.Posicion.Y, u.TopeTexto);
            s.Nota.Coord = arriba + derecha * (s.Medio - arriba.DotProduct(derecha));
        }
    }

    /// <summary>Las barras rectas que van más adentro que la capa exterior.</summary>
    private static HashSet<ElementId> Bastones(List<Rebar> longitudinales)
    {
        List<double> alturas = longitudinales.Select(Altura).ToList();
        return longitudinales.Where(b => Curvas(b).Count == 1 && ClasificadorBastones.EsBaston(Altura(b), alturas, ToleranciaBaston)).Select(b => b.Id).ToHashSet();
    }

    /// <summary>La altura del tramo largo: la de la caja engaña en las barras con gancho.</summary>
    private static double Altura(Rebar b)
    {
        Curve mayor = Mayor(b);
        return (mayor.GetEndPoint(0).Z + mayor.GetEndPoint(1).Z) / 2;
    }

    /// <summary>
    /// Debajo de la viga: la cadena columna, luz, columna y, debajo, el total. Son cotas de Revit contra planos de
    /// referencia (decisión 4 del plan). Devuelve hasta dónde bajan.
    /// </summary>
    private static double Cotas(Document doc, View vista, VigaContinua viga, TiposPropios tipos, XYZ derecha, XYZ enPlano, XYZ min, XYZ max, double piso)
    {
        double a = min.DotProduct(derecha), b = max.DotProduct(derecha);
        double holgura = 0.05 * M;
        var caja = new Outline(new XYZ(min.X - holgura, min.Y - holgura, min.Z - holgura), new XYZ(max.X + holgura, max.Y + holgura, max.Z + holgura));
        List<(double Desde, double Hasta)> columnas = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns).WhereElementIsNotElementType()
            .WherePasses(new BoundingBoxIntersectsFilter(caja)).Select(c => c.get_BoundingBox(null)).Where(c => c is not null)
            .Select(c => (Math.Min(c.Min.DotProduct(derecha), c.Max.DotProduct(derecha)), Math.Max(c.Min.DotProduct(derecha), c.Max.DotProduct(derecha)))).ToList();
        IReadOnlyList<TramoViga> tramos = TramosViga.Calcular(Math.Min(a, b), Math.Max(a, b), columnas);
        Registro.Paso($"longitudinal: {columnas.Count} columnas, {tramos.Count} tramos");

        XYZ En(double s, double z) => new XYZ(enPlano.X, enPlano.Y, z) + derecha * (s - enPlano.DotProduct(derecha));
        Reference Plano(double s) => doc.Create.NewReferencePlane(En(s, max.Z), En(s, min.Z), vista.ViewDirection, vista).GetReference();

        var marcas = new List<double> { tramos[0].Desde };
        marcas.AddRange(tramos.Select(t => t.Hasta));
        List<Reference> planos = marcas.Select(Plano).ToList();
        doc.Regenerate();

        double cadena = piso - SeparacionCotas, total = cadena - SeparacionCotas;
        var todos = new ReferenceArray();
        planos.ForEach(todos.Append);
        doc.Create.NewDimension(vista, Line.CreateBound(En(marcas[0], cadena), En(marcas[^1], cadena)), todos, tipos.Cota);
        double fondo = cadena;
        if (tramos.Count > 1)
        {
            var extremos = new ReferenceArray();
            extremos.Append(planos[0]);
            extremos.Append(planos[^1]);
            doc.Create.NewDimension(vista, Line.CreateBound(En(marcas[0], total), En(marcas[^1], total)), extremos, tipos.Cota);
            fondo = total;
        }
        vista.SetCategoryHidden(new ElementId(BuiltInCategory.OST_CLines), true);
        return fondo;
    }

    private static View Seccion(Document doc, ViewFamilyType tipo, VigaContinua viga, List<Rebar> barras, TiposPropios tipos, bool textosNegros)
    {
        (XYZ min, XYZ max) = Caja(doc, viga);
        XYZ dir = viga.Direccion;
        XYZ base_ = dir.CrossProduct(XYZ.BasisZ).Negate();
        double ancho = Math.Abs((max - min).DotProduct(base_));
        double alto = max.Z - min.Z;
        XYZ medio = (min + max) / 2;
        // En el centro de la primera viga: en plena luz, no sobre un apoyo.
        XYZ enLuz = ((LocationCurve)doc.GetElement(viga.Vigas[0]).Location).Curve.Evaluate(0.5, true);
        double aLoLargo = enLuz.DotProduct(dir);
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
        List<Rebar> longitudinales = barras.Where(b => EsLongitudinal(b, dir)).ToList();
        HashSet<ElementId> bastones = Bastones(longitudinales);
        var cortadas = new List<(Rebar Barra, XYZ Punto)>();
        var etiquetas = new List<TextNote>();
        foreach (Rebar b in longitudinales.Where(b => !bastones.Contains(b.Id)))
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
                if (!textosNegros)
                    Pintar(vista, nota.Id, barra, null);
                etiquetas.Add(nota);
            }
        }
        Etiquetar(cortadas.Where(c => c.Punto.Z > medio.Z).ToList(), alto / 2 + 0.38 * M);
        Etiquetar(cortadas.Where(c => c.Punto.Z <= medio.Z).ToList(), -alto / 2 - 0.2 * M);

        Estribos(doc, vista, barras.Where(b => !EsLongitudinal(b, dir)).ToList(), etiquetas, tipos, textosNegros, corte, derecha, ancho, alto);
        return vista;
    }

    /// <summary>
    /// Al lado de la sección, una vez cada forma de estribo (también los menores), cada una con su cantidad, largo y gancho.
    /// El recorte final abarca todo lo que quedó.
    /// </summary>
    private static void Estribos(Document doc, View vista, List<Rebar> estribos, List<TextNote> etiquetas, TiposPropios tipos, bool textosNegros, XYZ corte, XYZ derecha, double ancho, double alto)
    {
        double arriba = alto / 2 + 0.5 * M, abajo = alto / 2 + 0.5 * M, aLaDerecha = ancho / 2 + 0.3 * M;
        var formas = estribos.GroupBy(b => (Tipo: b.GetTypeId().Value, Forma: b.GetShapeId().Value, Largo: Math.Round(LectorDespiece.Mm(LectorDespiece.LargoBarra(b)))))
            .OrderByDescending(f => f.Key.Largo).ToList();
        var creados = new List<(Rebar Barra, Element Detalle, TextNote Nota, XYZ Posicion)>();
        var enLaVista = new HashSet<ElementId>(new FilteredElementCollector(doc, vista.Id).OfCategory(BuiltInCategory.OST_Rebar).ToElementIds());
        foreach (var forma in formas)
        {
            // Una barra que se vea en la sección, si la hay: es de donde sale el detalle.
            Rebar b = forma.FirstOrDefault(e => enLaVista.Contains(e.Id)) ?? forma.First();
            XYZ p = corte + derecha * (ancho / 2 + 1.0 * M);
            Element d = RebarBendingDetail.Create(doc, vista.Id, b.Id, 0, tipos.Barra, p, 0);
            Pintar(vista, d.Id, b, 3);
            string nombre = doc.GetElement(b.GetTypeId()).Name;
            TextNote nota = TextNote.Create(doc, vista.Id, p, TextosDespiece.Estribo(nombre, forma.Sum(e => e.Quantity), LectorDespiece.LargoBarra(b) * 0.3048, Gancho(b) * 0.3048), tipos.Nota(HorizontalTextAlignment.Center));
            if (!textosNegros)
                Pintar(vista, nota.Id, b, null);
            creados.Add((b, d, nota, p));
        }
        doc.Regenerate();

        double centro = corte.DotProduct(derecha);
        double borde = etiquetas.Count == 0 ? centro + ancho / 2 : Math.Max(centro + ancho / 2, etiquetas.Max(n => Medir(n, vista, derecha).Derecha));
        List<(Rebar Barra, Element Detalle, TextNote Nota, XYZ Posicion, Medida MedidaDetalle, Medida MedidaNota)> medidos =
            creados.Select(c => (c.Barra, c.Detalle, c.Nota, c.Posicion, Medir(c.Detalle, vista, derecha), Medir(c.Nota, vista, derecha))).ToList();
        if (medidos.Count > 0)
        {
            double anchoColumna = medidos.Max(m => Math.Max(m.MedidaDetalle.Ancho, m.MedidaNota.Ancho));
            double mitadColumna = borde + 0.2 * M + anchoColumna / 2;
            double tope = corte.Z + alto / 2 + 0.3 * M;
            IReadOnlyList<double> pisos = AcomodadorFilas.Apilar(medidos.Select(m => m.MedidaDetalle.Alto + SeparacionTexto + m.MedidaNota.Alto), tope, HuecoFilas);
            for (int i = 0; i < medidos.Count; i++)
            {
                var m = medidos[i];
                double dx = mitadColumna - (m.MedidaDetalle.Izquierda + m.MedidaDetalle.Derecha) / 2;
                RebarBendingDetail.SetPosition(m.Detalle, new XYZ(m.Posicion.X, m.Posicion.Y, m.Posicion.Z + pisos[i] - m.MedidaDetalle.Abajo) + derecha * dx);
                XYZ alTexto = corte + derecha * (mitadColumna - centro);
                m.Nota.Coord = new XYZ(alTexto.X, alTexto.Y, pisos[i] + m.MedidaDetalle.Alto + SeparacionTexto + m.MedidaNota.Alto);
            }
            aLaDerecha = mitadColumna + anchoColumna / 2 - centro + 0.15 * M;
            arriba = Math.Max(arriba, tope - corte.Z + 0.15 * M);
            abajo = Math.Max(abajo, corte.Z - pisos[^1] + 0.15 * M);
        }
        Recortar(vista, corte, ancho / 2 + 0.55 * M, aLaDerecha, abajo, arriba);
    }

    /// <summary>El gancho que dice Revit para esta barra: el más largo de sus dos puntas. 0 si no tiene.</summary>
    private static double Gancho(Rebar b)
    {
        if (b.Document.GetElement(b.GetTypeId()) is not RebarBarType tipo)
            return 0;
        return new[] { 0, 1 }.Select(b.GetHookTypeId).Where(id => id != ElementId.InvalidElementId).Select(tipo.GetHookLength).DefaultIfEmpty(0).Max();
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

    /// <summary>Solo la viga y su acero (#217): sin rellenos ni dibujos que imiten otra cosa.</summary>
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

    private static IList<Curve> Curvas(Rebar b) => b.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);

    private static Curve Mayor(Rebar b) => Curvas(b).OrderByDescending(c => c.Length).First();

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

    private TiposPropios(RebarBendingDetailType barra, ElementId notaId, DimensionType cota)
    {
        Barra = barra;
        this.notaId = notaId;
        Cota = cota;
    }

    public RebarBendingDetailType Barra { get; }

    /// <summary>Las cotas de la viga, en metros.</summary>
    public DimensionType Cota { get; }

    public TextNoteOptions Nota(HorizontalTextAlignment alineacion) => new(notaId) { HorizontalAlignment = alineacion };

    public static TiposPropios Crear(Document doc)
    {
        string sufijo = Guid.NewGuid().ToString("N");
        List<RebarBendingDetailType> todos = new FilteredElementCollector(doc).OfClass(typeof(RebarBendingDetailType)).Cast<RebarBendingDetailType>().ToList();
        // El que no trae etiqueta: la del proyecto dice "#5 CQEING - 34", y borrarla después borra también el detalle.
        RebarBendingDetailType original = todos.FirstOrDefault(t => !TieneEtiqueta(doc, t)) ?? todos.First();
        var barra = (RebarBendingDetailType)original.Duplicate("CQEING temporal " + sufijo);
        DimensionType? metros = null;
        foreach (Parameter p in barra.Parameters)
        {
            if (p.IsReadOnly || p.StorageType != StorageType.ElementId)
                continue;
            if (doc.GetElement(p.AsElementId()) is DimensionType { StyleType: DimensionStyleType.Linear } lineal)
            {
                Registro.Paso("tipos: cota en metros " + p.Definition.Name);
                metros ??= EnMetros(lineal, sufijo);
                p.Set(metros.Id);
            }
        }
        Registro.Paso("tipos: nota de texto");
        TextNoteType base_ = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().First();
        var nota = (TextNoteType)base_.Duplicate("CQEING temporal " + sufijo);
        nota.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(0.0025 / 0.3048);
        nota.get_Parameter(BuiltInParameter.TEXT_BACKGROUND).Set(1);
        nota.get_Parameter(BuiltInParameter.LINE_COLOR).Set(0);
        metros ??= EnMetros(new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().First(d => d.StyleType == DimensionStyleType.Linear && d.Name.Length > 0), sufijo);
        return new TiposPropios(barra, nota.Id, metros);
    }

    private static bool TieneEtiqueta(Document doc, RebarBendingDetailType tipo) => tipo.Parameters.Cast<Parameter>()
        .Any(p => p.StorageType == StorageType.ElementId && doc.GetElement(p.AsElementId())?.Category?.Id.Value == (long)BuiltInCategory.OST_RebarTags);

    private static DimensionType EnMetros(DimensionType original, string sufijo)
    {
        var copia = (DimensionType)original.Duplicate("CQEING temporal " + sufijo);
        copia.SetUnitsFormatOptions(new FormatOptions(UnitTypeId.Meters) { UseDefault = false, Accuracy = 0.01, SuppressTrailingZeros = false });
        return copia;
    }
}
