using Autodesk.Revit.UI;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace practicacq;
public class App : IExternalApplication
{
    /// <summary>Pestaña de la empresa en el ribbon. Confirmar la grafía exacta con William.</summary>
    private const string NombrePestana = "CQEING";

    public Result OnStartup(UIControlledApplication app)
    {
        app.CreateRibbonTab(NombrePestana);

        string dllPath = Assembly.GetExecutingAssembly().Location;
        string folder = Path.GetDirectoryName(dllPath) ?? string.Empty;

        RibbonPanel panelEstructural = app.CreateRibbonPanel(NombrePestana, "Estructural");

        panelEstructural.AddItem(CrearBoton(
            "ComandoEstribos",
            "Estribos\nde Columna",
            "practicacq.ComandoEstribos",
            "Genera el acero de estribos con espaciado uniforme en las columnas seleccionadas.",
            dllPath, folder));

        panelEstructural.AddItem(CrearBoton(
            "ComandoContexto",
            "Leer\nTramos",
            "practicacq.ComandoContexto",
            "Muestra en qué tramos está partida una columna: libres, dentro de viga " +
            "y contra zapata. Solo lee, no modifica el modelo.",
            dllPath, folder));

        RibbonPanel panelHerramientas = app.CreateRibbonPanel(NombrePestana, "Herramientas");

        panelHerramientas.AddItem(CrearBoton(
            "Comando",
            "Columnetas\ny Viguetas",
            "practicacq.Comando",
            "Genera columnetas y viguetas de confinamiento en los muros seleccionados.",
            dllPath, folder));

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app)
    {
        return Result.Succeeded;
    }

    private PushButtonData CrearBoton(
        string nombre, string texto, string clase, string ayuda, string dllPath, string folder)
    {
        var boton = new PushButtonData(nombre, texto, dllPath, clase) { ToolTip = ayuda };

        boton.LargeImage = CargarIcono(Path.Combine(folder, "Resources", "icono32.png"));
        boton.Image = CargarIcono(Path.Combine(folder, "Resources", "icono16.png"));

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
