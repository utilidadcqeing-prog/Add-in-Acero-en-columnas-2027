using Autodesk.Revit.UI;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace practicacq;
public class App : IExternalApplication
{
    /// <summary>Pestaña de la empresa en el ribbon. Confirmar la grafía exacta con William.</summary>
    private const string NombrePestana = "CQEING";

    public Result OnStartup(UIControlledApplication app)
    {
        try
        {
            app.CreateRibbonTab(NombrePestana);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Ya existe: pasa si el add-in se recarga en la misma sesión de
            // Revit, o si hay otro add-in de la empresa (por ejemplo, otra
            // rama de este mismo repo) que ya haya creado la pestaña "CQEING"
            // antes. No es un error real -- se sigue usando la que ya está.
        }

        string dllPath = Assembly.GetExecutingAssembly().Location;
        string folder = Path.GetDirectoryName(dllPath) ?? string.Empty;

        RibbonPanel panelEstructural = ObtenerOCrearPanel(app, "Estructural");

        panelEstructural.AddItem(CrearBoton(
            "ComandoEstribos",
            "Estribos\nde Columna",
            "practicacq.ComandoEstribos",
            "Genera el acero de estribos con espaciado uniforme en las columnas seleccionadas.",
            dllPath, folder, "icono"));

        RibbonPanel panelHerramientas = ObtenerOCrearPanel(app, "Herramientas");

        panelHerramientas.AddItem(CrearBoton(
            "Comando",
            "Columnetas\ny Viguetas",
            "practicacq.Comando",
            "Genera columnetas y viguetas de confinamiento en los muros seleccionados.",
            dllPath, folder, "iconoCQ"));

        // TEMPORAL -- ver comentario de ComandoDiagnosticoRebar.cs. Sacar
        // este botón (y el comando) una vez confirmado el corrimiento lateral
        // real del traslapo.
        panelHerramientas.AddItem(CrearBoton(
            "ComandoDiagnosticoRebar",
            "Diagnóstico\nde Rebar",
            "practicacq.ComandoDiagnosticoRebar",
            "TEMPORAL: selecciona barras y muestra las coordenadas de su geometría, para medir el corrimiento real de un traslapo.",
            dllPath, folder, "iconoCQ"));

        // TEMPORAL -- ver comentario de ComandoDiagnosticoSplice en
        // ComandoDiagnosticoRebar.cs. Sacar este botón (y el comando) una vez
        // decidido si se migra a RebarSpliceUtils.
        panelHerramientas.AddItem(CrearBoton(
            "ComandoDiagnosticoSplice",
            "Diagnóstico\nde Empalme",
            "practicacq.ComandoDiagnosticoSplice",
            "TEMPORAL: prueba la API real de empalmes de Revit (RebarSpliceUtils) sobre una barra seleccionada, sin dejar cambios permanentes.",
            dllPath, folder, "iconoCQ"));

        // Issue #183 del repo de cantidades: imágenes por actividad. PROVISIONAL en este add-in --
        // ver el comentario de Imagenes/ComandoImagenesActividad.cs. Si se mueve a otro proyecto,
        // se lleva la carpeta Imagenes/ y se quita este panel.
        RibbonPanel panelImagenes = ObtenerOCrearPanel(app, "Imágenes");

        panelImagenes.AddItem(CrearBoton(
            "ComandoImagenesActividad",
            "Imágenes\npor actividad",
            "practicacq.Imagenes.ComandoImagenesActividad",
            "Crea imágenes de los elementos de cada actividad del presupuesto: selecciona en Revit, elige colores y guarda con el código de la actividad.",
            dllPath, folder, "iconoCQ"));

        return Result.Succeeded;
    }

    /// <summary>
    /// Mismo motivo que el try/catch de arriba: si otro add-in ya creó un
    /// panel con este nombre en la pestaña CQEING, <c>CreateRibbonPanel</c>
    /// tira ArgumentException en vez de devolver el existente.
    /// </summary>
    private static RibbonPanel ObtenerOCrearPanel(UIControlledApplication app, string nombre) =>
        app.GetRibbonPanels(NombrePestana).FirstOrDefault(p => p.Name == nombre)
        ?? app.CreateRibbonPanel(NombrePestana, nombre);

    public Result OnShutdown(UIControlledApplication app)
    {
        return Result.Succeeded;
    }

    private PushButtonData CrearBoton(
        string nombre, string texto, string clase, string ayuda, string dllPath, string folder, string iconoBase)
    {
        var boton = new PushButtonData(nombre, texto, dllPath, clase) { ToolTip = ayuda };

        boton.LargeImage = CargarIcono(Path.Combine(folder, "Resources", $"{iconoBase}32.png"));
        boton.Image = CargarIcono(Path.Combine(folder, "Resources", $"{iconoBase}16.png"));

        return boton;
    }

    private BitmapImage CargarIcono(string rutaArchivo)
    {
        var imagen = new BitmapImage();
        imagen.BeginInit();
        imagen.UriSource = new Uri(rutaArchivo, UriKind.Absolute);
        imagen.CacheOption = BitmapCacheOption.OnLoad;
        imagen.EndInit();
        return imagen;
    }
}
