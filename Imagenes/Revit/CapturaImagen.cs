using System.IO;
using Autodesk.Revit.DB;
using practicacq.Imagenes.Modelo;

namespace practicacq.Imagenes.Revit;

/// <summary>
/// Genera el PNG de una imagen por actividad haciendo de forma automática lo que hoy se hace a mano
/// en Revit: copiar la vista, pintar los elementos de la actividad, dejar el resto como "fantasma",
/// limpiar lo que estorba y exportar.
///
/// La receta (orden y valores) se validó contra Revit real en la fase 0, sobre
/// <c>Prueba 080720sfsd26.rvt</c>: ver "Resultados de la fase 0" en
/// <c>docs/planes/2026-10-13-addin-imagenes-por-actividad.md</c> y la imagen
/// <c>docs/planes/imagenes/2026-10-13-fase0-resultado.png</c>.
///
/// La vista del usuario nunca se toca: todo se hace sobre una copia dentro de un
/// <see cref="TransactionGroup"/> que al final se deshace (<c>RollBack</c>), así que no queda la vista
/// temporal ni una entrada en "Deshacer". Si el RollBack fallara, se borra la copia a mano.
/// </summary>
public static class CapturaImagen
{
    /// <summary>Gris del "fantasma" (relleno) y de sus líneas. Validados en la fase 0.</summary>
    private static readonly Color GrisFantasma = new(200, 200, 200);
    private static readonly Color GrisLineasFantasma = new(160, 160, 160);

    /// <summary>
    /// Transparencia automática del resto del modelo. Fija, sin control para el usuario (issue #183,
    /// paso 5).
    /// </summary>
    private const int TransparenciaFantasma = 70;

    /// <summary>Terreno y entorno: en la fase 0 el Toposolid tapaba la estructura y la achicaba.</summary>
    private static readonly BuiltInCategory[] CategoriasOcultas =
    {
        BuiltInCategory.OST_Toposolid,
        BuiltInCategory.OST_Topography,
        BuiltInCategory.OST_Site,
        BuiltInCategory.OST_Planting,
        BuiltInCategory.OST_Entourage,
        BuiltInCategory.OST_Cameras,
    };

    public static bool EsVistaSoportada(View vista, out string tipoVista)
    {
        tipoVista = vista switch
        {
            View3D { IsTemplate: false } => "3D",
            ViewPlan { IsTemplate: false } => "Planta",
            _ => string.Empty,
        };
        return tipoVista.Length > 0;
    }

    /// <summary>
    /// Exporta la vista activa con los colores de <paramref name="seleccion"/> y devuelve la ruta del
    /// PNG generado. Con <paramref name="camara"/> (al actualizar una imagen, issue #186) la copia toma
    /// esa orientación, caja de sección o recorte en vez de los que tenga hoy la vista base.
    /// </summary>
    public static string Exportar(Document doc, View vistaBase, ResultadoSeleccion seleccion, string rutaSinExtension,
        CamaraVista? camara = null)
    {
        if (!EsVistaSoportada(vistaBase, out _))
            throw new InvalidOperationException("Abre una vista 3D o de planta para generar la imagen.");

        Dictionary<long, Color> colorPorElemento = seleccion.Tipos
            .SelectMany(t => t.IdsElementos.Select(id => (id, color: ColorDesdeHex(t.ColorHex))))
            .ToDictionary(x => x.id, x => x.color);

        string carpeta = Path.GetDirectoryName(rutaSinExtension)!;
        string prefijo = Path.GetFileName(rutaSinExtension);
        string marca = "CQ_Imagen_" + Guid.NewGuid().ToString("N")[..8];
        string rutaExport = Path.Combine(Path.GetTempPath(), marca);

        using var grupo = new TransactionGroup(doc, "CQEING · imagen por actividad");
        grupo.Start();
        View? copia = null;
        try
        {
            using (var t = new Transaction(doc, "Preparar vista para la imagen"))
            {
                t.Start();
                copia = PrepararCopia(doc, vistaBase, marca, colorPorElemento, camara);
                t.Commit();
            }

            var opciones = new ImageExportOptions
            {
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = 2048,
                FilePath = rutaExport,
                ImageResolution = ImageResolution.DPI_300,
                ExportRange = ExportRange.SetOfViews,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
            };
            opciones.SetViewsAndSheets(new List<ElementId> { copia.Id });

            // El fondo de las vistas que no son 3D (planta) sale del fondo de Revit del usuario: con el
            // tema oscuro, la planta se exportaba sobre gris oscuro (visto en Revit real el 2026-10-14,
            // issue #186). Se pone blanco solo durante la exportación y se devuelve el del usuario.
            Color fondoUsuario = doc.Application.BackgroundColor;
            try
            {
                doc.Application.BackgroundColor = new Color(255, 255, 255);
                doc.ExportImage(opciones);
            }
            finally
            {
                doc.Application.BackgroundColor = fondoUsuario;
            }
        }
        finally
        {
            DeshacerCopia(doc, grupo, copia);
        }

        // Revit le agrega el nombre de la vista al archivo ("CQ_Imagen_xxx - 3D View - CQ_Imagen_xxx.png").
        // Por eso se exporta a %TEMP% con un nombre único y se mueve después.
        string? exportado = Directory.GetFiles(Path.GetTempPath(), marca + "*.png").FirstOrDefault();
        if (exportado is null)
            throw new InvalidOperationException("Revit no generó el archivo de la imagen.");

        Directory.CreateDirectory(carpeta);
        string destino = Path.Combine(carpeta, prefijo + ".png");
        File.Move(exportado, destino, overwrite: true);
        return destino;
    }

    private static View PrepararCopia(Document doc, View vistaBase, string nombre, Dictionary<long, Color> colorPorElemento,
        CamaraVista? camara)
    {
        var copia = (View)doc.GetElement(vistaBase.Duplicate(ViewDuplicateOption.Duplicate));

        // Nombre propio y seguro: en modelos colaborativos la vista se llama "{3D - usuario}" y una
        // "\" en el usuario rompe la ruta del PNG (mismo problema que RevitImageCaptureService.cs:138-142
        // del add-in de cantidades).
        copia.Name = nombre;

        // Una plantilla de vista bloquearía el estilo visual y los gráficos.
        copia.ViewTemplateId = ElementId.InvalidElementId;

        // 1. Estilo y detalle fijos, para que todas las imágenes salgan iguales.
        copia.DisplayStyle = DisplayStyle.ShadingWithEdges;
        copia.DetailLevel = ViewDetailLevel.Fine;

        // 2. Fondo blanco: si no, sale el fondo del tema de Revit del usuario (oscuro, en la fase 0).
        //    Solo existe para vistas 3D; en planta se cambia el fondo de Revit al exportar (ver Exportar).
        if (copia is View3D)
        {
            var blanco = new Color(255, 255, 255);
            copia.SetBackground(ViewDisplayBackground.CreateGradient(blanco, blanco, blanco));
        }

        // 3. Sin niveles, rejillas, cotas ni etiquetas.
        copia.AreAnnotationCategoriesHidden = true;

        // 4. Sin terreno ni entorno.
        foreach (BuiltInCategory bic in CategoriasOcultas)
        {
            var idCategoria = new ElementId(bic);
            try
            {
                if (copia.CanCategoryBeHidden(idCategoria)) copia.SetCategoryHidden(idCategoria, true);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // La categoría no existe en este modelo/plantilla: no hay nada que ocultar.
            }
        }

        ElementId relleno = RellenoSolido(doc);
        OverrideGraphicSettings fantasma = AjustesFantasma(relleno);

        // 5 y 6. Fantasma ELEMENTO POR ELEMENTO, nunca por categoría (hallazgo de la fase 0): en
        //    OverrideGraphicSettings un valor de fábrica (transparencia 0, sin halftone) significa
        //    "no sobrescribir", así que un elemento coloreado heredaría el 70 % de su categoría y
        //    saldría translúcido. Por eso los seleccionados solo llevan color y el fantasma va a cada
        //    uno de los demás.
        // Con cámara guardada, lo visible puede cambiar (otra caja de sección o recorte): se recorre la
        // copia ya regenerada. Sin cámara, la copia es igual a la vista base (receta de la fase 0).
        View vistaElementos = vistaBase;
        if (camara is not null)
        {
            AplicarCamara(copia, camara);
            doc.Regenerate();
            vistaElementos = copia;
        }

        foreach (Element elemento in new FilteredElementCollector(doc, vistaElementos.Id).WhereElementIsNotElementType())
        {
            if (elemento.Category?.CategoryType != CategoryType.Model) continue;
            try
            {
                copia.SetElementOverrides(
                    elemento.Id,
                    colorPorElemento.TryGetValue(elemento.Id.Value, out Color? color) && color is not null
                        ? AjustesColor(relleno, color)
                        : fantasma);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Algunos elementos internos no admiten sobrescritura gráfica; se dejan como están.
            }
        }

        return copia;
    }

    /// <summary>Lee la cámara (3D) o el recorte (planta) de una vista, para guardarlos con la imagen.</summary>
    public static CamaraVista CamaraDe(View vista)
    {
        var camara = new CamaraVista();
        if (vista is View3D v3)
        {
            ViewOrientation3D o = v3.GetOrientation();
            camara.Ojo = APuntos(o.EyePosition);
            camara.Adelante = APuntos(o.ForwardDirection);
            camara.Arriba = APuntos(o.UpDirection);
            if (v3.IsSectionBoxActive) camara.CajaSeccion = ACaja(v3.GetSectionBox());
        }
        else if (vista.CropBoxActive)
        {
            camara.Recorte = ACaja(vista.CropBox);
        }
        return camara;
    }

    private static void AplicarCamara(View copia, CamaraVista camara)
    {
        if (copia is View3D v3)
        {
            if (camara.TieneOrientacion3D)
                v3.SetOrientation(new ViewOrientation3D(AXyz(camara.Ojo), AXyz(camara.Arriba), AXyz(camara.Adelante)));

            if (camara.CajaSeccion is not null)
            {
                v3.IsSectionBoxActive = true;
                v3.SetSectionBox(DesdeCaja(camara.CajaSeccion));
            }
            else
            {
                v3.IsSectionBoxActive = false;
            }
        }
        else if (camara.Recorte is not null)
        {
            copia.CropBox = DesdeCaja(camara.Recorte);
            copia.CropBoxActive = true;
        }
        else
        {
            copia.CropBoxActive = false;
        }
    }

    private static double[] APuntos(XYZ p) => new[] { p.X, p.Y, p.Z };

    private static XYZ AXyz(double[] p) => new(p[0], p[1], p[2]);

    private static CajaVista ACaja(BoundingBoxXYZ caja) => new()
    {
        Min = APuntos(caja.Min),
        Max = APuntos(caja.Max),
        Origen = APuntos(caja.Transform.Origin),
        EjeX = APuntos(caja.Transform.BasisX),
        EjeY = APuntos(caja.Transform.BasisY),
        EjeZ = APuntos(caja.Transform.BasisZ),
    };

    private static BoundingBoxXYZ DesdeCaja(CajaVista caja)
    {
        Transform t = Transform.Identity;
        t.Origin = AXyz(caja.Origen);
        t.BasisX = AXyz(caja.EjeX);
        t.BasisY = AXyz(caja.EjeY);
        t.BasisZ = AXyz(caja.EjeZ);
        return new BoundingBoxXYZ { Min = AXyz(caja.Min), Max = AXyz(caja.Max), Transform = t };
    }

    private static OverrideGraphicSettings AjustesFantasma(ElementId relleno)
    {
        var a = new OverrideGraphicSettings();
        a.SetHalftone(true);
        a.SetSurfaceTransparency(TransparenciaFantasma);
        a.SetSurfaceForegroundPatternId(relleno);
        a.SetSurfaceForegroundPatternColor(GrisFantasma);
        a.SetProjectionLineColor(GrisLineasFantasma);
        a.SetCutLineColor(GrisLineasFantasma);
        a.SetProjectionLineWeight(1);
        a.SetCutLineWeight(1);
        return a;
    }

    /// <summary>
    /// Relleno sólido en superficie (3D) y en corte (planta: las columnas se ven cortadas), con líneas
    /// del mismo tono más oscuro. No se toca transparencia ni halftone (ver paso 5).
    /// </summary>
    private static OverrideGraphicSettings AjustesColor(ElementId relleno, Color color)
    {
        var oscuro = new Color((byte)(color.Red * 0.4), (byte)(color.Green * 0.4), (byte)(color.Blue * 0.4));
        var a = new OverrideGraphicSettings();
        a.SetSurfaceForegroundPatternId(relleno);
        a.SetSurfaceForegroundPatternColor(color);
        a.SetCutForegroundPatternId(relleno);
        a.SetCutForegroundPatternColor(color);
        a.SetProjectionLineColor(oscuro);
        a.SetCutLineColor(oscuro);
        a.SetProjectionLineWeight(3);
        a.SetCutLineWeight(3);
        return a;
    }

    private static ElementId RellenoSolido(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(p => p.GetFillPattern().IsSolidFill)?.Id
        ?? throw new InvalidOperationException("El modelo no tiene un patrón de relleno sólido.");

    private static void DeshacerCopia(Document doc, TransactionGroup grupo, View? copia)
    {
        try
        {
            if (grupo.HasStarted()) grupo.RollBack();
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CapturaImagen] RollBack falló, se borra la copia a mano: {ex.Message}");
        }

        if (copia is not null && copia.IsValidObject)
        {
            using var t = new Transaction(doc, "Borrar vista temporal de la imagen");
            t.Start();
            doc.Delete(copia.Id);
            t.Commit();
        }
    }

    public static Color ColorDesdeHex(string hex)
    {
        string h = hex.TrimStart('#');
        if (h.Length != 6) throw new ArgumentException($"Color inválido: {hex}");
        return new Color(
            Convert.ToByte(h[..2], 16),
            Convert.ToByte(h.Substring(2, 2), 16),
            Convert.ToByte(h.Substring(4, 2), 16));
    }
}
