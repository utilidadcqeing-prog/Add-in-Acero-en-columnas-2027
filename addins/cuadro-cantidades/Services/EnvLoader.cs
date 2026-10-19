using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Clase de utilidad para cargar variables de entorno desde un archivo .env en el Add-in de Revit.
/// </summary>
public static class EnvLoader
{
    private static readonly Dictionary<string, string> _envVariables = new();
    private static bool _isLoaded = false;

    /// <summary>
    /// Carga el archivo .env si aún no ha sido cargado.
    /// </summary>
    public static void Load()
    {
        if (_isLoaded)
            return;

        try
        {
            // 1. Buscar en el directorio de la Assembly que se está ejecutando
            string assemblyDir =
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string envPath = Path.Combine(assemblyDir, ".env");

            // 2. Buscar hacia arriba (hasta 4 niveles de carpetas) en caso de pruebas o desarrollo
            if (!File.Exists(envPath))
            {
                string currentDir = assemblyDir;
                for (int i = 0; i < 4; i++)
                {
                    if (string.IsNullOrEmpty(currentDir))
                        break;
                    string parentDir = Directory.GetParent(currentDir)?.FullName ?? string.Empty;
                    if (string.IsNullOrEmpty(parentDir))
                        break;

                    string checkPath = Path.Combine(parentDir, ".env");
                    if (File.Exists(checkPath))
                    {
                        envPath = checkPath;
                        break;
                    }
                    currentDir = parentDir;
                }
            }

            // 2b. Lugares fijos. Con el gestor, la DLL cambia de carpeta en cada versión: el .env va junto a los datos
            //     del add-in y sobrevive a las actualizaciones. Si no está, se usa el de la instalación vieja (Instalar.bat).
            if (!File.Exists(envPath))
            {
                envPath = UbicacionesFijas().FirstOrDefault(File.Exists) ?? envPath;
            }

            // 3. Procesar el archivo si existe
            if (File.Exists(envPath))
            {
                var lines = File.ReadAllLines(envPath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    string trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("#"))
                        continue; // Ignorar comentarios

                    int separatorIndex = trimmedLine.IndexOf('=');
                    if (separatorIndex > 0)
                    {
                        string key = trimmedLine.Substring(0, separatorIndex).Trim();
                        string value = trimmedLine.Substring(separatorIndex + 1).Trim();

                        // Quitar comillas simples o dobles envolventes si existen
                        if (
                            value.Length >= 2
                            && (
                                (value.StartsWith("\"") && value.EndsWith("\""))
                                || (value.StartsWith("'") && value.EndsWith("'"))
                            )
                        )
                        {
                            value = value.Substring(1, value.Length - 2);
                        }

                        _envVariables[key] = value;
                        Environment.SetEnvironmentVariable(key, value);
                    }
                }
            }
        }
        catch
        {
            // Falla silenciosa para evitar caídas de Revit en problemas de I/O de disco
        }
        finally
        {
            _isLoaded = true;
        }
    }

    /// <summary>
    /// %APPDATA%\CQEING_CuadroCantidades\.env (junto a los datos del add-in) y C:\CQEING\CuadroCantidades\.env
    /// (donde lo dejaba Instalar.bat), en ese orden.
    /// </summary>
    public static IEnumerable<string> UbicacionesFijas()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(appData, "CQEING_CuadroCantidades", ".env");
        yield return Path.Combine(@"C:\CQEING\CuadroCantidades", ".env");
    }

    /// <summary>
    /// Obtiene el valor de una variable de entorno cargada del archivo .env,
    /// o del entorno del sistema en su defecto.
    /// </summary>
    public static string Get(string key, string defaultValue = "")
    {
        Load();
        if (_envVariables.TryGetValue(key, out string? value))
        {
            return value;
        }

        string? envVal = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrEmpty(envVal))
        {
            return envVal;
        }

        return defaultValue;
    }
}
