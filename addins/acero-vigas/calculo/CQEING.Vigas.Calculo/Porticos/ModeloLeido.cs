using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Porticos;

/// <summary>Un punto en planta, en mm (coordenadas del modelo).</summary>
public readonly record struct Punto(double X, double Y);

/// <summary>Eje de la grilla: su nombre y su línea en planta.</summary>
public sealed record EjeLeido(string Nombre, Punto Desde, Punto Hasta);

/// <summary>Nivel de Revit con su cota, en mm.</summary>
public sealed record NivelLeido(string Nombre, double Elevacion);

/// <summary>
/// Una viga del modelo, sin tipos de Revit: su eje en planta, su alto real (de la geometría) y su ancho, en mm.
/// <paramref name="Centro"/> es el centro en planta de su geometría (#85): la línea de ubicación de Revit depende de la
/// justificación y puede ir por una cara. Null = se usa la línea. <paramref name="NombreTipo"/> es el nombre del tipo
/// de Revit (<c>VT_10X35</c>), para reconocer las viguetas (#939).
/// </summary>
public sealed record VigaLeida(string Id, Punto Desde, Punto Hasta, double ZAbajo, double ZArriba, double Ancho, NivelLeido? Nivel,
    Punto? Centro = null, string? NombreTipo = null)
{
    public double Alto => ZArriba - ZAbajo;
}

public enum TipoApoyo
{
    Columna,
    Muro,
    Viga,
}

/// <summary>
/// Una columna o un muro de carga. <paramref name="Huella"/> son puntos de su contorno en planta;
/// <paramref name="Direccion"/> es la de la línea del muro (null en columnas), para descartar los muros que van a lo
/// largo de la viga.
/// </summary>
public sealed record ApoyoLeido(string Id, TipoApoyo Tipo, IReadOnlyList<Punto> Huella, double ZAbajo, double ZArriba, Punto? Direccion = null);

/// <summary>Lo que el adaptador de Revit le pasa al agrupador (#81). Todo en mm.</summary>
public sealed record ModeloLeido(IReadOnlyList<EjeLeido> Ejes, IReadOnlyList<VigaLeida> Vigas, IReadOnlyList<ApoyoLeido> Apoyos);

/// <summary>Un tramo del pórtico (una luz entre apoyos o un voladizo), en mm desde el eje A, con su sección.</summary>
public sealed record TramoLeido(string Nombre, int Desde, int Hasta, bool EsVoladizo, double Ancho, double Alto);

/// <summary>Una viga del pórtico, a lo largo de él, en mm desde el eje A.</summary>
public sealed record VigaEnPortico(string Id, int Desde, int Hasta);

/// <summary>
/// Dónde está el pórtico en el modelo (#85), en mm del modelo. (<paramref name="Ux"/>, <paramref name="Uy"/>) es la
/// dirección en que crece x; (<paramref name="OrigenX"/>, <paramref name="OrigenY"/>), el eje A sobre el centro lateral
/// de la viga. <paramref name="ZAbajo"/> y <paramref name="ZArriba"/> son las caras de abajo y de arriba.
/// </summary>
public sealed record UbicacionPortico(double Ux, double Uy, double OrigenX, double OrigenY, double ZAbajo, double ZArriba,
    double Ancho, double Alto, IReadOnlyList<VigaEnPortico> Vigas);

/// <summary>
/// Un pórtico listo para el motor: <see cref="Portico"/> es lo que recibe <c>MotorVigas.Calcular</c>; el resto es
/// lo que muestra la lista de pórticos del tablero. <paramref name="FondosDeApoyo"/> es lo que mide cada apoyo de lado a
/// lado de la viga, en mm (el ancho a lo largo ya está en <see cref="Portico"/>), para la sección de columnas (#100).
/// <paramref name="Ubicacion"/> es para generar el acero en Revit (#85). <paramref name="Elemento"/> es si se detectó
/// como viga o vigueta (#939).
/// </summary>
public sealed record PorticoLeido(
    string Nombre,
    string? Eje,
    string? Nivel,
    TipoViga Tipo,
    Portico Portico,
    IReadOnlyList<TipoApoyo> TiposDeApoyo,
    IReadOnlyList<TramoLeido> Tramos,
    IReadOnlyList<string> Vigas,
    IReadOnlyList<double>? FondosDeApoyo = null,
    UbicacionPortico? Ubicacion = null,
    Elemento Elemento = Elemento.Viga);

public enum TipoAvisoLectura
{
    /// <summary>Vigas sin ningún apoyo que las cruce en planta y en alto.</summary>
    SinApoyos,
    /// <summary>El pórtico no coincide con ningún eje de la grilla.</summary>
    SinEje,
    /// <summary>La sección cambia entre tramos (#92).</summary>
    CambioDeSeccion,
    /// <summary>La viga no tiene nivel: se toma como aérea.</summary>
    SinNivel,
}

/// <summary>Aviso de la lectura; <paramref name="Elemento"/> es el nombre del pórtico o los ids de las vigas.</summary>
public sealed record AvisoLectura(TipoAvisoLectura Tipo, string Elemento);

/// <summary>
/// Lo que sale del agrupador. <paramref name="Modelo"/> es lo leído de Revit, para la vista en planta del tablero: ejes,
/// columnas y el nivel y el tipo de cada viga (planta, 27-oct). Null = no se tiene (la planta va sin ejes ni columnas).
/// </summary>
public sealed record ResultadoLectura(IReadOnlyList<PorticoLeido> Porticos, IReadOnlyList<AvisoLectura> Avisos, ModeloLeido? Modelo = null);
