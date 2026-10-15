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

    public PuenteRevit(UIApplication app, string idModeloActivo, string nombreModeloActivo)
    {
        _app = app;
        IdModeloActivo = idModeloActivo;
        NombreModeloActivo = nombreModeloActivo;
    }

    public string IdModeloActivo { get; }
    public string NombreModeloActivo { get; }

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

                string ruta = SinSeleccion(app.ActiveUIDocument, () => CapturaImagen.Exportar(doc, vista, seleccion, rutaSinExtension));
                seleccion.Camara = CapturaImagen.CamaraDe(vista);
                alTerminar(ruta, null);
            }
            catch (Exception ex)
            {
                alTerminar(null, ex.Message);
            }
        });
    }

    public void Recapturar(ImagenActividad imagen, string rutaSinExtension, Action<ResultadoRecaptura?, string?> alTerminar)
    {
        EjecutorRevit.Ejecutar(app =>
        {
            try
            {
                Document doc = app.ActiveUIDocument?.Document
                    ?? throw new InvalidOperationException("No hay ningún modelo abierto en Revit.");
                if (IdentificadorModelo.Obtener(doc) != IdModeloActivo)
                    throw new InvalidOperationException("El modelo activo en Revit cambió. Vuelve al modelo donde abriste el add-in.");

                View vista = VistaParaRecapturar(doc, imagen);
                var (seleccion, faltantes) = ArmarSeleccionGuardada(doc, vista, imagen);
                if (seleccion.TotalElementos == 0)
                    throw new InvalidOperationException("Ninguno de los elementos de la imagen existe ya en el modelo.");

                string ruta = SinSeleccion(app.ActiveUIDocument, () => CapturaImagen.Exportar(doc, vista, seleccion, rutaSinExtension, imagen.Camara));
                alTerminar(new ResultadoRecaptura
                {
                    RutaPng = ruta,
                    Encontrados = seleccion.TotalElementos,
                    Faltantes = faltantes,
                    NombreVista = vista.Name,
                }, null);
            }
            catch (Exception ex)
            {
                alTerminar(null, ex.Message);
            }
        });
    }

    /// <summary>
    /// Exporta con la selección de Revit vacía y después la devuelve. Revit pinta el resaltado de
    /// selección (cian translúcido) en la imagen exportada y tapa los colores: visto en Revit real el
    /// 2026-10-14 (issue #186), con los elementos todavía seleccionados después de pulsar Aceptar.
    /// </summary>
    private static string SinSeleccion(UIDocument uidoc, Func<string> exportar)
    {
        ICollection<ElementId> seleccionUsuario = uidoc.Selection.GetElementIds();
        if (seleccionUsuario.Count == 0) return exportar();

        uidoc.Selection.SetElementIds(new List<ElementId>());
        try
        {
            return exportar();
        }
        finally
        {
            try { uidoc.Selection.SetElementIds(seleccionUsuario.Where(id => uidoc.Document.GetElement(id) is not null).ToList()); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PuenteRevit] No se pudo devolver la selección: {ex.Message}"); }
        }
    }

    /// <summary>
    /// La vista donde se hizo la imagen. Si la borraron, en 3D sirve cualquier vista 3D (se le aplica
    /// la cámara guardada); en planta no hay forma segura de elegir otra, así que se avisa.
    /// </summary>
    private static View VistaParaRecapturar(Document doc, ImagenActividad imagen)
    {
        if (doc.GetElement(imagen.UniqueIdVista) is View vista && CapturaImagen.EsVistaSoportada(vista, out _))
            return vista;

        if (imagen.TipoVista == "3D" && imagen.Camara?.TieneOrientacion3D == true)
        {
            View3D? otra = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && !v.IsPerspective);
            if (otra is not null) return otra;
        }

        throw new InvalidOperationException(
            $"La vista \"{imagen.NombreVistaRevit}\" ya no existe en el modelo. Rehaz la imagen con Nueva imagen.");
    }

    /// <summary>
    /// Vuelve a armar la selección desde lo guardado: cada grupo de color con sus elementos por
    /// <c>UniqueId</c>. Los que ya no existen se cuentan como faltantes. Con "mismo tipo" se suman los
    /// elementos visibles de ese tipo (decisión 5 del plan). Un elemento va en un solo grupo.
    /// </summary>
    private static (ResultadoSeleccion seleccion, int faltantes) ArmarSeleccionGuardada(Document doc, View vista, ImagenActividad imagen)
    {
        var usados = new HashSet<long>();
        int faltantes = 0;

        Dictionary<long, List<Element>> visiblesPorTipo = imagen.MismoTipo
            ? new FilteredElementCollector(doc, vista.Id).WhereElementIsNotElementType()
                .Where(EsDeModelo)
                .GroupBy(e => e.GetTypeId().Value)
                .ToDictionary(g => g.Key, g => g.ToList())
            : new();

        var tipos = new List<TipoSeleccionado>();
        foreach (ColorDeTipo grupo in imagen.Colores)
        {
            var elementos = new List<Element>();
            foreach (string uniqueId in grupo.UniqueIdsElementos)
            {
                Element? e = doc.GetElement(uniqueId);
                if (e is null || !EsDeModelo(e)) { faltantes++; continue; }
                if (usados.Add(e.Id.Value)) elementos.Add(e);
            }
            if (imagen.MismoTipo && visiblesPorTipo.TryGetValue(grupo.IdTipo, out List<Element>? delTipo))
            {
                elementos.AddRange(delTipo.Where(e => usados.Add(e.Id.Value)));
            }
            if (elementos.Count == 0) continue;

            tipos.Add(new TipoSeleccionado
            {
                IdTipo = grupo.IdTipo,
                Nombre = grupo.NombreTipo,
                ColorHex = grupo.ColorHex,
                IdsElementos = elementos.Select(e => e.Id.Value).ToList(),
                UniqueIdsElementos = elementos.Select(e => e.UniqueId).ToList(),
            });
        }

        CapturaImagen.EsVistaSoportada(vista, out string tipoVista);
        var seleccion = new ResultadoSeleccion
        {
            Tipos = tipos,
            IdVista = vista.Id.Value,
            NombreVista = vista.Name,
            UniqueIdVista = vista.UniqueId,
            TipoVista = tipoVista,
            MismoTipo = imagen.MismoTipo,
        };
        return (seleccion, faltantes);
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
                UniqueIdsElementos = g.Select(e => e.UniqueId).ToList(),
            })
            .OrderBy(t => t.Categoria).ThenBy(t => t.Nombre)
            .ToList();

        return new ResultadoSeleccion
        {
            Tipos = tipos,
            IdVista = vista.Id.Value,
            NombreVista = vista.Name,
            UniqueIdVista = vista.UniqueId,
            TipoVista = tipoVista,
            MismoTipo = _mismoTipo,
        };
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
