namespace practicacq.PreviewUI;

using System.Windows;
using practicacq.Modelo;
using practicacq.Vistas;

/// <summary>
/// Abre el configurador de estribos con datos simulados. Nada de esto corre
/// dentro de Revit: es solo para poder ver y probar la interfaz.
///
/// Los eventos que en el add-in real van contra el documento
/// (<c>ElementosActivados</c>, <c>SeleccionMarcadaCambio</c>,
/// <c>GenerarSolicitado</c>) acá se enganchan a mensajes en pantalla, así se ve
/// QUÉ se le habría pedido a Revit sin necesitar a Revit.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var ventana = new VentanaConfiguradorEstribos(
            new ProveedorContextoFalso(), new ProveedorTiposFalso(), modoSimulado: true);

        ventana.GenerarSolicitado += opciones =>
        {
            int columnas = opciones.Plan.Columnas.Count;
            int tramos = opciones.Plan.Columnas.Sum(c => c.Tramos.Count(t => t is not null));
            ventana.MostrarResultadoGeneracion(new ResultadoArmado
            {
                Detalles =
                {
                    "Vista previa: acá el add-in real generaría el armado en Revit.",
                    $"Se habrían armado {columnas} columna(s) y {tramos} tramo(s) tildado(s).",
                    opciones.BorrarAnteriores
                        ? "Con borrado previo de TODO el armado existente en esas columnas."
                        : "Sin borrar el armado existente.",
                },
            });
        };

        ventana.Show();
    }
}
