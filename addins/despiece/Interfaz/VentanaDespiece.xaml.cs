using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CQEING.Vigas.Calculo.Tablero;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CQEING.Despiece.Interfaz;

/// <summary>La ventana del despiece (#220): un WebView2 que muestra la página de <see cref="CarpetaWeb"/>, la maqueta portada.</summary>
internal sealed partial class VentanaDespiece : Window
{
    /// <summary>Host virtual con el que WebView2 sirve <see cref="CarpetaWeb"/>.</summary>
    public const string Host = "despiece.cqeing";

    private bool cerrada;

    private WindowState estadoAntes = WindowState.Normal;

    /// <summary>
    /// Abre a 1440 × 900, el tamaño de la maqueta; al cargar se recorta al área de trabajo.
    /// Se crea en código y no en el XAML: en Revit 2027 el lector de XAML no resuelve el WebView2 que trae Revit y la
    /// ventana no abre ("Set connectionId threw an exception").
    /// </summary>
    private readonly WebView2 navegador = new()
    {
        Width = 1440,
        Height = 900,
        DefaultBackgroundColor = System.Drawing.Color.FromArgb(0xF4, 0xF6, 0xF8),
    };

    public VentanaDespiece(IntPtr duenio)
    {
        InitializeComponent();
        Content = navegador;
        new WindowInteropHelper(this).Owner = duenio;
        Loaded += async (_, _) =>
        {
            AjustarAlArea();
            try
            {
                await IniciarAsync();
            }
            catch (Exception e)
            {
                // Sin esto la excepción se pierde y la ventana queda en blanco.
                MessageBox.Show(this, $"No se pudo abrir el despiece.\n\n{e}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        };
        // La página mide 1440 × 900 como la maqueta: si no entra, se achica entera y nada se corta.
        navegador.SizeChanged += (_, _) => navegador.ZoomFactor = CalculadoraZoom.Factor(navegador.ActualWidth, navegador.ActualHeight);
        Closed += (_, _) =>
        {
            cerrada = true;
            navegador.Dispose();
        };
    }

    /// <summary>La carpeta <c>web</c> junto a la DLL, donde la compilación copia la página.</summary>
    public static string CarpetaWeb { get; } =
        Path.Combine(Path.GetDirectoryName(typeof(VentanaDespiece).Assembly.Location) ?? string.Empty, "web");

    /// <summary>Datos de usuario de WebView2: la carpeta de Revit no se puede escribir.</summary>
    public static string CarpetaDatos { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CQEING", "despiece", "WebView2");

    public void Volver()
    {
        if (cerrada)
            return;
        if (WindowState == WindowState.Minimized)
            WindowState = estadoAntes;
        Activate();
    }

    /// <summary>
    /// La ventana abrió a su tamaño natural (1440 × 900 más el borde), centrada sobre Revit: se encaja en el área de
    /// trabajo de ese monitor y el navegador pasa a llenar la ventana, que desde ahí se puede cambiar de tamaño.
    /// </summary>
    private void AjustarAlArea()
    {
        Caja c = CalculadoraZoom.Encajar(new Caja(Left, Top, ActualWidth, ActualHeight), AreaDeTrabajo());
        SizeToContent = SizeToContent.Manual;
        navegador.Width = double.NaN;
        navegador.Height = double.NaN;
        (Left, Top, Width, Height) = (c.X, c.Y, c.Ancho, c.Alto);
    }

    /// <summary>
    /// El área de trabajo del monitor donde está la ventana, en DIP. Con Revit en el segundo monitor no es la del
    /// principal (<see cref="SystemParameters.WorkArea"/>), que se usa solo si Windows no la da.
    /// </summary>
    private Caja AreaDeTrabajo()
    {
        IntPtr monitor = MonitorFromWindow(new WindowInteropHelper(this).Handle, MonitorMasCercano);
        var info = new InfoMonitor { Tamano = Marshal.SizeOf<InfoMonitor>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info) || PresentationSource.FromVisual(this) is not { CompositionTarget: { } destino })
        {
            Rect a = SystemParameters.WorkArea;
            return new Caja(a.X, a.Y, a.Width, a.Height);
        }
        Point arriba = destino.TransformFromDevice.Transform(new Point(info.Trabajo.Izquierda, info.Trabajo.Arriba));
        Point abajo = destino.TransformFromDevice.Transform(new Point(info.Trabajo.Derecha, info.Trabajo.Abajo));
        return new Caja(arriba.X, arriba.Y, abajo.X - arriba.X, abajo.Y - arriba.Y);
    }

    private const uint MonitorMasCercano = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr ventana, uint opciones);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref InfoMonitor info);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangulo
    {
        public int Izquierda, Arriba, Derecha, Abajo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InfoMonitor
    {
        public int Tamano;
        public Rectangulo Monitor;
        public Rectangulo Trabajo;
        public uint Opciones;
    }

    private async Task IniciarAsync()
    {
        CoreWebView2Environment entorno = await CoreWebView2Environment.CreateAsync(null, CarpetaDatos);
        await navegador.EnsureCoreWebView2Async(entorno);
        CoreWebView2 core = navegador.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping(Host, CarpetaWeb, CoreWebView2HostResourceAccessKind.Deny);
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsPinchZoomEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
#endif
        // Solo la página del despiece: soltar un archivo o un enlace sobre la ventana no la cambia por otra.
        core.NavigationStarting += (_, e) => e.Cancel = !EsDelDespiece(e.Uri);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        // El único mensaje de la página, por ahora: el botón «Cerrar» del paso 1.
        core.WebMessageReceived += (_, e) =>
        {
            if (EsDelDespiece(e.Source))
                Close();
        };
        core.Navigate($"https://{Host}/index.html");
    }

    private static bool EsDelDespiece(string? uri) => uri?.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase) == true;
}
