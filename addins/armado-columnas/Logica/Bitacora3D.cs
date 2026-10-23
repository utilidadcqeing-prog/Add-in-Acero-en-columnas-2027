namespace practicacq.Logica;

using System;
using System.IO;

/// <summary>
/// TEMPORAL -- registro paso a paso de la vista previa 3D (#123), para el
/// cierre de Revit del 23-oct al aceptar con estribos menores
/// (docs/planes/2026-10-23-cierre-revit-vista-3d.md). Cada línea se escribe
/// y se cierra el archivo en el momento: si Revit se cae, la última línea
/// dice dónde estaba. Solo en Debug: el paquete publicado no deja registros.
/// Sacar junto con el comando de diagnóstico al cerrar el #123.
/// </summary>
internal static class Bitacora3D
{
    public static readonly string Ruta = Path.Combine(Path.GetTempPath(), "practicacq-previa3d.log");

    public static void Anotar(string linea)
    {
#if DEBUG
        try
        {
            File.AppendAllText(Ruta, $"{DateTime.Now:HH:mm:ss.fff} {linea}{Environment.NewLine}");
        }
        catch
        {
            // Nunca molestar por un registro.
        }
#endif
    }
}
