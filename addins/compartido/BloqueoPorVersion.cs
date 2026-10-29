using System.IO;
using System.Windows.Threading;
using Autodesk.Revit.UI;

namespace CQEING.Compartido;

/// <summary>
/// La parte de <see cref="ActualizacionObligatoria"/> que toca Revit. Al abrir Revit, un add-in con una versión pendiente
/// no crea sus botones; si la versión nueva aparece con Revit abierto (el gestor la anota en segundo plano), desactiva los
/// que ya creó. El aviso con «Abrir el gestor» lo da el componente del gestor. Solo se compila en los add-ins.
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

            string carpeta = Licencia.CarpetaPorDefecto;
            if (ActualizacionObligatoria.Pendiente(instalacion, carpeta) is not null) return false;

            Vigilar(app, pestana, dll, instalacion, carpeta);
            return true;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static void Vigilar(UIControlledApplication app, string pestana, string dll, InstalacionAddin instalacion, string carpeta)
    {
        Directory.CreateDirectory(carpeta);
        Dispatcher hiloRevit = Dispatcher.CurrentDispatcher;
        var vigia = new FileSystemWatcher(carpeta, ActualizacionObligatoria.NombreArchivo(instalacion.Anio))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
        };
        // El gestor escribe aparte y reemplaza: llega como «Renamed»; «Changed» y «Created» cubren otras formas de escribir.
        FileSystemEventHandler alCambiar = (_, _) => hiloRevit.InvokeAsync(() => Revisar(app, pestana, dll, instalacion, carpeta), DispatcherPriority.ApplicationIdle);
        vigia.Changed += alCambiar;
        vigia.Created += alCambiar;
        vigia.Renamed += (o, e) => alCambiar(o, e);
        vigia.EnableRaisingEvents = true;
        _vigia = vigia;
    }

    private static void Revisar(UIControlledApplication app, string pestana, string dll, InstalacionAddin instalacion, string carpeta)
    {
        // Corre dentro del bucle de mensajes de Revit: una excepción aquí cerraría Revit.
        try
        {
            if (ActualizacionObligatoria.Pendiente(instalacion, carpeta) is not { } ultima) return;
            _vigia?.Dispose();
            _vigia = null;
            Desactivar(app, pestana, dll, ultima);
        }
        catch (Exception)
        {
            // Sin botones desactivados esta sesión; al reiniciar Revit no se crean.
        }
    }

    /// <summary>Desactiva los botones de esta DLL en la pestaña: cada uno sabe de qué DLL es su comando.</summary>
    private static void Desactivar(UIControlledApplication app, string pestana, string dll, string ultima)
    {
        string aviso = $"Hay una versión nueva ({ultima}). Actualícela desde el gestor y reinicie Revit para seguir usando este add-in.";
        foreach (RibbonPanel panel in app.GetRibbonPanels(pestana))
            foreach (RibbonItem item in panel.GetItems())
                if (item is PushButton boton && string.Equals(boton.AssemblyName, dll, StringComparison.OrdinalIgnoreCase))
                {
                    boton.Enabled = false;
                    boton.ToolTip = aviso;
                }
    }
}
