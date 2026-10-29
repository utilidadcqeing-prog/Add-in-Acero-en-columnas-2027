using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CQEING.Vigas.Calculo.Tablero;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CQEING.Vigas.Tablero;

/// <summary>
/// La ventana del tablero (#83): un WebView2 que muestra la página de <see cref="CarpetaWeb"/>, el HTML de la maqueta
/// portado. Habla con C# por mensajes JSON (<see cref="MensajeRecibido"/> y <see cref="Enviar"/>).
/// </summary>
internal sealed partial class VentanaTablero : Window
{
    /// <summary>Host virtual con el que WebView2 sirve <see cref="CarpetaWeb"/>.</summary>
    public const string Host = "tablero.cqeing";

    private bool cerrada;

    /// <summary>
    /// Abre a 1440 × 900, el tamaño de la maqueta; al cargar se recorta al área de trabajo (decisión 6 del plan).
    /// Se crea en código y no en el XAML: en Revit 2027 el lector de XAML no resuelve el WebView2 que trae Revit y la
    /// ventana no abre ("Set connectionId threw an exception"). Desde el código se resuelve igual que
    /// <see cref="CoreWebView2Environment"/> en <see cref="ComandoTablero"/>.
    /// </summary>
    private readonly WebView2 navegador = new()
    {
        Width = 1440,
        Height = 900,
        DefaultBackgroundColor = System.Drawing.Color.FromArgb(0xEE, 0xF0, 0xF2),
    };

    /// <summary>La página de <see cref="CarpetaWeb"/> que se abre: el tablero o «Crear viguetas» (#947).</summary>
    private readonly string pagina;

    public VentanaTablero(IntPtr duenio, string pagina = "index.html", string? titulo = null)
    {
        InitializeComponent();
        this.pagina = pagina;
        if (titulo is not null)
            Title = titulo;
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
                MessageBox.Show(this, $"No se pudo abrir el tablero.\n\n{e}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        };
        // La página mide 1440 × 900 como la maqueta: si no entra, se achica entera y nada se corta (decisión 6).
        navegador.SizeChanged += (_, _) => navegador.ZoomFactor = CalculadoraZoom.Factor(navegador.ActualWidth, navegador.ActualHeight);
        Closed += (_, _) =>
        {
            cerrada = true;
            navegador.Dispose();
        };
    }

    /// <summary>Un mensaje de la página, como JSON (lo que la página pasó a <c>chrome.webview.postMessage</c>).</summary>
    public event Action<string>? MensajeRecibido;

    /// <summary>La carpeta <c>web</c> junto a la DLL, donde la compilación copia la página.</summary>
    public static string CarpetaWeb { get; } =
        Path.Combine(Path.GetDirectoryName(typeof(VentanaTablero).Assembly.Location) ?? string.Empty, "web");

    /// <summary>Datos de usuario de WebView2: la carpeta de Revit no se puede escribir.</summary>
    public static string CarpetaDatos { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CQEING", "vigas", "WebView2");

    /// <summary>Lo que reportó WebView2 al iniciar, para el diagnóstico de la tarea 1 del plan.</summary>
    public string Diagnostico { get; private set; } = string.Empty;

    /// <summary>Manda <paramref name="json"/> a la página (llega como objeto en <c>chrome.webview</c> "message").</summary>
    private WindowState estadoAntes = WindowState.Normal;

    /// <summary>
    /// Se minimiza para dejar ver Revit: la ventana pertenece a Revit y siempre queda encima ("Ver en Revit", "3D",
    /// "Seleccionar en Revit"). <see cref="Volver"/> la deja como estaba.
    /// </summary>
    public void Apartar()
    {
        if (cerrada || WindowState == WindowState.Minimized)
            return;
        estadoAntes = WindowState;
        WindowState = WindowState.Minimized;
    }

    public void Volver()
    {
        if (cerrada)
            return;
        if (WindowState == WindowState.Minimized)
            WindowState = estadoAntes;
        Activate();
    }

    /// <remarks>Después de cerrar la ventana no hace nada: puede quedar un trabajo de Revit en cola.</remarks>
    public void Enviar(string json)
    {
        if (!cerrada)
            navegador.CoreWebView2?.PostWebMessageAsJson(json);
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
        // Tampoco con el gesto de pellizcar del touchpad (Juan, 17-oct: "puedo hacer zoom sobre el add-in"); el zoom
        // que se ajusta a la pantalla (ZoomFactor) no depende de esto.
        core.Settings.IsPinchZoomEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
#endif
        // Solo la página del tablero: soltar un archivo o un enlace sobre la ventana no la cambia por otra, y nada de
        // afuera puede mandarle mensajes a C#.
        core.NavigationStarting += (_, e) => e.Cancel = !EsDelTablero(e.Uri);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.WebMessageReceived += (_, e) =>
        {
            if (EsDelTablero(e.Source))
                MensajeRecibido?.Invoke(e.WebMessageAsJson);
        };
        Diagnostico = $"WebView2 {entorno.BrowserVersionString} · {typeof(CoreWebView2).Assembly.Location} · datos en {CarpetaDatos}";
#if DEBUG
        // Para anotar en la tarea 6 del plan qué WebView2 cargó Revit.
        Title += " · " + Diagnostico;
#endif
        core.Navigate($"https://{Host}/{pagina}");
    }

    private static bool EsDelTablero(string? uri) => uri?.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase) == true;
}
