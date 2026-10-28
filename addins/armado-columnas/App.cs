using Autodesk.Revit.UI;
using CQEING.Compartido;

namespace practicacq;
public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        // Sin licencia vigente no se crean los botones; el aviso lo da el componente del gestor (issue #189).
        if (!Licencia.Vigente(out _)) return Result.Succeeded;

        RibbonPanel panelEstructural = CintaCqeing.ObtenerPanel(app, "Estructural");

        panelEstructural.AddItem(CintaCqeing.CrearBoton(
            "ComandoEstribos",
            "Acero en\ncolumnas",
            "practicacq.ComandoEstribos",
            "Genera el acero de las columnas seleccionadas: barras longitudinales con sus traslapos, estribos y estribos menores.",
            "icono"));

        RibbonPanel panelHerramientas = CintaCqeing.ObtenerPanel(app, "Herramientas");

        panelHerramientas.AddItem(CintaCqeing.CrearBoton(
            "Comando",
            "Columnetas\ny Viguetas",
            "practicacq.Comando",
            "Genera columnetas y viguetas de confinamiento en los muros seleccionados.",
            "iconoColumnetas"));

#if DEBUG
        // Herramientas de diagnóstico: solo en las compilaciones de desarrollo
        // (Debug). El paquete que se publica (Release) no las trae -- ver el
        // comentario de practicacq.csproj.

        // TEMPORAL -- ver comentario de ComandoDiagnosticoRebar.cs. Sacar
        // este botón (y el comando) una vez confirmado el corrimiento lateral
        // real del traslapo.
        panelHerramientas.AddItem(CintaCqeing.CrearBoton(
            "ComandoDiagnosticoRebar",
            "Diagnóstico\nde Rebar",
            "practicacq.ComandoDiagnosticoRebar",
            "TEMPORAL: selecciona barras y muestra las coordenadas de su geometría, para medir el corrimiento real de un traslapo.",
            "iconoCQ"));

        // TEMPORAL -- ver comentario de ComandoDiagnosticoSplice en
        // ComandoDiagnosticoRebar.cs. Sacar este botón (y el comando) una vez
        // decidido si se migra a RebarSpliceUtils.
        panelHerramientas.AddItem(CintaCqeing.CrearBoton(
            "ComandoDiagnosticoSplice",
            "Diagnóstico\nde Empalme",
            "practicacq.ComandoDiagnosticoSplice",
            "TEMPORAL: prueba la API real de empalmes de Revit (RebarSpliceUtils) sobre una barra seleccionada, sin dejar cambios permanentes.",
            "iconoCQ"));

        // TEMPORAL -- ver comentario de ComandoDiagnosticoBarraContinua.cs.
        // Sacar este botón (y el comando) una vez decidido el diseño del #41.
        panelHerramientas.AddItem(CintaCqeing.CrearBoton(
            "ComandoDiagnosticoBarraContinua",
            "Diagnóstico\nbarra continua",
            "practicacq.ComandoDiagnosticoBarraContinua",
            "TEMPORAL: prueba si una sola barra puede recorrer varias columnas apiladas y llevar varios empalmes. Deja barras de prueba (Ctrl+Z las saca).",
            "iconoCQ"));

        // TEMPORAL -- ver comentario de ComandoDiagnosticoPrevia3D.cs. Sacar
        // este botón (y el comando) una vez cerrado el #123.
        panelHerramientas.AddItem(CintaCqeing.CrearBoton(
            "ComandoDiagnosticoPrevia3D",
            "Diagnóstico\nvista 3D",
            "practicacq.ComandoDiagnosticoPrevia3D",
            "TEMPORAL: mide cuánto tarda la vista previa 3D en generar de prueba una columna y deshacerlo, y comprueba que no queda nada.",
            "iconoCQ"));
#endif

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app)
    {
        return Result.Succeeded;
    }
}
