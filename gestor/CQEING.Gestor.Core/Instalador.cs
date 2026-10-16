using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace CQEING.Gestor.Core;

public sealed class Instalador(HttpClient http, RutasGestor rutas, TimeSpan? tiempoSinDatos = null)
{
    public const int VersionesConservadas = 3;

    private static readonly TimeSpan SinDatosPorDefecto = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EdadMaximaDeTemporales = TimeSpan.FromDays(1);

    private readonly TimeSpan _tiempoSinDatos = tiempoSinDatos ?? SinDatosPorDefecto;

    /// <summary>
    /// Deja <paramref name="version"/> activa en Revit <paramref name="anio"/>. Sirve igual para instalar, actualizar
    /// y volver a una versión anterior: si la versión ya está en disco, no se descarga nada. Con Revit abierto es
    /// seguro (cada versión tiene su carpeta); el cambio se ve al reiniciar Revit.
    /// </summary>
    /// <param name="progreso">Avance de la descarga de 0 a 1 (solo si hay que descargar).</param>
    public async Task InstalarAsync(VersionPublicada version, string anio, CancellationToken ct = default, IProgress<double>? progreso = null)
    {
        Manifiesto m = version.Manifiesto;
        PaqueteManifiesto paquete = m.PaqueteParaRevit(anio)
            ?? throw new GestorException(TipoError.SinPaqueteParaRevit, $"{m.Nombre} {m.Version} no tiene paquete para Revit {anio}.");

        // El id, la versión y el año vienen de internet y terminan en rutas que se crean y se borran: se validan antes de tocar nada.
        string carpeta, archivoAddin;
        try
        {
            carpeta = rutas.CarpetaVersion(m.Id, m.Version, anio);
            archivoAddin = rutas.ArchivoAddin(m.Id, anio);
        }
        catch (ArgumentException e)
        {
            throw new GestorException(TipoError.ManifiestoInvalido, $"El manifiesto de {m.Nombre} trae un id, versión o año no permitido.", e);
        }

        ct.ThrowIfCancellationRequested();
        BorrarTemporalesViejos();

        if (Directory.Exists(carpeta) && !TieneAddinValido(carpeta))
            RetirarCarpetaDanada(carpeta);

        if (!Directory.Exists(carpeta))
            await DescargarAsync(version.UrlDe(paquete), paquete.Sha256, carpeta, progreso, ct);
        progreso?.Report(1);

        ActivarAddin(carpeta, archivoAddin);
        RetirarHeredados(m.Heredados, anio);
        LimpiarVersionesViejas(m.Id);
    }

    /// <summary>
    /// Descarga, verifica y extrae en una carpeta de trabajo propia (fuera de las versiones); solo si todo salió
    /// bien mueve el resultado a su lugar. Con cualquier error o cancelación, la carpeta de trabajo se borra.
    /// </summary>
    private async Task DescargarAsync(Uri url, string sha256Esperado, string carpetaFinal, IProgress<double>? progreso, CancellationToken ct)
    {
        string trabajo = Path.Combine(rutas.CarpetaTemporal, Guid.NewGuid().ToString("N"));
        string zip = Path.Combine(trabajo, "paquete.zip");
        string extraido = Path.Combine(trabajo, "contenido");
        try
        {
            Directory.CreateDirectory(trabajo);
            await BajarAsync(url, zip, progreso, ct);

            if (Sha256Archivo.Calcular(zip) != sha256Esperado)
                throw new GestorException(TipoError.HashNoCoincide, "El archivo descargado no coincide con el publicado (SHA-256). No se instaló nada; intente de nuevo.");

            Extraer(zip, extraido);
            LeerAddinDelPaquete(extraido);

            Directory.CreateDirectory(Path.GetDirectoryName(carpetaFinal)!);
            Directory.Move(extraido, carpetaFinal);
        }
        finally
        {
            BorrarSinFallar(trabajo);
        }
    }

    private async Task BajarAsync(Uri url, string zip, IProgress<double>? progreso, CancellationToken ct)
    {
        // Si el servidor deja de mandar datos, se corta: el usuario no se queda esperando para siempre.
        using var inactividad = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            inactividad.CancelAfter(_tiempoSinDatos);
            using HttpResponseMessage respuesta = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, inactividad.Token);
            respuesta.EnsureSuccessStatusCode();
            long? total = respuesta.Content.Headers.ContentLength;

            await using Stream origen = await respuesta.Content.ReadAsStreamAsync(inactividad.Token);
            await using FileStream destino = File.Create(zip);
            byte[] buffer = new byte[81920];
            long leidos = 0;
            while (true)
            {
                inactividad.CancelAfter(_tiempoSinDatos);
                int n = await origen.ReadAsync(buffer, inactividad.Token);
                if (n == 0) break;
                await destino.WriteAsync(buffer.AsMemory(0, n), ct);
                leidos += n;
                if (total is > 0) progreso?.Report(Math.Min(1, (double)leidos / total.Value));
            }
        }
        catch (Exception e) when (e is HttpRequestException || (e is IOException && e is not DirectoryNotFoundException))
        {
            throw new GestorException(TipoError.SinConexion, "No se pudo descargar el paquete. Revise la conexión e intente de nuevo.", e);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new GestorException(TipoError.SinConexion, "La descarga se detuvo porque no llegaron más datos. Revise la conexión e intente de nuevo.", e);
        }
    }

    private static void Extraer(string zip, string destino)
    {
        try
        {
            // .NET rechaza las entradas que se salen de la carpeta de destino (por ejemplo «..\fuera.txt»).
            ZipFile.ExtractToDirectory(zip, destino);
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            throw new GestorException(TipoError.PaqueteInvalido, "El paquete está dañado o trae archivos con rutas no permitidas. No se instaló nada.", e);
        }
    }

    /// <summary>Exige exactamente un .addin, bien formado, que nombre DLL sueltas (sin carpeta) y que estén en el paquete.</summary>
    private static XDocument LeerAddinDelPaquete(string carpeta)
    {
        string[] addins = Directory.GetFiles(carpeta, "*.addin");
        if (addins.Length != 1)
            throw new GestorException(TipoError.PaqueteInvalido, $"El paquete debe traer exactamente un .addin y trae {addins.Length}.");

        XDocument doc;
        try
        {
            doc = XDocument.Load(addins[0]);
        }
        catch (XmlException e)
        {
            throw new GestorException(TipoError.PaqueteInvalido, "El .addin del paquete está dañado.", e);
        }

        List<string> assemblies = doc.Descendants("Assembly").Select(a => a.Value.Trim()).ToList();
        if (assemblies.Count == 0 || assemblies.Any(a => a.Length == 0 || Path.GetFileName(a) != a))
            throw new GestorException(TipoError.PaqueteInvalido, "El .addin del paquete debe nombrar su DLL sin carpeta.");
        if (assemblies.Any(a => !File.Exists(Path.Combine(carpeta, a))))
            throw new GestorException(TipoError.PaqueteInvalido, "El .addin del paquete nombra una DLL que el paquete no trae.");
        return doc;
    }

    private static bool TieneAddinValido(string carpeta)
    {
        try
        {
            LeerAddinDelPaquete(carpeta);
            return true;
        }
        catch (GestorException)
        {
            return false;
        }
    }

    /// <summary>Una carpeta de versión vacía o a medias (no debería pasar) se aparta para volver a descargarla.</summary>
    private static void RetirarCarpetaDanada(string carpeta)
    {
        string aBorrar = $"{carpeta}.borrar-{Guid.NewGuid():N}";
        try
        {
            Directory.Move(carpeta, aBorrar);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new GestorException(TipoError.PaqueteInvalido, "La versión que está en este equipo está dañada y en uso. Cierre Revit e intente de nuevo.", e);
        }
        BorrarSinFallar(aBorrar);
    }

    private static void ActivarAddin(string carpetaVersion, string destino)
    {
        XDocument doc = LeerAddinDelPaquete(carpetaVersion);
        foreach (XElement assembly in doc.Descendants("Assembly"))
            assembly.Value = Path.Combine(carpetaVersion, assembly.Value.Trim());

        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        string temporal = destino + ".tmp";
        try
        {
            doc.Save(temporal);
            File.Move(temporal, destino, overwrite: true); // Revit nunca ve un .addin a medio escribir
        }
        finally
        {
            if (File.Exists(temporal)) File.Delete(temporal);
        }
    }

    /// <summary>Solo se borran los .addin que el manifiesto nombra (ya validados como nombres sueltos): nunca otros archivos.</summary>
    private void RetirarHeredados(IReadOnlyList<string> heredados, string anio)
    {
        foreach (string nombre in heredados)
        {
            string ruta = Path.Combine(rutas.CarpetaAddinsRevit(anio), nombre);
            if (File.Exists(ruta)) File.Delete(ruta);
        }
    }

    /// <summary>
    /// Deja las 3 versiones más nuevas y las activas en algún año. Antes de borrar, renombra la carpeta: si Revit
    /// tiene una DLL abierta, Windows no deja renombrarla y la versión queda entera para la próxima vez, en vez de
    /// quedar a medio borrar.
    /// </summary>
    private void LimpiarVersionesViejas(string id)
    {
        string raiz = rutas.CarpetaVersiones(id);
        HashSet<string> activas = LectorEstado.Leer(rutas).Where(a => a.Id == id).Select(a => a.Version).ToHashSet();

        IEnumerable<string> sobrantes = Directory.GetDirectories(raiz)
            .Select(carpeta => (Carpeta: carpeta, Version: VersionAddin.Interpretar(Path.GetFileName(carpeta))))
            .Where(x => x.Version is not null)
            .OrderByDescending(x => x.Version)
            .Skip(VersionesConservadas)
            .Where(x => !activas.Contains(x.Version!.ToString()))
            .Select(x => x.Carpeta);

        foreach (string carpeta in sobrantes)
        {
            string aBorrar = $"{carpeta}.borrar-{Guid.NewGuid():N}";
            try
            {
                Directory.Move(carpeta, aBorrar);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue; // en uso: se reintenta en la próxima instalación
            }
            BorrarSinFallar(aBorrar);
        }

        foreach (string resto in Directory.GetDirectories(raiz, "*.borrar-*"))
            BorrarSinFallar(resto);
    }

    /// <summary>Restos de una descarga que se cortó por un cierre brusco (sin tiempo de limpiar).</summary>
    private void BorrarTemporalesViejos()
    {
        if (!Directory.Exists(rutas.CarpetaTemporal)) return;
        foreach (string carpeta in Directory.GetDirectories(rutas.CarpetaTemporal))
            if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(carpeta) > EdadMaximaDeTemporales)
                BorrarSinFallar(carpeta);
    }

    private static void BorrarSinFallar(string carpeta)
    {
        try
        {
            if (Directory.Exists(carpeta)) Directory.Delete(carpeta, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Se reintenta en la próxima instalación.
        }
    }
}
