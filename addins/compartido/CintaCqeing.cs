using System.IO;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace CQEING.Compartido;

/// <summary>
/// Pestaña CQEING del ribbon, compartida por todos los add-ins de la empresa. Se compila dentro de cada
/// DLL (addins/Directory.Build.props), así que cada add-in la crea o reutiliza sin depender de otro.
/// </summary>
internal static class CintaCqeing
{
    /// <summary>Pestaña de la empresa en el ribbon. Confirmar la grafía exacta con William.</summary>
    public const string NombrePestana = "CQEING";

    /// <summary>
    /// Devuelve el panel de la pestaña CQEING, creando lo que falte. Si otro add-in de la empresa ya creó
    /// la pestaña o el panel, Revit lanza ArgumentException en vez de devolverlos: no es un error real.
    /// </summary>
    public static RibbonPanel ObtenerPanel(UIControlledApplication app, string nombre)
    {
        try
        {
            app.CreateRibbonTab(NombrePestana);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Ya existe: se usa la que está.
        }

        return app.GetRibbonPanels(NombrePestana).FirstOrDefault(p => p.Name == nombre)
            ?? app.CreateRibbonPanel(NombrePestana, nombre);
    }

    /// <summary>Botón de un comando de la DLL que compila este archivo, con iconos de su carpeta Resources.</summary>
    public static PushButtonData CrearBoton(string nombre, string texto, string clase, string ayuda, string iconoBase)
    {
        string dll = typeof(CintaCqeing).Assembly.Location;
        string carpeta = Path.GetDirectoryName(dll) ?? string.Empty;

        return new PushButtonData(nombre, texto, dll, clase)
        {
            ToolTip = ayuda,
            LargeImage = CargarIcono(Path.Combine(carpeta, "Resources", $"{iconoBase}32.png")),
            Image = CargarIcono(Path.Combine(carpeta, "Resources", $"{iconoBase}16.png")),
        };
    }

    private static BitmapImage CargarIcono(string ruta)
    {
        var imagen = new BitmapImage();
        imagen.BeginInit();
        imagen.UriSource = new Uri(ruta, UriKind.Absolute);
        imagen.CacheOption = BitmapCacheOption.OnLoad;
        imagen.EndInit();
        return imagen;
    }
}
