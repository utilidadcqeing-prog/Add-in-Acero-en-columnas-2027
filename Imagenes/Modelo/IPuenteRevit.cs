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
}
