namespace practicacq.Imagenes.Modelo;

/// <summary>
/// Todo lo que la ventana le pide a Revit. Misma regla que el resto del repo: las ventanas no
/// referencian <c>Autodesk.Revit</c>; hablan con Revit solo a través de esta interfaz, y la
/// implementación (<c>Imagenes/Revit/PuenteRevit.cs</c>) pasa cada pedido por un
/// <c>ExternalEvent</c>, porque la ventana es no modal.
///
/// Todas las respuestas llegan por callback en el hilo de Revit, que es el mismo hilo de la
/// ventana: se puede tocar la interfaz directamente desde ellas.
/// </summary>
public interface IPuenteRevit
{
    /// <summary>Id del modelo abierto, calculado igual que en el add-in de cantidades.</summary>
    string IdModeloActivo { get; }

    /// <summary>Nombre del modelo abierto (título del documento de Revit).</summary>
    string NombreModeloActivo { get; }

    /// <summary>
    /// Empieza el modo selección: cada vez que cambia la selección en Revit se avisa con la
    /// cantidad de elementos. Con <paramref name="mismoTipo"/>, un clic en un elemento selecciona
    /// también todos los del mismo tipo visibles en la vista.
    /// </summary>
    void IniciarSeleccion(bool mismoTipo, Action<int> alCambiarCantidad);

    /// <summary>Cambia la opción "mismo tipo" sin salir del modo selección.</summary>
    void CambiarMismoTipo(bool mismoTipo);

    /// <summary>
    /// Termina el modo selección y devuelve lo seleccionado agrupado por tipo, o un error en texto
    /// (vista no soportada, nada seleccionado…).
    /// </summary>
    void TerminarSeleccion(Action<ResultadoSeleccion?, string?> alTerminar);

    void CancelarSeleccion();

    /// <summary>
    /// Genera el PNG: copia la vista activa, pinta cada tipo con su color, deja el resto como
    /// fantasma y exporta. Devuelve la ruta del PNG o un error en texto.
    /// </summary>
    void Capturar(ResultadoSeleccion seleccion, string rutaSinExtension, Action<string?, string?> alTerminar);

    /// <summary>
    /// Rehace una imagen guardada (issue #186): busca la vista y los elementos por <c>UniqueId</c>,
    /// les pone los mismos colores, aplica la cámara guardada y exporta. Los elementos que ya no
    /// existen se omiten y se cuentan en <see cref="ResultadoRecaptura.Faltantes"/>.
    /// </summary>
    void Recapturar(ImagenActividad imagen, string rutaSinExtension, Action<ResultadoRecaptura?, string?> alTerminar);
}

public sealed class ResultadoRecaptura
{
    public string RutaPng { get; init; } = string.Empty;
    public int Encontrados { get; init; }

    /// <summary>Elementos guardados que ya no están en el modelo.</summary>
    public int Faltantes { get; init; }

    /// <summary>Vista usada (puede ser otra 3D si la original se borró).</summary>
    public string NombreVista { get; init; } = string.Empty;
}
