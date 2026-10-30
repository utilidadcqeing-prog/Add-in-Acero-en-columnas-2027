namespace practicacq.Modelo;

using System.Collections.Generic;

/// <summary>
/// Valida un <see cref="ConfigTramo"/> campo por campo, con mensaje concreto
/// -- nunca "valor inválido" a secas. Salió de <c>VentanaConfiguradorEstribos</c>
/// sin cambiar sus mensajes, para que la interfaz web diga lo mismo (#209).
/// </summary>
public static class ValidacionTramo
{
    /// <summary>
    /// Si se pasa el tramo, suma el chequeo de norma (L1 ≥ altura/2 no deja
    /// zona central); sin tramo (por ejemplo al validar la plantilla de
    /// "aplicar a selección", donde la altura depende de a cuáles se les vaya
    /// a aplicar) se valida solo lo que no depende de un tramo puntual.
    /// </summary>
    public static List<string> Validar(ConfigTramo cfg, Tramo? tramo)
    {
        var errores = new List<string>();

        if (string.IsNullOrWhiteSpace(cfg.TipoBarraNombre))
            errores.Add("Elige un tipo de barra.");
        if (string.IsNullOrWhiteSpace(cfg.TipoGanchoNombre))
            errores.Add("Elige un tipo de gancho.");

        if (cfg.Modo == ModoEstribo.Uniforme)
        {
            if (cfg.EspaciadoUniforme <= 0)
                errores.Add("El espaciado tiene que ser mayor que cero.");
        }
        else
        {
            if (cfg.LongitudConfinamiento <= 0)
                errores.Add("La longitud de confinamiento (L1) tiene que ser mayor que cero.");
            if (cfg.EspaciadoConfinado <= 0)
                errores.Add("El espaciado confinado tiene que ser mayor que cero.");
            if (cfg.EspaciadoNoConfinado <= 0)
                errores.Add("El espaciado no confinado tiene que ser mayor que cero.");

            if (tramo is not null && tramo.Altura > 0 && cfg.LongitudConfinamiento * 2 >= tramo.Altura)
                errores.Add(
                    $"L1 ({cfg.LongitudConfinamiento:0.00} m) x2 ≥ la altura del tramo " +
                    $"({tramo.Altura:0.00} m): no queda zona central.");

            // El caso "esto es un nudo" no se valida acá: desde el issue #58
            // el modo Confinado no se puede elegir en un nudo.
        }

        return errores;
    }
}
