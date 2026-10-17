using System.Collections.Concurrent;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Tablero;

/// <summary>
/// Lo que el tablero (#83) hace en Revit, en el hilo de Revit: la ventana no tiene modo, así que todo pasa por un
/// <see cref="ExternalEvent"/>. Es una cola de trabajos: los mensajes de la página (la sesión lee el modelo) y las
/// <see cref="AccionRevit"/> sobre las vigas de un pórtico. Se crea dentro de un comando, donde la API es válida.
/// </summary>
internal sealed class AccionesRevit : IExternalEventHandler, IDisposable
{
    /// <summary>La vista 3D del botón "3D" (decisión 11 del plan): se crea la primera vez y se reutiliza.</summary>
    public const string Nombre3d = "CQEING Vigas 3D";

    /// <summary>Margen de la caja de sección alrededor de las vigas, en pies (0.5 m).</summary>
    private const double Margen = 0.5 / 0.3048;

    private readonly ConcurrentQueue<Action<UIApplication>> cola = new();
    private readonly ExternalEvent evento;

    public AccionesRevit() => evento = ExternalEvent.Create(this);

    /// <summary>Un trabajo que falló, con su mensaje, para mostrarlo en la página.</summary>
    public event Action<string>? Fallo;

    /// <summary>Corre <paramref name="trabajo"/> en el hilo de Revit, cuando Revit quede libre.</summary>
    public void Encolar(Action<UIApplication> trabajo)
    {
        cola.Enqueue(trabajo);
        evento.Raise();
    }

    public void Execute(UIApplication app)
    {
        while (cola.TryDequeue(out Action<UIApplication>? trabajo))
        {
            // Nada se escapa hacia Revit: un error se muestra en la página y la cola sigue.
            try
            {
                trabajo(app);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Fallo?.Invoke(e.Message);
            }
        }
    }

    public string GetName() => "Tablero de vigas";

    public void Dispose() => evento.Dispose();

    /// <summary>
    /// Hace <paramref name="accion"/> sobre <paramref name="vigas"/>. Ya corre en el hilo de Revit (dentro de un trabajo).
    /// <see cref="AccionRevit.PedirSeleccion"/> devuelve las vigas que eligió el usuario; las demás, ninguna.
    /// </summary>
    public static IReadOnlyList<string> Hacer(UIApplication app, AccionRevit accion, IReadOnlyList<string> vigas)
    {
        UIDocument? uidoc = app.ActiveUIDocument;
        if (uidoc is null)
            return [];
        List<ElementId> ids = vigas.Select(v => new ElementId(long.Parse(v))).Where(id => uidoc.Document.GetElement(id) is not null).ToList();
        switch (accion)
        {
            case AccionRevit.Seleccionar:
                uidoc.Selection.SetElementIds(ids);
                break;
            case AccionRevit.Mostrar:
                uidoc.Selection.SetElementIds(ids);
                if (ids.Count > 0)
                    uidoc.ShowElements(ids);
                break;
            case AccionRevit.Abrir3d:
                if (ids.Count > 0)
                    Abrir3d(uidoc, ids);
                break;
            case AccionRevit.PedirSeleccion:
                return uidoc.Selection.PickObjects(ObjectType.Element, new SoloVigas(), "Selecciona vigas del pórtico y pulsa Terminar")
                    .Select(r => r.ElementId.ToString()).ToList();
        }
        return [];
    }

    /// <summary>
    /// La vista <see cref="Nombre3d"/>, aislada temporalmente en las vigas y con la caja de sección ajustada a ellas, en
    /// una transacción corta (decisión 11 del plan).
    /// </summary>
    private static void Abrir3d(UIDocument uidoc, List<ElementId> ids)
    {
        Document doc = uidoc.Document;
        View3D? vista = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
            .FirstOrDefault(v => !v.IsTemplate && v.Name == Nombre3d);
        using (var t = new Transaction(doc, "Tablero de vigas: 3D"))
        {
            t.Start();
            if (vista is null)
            {
                ElementId tipo = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .First(x => x.ViewFamily == ViewFamily.ThreeDimensional).Id;
                vista = View3D.CreateIsometric(doc, tipo);
                vista.Name = Nombre3d;
            }
            if (vista.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
                vista.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
            vista.IsolateElementsTemporary(ids);
            if (Caja(doc, ids) is BoundingBoxXYZ caja)
            {
                vista.SetSectionBox(caja);
                vista.IsSectionBoxActive = true;
            }
            t.Commit();
        }
        uidoc.ActiveView = vista;
    }

    /// <summary>La caja que encierra las vigas, con <see cref="Margen"/> a cada lado.</summary>
    private static BoundingBoxXYZ? Caja(Document doc, IEnumerable<ElementId> ids)
    {
        List<BoundingBoxXYZ> cajas = ids.Select(id => doc.GetElement(id)?.get_BoundingBox(null)).OfType<BoundingBoxXYZ>().ToList();
        if (cajas.Count == 0)
            return null;
        return new BoundingBoxXYZ
        {
            Min = new XYZ(cajas.Min(c => c.Min.X) - Margen, cajas.Min(c => c.Min.Y) - Margen, cajas.Min(c => c.Min.Z) - Margen),
            Max = new XYZ(cajas.Max(c => c.Max.X) + Margen, cajas.Max(c => c.Max.Y) + Margen, cajas.Max(c => c.Max.Z) + Margen),
        };
    }

    /// <summary>Solo deja elegir vigas estructurales.</summary>
    private sealed class SoloVigas : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem.Category?.BuiltInCategory == BuiltInCategory.OST_StructuralFraming;

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
