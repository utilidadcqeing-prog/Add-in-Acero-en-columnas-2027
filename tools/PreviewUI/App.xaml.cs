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
                    $"Se habrían armado {columnas} columna(s) y {tramos} tramo(s) aceptado(s).",
                    opciones.BorrarArmadoAjeno
                        ? "Reemplazando el acero del add-in y borrando también el que no generó el add-in en esas columnas."
                        : "Reemplazando el acero que el add-in había generado antes en esas columnas.",
                },
            });
        };

        ventana.Show();
    }
}
