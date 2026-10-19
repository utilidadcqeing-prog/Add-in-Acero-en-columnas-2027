using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Application.Interfaces;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Servicio responsable de persistir y recuperar datos localmente en formato JSON dentro de la carpeta AppData de Windows.
/// Proporciona un mecanismo de escritura atómica para prevenir la corrupción de datos en caso de apagados repentinos.
/// </summary>
public sealed class LocalStorageService : ILocalStorageService
{
    private readonly string _baseDirectory;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="LocalStorageService"/>.
    /// Crea el directorio base en la carpeta ApplicationData si este no existe.
    /// </summary>
    public LocalStorageService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _baseDirectory = Path.Combine(appData, "CQEING_CuadroCantidades");
        Directory.CreateDirectory(_baseDirectory);
    }

    /// <summary>
    /// Obtiene la ruta física del archivo JSON asociado a una clave específica.
    /// </summary>
    /// <param name="key">Clave del almacenamiento o ruta identificadora.</param>
    /// <returns>La ruta absoluta del archivo JSON.</returns>
    private string GetFilePath(string key)
    {
        string safeKey = GetSafeFileName(key);
        return Path.Combine(_baseDirectory, $"{safeKey}.json");
    }

    /// <summary>
    /// Convierte la clave provista en un nombre de archivo seguro eliminando y reemplazando caracteres inválidos.
    /// </summary>
    /// <param name="key">Clave original a procesar.</param>
    /// <returns>Una cadena de texto sanitizada apta para nombres de archivo en Windows.</returns>
    private string GetSafeFileName(string key)
    {
        // Remplazar caracteres no permitidos en nombres de archivos.
        // Si es una ruta completa de documento, convertimos barras y caracteres inválidos a guión bajo.
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            key = key.Replace(c, '_');
        }
        
        // También reemplazamos barras y caracteres que puedan causar problemas de rutas en Windows
        key = key.Replace(':', '_')
                 .Replace('\\', '_')
                 .Replace('/', '_')
                 .Replace(' ', '_');
                 
        return key;
    }

    /// <summary>
    /// Guarda datos serializados en JSON de forma asíncrona usando una clave única.
    /// Realiza un guardado atómico escribiendo primero en un archivo temporal (.tmp) y luego renombrándolo.
    /// </summary>
    /// <typeparam name="T">El tipo de objeto que se va a serializar.</typeparam>
    /// <param name="key">Clave de almacenamiento asociada a los datos.</param>
    /// <param name="data">El objeto con los datos a persistir.</param>
    public async Task SaveAsync<T>(string key, T data)
    {
        try
        {
            string filePath = GetFilePath(key);
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            // Usar un guardado atómico (escribir a archivo temporal y renombrar)
            // para evitar corrupción de archivos en caídas o cierres bruscos.
            string tempPath = filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            File.Move(tempPath, filePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al escribir local storage para {key}: {ex.Message}");
        }
    }

    /// <summary>
    /// Recupera y deserializa datos en formato JSON de forma asíncrona a partir de una clave.
    /// </summary>
    /// <typeparam name="T">El tipo de objeto al que se deserializarán los datos.</typeparam>
    /// <param name="key">La clave de almacenamiento asociada.</param>
    /// <returns>El objeto deserializado, o default si no existe el archivo o hay un error.</returns>
    public async Task<T?> LoadAsync<T>(string key)
    {
        string filePath = GetFilePath(key);
        if (!File.Exists(filePath))
            return default;

        try
        {
            string json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al leer local storage para {key}: {ex.Message}");
            return default;
        }
    }

    /// <summary>
    /// Comprueba si existe un archivo de almacenamiento asociado a la clave provista.
    /// </summary>
    /// <param name="key">Clave de almacenamiento.</param>
    /// <returns>True si el archivo existe; de lo contrario, False.</returns>
    public bool Exists(string key)
    {
        return File.Exists(GetFilePath(key));
    }

    /// <summary>
    /// Elimina el archivo de almacenamiento local asociado a la clave provista, si existe.
    /// </summary>
    /// <param name="key">Clave de almacenamiento a eliminar.</param>
    public void Delete(string key)
    {
        string filePath = GetFilePath(key);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
