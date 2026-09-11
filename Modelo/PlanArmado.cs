namespace practicacq.Modelo;

using System;
using System.Collections.Generic;

/// <summary>
/// Lo que L5 exporta a JSON al pulsar Aceptar: todas las columnas que se
/// configuraron en la sesión, con su armado completo. Es el criterio de
/// aceptación de L5 hecho tipo — "sin abrir Revit, en el portátil de 8 GB".
/// </summary>
public sealed class PlanArmado
{
    public DateTime GeneradoEl { get; set; } = DateTime.Now;

    public List<ColumnaPlan> Columnas { get; set; } = new();
}

/// <summary>
/// El armado completo de una columna: un <see cref="ConfigTramo"/> por cada
/// tramo de <see cref="Contexto"/>, en el mismo orden e índice que
/// <c>Contexto.Tramos</c> — no se guardan aparte para no correr el riesgo de
/// que se desalineen entre sí.
/// </summary>
public sealed class ColumnaPlan
{
    public string Id { get; set; } = "";

    /// <summary>
    /// El contexto tal como lo devolvió <see cref="IProveedorContexto"/> (falso
    /// o real) en el momento de configurar. Va completo acá adentro para que el
    /// JSON quede autocontenido: se puede auditar un armado sin volver a leer
    /// el modelo de Revit.
    /// </summary>
    public ColumnaContexto? Contexto { get; set; }

    /// <summary>
    /// Uno por tramo de <see cref="Contexto"/>, mismo índice -- pero solo para
    /// los tramos que estaban aceptados en la ventana al generar; los
    /// demás índices quedan en <c>null</c> (no "sin tildar" descartado del
    /// array, porque eso correría los índices y los desalinearía de
    /// <c>Contexto.Tramos</c>). Un tramo en <c>null</c> significa "no pedido
    /// en esta corrida", no "usar la config por defecto".
    /// </summary>
    public List<ConfigTramo?> Tramos { get; set; } = new();

    public ConfigLongitudinal? Longitudinal { get; set; }
}
