namespace practicacq.Modelo;

using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Una configuración con nombre, para no repetir los mismos clics en cada
/// columna (pedido de Luciano, 10-sep). Guarda el panel derecho completo: lo
/// del tramo (modo, tipo de barra, gancho, espaciados) y lo del acero
/// longitudinal de la columna.
///
/// Elegir una configuración solo CARGA el panel: de ahí sale hacia los tramos
/// cuando se aprieta Aceptar. Cargar y aceptar son dos gestos
/// distintos a propósito -- ver <c>TramosResaltados</c> en
/// <c>VentanaConfiguradorEstribos</c>.
/// </summary>
public sealed class ConfiguracionGuardada
{
    public string Nombre { get; set; } = "";

    public ConfigTramo Tramo { get; set; } = new();

    public ConfigLongitudinal Longitudinal { get; set; } = new();
}

/// <summary>
/// De dónde salen y a dónde van las configuraciones guardadas. La ventana solo
/// conoce esta interfaz -- igual que con <see cref="IProveedorContexto"/> y
/// <see cref="IProveedorTipos"/>, no hay una línea de <c>Autodesk.Revit</c> del
/// lado de <c>Vistas/</c>.
///
/// La implementación real las guarda DENTRO del modelo de Revit (decisión de
/// Luciano: así viajan con el proyecto y las ve cualquiera que lo abra), ver
/// <c>Logica/AlmacenConfiguracionesRevit</c>.
/// </summary>
public interface IAlmacenConfiguraciones
{
    IReadOnlyList<ConfiguracionGuardada> Listar();

    /// <summary>Agrega, o reemplaza la que ya tuviera ese nombre.</summary>
    void Guardar(ConfiguracionGuardada configuracion);

    void Borrar(string nombre);
}

/// <summary>
/// Almacén de mentira: vive en memoria y se pierde al cerrar. Es el que usa la
/// vista previa sin Revit (<c>tools/PreviewUI</c>) y el que queda por defecto
/// si nadie pasa uno real, para que la ventana nunca dependa de tener Revit
/// para abrirse.
/// </summary>
public sealed class AlmacenConfiguracionesEnMemoria : IAlmacenConfiguraciones
{
    private readonly List<ConfiguracionGuardada> _guardadas = new();

    public IReadOnlyList<ConfiguracionGuardada> Listar() => _guardadas;

    public void Guardar(ConfiguracionGuardada configuracion)
    {
        _guardadas.RemoveAll(g => g.Nombre == configuracion.Nombre);
        _guardadas.Add(configuracion);
    }

    public void Borrar(string nombre) => _guardadas.RemoveAll(g => g.Nombre == nombre);
}

/// <summary>
/// Si dos configuraciones dicen lo mismo, campo por campo. Se compara el JSON
/// (el mismo que se guarda en el modelo), así un campo nuevo entra solo en la
/// comparación sin acordarse de agregarlo acá.
/// </summary>
public static class ComparadorConfig
{
    public static bool Igual(ConfigTramo? a, ConfigTramo? b) => Json(a) == Json(b);

    public static bool Igual(ConfigLongitudinal? a, ConfigLongitudinal? b) => Json(a) == Json(b);

    private static string Json<T>(T valor) => JsonSerializer.Serialize(valor);
}
