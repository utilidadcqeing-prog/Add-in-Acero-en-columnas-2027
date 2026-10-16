using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace CQEING.Vigas.Tablero;

/// <summary>
/// La ventana del tablero (#83): un WebView2 que muestra la página de <see cref="CarpetaWeb"/>, el HTML de la maqueta
/// portado. Habla con C# por mensajes JSON (<see cref="MensajeRecibido"/> y <see cref="Enviar"/>).
/// </summary>
internal sealed partial class VentanaTablero : Window
{
    /// <summary>Host virtual con el que WebView2 sirve <see cref="CarpetaWeb"/>.</summary>
    public const string Host = "tablero.cqeing";

    public VentanaTablero(IntPtr duenio)
    {
        InitializeComponent();
        new WindowInteropHelper(this).Owner = duenio;
        Loaded += async (_, _) => await IniciarAsync();
        Closed += (_, _) => Navegador.Dispose();
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
    public void Enviar(string json) => Navegador.CoreWebView2?.PostWebMessageAsJson(json);

    private async Task IniciarAsync()
    {
        CoreWebView2Environment entorno = await CoreWebView2Environment.CreateAsync(null, CarpetaDatos);
        await Navegador.EnsureCoreWebView2Async(entorno);
        CoreWebView2 core = Navegador.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping(Host, CarpetaWeb, CoreWebView2HostResourceAccessKind.Deny);
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
#endif
        core.WebMessageReceived += (_, e) => MensajeRecibido?.Invoke(e.WebMessageAsJson);
        Diagnostico = $"WebView2 {entorno.BrowserVersionString} · {typeof(CoreWebView2).Assembly.Location} · datos en {CarpetaDatos}";
        core.Navigate($"https://{Host}/index.html");
    }
}
