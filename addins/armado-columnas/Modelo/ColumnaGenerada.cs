namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Con qué se generó el acero de una columna (#180): al volver a elegirla en
/// la ventana se muestra esto, en vez de los valores por defecto o de la
/// configuración elegida en la lista. Se guarda junto con la medida que tenía
/// la columna al generar, para avisar si después cambió en Revit.
///
/// Lo escribe <c>Logica/RegistroGeneradosRevit</c> dentro del modelo, en la
/// misma transacción que genera el acero.
/// </summary>
public sealed class ColumnaGenerada
{
    /// <summary>
    /// Uno por tramo, mismo índice que <see cref="ColumnaContexto.Tramos"/>:
    /// lo último con que se generó cada uno, o <c>null</c> si nunca se generó.
    /// </summary>
    public List<ConfigTramo?> Tramos { get; set; } = new();

    public ConfigLongitudinal Longitudinal { get; set; } = new();

    /// <summary>El recubrimiento elegido en la ventana, o null si se usó el de la columna.</summary>
    public double? Recubrimiento { get; set; }

    public DateTime GeneradoEl { get; set; }

    // La columna como estaba al generar (metros).
    public double BaseX { get; set; }
    public double BaseY { get; set; }
    public double ZInferior { get; set; }
    public double ZSuperior { get; set; }

    /// <summary>
    /// Suma una corrida a lo que ya estaba: los tramos que se generaron ahora
    /// reemplazan a los de antes, los demás quedan (su acero sigue en el
    /// modelo: regenerar solo borra los estribos de los tramos aceptados, #145).
    /// El longitudinal se regenera siempre entero, así que manda el nuevo.
    /// </summary>
    public static ColumnaGenerada Combinar(ColumnaGenerada? previa, ColumnaPlan plan, DateTime cuando)
    {
        ColumnaContexto? contexto = plan.Contexto;
        int cuantos = contexto?.Tramos.Count ?? plan.Tramos.Count;

        var tramos = new List<ConfigTramo?>();
        for (int i = 0; i < cuantos; i++)
            tramos.Add(i < plan.Tramos.Count && plan.Tramos[i] is ConfigTramo nueva
                ? nueva
                : previa is not null && i < previa.Tramos.Count ? previa.Tramos[i] : null);

        return new ColumnaGenerada
        {
            Tramos = tramos,
            Longitudinal = plan.Longitudinal?.Copiar() ?? previa?.Longitudinal ?? new ConfigLongitudinal(),
            Recubrimiento = plan.Recubrimiento,
            GeneradoEl = cuando,
            BaseX = contexto?.BaseX ?? previa?.BaseX ?? 0,
            BaseY = contexto?.BaseY ?? previa?.BaseY ?? 0,
            ZInferior = contexto?.ZInferior ?? previa?.ZInferior ?? 0,
            ZSuperior = contexto?.ZSuperior ?? previa?.ZSuperior ?? 0,
        };
    }

    /// <summary>
    /// Qué cambió en la columna desde que se generó (vacío si nada). Medio
    /// centímetro de tolerancia: lo que lee Revit trae decimales de más.
    /// </summary>
    public IReadOnlyList<string> Cambios(ColumnaContexto actual)
    {
        const double tolerancia = 0.005;
        var cambios = new List<string>();

        if (Math.Abs(actual.BaseX - BaseX) > tolerancia || Math.Abs(actual.BaseY - BaseY) > tolerancia)
            cambios.Add($"la sección pasó de {BaseX * 100:0.#}×{BaseY * 100:0.#} a {actual.BaseX * 100:0.#}×{actual.BaseY * 100:0.#} cm");
        if (Math.Abs(actual.ZInferior - ZInferior) > tolerancia || Math.Abs(actual.ZSuperior - ZSuperior) > tolerancia)
            cambios.Add($"la altura pasó de {ZSuperior - ZInferior:0.00} a {actual.ZSuperior - actual.ZInferior:0.00} m");
        if (actual.Tramos.Count != Tramos.Count)
            cambios.Add($"tenía {Tramos.Count} tramo(s) y ahora tiene {actual.Tramos.Count}");

        return cambios;
    }

    /// <summary>Si algún tramo se generó alguna vez.</summary>
    public bool AlgunTramo => Tramos.Any(t => t is not null);
}

/// <summary>
/// De dónde lee la ventana con qué se generó cada columna (#180). Igual que
/// <see cref="IAlmacenConfiguraciones"/>: la ventana no conoce Revit.
/// </summary>
public interface IRegistroGenerados
{
    /// <summary>Lo generado en esa columna (por el id que usa la ventana), o null.</summary>
    ColumnaGenerada? Leer(string idColumna);
}

/// <summary>Sin Revit (vista previa, pruebas): empieza vacío.</summary>
public sealed class RegistroGeneradosEnMemoria : IRegistroGenerados
{
    private readonly Dictionary<string, ColumnaGenerada> _porColumna = new();

    public ColumnaGenerada? Leer(string idColumna) => _porColumna.TryGetValue(idColumna, out ColumnaGenerada? g) ? g : null;

    public void Recordar(string idColumna, ColumnaGenerada generada) => _porColumna[idColumna] = generada;
}
