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

    /// <summary>Distinto del de vigas, para que los dos dibujos no se pisen.</summary>
    private static readonly Guid Id = new("C3A1F0D2-7B4E-4E59-9F1A-2D6B8E0C4A93");
    private static Tubos3D? servidor;

    /// <summary>Un tubo: su eje en pies (coordenadas del modelo), su radio en pies y su color.</summary>
    public sealed record Tubo(IReadOnlyList<XYZ> Puntos, double Radio, ColorWithTransparency Color);

    private Document? documento;
    private List<Malla> mallas = new();
    private List<Bloque>? bloques;
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

        s.documento = doc;
        s.mallas = lista.Select(Malla.De).ToList();
        s.caja = Caja(lista);
        s.bloques = null; // se arman en el próximo dibujo, en el hilo de dibujo
    }

    /// <summary>Quita lo dibujado (al cerrar la ventana o el documento). No registra el servidor si no estaba.</summary>
    public static void Quitar() => servidor?.Vaciar();

    private void Vaciar()
    {
        documento = null;
        mallas = new List<Malla>();
        caja = null;
        bloques = null;
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

    /// <summary>Los vértices y triángulos (índices locales) de un tubo.</summary>
    private sealed record Malla(List<Vertice> Vertices, List<(int A, int B, int C)> Triangulos)
    {
        /// <summary>
        /// Un cilindro de <see cref="Lados"/> lados por cada tramo recto del
        /// eje. Los tramos de los dobleces son cortos: así no hace falta unir
        /// los cilindros.
        /// </summary>
        public static Malla De(Tubo t)
        {
            var vertices = new List<Vertice>();
            var triangulos = new List<(int, int, int)>();
            for (int i = 0; i + 1 < t.Puntos.Count; i++)
            {
                XYZ a = t.Puntos[i], b = t.Puntos[i + 1];
                XYZ eje = b - a;
                if (eje.GetLength() < 1e-9) continue;

                eje = eje.Normalize();
                XYZ u = (Math.Abs(eje.Z) < 0.9 ? eje.CrossProduct(XYZ.BasisZ) : eje.CrossProduct(XYZ.BasisX)).Normalize();
                XYZ v = eje.CrossProduct(u);
                int base0 = vertices.Count;
                for (int k = 0; k < Lados; k++)
                {
                    double angulo = 2 * Math.PI * k / Lados;
                    XYZ normal = u * Math.Cos(angulo) + v * Math.Sin(angulo);
                    vertices.Add(new Vertice(a + normal * t.Radio, normal, t.Color));
                    vertices.Add(new Vertice(b + normal * t.Radio, normal, t.Color));
                }

                for (int k = 0; k < Lados; k++)
                {
                    int a0 = base0 + 2 * k, a1 = a0 + 1, b0 = base0 + 2 * ((k + 1) % Lados), b1 = b0 + 1;
                    triangulos.Add((a0, b0, a1));
                    triangulos.Add((a1, b0, b1));
                }
            }

            return new Malla(vertices, triangulos);
        }
    }

    /// <summary>Un búfer de Revit listo para dibujar.</summary>
    private sealed record Bloque(VertexBuffer Vertices, int CantidadVertices, IndexBuffer Indices, int CantidadTriangulos);

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
            if (primera) Bitacora3D.Anotar("dibujar: listo");
        }
        catch (Exception ex)
        {
            Bitacora3D.Anotar($"dibujar: EXCEPCIÓN {ex.Message}");
            // Revit llama desde su lado nativo: una excepción aquí lo podría tumbar. Sin dibujo, la vista sigue igual.
        }
    }
}
