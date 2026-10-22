using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.DB.Structure;
using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Revit;

/// <summary>
/// La vista previa 3D del acero (el botón "3D"): dibuja el plan del pórtico (<see cref="PreviaAcero"/>) como tubos del
/// color de su diámetro, con un contorno negro (como el acero generado, <see cref="FiltrosColor"/>),
/// en la vista 3D del tablero, con DirectContext3D. No crea nada en el modelo: no se guarda, no se
/// selecciona, no entra en tablas ni en el Ctrl+Z, y desaparece al quitarla. Un solo servidor por sesión de Revit; se
/// registra la primera vez que se usa.
/// </summary>
internal sealed class PreviaAcero3D : IDirectContext3DServer
{
    /// <summary>Lados de cada tubo.</summary>
    private const int Lados = 8;

    /// <summary>Vértices por bloque: los búferes de Revit se dibujan por partes para no pasar de su tamaño.</summary>
    private const int VerticesPorBloque = 60000;

    /// <summary>
    /// El contorno va un poco por fuera del tubo: sobre la superficie misma, las líneas y los triángulos se pelean por el
    /// mismo píxel y el contorno parpadea.
    /// </summary>
    private const double SeparacionContorno = 1.06;

    private static readonly ColorWithTransparency ColorContorno =
        new(ColoresDiametro.RgbContorno.R, ColoresDiametro.RgbContorno.G, ColoresDiametro.RgbContorno.B, 0);

    private static readonly Guid Id = new("6E2B7B9E-3C1F-4D7A-9A53-8F1C0B4E2D71");
    private static PreviaAcero3D? servidor;

    private readonly string nombreVista;
    private Document? documento;
    private List<Malla> mallas = [];
    private List<Bloque>? bloques;
    private List<BloqueLineas>? bloquesContorno;
    private Outline? caja;

    private PreviaAcero3D(string nombreVista) => this.nombreVista = nombreVista;

    /// <summary>
    /// Dibuja el pórtico en la vista <paramref name="nombreVista"/> de <paramref name="doc"/>, o quita lo dibujado si
    /// <paramref name="p"/> es null.
    /// </summary>
    public static void Mostrar(Document doc, string nombreVista, PorticoAGenerar? p, Reglas reglas)
    {
        PreviaAcero3D s = Registrado(nombreVista);
        if (p is null)
        {
            s.Vaciar();
            return;
        }
        IReadOnlyList<Trazo> trazos = PreviaAcero.Trazos(p.Plan, p.Ubicacion, reglas, Doblados(doc, p.Plan.Diametros, reglas));
        s.documento = doc;
        s.mallas = trazos.Select(Tubo).ToList();
        s.caja = Caja(trazos);
        // se arman en el próximo dibujo, en el hilo de dibujo
        s.bloques = null;
        s.bloquesContorno = null;
    }

    /// <summary>Quita lo dibujado (al cerrar el tablero o cambiar de documento). No registra el servidor si no estaba.</summary>
    public static void Quitar() => servidor?.Vaciar();

    private void Vaciar()
    {
        documento = null;
        mallas = [];
        caja = null;
        bloques = null;
        bloquesContorno = null;
    }

    private static PreviaAcero3D Registrado(string nombreVista)
    {
        if (servidor is not null)
            return servidor;
        servidor = new PreviaAcero3D(nombreVista);
        var servicio = (MultiServerService)ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService);
        servicio.AddServer(servidor);
        IList<Guid> activos = servicio.GetActiveServerIds();
        activos.Add(Id);
        servicio.SetActiveServers(activos);
        return servidor;
    }

    /// <summary>El diámetro y los doblados de cada tipo de barra del modelo (los mismos que usa "Generar").</summary>
    private static Dictionary<int, DobladoBarra> Doblados(Document doc, IEnumerable<int> diametros, Reglas reglas)
    {
        TiposAcero tipos = TiposAcero.Leer(doc, reglas);
        var doblados = new Dictionary<int, DobladoBarra>();
        foreach (int n in diametros)
            if (tipos.Barra(n) is RebarBarType t)
                doblados[n] = new DobladoBarra(LectorModelo.Mm(t.BarNominalDiameter), LectorModelo.Mm(t.StandardHookBendDiameter),
                    LectorModelo.Mm(t.StirrupTieBendDiameter));
        return doblados;
    }

    private static XYZ Pies(Punto3 p) => new(CreadorBarras.Pies(p.X), CreadorBarras.Pies(p.Y), CreadorBarras.Pies(p.Z));

    private static Outline? Caja(IReadOnlyList<Trazo> trazos)
    {
        List<Punto3> puntos = trazos.SelectMany(t => t.Puntos).ToList();
        if (puntos.Count == 0)
            return null;
        double margen = trazos.Max(t => t.Diametro);
        return new Outline(
            Pies(new Punto3(puntos.Min(p => p.X) - margen, puntos.Min(p => p.Y) - margen, puntos.Min(p => p.Z) - margen)),
            Pies(new Punto3(puntos.Max(p => p.X) + margen, puntos.Max(p => p.Y) + margen, puntos.Max(p => p.Z) + margen)));
    }

    /// <summary>Un vértice del tubo: posición y normal en pies, y el color de su diámetro.</summary>
    private readonly record struct Vertice(XYZ Posicion, XYZ Normal, ColorWithTransparency Color);

    /// <summary>
    /// Los vértices y triángulos (índices locales) de un trazo, y su contorno: pares de puntos en pies, cada par una línea.
    /// </summary>
    private sealed record Malla(List<Vertice> Vertices, List<(int A, int B, int C)> Triangulos, List<XYZ> Contorno);

    /// <summary>
    /// Un cilindro de <see cref="Lados"/> lados por cada tramo recto del trazo, del diámetro de la barra. Los tramos de los
    /// dobleces son cortos: así no hace falta unir los cilindros. El contorno son cuatro líneas a lo largo del tubo (cada
    /// cuarto de vuelta, así se ve desde cualquier lado) y un anillo en cada punta de la barra.
    /// </summary>
    private static Malla Tubo(Trazo t)
    {
        (byte r, byte g, byte b) = ColoresDiametro.Rgb(t.Numero);
        var color = new ColorWithTransparency(r, g, b, 0);
        double radio = CreadorBarras.Pies(t.Diametro / 2), radioContorno = radio * SeparacionContorno;
        var vertices = new List<Vertice>();
        var triangulos = new List<(int, int, int)>();
        var contorno = new List<XYZ>();
        (XYZ Centro, XYZ U, XYZ V)? primera = null, ultima = null;
        for (int i = 0; i + 1 < t.Puntos.Count; i++)
        {
            XYZ a = Pies(t.Puntos[i]), b2 = Pies(t.Puntos[i + 1]);
            XYZ eje = b2 - a;
            if (eje.GetLength() < 1e-9)
                continue;
            eje = eje.Normalize();
            XYZ u = (Math.Abs(eje.Z) < 0.9 ? eje.CrossProduct(XYZ.BasisZ) : eje.CrossProduct(XYZ.BasisX)).Normalize();
            XYZ v = eje.CrossProduct(u);
            primera ??= (a, u, v);
            ultima = (b2, u, v);
            int base0 = vertices.Count;
            for (int k = 0; k < Lados; k++)
            {
                XYZ normal = Radial(u, v, k);
                vertices.Add(new Vertice(a + normal * radio, normal, color));
                vertices.Add(new Vertice(b2 + normal * radio, normal, color));
                if (k % (Lados / 4) == 0)
                    contorno.AddRange([a + normal * radioContorno, b2 + normal * radioContorno]);
            }
            for (int k = 0; k < Lados; k++)
            {
                int a0 = base0 + 2 * k, a1 = a0 + 1, b0 = base0 + 2 * ((k + 1) % Lados), b1 = b0 + 1;
                triangulos.Add((a0, b0, a1));
                triangulos.Add((a1, b0, b1));
            }
        }
        foreach ((XYZ centro, XYZ u, XYZ v) in new[] { primera, ultima }.OfType<(XYZ, XYZ, XYZ)>())
            for (int k = 0; k < Lados; k++)
                contorno.AddRange([centro + Radial(u, v, k) * radioContorno, centro + Radial(u, v, k + 1) * radioContorno]);
        return new Malla(vertices, triangulos, contorno);
    }

    /// <summary>La dirección del lado <paramref name="k"/> del tubo, de largo 1.</summary>
    private static XYZ Radial(XYZ u, XYZ v, int k)
    {
        double ang = 2 * Math.PI * k / Lados;
        return u * Math.Cos(ang) + v * Math.Sin(ang);
    }

    /// <summary>Un búfer de Revit listo para dibujar.</summary>
    private sealed record Bloque(VertexBuffer Vertices, int CantidadVertices, IndexBuffer Indices, int CantidadTriangulos);

    /// <summary>Un búfer de las líneas del contorno listo para dibujar.</summary>
    private sealed record BloqueLineas(VertexBuffer Vertices, int CantidadVertices, IndexBuffer Indices, int CantidadLineas);

    /// <summary>Junta las mallas en bloques de hasta <see cref="VerticesPorBloque"/> vértices.</summary>
    private List<Bloque> Armar() =>
        EnBloques(mallas.Where(m => m.Vertices.Count > 0), m => m.Vertices.Count).Select(ArmarBloque).ToList();

    /// <summary>Lo mismo con el contorno: cada punto es un vértice.</summary>
    private List<BloqueLineas> ArmarContorno() =>
        EnBloques(mallas.Where(m => m.Contorno.Count > 0), m => m.Contorno.Count).Select(ArmarBloqueLineas).ToList();

    /// <summary>Parte las mallas en grupos que no pasan de <see cref="VerticesPorBloque"/> vértices.</summary>
    private static List<List<Malla>> EnBloques(IEnumerable<Malla> partes, Func<Malla, int> vertices)
    {
        var grupos = new List<List<Malla>>();
        var actual = new List<Malla>();
        int enActual = 0;
        foreach (Malla m in partes)
        {
            if (enActual + vertices(m) > VerticesPorBloque && actual.Count > 0)
            {
                grupos.Add(actual);
                (actual, enActual) = ([], 0);
            }
            actual.Add(m);
            enActual += vertices(m);
        }
        if (actual.Count > 0)
            grupos.Add(actual);
        return grupos;
    }

    private static BloqueLineas ArmarBloqueLineas(List<Malla> partes)
    {
        int nv = partes.Sum(m => m.Contorno.Count), nl = nv / 2;
        var vb = new VertexBuffer(nv * VertexPositionColored.GetSizeInFloats());
        vb.Map(nv * VertexPositionColored.GetSizeInFloats());
        VertexStreamPositionColored vs = vb.GetVertexStreamPositionColored();
        foreach (XYZ p in partes.SelectMany(m => m.Contorno))
            vs.AddVertex(new VertexPositionColored(p, ColorContorno));
        vb.Unmap();

        var ib = new IndexBuffer(nl * IndexLine.GetSizeInShortInts());
        ib.Map(nl * IndexLine.GetSizeInShortInts());
        IndexStreamLine ls = ib.GetIndexStreamLine();
        for (int i = 0; i < nl; i++)
            ls.AddLine(new IndexLine(2 * i, 2 * i + 1));
        ib.Unmap();
        return new BloqueLineas(vb, nv, ib, nl);
    }

    private static Bloque ArmarBloque(List<Malla> partes)
    {
        int nv = partes.Sum(m => m.Vertices.Count), nt = partes.Sum(m => m.Triangulos.Count);
        var vb = new VertexBuffer(nv * VertexPositionNormalColored.GetSizeInFloats());
        vb.Map(nv * VertexPositionNormalColored.GetSizeInFloats());
        VertexStreamPositionNormalColored vs = vb.GetVertexStreamPositionNormalColored();
        foreach (Vertice x in partes.SelectMany(m => m.Vertices))
            vs.AddVertex(new VertexPositionNormalColored(x.Posicion, x.Normal, x.Color));
        vb.Unmap();

        var ib = new IndexBuffer(nt * IndexTriangle.GetSizeInShortInts());
        ib.Map(nt * IndexTriangle.GetSizeInShortInts());
        IndexStreamTriangle ts = ib.GetIndexStreamTriangle();
        int desplazamiento = 0;
        foreach (Malla m in partes)
        {
            foreach ((int a, int b, int c) in m.Triangulos)
                ts.AddTriangle(new IndexTriangle(a + desplazamiento, b + desplazamiento, c + desplazamiento));
            desplazamiento += m.Vertices.Count;
        }
        ib.Unmap();
        return new Bloque(vb, nv, ib, nt);
    }

    // --- IDirectContext3DServer ---

    public Guid GetServerId() => Id;
    public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
    public string GetName() => "CQEING Vigas: vista previa del acero";
    public string GetVendorId() => "CQEING";
    public string GetDescription() => "Dibuja el acero del pórtico en la vista 3D del tablero, sin crearlo en el modelo.";
    public string GetApplicationId() => "";
    public string GetSourceId() => "";
    public bool UsesHandles() => false;
    public bool UseInTransparentPass(View view) => false;

    public bool CanExecute(View view) =>
        documento is not null && caja is not null && view is View3D && view.Name == nombreVista && view.Document.Equals(documento);

    public Outline? GetBoundingBox(View view) => caja;

    public void RenderScene(View view, DisplayStyle displayStyle)
    {
        try
        {
            if (caja is null)
                return;
            bloques ??= Armar();
            var formato = new VertexFormat(VertexFormatBits.PositionNormalColored);
            var efecto = new EffectInstance(VertexFormatBits.PositionNormalColored);
            foreach (Bloque b in bloques)
                DrawContext.FlushBuffer(b.Vertices, b.CantidadVertices, b.Indices, b.CantidadTriangulos * 3, formato, efecto,
                    PrimitiveType.TriangleList, 0, b.CantidadTriangulos);

            bloquesContorno ??= ArmarContorno();
            var formatoLineas = new VertexFormat(VertexFormatBits.PositionColored);
            var efectoLineas = new EffectInstance(VertexFormatBits.PositionColored);
            foreach (BloqueLineas l in bloquesContorno)
                DrawContext.FlushBuffer(l.Vertices, l.CantidadVertices, l.Indices, l.CantidadLineas * 2, formatoLineas, efectoLineas,
                    PrimitiveType.LineList, 0, l.CantidadLineas);
        }
        catch
        {
            // Revit llama desde su lado nativo: una excepción aquí lo podría tumbar. Sin dibujo, la vista sigue igual.
        }
    }
}
