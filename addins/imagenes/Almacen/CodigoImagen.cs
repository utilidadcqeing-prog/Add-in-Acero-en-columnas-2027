using System.Globalization;

namespace practicacq.Imagenes.Almacen;

/// <summary>
/// Arma el código de una imagen: <c>{codigo_actividad}_{NN}</c> (por ejemplo <c>ACT_006_01</c>).
/// Se usa el código de la actividad y no el número de ítem porque el ítem cambia al reordenar el
/// presupuesto (issue #183, paso 6). Lógica pura, sin Revit.
/// </summary>
public static class CodigoImagen
{
    public static string Armar(string codigoActividad, int numero) =>
        $"{codigoActividad}_{numero.ToString("00", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Siguiente número libre para la actividad a partir de los códigos que ya existen (en esta PC
    /// o en Cloudinary). Empieza en 1. Se toma el mayor + 1, no el primer hueco, para que un código
    /// borrado no se reutilice con otra imagen.
    /// </summary>
    public static int SiguienteNumero(string codigoActividad, IEnumerable<string> codigosExistentes)
    {
        string prefijo = codigoActividad + "_";
        int mayor = 0;
        foreach (string codigo in codigosExistentes)
        {
            if (!codigo.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(codigo[prefijo.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                mayor = Math.Max(mayor, n);
        }
        return mayor + 1;
    }
}
