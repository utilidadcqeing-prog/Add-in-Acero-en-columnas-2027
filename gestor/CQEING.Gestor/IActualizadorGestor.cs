using CQEING.Gestor.Core;

namespace CQEING.Gestor;

/// <summary>Autoactualización del propio gestor. La ventana no sabe de Velopack: así se puede abrir con datos simulados.</summary>
public interface IActualizadorGestor
{
    /// <summary>Devuelve la versión nueva del gestor, o null si no hay (o no se pudo saber: sin red, sin instalar).</summary>
    Task<string?> BuscarAsync(EtiquetaRelease? ultimoPublicado);

    /// <summary>Descarga la versión encontrada por <see cref="BuscarAsync"/>, la aplica y reinicia el gestor.</summary>
    Task AplicarAsync(IProgress<int> progreso);
}
