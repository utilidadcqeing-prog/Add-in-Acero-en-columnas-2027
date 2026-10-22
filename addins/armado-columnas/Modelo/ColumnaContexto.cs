namespace practicacq.Modelo;

using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// Lo que la ventana necesita saber de una columna para poder configurarla:
/// su sección, su recubrimiento y en qué tramos está partida.
///
/// No lleva una sola referencia a Autodesk.Revit, a propósito. La ventana se
/// desarrolla contra esto y contra un proveedor falso, sin abrir Revit y sin
/// necesitar los 16 GB que pide el modelo real.
///
/// TODAS las medidas van en METROS. La conversión desde los pies internos de
/// Revit ocurre una sola vez, en el lector, y la UI nunca ve un pie.
/// </summary>
public sealed class ColumnaContexto
{
    /// <summary>Marca de la columna («C-12»). Si no la tiene, su id.</summary>
    public string Marca { get; init; } = "";

    /// <summary>
    /// Lado paralelo al eje X global de la ZONA COMÚN en planta de todos los
    /// elementos de la columna (la intersección de sus huellas), en metros: es
    /// donde el generador pone las barras longitudinales (#42). Si toda la
    /// columna tiene la misma sección, es esa. La medida real de cada tramo
    /// está en <see cref="Tramo.Seccion"/>: con un dado de 40×40 debajo de una
    /// columna de 30×30, esto vale 30 y el tramo del dado, 40 (22-oct; antes
    /// valía la del elemento de más abajo, y toda la columna se veía de 40).
    /// </summary>
    public double BaseX { get; init; }

    /// <summary>Lo mismo que <see cref="BaseX"/>, paralelo al eje Y global.</summary>
    public double BaseY { get; init; }

    /// <summary>
    /// Recubrimiento lateral leído del parámetro del elemento de MÁS ABAJO de
    /// la columna, en metros. Es el que se usa si nadie eligió otro: el que
    /// manda al generar es <see cref="ColumnaPlan.Recubrimiento"/>.
    /// </summary>
    public double Recubrimiento { get; init; }

    /// <summary>Cota real de arranque del estribo. Si hay zapata, es su cara superior.</summary>
    public double ZInferior { get; init; }

    /// <summary>
    /// Cota real de arranque del acero LONGITUDINAL -- a diferencia del
    /// estribo (<see cref="ZInferior"/>), si hay zapata el longitudinal entra
    /// a anclarse hasta quedar a 7.5cm de su cara inferior (issue #26). Sin
    /// zapata, vale lo mismo que <see cref="ZInferior"/>.
    /// </summary>
    public double ZInferiorLongitudinal { get; init; }

    /// <summary>
    /// Id del elemento de Revit de la zapata sobre la que arranca esta
    /// columna, si la hay -- mismo formato que <see cref="Tramo.IdElementoHost"/>.
    /// El tramo de anclaje del longitudinal (entre <see cref="ZInferiorLongitudinal"/>
    /// y <see cref="ZInferior"/>) vive DENTRO del sólido de la zapata, no del
    /// de la columna -- Revit no aloja un Rebar cuya curva se sale del
    /// sólido de su host (probado sobre Revit real, ver <see cref="ZInferior"/>
    /// y el generador de longitudinal), así que ese tramo necesita hostearse
    /// en la zapata, no en el primer elemento de la columna. <c>null</c> si
    /// no hay zapata (o del lado del proveedor falso, donde no aplica).
    /// </summary>
    public string? IdZapata { get; init; }

    /// <summary>Cota real del remate, sacada de la geometría y no de «Longitud del sistema».</summary>
    public double ZSuperior { get; init; }

    /// <summary>
    /// Los sólidos que la ventana recorre con las flechas, de abajo hacia arriba.
    /// Alternan tramo libre y nudo de viga, sin huecos entre uno y el siguiente.
    /// </summary>
    public IReadOnlyList<Tramo> Tramos { get; init; } = new List<Tramo>();

    /// <summary>Rótulos de nivel para dibujar la elevación, ordenados de abajo hacia arriba.</summary>
    public IReadOnlyList<NivelRotulo> Niveles { get; init; } = new List<NivelRotulo>();

    /// <summary>
    /// La línea de propiedad del documento, en planta (issue #139): los
    /// ganchos no pueden terminar del otro lado. Vacía si el modelo no tiene
    /// línea de propiedad (o del lado del proveedor falso).
    /// </summary>
    public IReadOnlyList<TramoLindero> Lindero { get; init; } = new List<TramoLindero>();

    /// <summary>Lo que no se pudo leer bien de esta columna. Vacío es lo normal.</summary>
    public IReadOnlyList<string> Avisos { get; init; } = new List<string>();
}

/// <summary>Qué cierra un tramo por abajo y por arriba.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoTramo
{
    /// <summary>Entre dos vigas, o entre el arranque y la primera viga.</summary>
    Libre,

    /// <summary>
    /// Dentro del canto de una viga (el nudo). Lleva estribos UNIFORMES, nunca
    /// confinados (issue #58, William 11-sep): la ventana deshabilita ahí el
    /// modo confinado.
    /// </summary>
    DentroDeViga,

    /// <summary>El de más abajo cuando arranca sobre una zapata: el acero no entra en ella.</summary>
    ContraZapata
}

/// <summary>Un sólido de la columna. Uno por cada paso de la navegación «x / N».</summary>
public sealed class Tramo
{
    /// <summary>Posición en la navegación, empezando en 0.</summary>
    public int Indice { get; init; }

    /// <summary>Cota inferior, en metros.</summary>
    public double ZInferior { get; init; }

    /// <summary>Cota superior, en metros.</summary>
    public double ZSuperior { get; init; }

    public TipoTramo Tipo { get; init; }

    /// <summary>Altura libre del tramo, en metros.</summary>
    public double Altura => ZSuperior - ZInferior;

    /// <summary>
    /// Recubrimiento lateral que tiene en Revit el elemento que contiene este
    /// tramo, en metros (0 si no tiene ninguno asignado). Las piezas de una
    /// misma columna pueden traer valores distintos -- lo típico, un dado de
    /// cimentación con 7,5 cm debajo de columnas con 4 cm. La ventana los
    /// ofrece para elegir UNO para toda la columna (ver
    /// <see cref="ColumnaPlan.Recubrimiento"/>).
    /// </summary>
    public double Recubrimiento { get; init; }

    /// <summary>
    /// Id del elemento de Revit que físicamente contiene este tramo — mismo
    /// formato que <see cref="ColumnaResumen.Id"/>. No es redundante con el id
    /// de la columna: cuando el modelo parte una columna en un elemento por
    /// piso (caso real, confirmado sobre el modelo de prueba: cimentación,
    /// primer piso, nudo de viga y segundo piso llegan como cuatro
    /// <c>FamilyInstance</c> distintos, no como una sola columna continua),
    /// cada tramo puede vivir en un elemento distinto dentro de la misma
    /// columna «lógica». La generación real (J3) lo necesita para saber en
    /// qué elemento hostear cada Rebar. <c>null</c> del lado del proveedor
    /// falso, donde no aplica.
    /// </summary>
    public string? IdElementoHost { get; init; }

    /// <summary>
    /// Lados de la sección del elemento que contiene este tramo, en metros. 0
    /// = no se leyó (proveedor falso, configuraciones viejas): se usa la de la
    /// columna. Ver <see cref="Seccion"/>.
    /// </summary>
    public double LadoX { get; init; }

    /// <summary>Ver <see cref="LadoX"/>.</summary>
    public double LadoY { get; init; }

    /// <summary>
    /// Dónde arranca la zona común de la columna (<see cref="ColumnaContexto.BaseX"/>)
    /// dentro de este tramo, medido desde su esquina de abajo a la izquierda,
    /// en metros. 0 cuando el tramo es la zona común.
    /// </summary>
    public double ComunDesdeX { get; init; }

    /// <summary>Ver <see cref="ComunDesdeX"/>.</summary>
    public double ComunDesdeY { get; init; }

    /// <summary>
    /// La sección de este tramo para dibujarla: sus lados y el corrimiento que
    /// hay que sumarle a lo que se calcula en la zona común (barras, estribos
    /// menores) para llevarlo a este tramo. Sin medida propia, la de la
    /// columna sin corrimiento.
    /// </summary>
    public (double LadoX, double LadoY, double DesdeX, double DesdeY) Seccion(ColumnaContexto columna) =>
        LadoX > 0 && LadoY > 0
            ? (LadoX, LadoY, ComunDesdeX, ComunDesdeY)
            : (columna.BaseX, columna.BaseY, 0, 0);
}

/// <summary>Una etiqueta de la elevación: «N+8.41  P3».</summary>
public sealed class NivelRotulo
{
    public string Nombre { get; init; } = "";

    /// <summary>Cota del nivel, en metros.</summary>
    public double Cota { get; init; }
}
