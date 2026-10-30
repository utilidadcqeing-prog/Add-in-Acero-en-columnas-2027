namespace practicacq.Tablero;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using practicacq.Logica;
using practicacq.Modelo;

/// <summary>
/// Une la página de columnas (#207) con <see cref="SesionColumnas"/> y con
/// Revit. Cada mensaje de la página se atiende en el hilo de Revit, por la
/// <see cref="ColaRevit"/> (la sesión lee el modelo): la vista vuelve a la
/// página y lo que la sesión deja pedido (seleccionar, la vista 3D, generar)
/// se hace ahí mismo, con los manejadores de siempre.
/// </summary>
internal sealed class PuenteColumnas
{
    private readonly VentanaColumnas ventana;
    private readonly UIApplication uiapp;
    private readonly Document documento;
    private readonly ColaRevit cola = new();
    private readonly SesionColumnas sesion;

    private readonly ManejadorGeneracionArmado generar = new();
    private readonly ManejadorVistaPrevia3D previa3D = new();
    private readonly ManejadorSeleccionElemento seleccionar = new() { CentrarVista = true };
    private readonly ManejadorPersistirConfiguraciones persistir = new();
    private BarraSeleccion? barra;

    public PuenteColumnas(VentanaColumnas ventana, UIApplication uiapp, Document documento)
    {
        this.ventana = ventana;
        this.uiapp = uiapp;
        this.documento = documento;

        // Las configuraciones con nombre viven DENTRO del modelo (Extensible
        // Storage): guardarlas necesita transacción, y se hace en la cola.
        var almacen = new AlmacenConfiguracionesRevit(documento);
        persistir.Almacen = almacen;
        persistir.AlFallar = motivo => Enviar(sesion!.Avisar($"No se pudieron guardar las configuraciones en el modelo: {motivo}"));
        almacen.CambiosPendientes += () => cola.Encolar(persistir.Execute);

        sesion = new SesionColumnas(new LectorContexto(documento), new ProveedorTiposReal(documento), almacen, new RegistroGeneradosRevit(documento))
        {
            Modelo = documento.Title,
        };

        generar.AlTerminar = resultado => Enviar(sesion.ResultadoGeneracion(resultado));
        generar.AlFallar = mensaje => Enviar(sesion.ErrorGeneracion(mensaje));
        previa3D.Documento = documento;
        // El estado y los números de varilla dibujados van al panel 3D de la página (decisión 18).
        previa3D.AlTerminar = resumen => Enviar(sesion.Resultado3D(resumen));

        ventana.MensajeRecibido += json => cola.Encolar(app => Atender(app, json));
        cola.Fallo += texto => Enviar(sesion.Avisar($"No se pudo hacer en Revit: {texto}"));
        uiapp.SelectionChanged += AlCambiarSeleccion;
        uiapp.Application.DocumentClosing += AlCerrarDocumento;
        ventana.Closed += (_, _) => Soltar();
    }

    /// <summary>Atiende un mensaje de la página en el hilo de Revit.</summary>
    private void Atender(UIApplication app, string json)
    {
        Enviar(sesion.Atender(json));
        HacerLoPedido(app);
    }

    /// <summary>Lo que la sesión dejó pedido a Revit, en el orden en que conviene: elegir, mirar, generar.</summary>
    private void HacerLoPedido(UIApplication app)
    {
        if (sesion.SeleccionPedida)
        {
            sesion.SeleccionPedida = false;
            EmpezarSeleccion(app);
        }

        if (sesion.SeleccionEnModelo is IReadOnlyList<string> elementos)
        {
            sesion.SeleccionEnModelo = null;
            seleccionar.Objetivos = elementos.Select(id => long.TryParse(id, out long n) ? new ElementId(n) : null).OfType<ElementId>().ToList();
            seleccionar.Execute(app);
        }

        if (sesion.Pedido3D is FiltroPrevia3D filtro)
        {
            sesion.Pedido3D = null;
            bool abrir = sesion.Abrir3D;
            sesion.Abrir3D = false;
            if (sesion.OpcionesPara3D() is OpcionesArmado plan)
            {
                // La ventana siempre queda encima de Revit: se aparta para que se vea la 3D; vuelve con el botón de la cinta.
                if (abrir) ventana.Apartar();
                previa3D.Plan = plan;
                previa3D.Filtro = filtro;
                previa3D.AbrirVista |= abrir;
                previa3D.Execute(app);
            }
        }

        if (sesion.GeneracionPedida is OpcionesArmado opciones)
        {
            sesion.GeneracionPedida = null;
            generar.Opciones = opciones;
            generar.Execute(app);
            app.ActiveUIDocument?.RefreshActiveView();
        }
    }

    private void Enviar(VistaColumnas vista) => ventana.Enviar("{\"tipo\":\"vista\",\"vista\":" + vista.AJson() + "}");

    // ------------------------------------------------------------- «Seleccionar en Revit»

    /// <summary>
    /// La ventana se aparta y la barra del add-in queda arriba del dibujo
    /// mientras el usuario elige columnas con la selección normal de Revit.
    /// «Terminar» deja elegidas en la página las columnas seleccionadas.
    /// </summary>
    private void EmpezarSeleccion(UIApplication app)
    {
        if (barra is not null)
        {
            barra.Activate();
            return;
        }
        ventana.Apartar();
        Rectangle area = app.DrawingAreaExtents;
        var nueva = new BarraSeleccion(app.MainWindowHandle, (area.Left, area.Top, area.Right, area.Bottom));
        nueva.Todas += () => cola.Encolar(x =>
            x.ActiveUIDocument?.Selection.SetElementIds(
                sesion.ElementosEnRevit.Select(id => long.TryParse(id, out long n) ? new ElementId(n) : null).OfType<ElementId>().ToList()));
        nueva.Terminar += () => cola.Encolar(x =>
        {
            List<string> elegidas = Seleccion(x);
            TerminarSeleccion();
            Enviar(sesion.SincronizarDesdeRevit(elegidas));
            HacerLoPedido(x);
        });
        nueva.Cancelar += TerminarSeleccion;
        // Se cierre como se cierre (Terminar, Cancelar, Alt+F4 o la ventana), se suelta y la ventana vuelve.
        nueva.Closed += (_, _) =>
        {
            if (barra != nueva)
                return;
            barra = null;
            ventana.Volver();
        };
        barra = nueva;
        Contar(Seleccion(app));
        nueva.Show();
    }

    private void TerminarSeleccion() => barra?.Close();

    /// <summary>Cuántas columnas hay elegidas: dos pisos de la misma columna cuentan una (hallazgo 12 del plan).</summary>
    private void Contar(IEnumerable<string> ids) => barra?.Mostrar(sesion.ColumnasDe(ids).Count);

    private static List<string> Seleccion(UIApplication app) =>
        app.ActiveUIDocument?.Selection.GetElementIds().Select(IdTexto).ToList() ?? new List<string>();

    /// <summary>Mismo formato que <c>LectorContexto.IdTexto</c>: <c>ElementId.Value</c>.</summary>
    private static string IdTexto(ElementId id) => id.Value.ToString(CultureInfo.InvariantCulture);

    // Los manejadores de eventos de Revit nunca dejan escapar una excepción:
    // Revit los llama desde su lado nativo y podría caerse entero.

    /// <summary>Con la barra de selección abierta, cuenta las columnas que lleva elegidas.</summary>
    private void AlCambiarSeleccion(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (barra is not null && e.GetDocument().Equals(documento))
                Contar(e.GetSelectedElements().Select(IdTexto));
        }
        catch
        {
            // ver arriba
        }
    }

    /// <summary>Si se cierra el documento con la ventana abierta, el dibujo 3D se va con él y la ventana se cierra.</summary>
    private void AlCerrarDocumento(object? sender, DocumentClosingEventArgs e)
    {
        try
        {
            if (!e.Document.Equals(documento)) return;
            previa3D.Olvidar();
            ventana.Dispatcher.BeginInvoke(new Action(ventana.Close));
        }
        catch
        {
            // ver arriba
        }
    }

    private void Soltar()
    {
        barra?.Close();
        try
        {
            // La vista previa 3D vive mientras la ventana esté abierta (#123).
            previa3D.Olvidar();
            uiapp.ActiveUIDocument?.RefreshActiveView();
            uiapp.SelectionChanged -= AlCambiarSeleccion;
            uiapp.Application.DocumentClosing -= AlCerrarDocumento;
        }
        catch
        {
            // Revit puede estar cerrándose.
        }
        cola.Dispose();
    }
}
