namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using practicacq.Modelo;

/// <summary>
/// Ejecuta la generación de armado (J3) dentro de una transacción, en
/// contexto de API válido -- la ventana es modeless y no puede abrir
/// transacciones por su cuenta. Mismo patrón que <see cref="ManejadorGeneracion"/>
/// (columnetas/viguetas) y <see cref="ManejadorSeleccionElemento"/> (#24).
/// </summary>
public class ManejadorGeneracionArmado : IExternalEventHandler
{
    public OpcionesArmado? Opciones { get; set; }
    public Action<ResultadoArmado>? AlTerminar { get; set; }
    public Action<string>? AlFallar { get; set; }

    public void Execute(UIApplication app)
    {
        if (Opciones is null) return;

        Document doc = app.ActiveUIDocument?.Document!;
        if (doc is null) return;

        try
        {
            using var t = new Transaction(doc, "Generar armado de columnas");
            t.Start();

            // Estribos primero: es quien corre BorrarAnteriores (issue #2),
            // así que cualquier longitudinal/menor de una corrida vieja
            // (misma marca, mismo esquema) ya queda limpio antes de que J4
            // cree lo nuevo. También es quien llena
            // res.PosicionesEstribosPorTramo (issue #32) -- J4 depende de que
            // esto ya corrió para saber dónde poner los estribos menores.
            ResultadoArmado res = GeneradorArmado.Generar(doc, Opciones);
            GeneradorLongitudinal.Generar(doc, Opciones, res);

            // Colores por diámetro en las vistas donde se ven las columnas
            // (docs/planes/2026-10-20-colores-por-diametro.md).
            var columnas = Opciones.Plan.Columnas
                .SelectMany(c => c.Contexto?.Tramos ?? new List<Tramo>())
                .Select(tr => long.TryParse(tr.IdElementoHost, out long id) ? new ElementId(id) : null)
                .OfType<ElementId>()
                .ToHashSet();
            int vistas = ColoresEnVistas.Aplicar(doc, columnas, res.Avisos);
            res.Detalles.Add($"Colores por diámetro aplicados en {vistas} vistas.");

            t.Commit();
            AlTerminar?.Invoke(res);
        }
        catch (Exception ex)
        {
            AlFallar?.Invoke(ex.Message);
        }
    }

    public string GetName() => "Generación de armado de columnas";
}
