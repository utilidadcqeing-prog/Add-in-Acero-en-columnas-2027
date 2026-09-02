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

    /// <summary>Lado de la sección paralelo al eje X global, en metros.</summary>
    public double BaseX { get; init; }

    /// <summary>Lado de la sección paralelo al eje Y global, en metros.</summary>
    public double BaseY { get; init; }

    /// <summary>
    /// Recubrimiento lateral leído del parámetro de la columna, en metros.
    /// Se lee, nunca se impone: el desfase del estribo sale de aquí.
    /// </summary>
    public double Recubrimiento { get; init; }

    /// <summary>Cota real de arranque del acero. Si hay zapata, es su cara superior.</summary>
    public double ZInferior { get; init; }

    /// <summary>Cota real del remate, sacada de la geometría y no de «Longitud del sistema».</summary>
    public double ZSuperior { get; init; }

    /// <summary>
    /// Los sólidos que la ventana recorre con las flechas, de abajo hacia arriba.
    /// Alternan tramo libre y nudo de viga, sin huecos entre uno y el siguiente.
    /// </summary>
    public IReadOnlyList<Tramo> Tramos { get; init; } = new List<Tramo>();

    /// <summary>Rótulos de nivel para dibujar la elevación, ordenados de abajo hacia arriba.</summary>
    public IReadOnlyList<NivelRotulo> Niveles { get; init; } = new List<NivelRotulo>();

    /// <summary>Lo que no se pudo leer bien de esta columna. Vacío es lo normal.</summary>
    public IReadOnlyList<string> Avisos { get; init; } = new List<string>();
}

/// <summary>Qué cierra un tramo por abajo y por arriba.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoTramo
{
    /// <summary>Entre dos vigas, o entre el arranque y la primera viga.</summary>
    Libre,

    /// <summary>Dentro del canto de una viga. Aquí no hay confinamiento.</summary>
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
}

/// <summary>Una etiqueta de la elevación: «N+8.41  P3».</summary>
public sealed class NivelRotulo
{
    public string Nombre { get; init; } = "";

    /// <summary>Cota del nivel, en metros.</summary>
    public double Cota { get; init; }
}
