namespace practicacq.Modelo;

/// <summary>Sobre qué lado de la sección se tildaron las casillas que arman una <see cref="CajaMenor"/>.</summary>
public enum EjeCaja
{
    /// <summary>Casillas de fila (posición Y): la caja cruza de borde a borde en X.</summary>
    Fila,

    /// <summary>Casillas de columna (posición X): la caja cruza de borde a borde en Y.</summary>
    Columna,
}

/// <summary>
/// OBSOLETO desde el 20-oct (ver <see cref="EstriboMenor"/>): queda para leer
/// configuraciones guardadas con el panel de casillas.
///
/// Un estribo menor tipo "caja": un rectángulo nested adentro del estribo
/// principal. Cada lado de la sección arma las suyas por separado y se
/// superponen entre sí -- ya NO es el cruce de una fila con una columna
/// (producto cruzado), que era la interpretación anterior (imágenes del
/// usuario, 10-sep): tildar solo de un lado ya alcanza para que salga una caja.
///
/// La caja abarca de <see cref="Desde"/> a <see cref="Hasta"/> sobre su propio
/// <see cref="Eje"/>, y en el eje perpendicular va siempre de extremo a
/// extremo de la sección -- por eso no hay nada que guardar de ese otro eje.
///
/// Cómo se agrupan las casillas tildadas en cajas es responsabilidad de
/// <c>VentanaEstribosMenores.RangosDeCaja</c>; acá solo viaja el resultado.
/// <c>GeneradorLongitudinal.GenerarMenores</c> es quien lo geometriza.
///
/// Las grapas no viven acá: son
/// <see cref="ConfigLongitudinal.GrapasFila"/>/<see cref="ConfigLongitudinal.GrapasColumna"/>
/// -- una grapa cruza de borde a borde en una sola fila o columna, no abarca
/// un rango.
/// </summary>
public sealed class CajaMenor
{
    /// <summary>Sobre qué lado se tildaron las casillas de esta caja.</summary>
    public EjeCaja Eje { get; set; }

    /// <summary>Índice (0-based) sobre <see cref="Eje"/> donde arranca la caja, inclusive.</summary>
    public int Desde { get; set; }

    /// <summary>Índice (0-based) sobre <see cref="Eje"/> donde termina la caja, inclusive.</summary>
    public int Hasta { get; set; }
}
