using System.Collections.Concurrent;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
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

    /// <summary>Transparencia de vigas y apoyos en la 3D, en %: el acero se ve a través del concreto.</summary>
    private const int Transparencia = 90;

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
    /// <see cref="AccionRevit.PedirSeleccion"/> no se hace aquí: la atiende <see cref="PuenteTablero"/> con su barra.
    /// </summary>
    public static void Hacer(UIApplication app, AccionRevit accion, IReadOnlyList<string> vigas)
    {
        UIDocument? uidoc = app.ActiveUIDocument;
        if (uidoc is null)
            return;
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
        }
    }

    /// <summary>
    /// La vista <see cref="Nombre3d"/>, con solo las vigas y en los apoyos que entran en la caja de
    /// sección, ajustada a ellas, en una transacción corta (decisión 11 del plan). Todo al <see cref="Transparencia"/> %
    /// y sombreado, para que el acero se vea (Juan, 20-oct). Al abrirla se acerca a las vigas: si no, queda con el zoom
    /// de todo el modelo y las vigas se ven diminutas (Juan, 16-oct: "lo deja super lejos").
    /// </summary>
    private static void Abrir3d(UIDocument uidoc, List<ElementId> vigas)
    {
        Document doc = uidoc.Document;
        View3D? vista = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
            .FirstOrDefault(v => !v.IsTemplate && v.Name == Nombre3d);
        BoundingBoxXYZ? caja = Caja(doc, vigas);
        List<ElementId> ids = caja is null ? vigas : vigas.Union(Apoyos(doc, caja)).ToList();
        var transparente = new OverrideGraphicSettings();
        transparente.SetSurfaceTransparency(Transparencia);
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
            // Sombreado: en alambre Revit no pinta superficies, y el acero (la vista previa y las barras con filtro de color
            // por diámetro) se vería sin color.
            if (vista.DisplayStyle != DisplayStyle.Shading)
                vista.DisplayStyle = DisplayStyle.Shading;
            if (vista.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate))
                vista.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
            Aislar(doc, vista, ids);
            foreach (ElementId id in ids)
                vista.SetElementOverrides(id, transparente);
            if (caja is not null)
            {
                vista.SetSectionBox(caja);
                vista.IsSectionBoxActive = true;
            }
            t.Commit();
        }
        uidoc.ActiveView = vista;
        if (caja is not null && uidoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == vista.Id) is UIView abierta)
            abierta.ZoomAndCenterRectangle(caja.Min, caja.Max);
    }

    /// <summary>
    /// Deja visibles en <paramref name="vista"/> solo <paramref name="ids"/>. Oculta de verdad, no con el aislamiento
    /// temporal: con él seguían saliendo los armazones que cruzan la caja (Juan, 20-oct). La vista es del add-in, así
    /// que primero se muestra lo que ocultó la vez anterior.
    /// </summary>
    private static void Aislar(Document doc, View3D vista, List<ElementId> ids)
    {
        List<ElementId> ocultos = new FilteredElementCollector(doc).WhereElementIsNotElementType()
            .Where(e => e.IsHidden(vista)).Select(e => e.Id).ToList();
        if (ocultos.Count > 0)
            vista.UnhideElements(ocultos);
        var quedan = ids.ToHashSet();
        List<ElementId> sobran = new FilteredElementCollector(doc, vista.Id).WhereElementIsNotElementType()
            .Where(e => !quedan.Contains(e.Id) && e.CanBeHidden(vista)).Select(e => e.Id).ToList();
        if (sobran.Count > 0)
            vista.HideElements(sobran);
    }

    /// <summary>
    /// Columnas y muros de carga que tocan la caja: los mismos apoyos que lee <see cref="Revit.LectorModelo"/>. La caja
    /// los corta, así que en la 3D se ven como tramos cortos alrededor de las vigas.
    /// </summary>
    private static IEnumerable<ElementId> Apoyos(Document doc, BoundingBoxXYZ caja)
    {
        var enLaCaja = new BoundingBoxIntersectsFilter(new Outline(caja.Min, caja.Max));
        IEnumerable<ElementId> columnas = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns)
            .WhereElementIsNotElementType().WherePasses(enLaCaja).ToElementIds();
        IEnumerable<ElementId> muros = new FilteredElementCollector(doc).OfClass(typeof(Wall)).WherePasses(enLaCaja).Cast<Wall>()
            .Where(m => m.StructuralUsage != Autodesk.Revit.DB.Structure.StructuralWallUsage.NonBearing).Select(m => m.Id);
        return columnas.Concat(muros);
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
}
