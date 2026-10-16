namespace CQEING.Vigas.Calculo.Modelo;

/// <summary>
/// Cambios que hizo el usuario en el tablero, por id: largo total de una barra (Subir / Bajar) y largo escrito
/// para un traslapo. Todo en mm. Se guardan con el pórtico y se vuelven a aplicar en cada cálculo.
/// </summary>
public sealed record CambiosManuales(
    IReadOnlyDictionary<string, int>? Barras = null,
    IReadOnlyDictionary<string, int>? Traslapos = null);

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
    /// <summary>El traslapo se metería en el nudo (a menos de 5 cm de la cara).</summary>
    EntraAlNudo,
    /// <summary>Con ese largo no hay forma de acomodar las barras vecinas cumpliendo las reglas.</summary>
    SinSolucion,
}

/// <summary>Resultado de una vista previa: si el botón Aplicar queda activo, por qué no, y cómo quedaría.</summary>
public sealed record Previsualizacion(bool SePuede, MotivoBloqueo Motivo, ResultadoVigas? Resultado);
