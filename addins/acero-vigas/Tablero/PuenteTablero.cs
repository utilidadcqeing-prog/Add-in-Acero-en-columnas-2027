using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Tablero;
using CQEING.Vigas.Revit;

namespace CQEING.Vigas.Tablero;

/// <summary>
/// Une la página del tablero (#83) con <see cref="SesionTablero"/> y con Revit. Cada mensaje de la página se atiende en
/// el hilo de Revit (la sesión lee el modelo): lo que la sesión devuelve para la página va con
/// <see cref="VentanaTablero.Enviar"/> y lo que toca Revit, con <see cref="AccionesRevit.Hacer"/>.
/// También sigue a Revit: "En vivo" con la selección y la recarga al cambiar o cerrar el documento.
/// </summary>
internal sealed class PuenteTablero
{
    private readonly VentanaTablero ventana;
    private readonly UIApplication uiapp;
    private readonly AccionesRevit acciones = new();
    private readonly SesionTablero sesion;
    private Document? leido;

    public PuenteTablero(VentanaTablero ventana, UIApplication uiapp)
    {
        this.ventana = ventana;
        this.uiapp = uiapp;
        leido = uiapp.ActiveUIDocument?.Document;
        sesion = new SesionTablero(Leer, new AlmacenReglas(AlmacenReglas.RutaPorDefecto), leido?.Title ?? "");

        ventana.MensajeRecibido += json => acciones.Encolar(app => Atender(app, ProtocoloTablero.Leer(json)));
        acciones.Fallo += texto => ventana.Enviar(ProtocoloTablero.Aviso($"No se pudo hacer en Revit: {texto}"));
        uiapp.SelectionChanged += AlCambiarSeleccion;
        uiapp.ViewActivated += AlActivarVista;
        uiapp.Application.DocumentClosing += AlCerrarDocumento;
        ventana.Closed += (_, _) => Soltar();
    }

    /// <summary>Atiende un mensaje en el hilo de Revit. <paramref name="desdeRevit"/>: no se devuelve la selección a Revit.</summary>
    private void Atender(UIApplication app, MensajeTablero mensaje, bool desdeRevit = false)
    {
        foreach (Salida salida in sesion.Manejar(mensaje))
        {
            if (salida is AJs js)
                ventana.Enviar(js.Json);
            else if (salida is ARevit(AccionRevit accion, IReadOnlyList<string> vigas) && !desdeRevit)
                Hacer(app, accion, vigas);
        }
    }

    private void Hacer(UIApplication app, AccionRevit accion, IReadOnlyList<string> vigas)
    {
        IReadOnlyList<string> elegidas = AccionesRevit.Hacer(app, accion, vigas);
        if (accion != AccionRevit.PedirSeleccion || elegidas.Count == 0)
            return;
        // "Seleccionar en Revit": se abre el pórtico de la primera viga elegida que esté en alguno.
        if (elegidas.Select(sesion.PorticoDeViga).FirstOrDefault(p => p is not null) is string portico)
            Atender(app, new ElegirPortico(portico), desdeRevit: true);
        else
            ventana.Enviar(ProtocoloTablero.Aviso("Esas vigas no están en ningún pórtico del tablero."));
    }

    /// <summary>Lee los pórticos del documento activo; sin documento, ninguno.</summary>
    private ResultadoLectura Leer()
    {
        leido = uiapp.ActiveUIDocument?.Document;
        return leido is null || leido.IsFamilyDocument ? new ResultadoLectura([], []) : AgrupadorPorticos.Agrupar(LectorModelo.Leer(leido));
    }

    private void Recargar(UIApplication app)
    {
        sesion.Recargar(app.ActiveUIDocument?.Document?.Title ?? "");
        ventana.Enviar(sesion.Vista().Json);
    }

    // Los manejadores de eventos de Revit nunca dejan escapar una excepción: Revit los llama desde su lado nativo y
    // podría caerse entero. Solo encolan; el trabajo corre en AccionesRevit, que ya atrapa los errores.

    /// <summary>"En vivo" (decisión 10): una viga elegida en Revit abre su pórtico, si es otro.</summary>
    private void AlCambiarSeleccion(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (!sesion.EnVivo || !e.GetDocument().Equals(leido))
                return;
            List<string> ids = e.GetSelectedElements().Select(id => id.ToString()).ToList();
            acciones.Encolar(app =>
            {
                if (ids.Select(sesion.PorticoDeViga).FirstOrDefault(p => p is not null) is string portico && portico != sesion.Actual)
                    Atender(app, new ElegirPortico(portico), desdeRevit: true);
            });
        }
        catch
        {
            // ver arriba
        }
    }

    /// <summary>Otro documento pasó a ser el activo: el tablero muestra sus pórticos.</summary>
    private void AlActivarVista(object? sender, ViewActivatedEventArgs e)
    {
        try
        {
            if (!e.Document.Equals(leido))
                acciones.Encolar(Recargar);
        }
        catch
        {
            // ver arriba
        }
    }

    /// <summary>Se cierra el documento leído: al terminar, el tablero lee el que quede activo (o ninguno).</summary>
    private void AlCerrarDocumento(object? sender, DocumentClosingEventArgs e)
    {
        try
        {
            if (e.Document.Equals(leido))
            {
                leido = null;
                acciones.Encolar(Recargar);
            }
        }
        catch
        {
            // ver arriba
        }
    }

    private void Soltar()
    {
        try
        {
            uiapp.SelectionChanged -= AlCambiarSeleccion;
            uiapp.ViewActivated -= AlActivarVista;
            uiapp.Application.DocumentClosing -= AlCerrarDocumento;
        }
        catch
        {
            // Revit puede estar cerrándose.
        }
        acciones.Dispose();
    }
}
