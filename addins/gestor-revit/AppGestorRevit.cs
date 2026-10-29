using Autodesk.Revit.UI;
using CQEING.Compartido;
using CQEING.Gestor.Core;

namespace CQEING.GestorRevit;

/// <summary>
/// Registra el panel «Gestor» en la pestaña CQEING. Es un panel propio, aparte de los de los add-ins, para que el
/// botón del gestor nunca se confunda con una herramienta de trabajo. Al abrir Revit consulta, en segundo plano, si hay
/// versiones nuevas de los add-ins instalados y avisa (<see cref="AvisoNovedades"/>). Además anota qué modelo está
/// activo, para que el gestor lo reabra si reinicia Revit (<see cref="ModeloActivo"/>). Issue #159.
/// No exige licencia: su botón siempre está, para poder pedir acceso. Avisa si falta la licencia y la renueva en segundo
/// plano (<see cref="AvisoLicencia"/>, issue #189).
/// </summary>
public class AppGestorRevit : IExternalApplication
{
    private readonly ModeloActivo _modeloActivo = new();

    public Result OnStartup(UIControlledApplication app)
    {
        RibbonPanel panel = CintaCqeing.ObtenerPanel(app, "Gestor");

        var boton = (PushButton)panel.AddItem(CintaCqeing.CrearBoton(
            "ComandoAbrirGestor",
            AvisoNovedades.TextoBoton,
            "CQEING.GestorRevit.ComandoAbrirGestor",
            AvisoNovedades.AyudaBoton,
            "iconoGestor"));

        // Lo viejo se borra también al abrir Revit, no solo al abrir el gestor: así no depende de que alguien lo abra.
        Task.Run(() => LimpiezaVersionesViejas.Ejecutar(RutasGestor.DelUsuario()));
        AvisoNovedades.Empezar(app.ControlledApplication.VersionNumber, boton);
        AvisoLicencia.Empezar();
        _modeloActivo.Empezar(app);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app)
    {
        _modeloActivo.Terminar(app);
        return Result.Succeeded;
    }
}
