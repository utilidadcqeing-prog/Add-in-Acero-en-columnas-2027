namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>
/// Cambios que hizo el usuario en el tablero, por id: largo total de una barra (Subir / Bajar) y largo escrito
/// para un traslapo. Todo en mm. Se guardan con el pórtico y se vuelven a aplicar en cada cálculo.
/// <paramref name="Ganchos"/> (#84) es el gancho de cada punta del borde: <c>"S.inicio"</c>, <c>"S.fin"</c>,
/// <c>"I.inicio"</c> e <c>"I.fin"</c> para las corridas de cada capa, y <c>"BS1.inicio"</c>… para los bastones.
/// <paramref name="Movidos"/> (#84) son los traslapos arrastrados; se aplican después de los largos.
/// </summary>
public sealed record CambiosManuales(
    IReadOnlyDictionary<string, int>? Barras = null,
    IReadOnlyDictionary<string, int>? Traslapos = null,
    IReadOnlyDictionary<string, TipoGancho>? Ganchos = null,
    IReadOnlyDictionary<string, TraslapoMovido>? Movidos = null);

/// <summary>Un traslapo corrido a mano: dónde empieza (mm). EnVoladizo = el usuario confirmó ponerlo en el voladizo.</summary>
public sealed record TraslapoMovido(int Desde, bool EnVoladizo = false);

/// <summary>Por qué no se puede aplicar un cambio manual.</summary>
public enum MotivoBloqueo
{
    Ninguno,
    /// <summary>No hay barra ni traslapo con ese id.</summary>
    NoExiste,
    /// <summary>El valor es igual al actual (o menor, en un traslapo): no cambia nada.</summary>
    SinCambio,
    /// <summary>El traslapo quedaría más corto que el mínimo.</summary>
    TraslapoBajoElMinimo,
    /// <summary>La barra no sale de la varilla (6 m, o 12 m si ya era larga).</summary>
    NoSaleDeLaVarilla,
    /// <summary>
    /// El traslapo se metería en el nudo (a menos de 5 cm de la cara), pasaría a otra luz o se saldría por la punta
    /// del voladizo.
    /// </summary>
    EntraAlNudo,
    /// <summary>Con ese largo no hay forma de acomodar las barras vecinas cumpliendo las reglas.</summary>
    SinSolucion,
    /// <summary>El largo de un bastón es 0 o negativo.</summary>
    LargoNoValido,
    /// <summary>Un gancho en una punta que no llega al borde del pórtico.</summary>
    NoLlegaAlBorde,
    /// <summary>Un gancho que esa punta no admite: una corrida no puede quedar sin gancho en el borde.</summary>
    GanchoNoValido,
    /// <summary>Un traslapo arrastrado al voladizo sin que el usuario lo confirmara: en un voladizo nunca se traslapa.</summary>
    TraslapoEnVoladizo,
}

/// <summary>Resultado de una vista previa: si el botón Aplicar queda activo, por qué no, y cómo quedaría.</summary>
public sealed record Previsualizacion(bool SePuede, MotivoBloqueo Motivo, ResultadoVigas? Resultado);
