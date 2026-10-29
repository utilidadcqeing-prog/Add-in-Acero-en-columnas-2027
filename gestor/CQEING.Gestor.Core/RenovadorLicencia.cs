using CQEING.Compartido;

namespace CQEING.Gestor.Core;

/// <param name="UltimaRespuesta">Lo que respondió el servicio en esta renovación; null si no se consultó.</param>
public sealed record InfoLicencia(EstadoSolicitud? UltimaRespuesta, bool Vigente, string? Nombre, DateTime? VenceUtc, string Motivo, string? CodigoEquipo);

/// <summary>
/// Mantiene al día licencia.json: lo usan el gestor al abrirse y el componente del gestor al abrir Revit (issue #189).
/// Solo consulta si la licencia no es vigente o se emitió hace más de 12 horas (Decisión 4), y nunca guarda una licencia
/// sin comprobar antes la firma y el código del equipo (Decisión 7). Sin conexión no toca nada: la licencia vigente sigue.
/// </summary>
public sealed class RenovadorLicencia(ServicioLicencias servicio, string carpeta, Func<DateTime> ahoraUtc, string? codigoEquipo, string clavePublicaPem)
{
    private const string NombreArchivoNombre = "licencia-nombre.txt";
    private static readonly TimeSpan Renovar = TimeSpan.FromHours(12);

    public static RenovadorLicencia Crear(HttpClient http) =>
        new(new ServicioLicencias(http), Licencia.CarpetaPorDefecto, () => DateTime.UtcNow, CodigoEquipo.Actual(), Licencia.ClavePublica);

    private string ArchivoLicencia => Path.Combine(carpeta, Licencia.NombreArchivo);

    private string ArchivoNombre => Path.Combine(carpeta, NombreArchivoNombre);

    /// <summary>El nombre con que este equipo pidió acceso, o null si nunca lo pidió.</summary>
    public string? NombreGuardado
    {
        get
        {
            try
            {
                return File.Exists(ArchivoNombre) && File.ReadAllText(ArchivoNombre).Trim() is { Length: > 0 } nombre ? nombre : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>La licencia guardada, sin consultar al servicio.</summary>
    public InfoLicencia Estado() => Info(Licencia.Leer(carpeta, codigoEquipo, ahoraUtc(), clavePublicaPem), null);

    /// <param name="nombre">El que escribió el usuario al pedir acceso; null para usar el guardado.</param>
    /// <param name="forzar">Consulta aunque la licencia sea reciente (el botón «Solicitar acceso»).</param>
    public async Task<InfoLicencia> RenovarAsync(string? nombre, bool forzar, string versionGestor, CancellationToken ct = default)
    {
        DateTime ahora = ahoraUtc();
        if (!string.IsNullOrWhiteSpace(nombre)) GuardarNombre(nombre.Trim());

        EstadoLicencia actual = Licencia.Leer(carpeta, codigoEquipo, ahora, clavePublicaPem);
        Licencia.AnotarVista(carpeta, ahora);
        if (codigoEquipo is null) return Info(actual, null);
        if (!forzar && actual.EsVigente && ahora - actual.EmitidaUtc < Renovar) return Info(actual, null);

        var datos = new DatosSolicitud(codigoEquipo, NombreGuardado ?? Environment.UserName, Environment.MachineName, Environment.UserName, versionGestor);
        RespuestaServicio respuesta = await servicio.PedirAsync(datos, ct);

        if (respuesta is { Estado: EstadoSolicitud.Aprobada, ContenidoLicencia: { } contenido }
            && Licencia.Verificar(contenido, codigoEquipo, ahora, Licencia.UltimaVista(carpeta), clavePublicaPem).EsVigente)
            GuardarLicencia(contenido);
        else if (respuesta.Estado == EstadoSolicitud.Revocada)
            BorrarLicencia();

        return Info(Licencia.Leer(carpeta, codigoEquipo, ahora, clavePublicaPem), respuesta.Estado);
    }

    private InfoLicencia Info(EstadoLicencia estado, EstadoSolicitud? respuesta) =>
        new(respuesta, estado.EsVigente, estado.Nombre, estado.VenceUtc, estado.Motivo(codigoEquipo), codigoEquipo);

    /// <summary>
    /// Escribe a un temporal y lo mueve: un corte a mitad nunca deja una licencia a medias. El temporal tiene nombre único
    /// porque el gestor y el componente dentro de Revit pueden renovar a la vez.
    /// </summary>
    private void GuardarLicencia(string contenido)
    {
        try
        {
            Directory.CreateDirectory(carpeta);
            string temporal = $"{ArchivoLicencia}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporal, contenido);
            File.Move(temporal, ArchivoLicencia, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Se vuelve a pedir en la próxima renovación.
        }
    }

    private void BorrarLicencia()
    {
        try
        {
            File.Delete(ArchivoLicencia);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Si no se puede borrar, igual vence sola.
        }
    }

    private void GuardarNombre(string nombre)
    {
        try
        {
            Directory.CreateDirectory(carpeta);
            File.WriteAllText(ArchivoNombre, nombre);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Sin nombre guardado se manda el usuario de Windows.
        }
    }
}
