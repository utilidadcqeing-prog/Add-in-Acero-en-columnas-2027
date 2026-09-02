namespace practicacq.Modelo;

/// <summary>
/// La costura entre la ventana y Revit. La ventana pide el contexto de una
/// columna por su id y recibe objetos planos; quién los saca del modelo no le
/// importa.
///
/// Vive en <c>Modelo/</c> a propósito, y el id de columna viaja como
/// <see cref="string"/> y no como <c>ElementId</c> de Revit: era el TODO que
/// dejaba pendiente la versión anterior de esta interfaz, y L1 lo cierra así
/// para que <c>Modelo/</c> (y con él, toda la ventana) compile y corra sin la
/// API de Revit referenciada — condición del criterio de aceptación de L5
/// ("sin abrir Revit, en el portátil de 8 GB").
///
/// El lado de Revit (<c>Logica/LectorContexto</c>, implementación real de esta
/// interfaz) es quien convierte ese string a <c>ElementId</c> — hoy con
/// <c>new ElementId(long.Parse(id))</c>, simétrico de
/// <c>elemento.Id.Value.ToString()</c> del lado falso. Ver
/// <see cref="ProveedorContextoFalso"/> en <c>Logica/</c> para el lado de
/// desarrollo sin Revit.
/// </summary>
public interface IProveedorContexto
{
    /// <summary>
    /// Lee una columna del modelo por su id. No abre transacción: solo
    /// consulta. Lo que no se pudo determinar sale en
    /// <see cref="ColumnaContexto.Avisos"/>, no como excepción, para que un
    /// lote no se caiga por una columna mala.
    /// </summary>
    ColumnaContexto Leer(string idColumna);

    /// <summary>
    /// Las columnas candidatas para elegir en la lista de selección de L2 —
    /// no su contexto completo (eso es <see cref="Leer"/> por columna, una
    /// vez que se sabe cuáles interesan), solo lo mínimo para poblar la lista:
    /// id y marca.
    /// </summary>
    IReadOnlyList<ColumnaResumen> ListarColumnas();
}

/// <summary>Fila de la lista de selección de L2, antes de leer el contexto completo.</summary>
public sealed record ColumnaResumen(string Id, string Marca);
