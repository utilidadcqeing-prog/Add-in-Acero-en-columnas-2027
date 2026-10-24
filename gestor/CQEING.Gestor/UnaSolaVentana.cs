using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CQEING.Gestor;

/// <summary>Garantiza una sola ventana del gestor por usuario. Se libera al terminar el proceso o al cerrar este objeto.</summary>
public sealed class UnaSolaVentana : IDisposable
{
    private const string NombreMutex = @"Local\CQEING.Gestor.UnaSolaVentana";
    private const string NombreProceso = "CQEING.Gestor";
    private const int RestaurarVentana = 9;

    private readonly Mutex _mutex;

    public UnaSolaVentana(string nombreMutex = NombreMutex)
    {
        _mutex = new Mutex(initiallyOwned: true, nombreMutex, out bool esLaPrimera);
        EsLaPrimera = esLaPrimera;
    }

    public bool EsLaPrimera { get; }

    public void Dispose()
    {
        if (EsLaPrimera) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    /// <summary>Restaura y trae al frente la ventana del gestor que ya está abierta, si hay una.</summary>
    public static void TraerAlFrenteLaAbierta()
    {
        int propio = Environment.ProcessId;
        foreach (Process proceso in Process.GetProcessesByName(NombreProceso))
        {
            using (proceso)
            {
                if (proceso.Id == propio || proceso.MainWindowHandle == IntPtr.Zero) continue;
                if (IsIconic(proceso.MainWindowHandle)) ShowWindow(proceso.MainWindowHandle, RestaurarVentana);
                SetForegroundWindow(proceso.MainWindowHandle);
                return;
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr ventana);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr ventana, int orden);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr ventana);
}
