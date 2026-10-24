namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;

/// <summary>
/// El dibujo de la vista previa 3D (#123): tubos de colores en la vista
/// <see cref="NombreVista"/>, con DirectContext3D. No crea nada en el modelo:
/// no se guarda, no se selecciona, no entra en tablas ni en el Ctrl+Z, y
/// desaparece al quitarlo. Un solo servidor por sesión de Revit; se registra
/// la primera vez que se usa.
///
/// COPIA ADAPTADA de <c>addins/acero-vigas/Revit/PreviaAcero3D.cs</c> (la
/// parte que dibuja), con su propio Guid: por ahora no se comparte código con
/// vigas (docs/planes/2026-10-21-vista-previa-3d-columnas-123.md, decisión 2).
/// Si se unifican, este es el archivo a juntar con aquel.
/// </summary>
internal sealed class Tubos3D : IDirectContext3DServer
{
    /// <summary>La vista 3D del add-in donde se pinta (la única).</summary>
    public const string NombreVista = "CQEING Columnas 3D";

    /// <summary>Lados de cada tubo.</summary>
    private const int Lados = 8;

    /// <summary>Vértices por bloque: los búferes de Revit se dibujan por partes para no pasar de su tamaño.</summary>
    private const int VerticesPorBloque = 60000;

    /// <summary>
    /// El contorno va un poco por fuera del tubo: sobre la superficie misma, las
    /// líneas y los triángulos se pelean por el mismo píxel y el contorno parpadea
    /// (mismo valor que vigas).
    /// </summary>
    private const double SeparacionContorno = 1.06;

    /// <summary>Dos puntas a menos de esto (pies) son la unión de dos pedazos de una barra: no llevan anillo.</summary>
    private const double ToleranciaUnion = 1e-4;

    /// <summary>
    /// Casi negro, como el contorno de vigas (<c>ColoresDiametro.RgbContorno</c>)
    /// y las líneas de las barras generadas en 3D (#144): con el fondo oscuro,
    /// Revit pinta de blanco el negro puro.
    /// </summary>
    private static readonly ColorWithTransparency ColorContorno = new(30, 30, 30, 0);

    /// <summary>Distinto del de vigas, para que los dos dibujos no se pisen.</summary>
    private static readonly Guid Id = new("C3A1F0D2-7B4E-4E59-9F1A-2D6B8E0C4A93");
    private static Tubos3D? servidor;

    /// <summary>Un tubo: su eje en pies (coordenadas del modelo), su radio en pies y su color.</summary>
    public sealed record Tubo(IReadOnlyList<XYZ> Puntos, double Radio, ColorWithTransparency Color);

    private Document? documento;
    private List<Malla> mallas = new();
    private List<Bloque>? bloques;
    private List<BloqueLineas>? bloquesContorno;
    private Outline? caja;

    private Tubos3D()
    {
    }

    /// <summary>Pinta <paramref name="tubos"/> en la vista <see cref="NombreVista"/> de <paramref name="doc"/>, en lugar de lo anterior.</summary>
    public static void Mostrar(Document doc, IEnumerable<Tubo> tubos)
    {
        Tubos3D s = Registrado();
        List<Tubo> lista = tubos.Where(t => t.Puntos.Count > 1).ToList();
        if (lista.Count == 0)
        {
            s.Vaciar();
            return;
        }

        // Cada tubo es un pedazo de barra (una curva, o un tramo recto, gancho
        // o traslapo): solo las puntas que no tocan otro pedazo son puntas de
        // la barra y llevan anillo (docs/planes/2026-10-23-contorno-vista-previa-3d.md).
        List<XYZ> puntas = lista.SelectMany(t => new[] { t.Puntos[0], t.Puntos[^1] }).ToList();
        bool Suelta(XYZ p) => puntas.Count(q => q.IsAlmostEqualTo(p, ToleranciaUnion)) == 1;

        s.documento = doc;
        s.mallas = lista.Select(t => Malla.De(t, Suelta(t.Puntos[0]), Suelta(t.Puntos[^1]))).ToList();
        s.caja = Caja(lista);
        s.bloques = null; // se arman en el próximo dibujo, en el hilo de dibujo
        s.bloquesContorno = null;
    }

    /// <summary>Quita lo dibujado (al cerrar la ventana o el documento). No registra el servidor si no estaba.</summary>
    public static void Quitar() => servidor?.Vaciar();

    private void Vaciar()
    {
        documento = null;
        mallas = new List<Malla>();
        caja = null;
        bloques = null;
        bloquesContorno = null;
    }

    private static Tubos3D Registrado()
    {
        if (servidor is not null) return servidor;

        servidor = new Tubos3D();
        var servicio = (MultiServerService)ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService);
        servicio.AddServer(servidor);
        IList<Guid> activos = servicio.GetActiveServerIds();
        activos.Add(Id);
        servicio.SetActiveServers(activos);
        return servidor;
    }

    private static Outline Caja(List<Tubo> tubos)
    {
        List<XYZ> puntos = tubos.SelectMany(t => t.Puntos).ToList();
        double margen = tubos.Max(t => t.Radio);
        return new Outline(
            new XYZ(puntos.Min(p => p.X) - margen, puntos.Min(p => p.Y) - margen, puntos.Min(p => p.Z) - margen),
            new XYZ(puntos.Max(p => p.X) + margen, puntos.Max(p => p.Y) + margen, puntos.Max(p => p.Z) + margen));
    }

    /// <summary>Un vértice del tubo: posición y normal en pies, y su color.</summary>
    private readonly record struct Vertice(XYZ Posicion, XYZ Normal, ColorWithTransparency Color);

    /// <summary>
    /// Los vértices y triángulos (índices locales) de un tubo, y su contorno:
    /// pares de puntos en pies, cada par una línea.
    /// </summary>
    private sealed record Malla(List<Vertice> Vertices, List<(int A, int B, int C)> Triangulos, List<XYZ> Contorno)
    {
        /// <summary>
        /// Un cilindro de <see cref="Lados"/> lados por cada tramo recto del
        /// eje. Los tramos de los dobleces son cortos: así no hace falta unir
        /// los cilindros. El contorno, como en vigas: cuatro líneas a lo largo
        /// (cada cuarto de vuelta, así se ve desde cualquier lado) y un anillo
        /// en las puntas que se piden.
        /// </summary>
        public static Malla De(Tubo t, bool anilloInicio, bool anilloFin)
        {
            var vertices = new List<Vertice>();
            var triangulos = new List<(int, int, int)>();
            var contorno = new List<XYZ>();
            double radioContorno = t.Radio * SeparacionContorno;
            (XYZ Centro, XYZ U, XYZ V)? primera = null, ultima = null;
            for (int i = 0; i + 1 < t.Puntos.Count; i++)
            {
                XYZ a = t.Puntos[i], b = t.Puntos[i + 1];
                XYZ eje = b - a;
                if (eje.GetLength() < 1e-9) continue;

                eje = eje.Normalize();
                XYZ u = (Math.Abs(eje.Z) < 0.9 ? eje.CrossProduct(XYZ.BasisZ) : eje.CrossProduct(XYZ.BasisX)).Normalize();
                XYZ v = eje.CrossProduct(u);
                primera ??= (a, u, v);
                ultima = (b, u, v);
                int base0 = vertices.Count;
                for (int k = 0; k < Lados; k++)
                {
                    XYZ normal = Radial(u, v, k);
                    vertices.Add(new Vertice(a + normal * t.Radio, normal, t.Color));
                    vertices.Add(new Vertice(b + normal * t.Radio, normal, t.Color));
                    if (k % (Lados / 4) == 0)
                        contorno.AddRange(new[] { a + normal * radioContorno, b + normal * radioContorno });
                }

                for (int k = 0; k < Lados; k++)
                {
                    int a0 = base0 + 2 * k, a1 = a0 + 1, b0 = base0 + 2 * ((k + 1) % Lados), b1 = b0 + 1;
                    triangulos.Add((a0, b0, a1));
                    triangulos.Add((a1, b0, b1));
                }
            }

            if (anilloInicio && primera is { } inicio) Anillo(contorno, inicio, radioContorno);
            if (anilloFin && ultima is { } fin) Anillo(contorno, fin, radioContorno);
            return new Malla(vertices, triangulos, contorno);
        }

        private static void Anillo(List<XYZ> contorno, (XYZ Centro, XYZ U, XYZ V) punta, double radio)
        {
            for (int k = 0; k < Lados; k++)
                contorno.AddRange(new[] { punta.Centro + Radial(punta.U, punta.V, k) * radio, punta.Centro + Radial(punta.U, punta.V, k + 1) * radio });
        }

        private static XYZ Radial(XYZ u, XYZ v, int k)
        {
            double angulo = 2 * Math.PI * k / Lados;
            return u * Math.Cos(angulo) + v * Math.Sin(angulo);
        }
    }

    /// <summary>Un búfer de Revit listo para dibujar.</summary>
    private sealed record Bloque(VertexBuffer Vertices, int CantidadVertices, IndexBuffer Indices, int CantidadTriangulos);

    /// <summary>Un búfer de líneas (el contorno) listo para dibujar.</summary>
    private sealed record BloqueLineas(VertexBuffer Vertices, int CantidadVertices, IndexBuffer Indices, int CantidadLineas);

    /// <summary>Junta los contornos en bloques de hasta <see cref="VerticesPorBloque"/> vértices, como <see cref="Armar"/>.</summary>
    private List<BloqueLineas> ArmarContorno()
    {
        var lista = new List<BloqueLineas>();
        var actual = new List<XYZ>();
        foreach (Malla m in mallas.Where(m => m.Contorno.Count > 0))
        {
            if (actual.Count + m.Contorno.Count > VerticesPorBloque && actual.Count > 0)
            {
                lista.Add(ArmarBloqueLineas(actual));
                actual = new List<XYZ>();
            }

            actual.AddRange(m.Contorno);
        }

        if (actual.Count > 0) lista.Add(ArmarBloqueLineas(actual));
        return lista;
    }

    private static BloqueLineas ArmarBloqueLineas(List<XYZ> puntos)
    {
        int nv = puntos.Count, nl = nv / 2;
        var vb = new VertexBuffer(nv * VertexPositionColored.GetSizeInFloats());
        vb.Map(nv * VertexPositionColored.GetSizeInFloats());
        VertexStreamPositionColored vs = vb.GetVertexStreamPositionColored();
        foreach (XYZ p in puntos)
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

    /// <summary>Junta las mallas en bloques de hasta <see cref="VerticesPorBloque"/> vértices.</summary>
    private List<Bloque> Armar()
    {
        var lista = new List<Bloque>();
        var actual = new List<Malla>();
        int enActual = 0;
        foreach (Malla m in mallas.Where(m => m.Vertices.Count > 0))
        {
            if (enActual + m.Vertices.Count > VerticesPorBloque && actual.Count > 0)
            {
                lista.Add(ArmarBloque(actual));
                (actual, enActual) = (new List<Malla>(), 0);
            }

            actual.Add(m);
            enActual += m.Vertices.Count;
        }

        if (actual.Count > 0) lista.Add(ArmarBloque(actual));
        return lista;
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
    public string GetName() => "CQEING Columnas: vista previa del armado";
    public string GetVendorId() => "CQEING";
    public string GetDescription() => "Dibuja el armado de la columna en la vista 3D del add-in, sin crearlo en el modelo.";
    public string GetApplicationId() => "";
    public string GetSourceId() => "";
    public bool UsesHandles() => false;
    public bool UseInTransparentPass(View view) => false;

    public bool CanExecute(View view) =>
        documento is not null && caja is not null && view is View3D && view.Name == NombreVista && view.Document.Equals(documento);

    public Outline? GetBoundingBox(View view) => caja;

    public void RenderScene(View view, DisplayStyle displayStyle)
    {
        try
        {
            if (caja is null) return;
            bool primera = bloques is null;
            if (primera) Bitacora3D.Anotar($"dibujar: armando bloques de {mallas.Count} mallas");
            bloques ??= Armar();
            if (primera) Bitacora3D.Anotar($"dibujar: {bloques.Count} bloques, {bloques.Sum(b => b.CantidadVertices)} vértices");
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
            if (primera) Bitacora3D.Anotar($"dibujar: listo, contorno en {bloquesContorno.Count} bloques");
        }
        catch (Exception ex)
        {
            Bitacora3D.Anotar($"dibujar: EXCEPCIÓN {ex.Message}");
            // Revit llama desde su lado nativo: una excepción aquí lo podría tumbar. Sin dibujo, la vista sigue igual.
        }
    }
}
