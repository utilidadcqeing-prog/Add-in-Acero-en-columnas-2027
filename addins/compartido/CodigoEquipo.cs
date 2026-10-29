using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace CQEING.Compartido;

/// <summary>
/// Código fijo de cada equipo, el que se aprueba en la hoja de licencias (issue #189). Sale del MachineGuid de Windows,
/// que no cambia al cambiar de usuario ni al reinstalar Revit. Es un resumen del GUID, no el GUID: no lo expone y se puede
/// dictar por teléfono. Plan docs/planes/2026-10-28-licencias-hoja-google.md, Decisión 5.
/// </summary>
internal static class CodigoEquipo
{
    /// <summary>Primeros 8 bytes de SHA-256("CQEING|" + guid) en hexadecimal mayúscula: XXXX-XXXX-XXXX-XXXX.</summary>
    public static string Desde(string machineGuid)
    {
        byte[] resumen = SHA256.HashData(Encoding.UTF8.GetBytes("CQEING|" + machineGuid.Trim()));
        string hex = Convert.ToHexString(resumen, 0, 8);
        return string.Join("-", hex[..4], hex[4..8], hex[8..12], hex[12..]);
    }

    /// <summary>
    /// El código de este equipo, o null si no se puede leer el MachineGuid. Se lee la vista de 64 bits del registro, que
    /// es donde Windows lo guarda, sin importar si quien pregunta es un proceso de 32 o 64 bits.
    /// </summary>
    public static string? Actual()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using RegistryKey raiz = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? clave = raiz.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return clave?.GetValue("MachineGuid") is string guid && !string.IsNullOrWhiteSpace(guid) ? Desde(guid) : null;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
