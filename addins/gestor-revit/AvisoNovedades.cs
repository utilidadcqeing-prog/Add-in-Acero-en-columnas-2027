using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using CQEING.Gestor.Core;

namespace CQEING.GestorRevit;

/// <summary>
/// Al abrir Revit, consulta si hay versiones nuevas de los add-ins instalados en ese Revit y, si las hay, cambia el
/// botón a «Gestor de add-ins (N)» y muestra <see cref="VentanaAviso"/>. Reglas: decisiones 6 y 10 a 14 del plan
/// docs/planes/2026-10-22-gestor-en-revit-boton-y-aviso-159.md.
/// </summary>
internal static class AvisoNovedades
{
    public const string TextoBoton = "Gestor de\nadd-ins";

    public const string AyudaBoton =
        "Abre el Gestor de add-ins CQEING: instala y actualiza los add-ins de CQEING. Las versiones nuevas se ven al reiniciar Revit.";

    /// <summary>Si la lista de GitHub se consultó hace menos que esto, se usa la guardada: cuida el cupo de 60 consultas por hora.</summary>
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Lanza la consulta en segundo plano: no retrasa el arranque de Revit. El resultado vuelve al hilo de Revit por su
    /// Dispatcher cuando Revit queda libre, porque la cinta y las ventanas solo se tocan desde ese hilo.
    /// </summary>
    public static void Empezar(string anio, PushButton boton)
    {
        Dispatcher hiloRevit = Dispatcher.CurrentDispatcher;
        Task.Run(async () =>
        {
            IReadOnlyList<Novedad> novedades;
            try
            {
                novedades = await GestorAddins.Crear().NovedadesAsync(anio, Vigencia, versionGestor: AvisoLicencia.VersionDelComponente());
                Anotar(null);
            }
            catch (Exception e)
            {
                // Sin red, GitHub limitado, caché ilegible...: el aviso es una ayuda, nunca debe molestar ni tumbar Revit.
                Anotar($"Revit {anio}: no se pudo consultar si hay versiones nuevas: {e.Message}");
                return;
            }
            if (novedades.Count > 0)
                _ = hiloRevit.InvokeAsync(() => Mostrar(boton, novedades), DispatcherPriority.ApplicationIdle);
        });
    }

    private static void Mostrar(PushButton boton, IReadOnlyList<Novedad> novedades)
    {
        // Corre dentro del bucle de mensajes de Revit: una excepción aquí cerraría Revit.
        try
        {
            boton.ItemText = $"{TextoBoton} ({novedades.Count})";
            boton.ToolTip = $"{VentanaAviso.Titulo(novedades.Count)}:\n{string.Join("\n", novedades.Select(VentanaAviso.Linea))}\n\n{AyudaBoton}";
            new VentanaAviso(novedades).Mostrar(Process.GetCurrentProcess().MainWindowHandle);
        }
        catch (Exception e)
        {
            Anotar($"No se pudo mostrar el aviso de versiones nuevas: {e.Message}");
        }
    }

    /// <summary>
    /// Deja en %LOCALAPPDATA%\CQEING\Gestor\aviso-revit.log por qué no hubo aviso, para quien da soporte. Una consulta
    /// que sale bien lo borra.
    /// </summary>
    private static void Anotar(string? problema) => RegistroSoporte.Anotar("aviso-revit.log", problema);
}
