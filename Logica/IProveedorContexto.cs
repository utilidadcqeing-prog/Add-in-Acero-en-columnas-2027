namespace practicacq.Logica;

using Autodesk.Revit.DB;
using practicacq.Modelo;

/// <summary>
/// La costura entre la ventana y Revit. La ventana pide el contexto de una
/// columna y recibe objetos planos; quién los saca del modelo no le importa.
///
/// Vive en Logica/ y no en Modelo/ a propósito: firma con <see cref="ElementId"/>,
/// que es de Revit, y Modelo/ tiene que poder compilar sin la API para que la
/// ventana corra en un portátil sin Revit instalado. Pendiente de resolver al
/// cerrar el contrato: o el id de la columna viaja como un tipo propio y la
/// interfaz se baja a Modelo/, o la ventana recibe el contexto ya leído y esta
/// interfaz se queda del lado de Revit para siempre.
/// </summary>
public interface IProveedorContexto
{
    /// <summary>
    /// Lee una columna del modelo. No abre transacción: solo consulta.
    /// Lo que no se pudo determinar sale en <see cref="ColumnaContexto.Avisos"/>,
    /// no como excepción, para que un lote no se caiga por una columna mala.
    /// </summary>
    ColumnaContexto Leer(ElementId columna);
}
