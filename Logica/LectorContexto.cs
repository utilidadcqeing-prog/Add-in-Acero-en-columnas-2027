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
///  - Una «columna» para el ingeniero no siempre es un solo elemento de Revit:
///    validado sobre el modelo de prueba (3-sep-2026), este parte cada columna
///    en un <c>FamilyInstance</c> por piso -- uno para la cimentación, uno por
///    nivel libre, uno por nudo de viga -- apilados en la misma posición en
///    planta. Lógicamente es una sola columna corrida y así se lee: se
///    agrupan por posición en planta (<see cref="AgruparPilas"/>) y se arman
///    los tramos de abajo hacia arriba recorriendo toda la pila, no un solo
///    elemento.
///
/// Ninguna columna mal modelada tumba el lote: lo que no se pudo determinar sale
/// como aviso dentro del propio contexto.
/// </summary>
public sealed class LectorContexto : IProveedorContexto
{
    /// <summary>Un centímetro en pies. Por debajo de esto un tramo no es un tramo.</summary>
    private static readonly double TramoMinimo = APies(0.01);

    /// <summary>
    /// Dos elementos de columna cuyo centro en planta cae a menos de esto se
    /// consideran la misma pila. Generoso a propósito (encaje de grilla,
    /// redondeos de modelado) pero corto para no fusionar dos columnas
    /// vecinas de verdad.
    /// </summary>
    private static readonly double ToleranciaPlanta = APies(0.05);

    private const double Tolerancia = 1e-6;

    private readonly Document _doc;

    /// <summary>
    /// Memoiza <see cref="LeerElementos"/> durante la vida de este
    /// <see cref="LectorContexto"/> (issue #28: recorrer y calcular geometría
    /// de TODAS las columnas del documento en cada click de la ventana no
    /// se nota en el modelo de prueba, pero sí en uno con muchas columnas).
    /// <c>ComandoEstribos</c> crea uno solo por apertura de ventana, así que
    /// esto alcanza para todo lo que se haga sin cerrarla -- si el usuario
    /// edita el modelo con la ventana abierta y vuelve a hacer click en una
    /// columna, ve la lectura de cuando la abrió, no la última: mismo
    /// comportamiento que ya tiene el resto de la ventana (no hay un botón
    /// "refrescar" ni un listener de cambios del documento todavía).
    /// </summary>
    private List<ElementoColumna>? _cacheElementos;

    public LectorContexto(Document doc) => _doc = doc;

    /// <summary>
    /// Descarta <see cref="_cacheElementos"/> -- hay que llamarlo después de
    /// generar armado. Los <c>Solid</c> que guarda quedan atados a la
    /// geometría de Revit de cuando se leyeron; en cuanto el documento se
    /// modifica (creación/borrado de Rebar, <c>Document.Regenerate()</c>) esos
    /// objetos dejan de ser válidos, y volver a usarlos (por ejemplo desde
    /// "Aplicar a selección" o el panel de elevación, sin cerrar la ventana)
    /// puede terminar en un crash nativo de Revit en vez de una excepción de
    /// .NET -- probado sobre Revit real.
    /// </summary>
    public void InvalidarCache() => _cacheElementos = null;

    /// <summary>Un elemento de columna ya resuelto: su sólido y dónde cae en el espacio.</summary>
    private sealed record ElementoColumna(FamilyInstance Instancia, Solid Solido, XYZ Centro, double ZInf, double ZSup);

    /// <summary>
    /// Lista las columnas «lógicas» (pilas) del documento para poblar la
    /// selección de L2 — una fila por pila, no por elemento.
    /// </summary>
    public IReadOnlyList<ColumnaResumen> ListarColumnas() =>
        AgruparPilas(LeerElementos())
            .Select(pila => new ColumnaResumen(
                IdTexto(pila[0].Instancia.Id),
                Marca(pila[0].Instancia),
                ColumnaResumen.CalcularEsColumneta(AMetros(LadoX(pila[0].Solido)), AMetros(LadoY(pila[0].Solido))),
                // El centro en planta del elemento de más abajo de la pila --
                // mismo criterio que ya usa AgruparPilas para agrupar (ver
                // ElementoColumna.Centro): alcanza para ubicar la columna en
                // el selector visual, aunque la sección esté rotada (ver
                // GeometriaSeccion.ValidarRectangular, misma limitación que
                // ya tiene el resto del generador para columnas rotadas).
                AMetros(pila[0].Centro.X),
                AMetros(pila[0].Centro.Y)))
            .ToList();

    /// <summary>
    /// Rejillas (grids) del documento, para el selector visual de planta. Se
    /// descartan las que no son rectas (Revit puede modelar una rejilla
    /// curva/de arco) -- no hay forma de representarlas en el selector, mismo
    /// criterio que <c>GeometriaSeccion.ValidarRectangular</c> aplica para
    /// secciones no rectangulares: se omiten en vez de fallar todo el lote.
    /// </summary>
    public IReadOnlyList<Rejilla> ListarRejillas() =>
        new FilteredElementCollector(_doc)
            .OfClass(typeof(Grid))
            .Cast<Grid>()
            .Select(g => (Grid: g, Curva: g.Curve))
            .Where(t => t.Curva is Line)
            .Select(t => new Rejilla(
                t.Grid.Name,
                AMetros(t.Curva.GetEndPoint(0).X), AMetros(t.Curva.GetEndPoint(0).Y),
                AMetros(t.Curva.GetEndPoint(1).X), AMetros(t.Curva.GetEndPoint(1).Y)))
            .ToList();

    public ColumnaContexto Leer(string idColumna)
    {
        var avisos = new List<string>();

        if (!long.TryParse(idColumna, out long valor))
            throw new InvalidOperationException($"«{idColumna}» no es un id de columna válido.");

        if (_doc.GetElement(new ElementId(valor)) is not FamilyInstance objetivo)
            throw new InvalidOperationException($"El elemento {idColumna} no es una columna.");

        List<ElementoColumna> elementos = LeerElementos();

        ElementoColumna? elObjetivo = elementos.FirstOrDefault(e => e.Instancia.Id == objetivo.Id)
            ?? throw new InvalidOperationException(
                $"No se pudo leer la geometría de la columna {Marca(objetivo)}.");

        List<ElementoColumna> pila = elementos
            .Where(e => e.Centro.DistanceTo(elObjetivo.Centro) <= ToleranciaPlanta)
            .OrderBy(e => e.ZInf)
            .ToList();

        var tramos = new List<Tramo>();
        bool huboNudo = false;
        bool primero = true;

        // El acero longitudinal SÍ entra en la zapata a anclarse (a 7.5cm de
        // su cara inferior -- issue #26, confirmado con Luciano), a
        // diferencia del estribo, que arranca en la cara superior. Por eso
        // esta cota se guarda aparte de zArranque/ZInferior: si no hay
        // zapata, vale lo mismo que el arranque normal (no hay dónde
        // anclarse más abajo).
        double zInferiorLongitudinal = double.MinValue;
        string? idZapata = null;
        const double AnclajeSobreElFondo = 0.075; // 7.5cm, issue #26.

        foreach (ElementoColumna el in pila)
        {
            double zArranque = el.ZInf;
            bool sobreZapata = false;

            // La zapata solo puede tocar el elemento de más abajo de la pila:
            // más arriba no hay nada contra qué arrancar.
            if (primero)
            {
                (double zZapata, double fondoZapata, Element? zapata) = TopeDeZapata(el.Solido);

                // Toca zapata en cuanto se encontró una -- no solo cuando el
                // sólido de la columna se mete DENTRO de ella (zZapata bien
                // por debajo de el.ZInf, el caso original). El caso normal de
                // modelado es la columna apoyada A RAS de la cara superior de
                // la zapata (zZapata ≈ el.ZInf, sin empotrar), y con la
                // condición vieja (zZapata > el.ZInf + 1cm) eso nunca daba
                // true: el tramo de abajo quedaba "libre" en vez de "contra
                // zapata" en el caso más común, no en el raro. Confirmado
                // sobre el modelo de prueba (columna de esquina, 3-sep).
                sobreZapata = zZapata > double.MinValue;

                if (sobreZapata)
                {
                    zArranque = Math.Max(el.ZInf, zZapata);
                    zInferiorLongitudinal = fondoZapata + APies(AnclajeSobreElFondo);
                    idZapata = zapata is not null ? IdTexto(zapata.Id) : null;
                    avisos.Add($"Arranca sobre cimentación: el acero empieza en {AMetros(zArranque):0.00} m.");
                }
            }

            if (el.ZSup - zArranque <= TramoMinimo)
            {
                if (primero)
                    throw new InvalidOperationException(
                        $"La columna {Marca(objetivo)} no deja altura libre sobre la cimentación.");

                avisos.Add($"El elemento {Marca(el.Instancia)} no dejó altura libre; se omitió.");
                primero = false;
                continue;
            }

            List<(double Inf, double Sup)> nudos = BandasDeViga(el.Solido, zArranque, el.ZSup);
            if (nudos.Count > 0) huboNudo = true;

            tramos.AddRange(ArmarTramos(
                zArranque, el.ZSup, nudos, sobreZapata, IdTexto(el.Instancia.Id), tramos.Count));

            primero = false;
        }

        if (tramos.Count == 0)
            throw new InvalidOperationException($"La columna {Marca(objetivo)} no dejó ningún tramo legible.");

        if (!huboNudo)
            avisos.Add(
                "Ninguna viga toca esta columna: queda un solo tramo libre de punta a punta. " +
                "Si esperabas nudos, revisa que las vigas lleguen a tocarla.");

        if (pila.Count > 1)
            AvisarSiCambiaLaSeccion(pila, avisos);

        ElementoColumna base_ = pila[0];

        // El primer sub-tramo del primer elemento siempre arranca en el mismo
        // zArranque que se calculó ahí arriba (con o sin zapata): tomarlo de
        // vuelta del propio tramo evita duplicar esa cuenta.
        double zInferiorGlobal = APies(tramos[0].ZInferior);

        // Sin zapata (o si por algún motivo no se pudo leer el fondo), el
        // longitudinal arranca en el mismo punto que el estribo -- no hay
        // dónde anclarse más abajo.
        double zInferiorLongitudinalGlobal =
            zInferiorLongitudinal > double.MinValue ? zInferiorLongitudinal : zInferiorGlobal;

        return new ColumnaContexto
        {
            Marca = Marca(base_.Instancia),
            BaseX = AMetros(LadoX(base_.Solido)),
            BaseY = AMetros(LadoY(base_.Solido)),
            Recubrimiento = AMetros(Recubrimiento(base_.Instancia, avisos)),
            ZInferior = AMetros(zInferiorGlobal),
            ZInferiorLongitudinal = AMetros(zInferiorLongitudinalGlobal),
            IdZapata = idZapata,
            ZSuperior = AMetros(pila[^1].ZSup),
            Tramos = tramos,
            Niveles = Rotulos(zInferiorGlobal, pila[^1].ZSup),
            Avisos = avisos
        };
    }

    // ------------------------------------------------------------ agrupado

    /// <summary>
    /// Todas las columnas estructurales del documento, resueltas a su sólido
    /// mayor, su centro en planta y su rango en Z -- ver <see cref="_cacheElementos"/>
    /// para hasta cuándo vale la memoización.
    /// </summary>
    private List<ElementoColumna> LeerElementos()
    {
        if (_cacheElementos is not null) return _cacheElementos;

        var elementos = new List<ElementoColumna>();

        foreach (FamilyInstance c in new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>())
        {
            Solid? solido = SolidoMayor(c.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }));
            if (solido is null) continue;

            (double zInf, double zSup) = ExtensionZ(solido);
            elementos.Add(new ElementoColumna(c, solido, CentroPlanta(solido), zInf, zSup));
        }

        _cacheElementos = elementos;
        return elementos;
    }

    /// <summary>
    /// Junta los elementos que comparten posición en planta en pilas, cada
    /// una ordenada de abajo hacia arriba — una pila es una columna lógica.
    /// </summary>
    private static List<List<ElementoColumna>> AgruparPilas(List<ElementoColumna> elementos)
    {
        var pilas = new List<List<ElementoColumna>>();

        foreach (ElementoColumna el in elementos)
        {
            List<ElementoColumna>? pila = pilas.FirstOrDefault(
                p => p[0].Centro.DistanceTo(el.Centro) <= ToleranciaPlanta);

            if (pila is null)
            {
                pila = new List<ElementoColumna>();
                pilas.Add(pila);
            }

            pila.Add(el);
        }

        foreach (List<ElementoColumna> pila in pilas)
            pila.Sort((a, b) => a.ZInf.CompareTo(b.ZInf));

        return pilas;
    }

    /// <summary>Centro en planta del sólido (promedio de su huella en X/Y, Z en 0).</summary>
    private static XYZ CentroPlanta(Solid solido)
    {
        Huella h = HuellaPlanta(solido);
        return new XYZ((h.XMin + h.XMax) / 2, (h.YMin + h.YMax) / 2, 0);
    }

    /// <summary>
    /// La sección puede cambiar de un piso a otro (columna que se achica al
    /// subir). Por ahora la ventana solo admite una sección por columna, así
    /// que se usa la del elemento más bajo y se avisa si alguna otra difiere.
    /// </summary>
    private static void AvisarSiCambiaLaSeccion(List<ElementoColumna> pila, List<string> avisos)
    {
        double baseX = LadoX(pila[0].Solido);
        double baseY = LadoY(pila[0].Solido);
        const double margen = 0.01; // un centímetro en pies, aprox.

        bool cambia = pila.Skip(1).Any(el =>
            Math.Abs(LadoX(el.Solido) - baseX) > margen || Math.Abs(LadoY(el.Solido) - baseY) > margen);

        if (cambia)
            avisos.Add(
                "La sección cambia entre los elementos que forman esta columna. " +
                "Se usó la del tramo más bajo para toda la columna.");
    }

    // ---------------------------------------------------------------- tramos

    /// <summary>
    /// Cose las bandas de viga de UN elemento con lo que queda libre entre
    /// ellas, de abajo hacia arriba y sin dejar huecos. El primer tramo de la
    /// pila entera se marca contra zapata si corresponde: ese borde no admite
    /// el mismo tratamiento que un extremo libre.
    /// </summary>
    private static List<Tramo> ArmarTramos(
        double zArranque,
        double zTope,
        List<(double Inf, double Sup)> nudos,
        bool sobreZapata,
        string idElementoHost,
        int indiceBase)
    {
        var tramos = new List<Tramo>();
        double cursor = zArranque;

        void Agregar(double inf, double sup, TipoTramo tipo)
        {
            if (sup - inf <= TramoMinimo) return;

            tramos.Add(new Tramo
            {
                Indice = indiceBase + tramos.Count,
                ZInferior = AMetros(inf),
                ZSuperior = AMetros(sup),
                // Solo el primer sub-tramo de este elemento, y solo si es
                // libre: si el elemento arrancara dentro de una viga, saber
                // eso importa más.
                Tipo = tramos.Count == 0 && sobreZapata && tipo == TipoTramo.Libre
                    ? TipoTramo.ContraZapata
                    : tipo,
                IdElementoHost = idElementoHost
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
    ///
    /// No exige que los sólidos se solapen en volumen (ver por qué en
    /// <see cref="SeTocan"/>): la banda sale directo del propio alto de la
    /// viga, recortado al rango de este elemento de columna.
    /// </summary>
    private List<(double Inf, double Sup)> BandasDeViga(Solid columna, double zArranque, double zTope)
    {
        var crudas = new List<(double Inf, double Sup)>();
        Huella huellaColumna = HuellaPlanta(columna);

        foreach (Element viga in CandidatasPorCaja(huellaColumna, BuiltInCategory.OST_StructuralFraming))
        {
            Solid? solidoViga = SolidoMayor(
                viga.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }));

            if (solidoViga is null) continue;

            Huella huellaViga = HuellaPlanta(solidoViga);
            if (!SeTocan(huellaColumna, huellaViga)) continue;

            (double inf, double sup) = ExtensionZ(solidoViga);

            // Una vigueta NO genera nudo -- criterio de William (reunión
            // 8-sep, sobre el script de referencia): "cómo sabemos que es una
            // vigueta? en su dimensión de base tiene menos de 20[cm], o la
            // altura tiene 20 o menos". La altura sale directo del sólido
            // (sup - inf, ya calculado arriba). El ancho de una viga
            // horizontal no se lee tan directo como el de una columna
            // vertical (acá el largo del tramo, no el ancho, es el lado
            // grande de la huella en planta) -- se aproxima con el menor de
            // los dos lados de su huella en planta, que en cualquier grilla
            // ortogonal normal es el ancho de la sección, no su largo. Mismo
            // umbral que ya usa `ColumnaResumen.UmbralMetros` para columnetas
            // (issue #33) -- incluso el criterio "base O altura" es el mismo.
            double anchoVigaAprox = Math.Min(huellaViga.XMax - huellaViga.XMin, huellaViga.YMax - huellaViga.YMin);
            if (AMetros(sup - inf) < ColumnaResumen.UmbralMetros || AMetros(anchoVigaAprox) < ColumnaResumen.UmbralMetros)
                continue;

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
    /// Cota superior e inferior de la zapata sobre la que arranca la columna
    /// (<see cref="double.MinValue"/> en ambas si no hay ninguna). Solo lee --
    /// el aviso y la decisión de qué hacer con el valor son de quien llama
    /// (<see cref="Leer"/>), que es quien sabe si además hace falta subir
    /// <c>zArranque</c>.
    ///
    /// Se leen las dos caras (no solo la superior, como antes) porque el
    /// acero longitudinal SÍ entra en la zapata a anclarse -- a diferencia
    /// del estribo, que arranca en la cara superior -- y para eso hace falta
    /// saber dónde está el fondo (ver <see cref="ColumnaContexto.ZInferiorLongitudinal"/>).
    ///
    /// También devuelve el elemento de la zapata (la más profunda, si hay
    /// más de una candidata) -- <see cref="ColumnaContexto.IdZapata"/> lo
    /// necesita porque el tramo de anclaje del longitudinal se hostea en la
    /// zapata, no en la columna (Revit no aloja un Rebar cuya curva se sale
    /// del sólido de su host).
    /// </summary>
    private (double Sup, double Inf, Element? Zapata) TopeDeZapata(Solid columna)
    {
        double tope = double.MinValue;
        double fondo = double.MinValue;
        Element? elementoFondo = null;
        Huella huellaColumna = HuellaPlanta(columna);

        foreach (Element zapata in CandidatasPorCaja(huellaColumna, BuiltInCategory.OST_StructuralFoundation))
        {
            Solid? solidoZapata = SolidoMayor(
                zapata.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }));

            if (solidoZapata is null || !SeTocan(huellaColumna, HuellaPlanta(solidoZapata))) continue;

            (double inf, double sup) = ExtensionZ(solidoZapata);

            // Del lado del fondo, si hay más de una zapata candidata (no
            // debería, pero por las dudas) nos quedamos con la MÁS PROFUNDA:
            // el ZInferiorLongitudinal tiene que valer para cualquiera que
            // esté ahí tocando, no solo la que dio el tope más alto.
            if (sup > tope) tope = sup;
            if (fondo == double.MinValue || inf < fondo)
            {
                fondo = inf;
                elementoFondo = zapata;
            }
        }

        return (tope, fondo, elementoFondo);
    }

    /// <summary>Rectángulo en planta que envuelve al sólido: (xMin, xMax, yMin, yMax).</summary>
    private readonly record struct Huella(double XMin, double XMax, double YMin, double YMax);

    /// <summary>
    /// Candidatos de esa categoría cuya caja (ampliada un margen) se cruza con
    /// la huella en planta de la columna. Es un filtro barato para no recorrer
    /// el documento entero: el corte de verdad lo hace <see cref="SeTocan"/>.
    /// </summary>
    private IEnumerable<Element> CandidatasPorCaja(Huella huella, BuiltInCategory categoria)
    {
        var caja = new Outline(
            new XYZ(huella.XMin - MargenCaja, huella.YMin - MargenCaja, -RangoZ),
            new XYZ(huella.XMax + MargenCaja, huella.YMax + MargenCaja, RangoZ));

        return new FilteredElementCollector(_doc)
            .OfCategory(categoria)
            .WhereElementIsNotElementType()
            .WherePasses(new BoundingBoxIntersectsFilter(caja))
            .ToElements();
    }

    /// <summary>Un metro y medio en pies: rango vertical de sobra para cualquier edificio real.</summary>
    private static readonly double RangoZ = APies(1500);

    /// <summary>Diez centímetros en pies: margen de la caja candidata, no de la huella real.</summary>
    private static readonly double MargenCaja = APies(0.10);

    /// <summary>
    /// Si dos huellas en planta se cruzan — el corte de verdad de «esta viga
    /// toca esta columna».
    ///
    /// A propósito NO exige que los sólidos se solapen en volumen (lo que
    /// hacía la versión anterior con <c>BooleanOperationsUtils</c>): cuando
    /// columna y viga están unidas con «Join Geometry» — el caso normal en
    /// cualquier modelo real — Revit recorta el sólido de la viga justo hasta
    /// la cara de la columna, y ahí ya no queda volumen que se solape aunque
    /// sí se estén tocando de verdad. Confirmado sobre el modelo de prueba:
    /// con la intersección booleana estricta, ninguna columna detectaba
    /// ningún nudo. Cruzar huellas en planta es más laxo pero, en un modelo
    /// estructural con grilla, una viga cuya huella cruza la de una columna
    /// casi siempre significa que apoya ahí — no que pasa cerca por casualidad.
    /// </summary>
    private static bool SeTocan(Huella a, Huella b) =>
        a.XMin <= b.XMax && b.XMin <= a.XMax && a.YMin <= b.YMax && b.YMin <= a.YMax;

    /// <summary>Huella del sólido en planta: extensión en X/Y de sus aristas, ignorando Z.</summary>
    private static Huella HuellaPlanta(Solid solido)
    {
        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;

        foreach (Edge arista in solido.Edges)
        {
            foreach (XYZ p in arista.Tessellate())
            {
                xMin = Math.Min(xMin, p.X);
                xMax = Math.Max(xMax, p.X);
                yMin = Math.Min(yMin, p.Y);
                yMax = Math.Max(yMax, p.Y);
            }
        }

        return new Huella(xMin, xMax, yMin, yMax);
    }

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
