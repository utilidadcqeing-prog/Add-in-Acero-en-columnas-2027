using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using practicacq.Imagenes.Modelo;

namespace practicacq.Imagenes.Revit;

/// <summary>
/// Implementación real de <see cref="IPuenteRevit"/>. Todo pedido de la ventana pasa por
/// <see cref="EjecutorRevit"/> (contexto de API válido) y la respuesta vuelve por callback en el mismo
/// hilo de la ventana.
///
/// Selección (fase 0, punto 1): el usuario selecciona con las herramientas normales de Revit y al
/// pulsar Aceptar se lee <c>Selection.GetElementIds()</c>. No se usa <c>PickObjects</c>: bloquea Revit
/// hasta terminar y no deja orbitar ni acomodar la vista a mitad de la selección.
/// </summary>
public sealed class PuenteRevit : IPuenteRevit
{
    private readonly UIApplication _app;
    private bool _mismoTipo;
    private bool _escuchando;
    private Action<int>? _alCambiarCantidad;

    public PuenteRevit(UIApplication app, string idModeloActivo)
    {
        _app = app;
        IdModeloActivo = idModeloActivo;
    }

    public string IdModeloActivo { get; }

    public void IniciarSeleccion(bool mismoTipo, Action<int> alCambiarCantidad)
    {
        _mismoTipo = mismoTipo;
        _alCambiarCantidad = alCambiarCantidad;
        EjecutorRevit.Ejecutar(app =>
        {
            if (!_escuchando)
            {
                app.SelectionChanged += AlCambiarSeleccion;
                _escuchando = true;
            }
            ActualizarSeleccion(app.ActiveUIDocument);
        });
    }

    public void CambiarMismoTipo(bool mismoTipo)
    {
        _mismoTipo = mismoTipo;
        EjecutorRevit.Ejecutar(app => ActualizarSeleccion(app.ActiveUIDocument));
    }

    public void TerminarSeleccion(Action<ResultadoSeleccion?, string?> alTerminar)
    {
        EjecutorRevit.Ejecutar(app =>
        {
            DejarDeEscuchar(app);
            try
            {
                alTerminar(LeerSeleccion(app.ActiveUIDocument), null);
            }
            catch (Exception ex)
            {
                alTerminar(null, ex.Message);
            }
        });
    }

    public void CancelarSeleccion() => EjecutorRevit.Ejecutar(DejarDeEscuchar);

    public void Capturar(ResultadoSeleccion seleccion, string rutaSinExtension, Action<string?, string?> alTerminar)
    {
        EjecutorRevit.Ejecutar(app =>
        {
            try
            {
                Document doc = app.ActiveUIDocument?.Document
                    ?? throw new InvalidOperationException("No hay ningún modelo abierto en Revit.");
                if (IdentificadorModelo.Obtener(doc) != IdModeloActivo)
                    throw new InvalidOperationException("El modelo activo en Revit cambió. Vuelve al modelo donde abriste el add-in.");

                var vista = doc.GetElement(new ElementId(seleccion.IdVista)) as View
                    ?? throw new InvalidOperationException($"La vista \"{seleccion.NombreVista}\" ya no existe.");

                alTerminar(CapturaImagen.Exportar(doc, vista, seleccion, rutaSinExtension), null);
            }
            catch (Exception ex)
            {
                alTerminar(null, ex.Message);
            }
        });
    }

    private void AlCambiarSeleccion(object? sender, SelectionChangedEventArgs e)
    {
        // Lo invoca Revit directamente: una excepción sin atrapar aquí puede tirar abajo Revit
        // entero (mismo motivo que en ComandoEstribos.AlCambiarSeleccionEnRevit).
        try
        {
            ActualizarSeleccion((sender as UIApplication)?.ActiveUIDocument ?? _app.ActiveUIDocument);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PuenteRevit] {ex}");
        }
    }

    /// <summary>
    /// Con "mismo tipo", amplía la selección de Revit a todos los elementos visibles en la vista con
    /// el mismo tipo, para que el usuario VEA lo que va a salir en la imagen. Al ampliarla, Revit
    /// vuelve a disparar SelectionChanged; la segunda vez la selección ya está completa y no cambia
    /// nada, así que no hay bucle.
    /// </summary>
    private void ActualizarSeleccion(UIDocument? uidoc)
    {
        if (uidoc is null) return;

        ICollection<ElementId> actuales = uidoc.Selection.GetElementIds();
        List<ElementId> efectivos = _mismoTipo ? AmpliarAlMismoTipo(uidoc.Document, uidoc.ActiveView, actuales) : actuales.ToList();

        if (efectivos.Count != actuales.Count)
        {
            try
            {
                uidoc.Selection.SetElementIds(efectivos);
            }
            catch (Exception ex)
            {
                // Si Revit no deja cambiar la selección desde aquí, igual se amplía al pulsar Aceptar.
                System.Diagnostics.Debug.WriteLine($"[PuenteRevit] No se pudo ampliar la selección: {ex.Message}");
            }
        }

        _alCambiarCantidad?.Invoke(efectivos.Count(id => EsDeModelo(uidoc.Document.GetElement(id))));
    }

    private ResultadoSeleccion LeerSeleccion(UIDocument? uidoc)
    {
        if (uidoc is null) throw new InvalidOperationException("No hay ningún modelo abierto en Revit.");

        View vista = uidoc.ActiveView;
        if (!CapturaImagen.EsVistaSoportada(vista, out string tipoVista))
            throw new InvalidOperationException($"La vista \"{vista.Name}\" no sirve: abre una vista 3D o de planta y vuelve a seleccionar.");

        Document doc = uidoc.Document;
        ICollection<ElementId> seleccion = uidoc.Selection.GetElementIds();
        List<ElementId> ids = _mismoTipo ? AmpliarAlMismoTipo(doc, vista, seleccion) : seleccion.ToList();

        List<Element> elementos = ids.Select(doc.GetElement).Where(EsDeModelo).ToList();
        if (elementos.Count == 0)
            throw new InvalidOperationException("No hay elementos del modelo seleccionados. Selecciona en Revit y pulsa Aceptar.");

        List<TipoSeleccionado> tipos = elementos
            .GroupBy(e => e.GetTypeId().Value)
            .Select(g => new TipoSeleccionado
            {
                IdTipo = g.Key,
                Categoria = g.First().Category?.Name ?? string.Empty,
                Nombre = NombreTipo(doc, g.First()),
                IdsElementos = g.Select(e => e.Id.Value).ToList(),
            })
            .OrderBy(t => t.Categoria).ThenBy(t => t.Nombre)
            .ToList();

        return new ResultadoSeleccion { Tipos = tipos, IdVista = vista.Id.Value, NombreVista = vista.Name, TipoVista = tipoVista };
    }

    /// <summary>"Mismo tipo" = todos los elementos con el mismo <c>GetTypeId()</c> visibles en la vista.</summary>
    private static List<ElementId> AmpliarAlMismoTipo(Document doc, View vista, ICollection<ElementId> seleccion)
    {
        var tipos = new HashSet<long>(seleccion
            .Select(doc.GetElement)
            .Where(EsDeModelo)
            .Select(e => e!.GetTypeId().Value)
            .Where(id => id != ElementId.InvalidElementId.Value));
        if (tipos.Count == 0) return seleccion.ToList();

        var resultado = new HashSet<ElementId>(seleccion);
        foreach (Element e in new FilteredElementCollector(doc, vista.Id).WhereElementIsNotElementType())
        {
            if (tipos.Contains(e.GetTypeId().Value)) resultado.Add(e.Id);
        }
        return resultado.ToList();
    }

    private static bool EsDeModelo(Element? e) => e?.Category?.CategoryType == CategoryType.Model;

    private static string NombreTipo(Document doc, Element e) =>
        doc.GetElement(e.GetTypeId()) is ElementType tipo
            ? $"{tipo.FamilyName} : {tipo.Name}"
            : e.Name;

    private void DejarDeEscuchar(UIApplication app)
    {
        _alCambiarCantidad = null;
        if (!_escuchando) return;
        try { app.SelectionChanged -= AlCambiarSeleccion; }
        catch { /* Revit cerrando: no hay nada que desuscribir. */ }
        _escuchando = false;
    }
}
