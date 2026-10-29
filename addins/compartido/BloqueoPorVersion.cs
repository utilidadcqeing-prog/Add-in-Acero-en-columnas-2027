using System.IO;
using System.Windows.Threading;
using Autodesk.Revit.UI;

namespace CQEING.Compartido;

/// <summary>
/// La parte de <see cref="ActualizacionObligatoria"/> que toca Revit. Al abrir Revit, un add-in desactualizado (o con el
/// gestor desactualizado) no crea sus botones; si la versión nueva aparece con Revit abierto (el gestor la anota en
/// segundo plano), desactiva los que ya creó. El aviso con «Abrir el gestor» lo da el componente del gestor. Solo se
/// compila en los add-ins.
/// </summary>
internal static class BloqueoPorVersion
{
    /// <summary>Se guarda para que el recolector no lo libere mientras Revit está abierto.</summary>
    private static FileSystemWatcher? _vigia;

    /// <summary>
    /// Va en OnStartup justo después de la licencia: «if (!BloqueoPorVersion.AlDia(app)) return Result.Succeeded;».
    /// <paramref name="pestana"/> es donde el add-in crea sus botones. Nunca lanza: ante cualquier problema deja usarlo.
    /// </summary>
    public static bool AlDia(UIControlledApplication app, string pestana = CintaCqeing.NombrePestana)
    {
        try
        {
            string dll = typeof(BloqueoPorVersion).Assembly.Location;
            if (ActualizacionObligatoria.Instalacion(dll) is not { } instalacion) return true;

            var revision = new Revision(instalacion, Licencia.CarpetaPorDefecto, CarpetaAddinsRevit(instalacion.Anio));
            if (revision.Motivo() is not null) return false;

            Vigilar(app, pestana, dll, revision);
            return true;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>Lo que hace falta para preguntar si el add-in sigue al día.</summary>
    private sealed record Revision(InstalacionAddin Instalacion, string Carpeta, string CarpetaAddinsRevit)
    {
        public string? Motivo() => ActualizacionObligatoria.Pendiente(Instalacion, Carpeta, CarpetaAddinsRevit);
    }

    private static string CarpetaAddinsRevit(string anio) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", anio);

    private static void Vigilar(UIControlledApplication app, string pestana, string dll, Revision revision)
    {
        Directory.CreateDirectory(revision.Carpeta);
        Dispatcher hiloRevit = Dispatcher.CurrentDispatcher;
        var vigia = new FileSystemWatcher(revision.Carpeta, ActualizacionObligatoria.NombreArchivo(revision.Instalacion.Anio))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
        };
        // El gestor escribe aparte y reemplaza: llega como «Renamed»; «Changed» y «Created» cubren otras formas de escribir.
        FileSystemEventHandler alCambiar = (_, _) => hiloRevit.InvokeAsync(() => Revisar(app, pestana, dll, revision), DispatcherPriority.ApplicationIdle);
        vigia.Changed += alCambiar;
        vigia.Created += alCambiar;
        vigia.Renamed += (o, e) => alCambiar(o, e);
        vigia.EnableRaisingEvents = true;
        _vigia = vigia;
    }

    private static void Revisar(UIControlledApplication app, string pestana, string dll, Revision revision)
    {
        // Corre dentro del bucle de mensajes de Revit: una excepción aquí cerraría Revit.
        try
        {
            if (revision.Motivo() is not { } motivo) return;
            _vigia?.Dispose();
            _vigia = null;
            Desactivar(app, pestana, dll, motivo);
        }
        catch (Exception)
        {
            // Sin botones desactivados esta sesión; al reiniciar Revit no se crean.
        }
    }

    /// <summary>Desactiva los botones de esta DLL en la pestaña: cada uno sabe de qué DLL es su comando.</summary>
    private static void Desactivar(UIControlledApplication app, string pestana, string dll, string motivo)
    {
        foreach (RibbonPanel panel in app.GetRibbonPanels(pestana))
            foreach (RibbonItem item in panel.GetItems())
                if (item is PushButton boton && string.Equals(boton.AssemblyName, dll, StringComparison.OrdinalIgnoreCase))
                {
                    boton.Enabled = false;
                    boton.ToolTip = motivo;
                }
    }
}
