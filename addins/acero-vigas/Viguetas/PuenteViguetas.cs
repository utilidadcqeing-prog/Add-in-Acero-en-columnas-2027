using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CQEING.Vigas.Calculo.Viguetas;
using CQEING.Vigas.Revit;
using CQEING.Vigas.Tablero;
using Panel = CQEING.Vigas.Calculo.Viguetas.Panel;

namespace CQEING.Vigas.Viguetas;

/// <summary>
/// Une la página de «Crear viguetas» (#951) con Revit. Leer el piso, crear y quitar van al hilo de Revit por
/// <see cref="AccionesRevit"/>; la propuesta de un panel no usa la API y se contesta enseguida, mientras se escribe.
/// </summary>
internal sealed class PuenteViguetas
{
    private readonly VentanaTablero ventana;
    private readonly Document doc;
    private readonly AccionesRevit acciones = new();
    private PisoViguetas? piso;

    public PuenteViguetas(VentanaTablero ventana, Document doc)
    {
        this.ventana = ventana;
        this.doc = doc;
        ventana.MensajeRecibido += Recibir;
        acciones.Fallo += texto => ventana.Enviar(ProtocoloViguetas.Aviso($"No se pudo hacer en Revit: {texto}"));
        ventana.Closed += (_, _) => acciones.Dispose();
    }

    private void Recibir(string json)
    {
        MensajeViguetas mensaje = ProtocoloViguetas.Leer(json);
        switch (mensaje)
        {
            case PedirPropuesta p:
                ventana.Enviar(ProtocoloViguetas.Propuesta(Propuesta(p), p.Panel));
                break;
            case CerrarViguetas:
                ventana.Close();
                break;
            case DesconocidoViguetas:
                break;
            default:
                acciones.Encolar(_ => Atender(mensaje));
                break;
        }
    }

    /// <summary>Lo que toca el modelo, en el hilo de Revit.</summary>
    private void Atender(MensajeViguetas mensaje)
    {
        if (!doc.IsValidObject)
        {
            ventana.Enviar(ProtocoloViguetas.Aviso("El modelo se cerró. Cierre esta ventana y vuelva a abrirla."));
            return;
        }
        switch (mensaje)
        {
            case ListoViguetas:
                ventana.Enviar(ProtocoloViguetas.Pisos(doc.Title, LectorPiso.Pisos(doc), LectorPiso.Tipos(doc)));
                break;
            case ElegirPiso e:
                Abrir(e.Id, e.ViguetasComoBorde);
                break;
            case CrearViguetas c:
                Crear(c);
                break;
            case QuitarCreadas q:
                Quitar(q.Panel);
                break;
        }
    }

    private void Abrir(string id, bool viguetasComoBorde)
    {
        if (Nivel(id) is not Level nivel)
            return;
        piso = new PisoViguetas(LectorPiso.Leer(doc, nivel), viguetasComoBorde);
        ventana.Enviar(ProtocoloViguetas.Piso(piso));
        if (piso.Paneles.Count == 0)
            ventana.Enviar(ProtocoloViguetas.Aviso($"En {nivel.Name} no se encontraron paneles cerrados por vigas."));
    }

    private void Crear(CrearViguetas c)
    {
        if (piso is null || piso.Panel(c.Propuesta.Panel) is not Panel panel || Propuesta(c.Propuesta) is not PropuestaViguetas propuesta)
            return;
        if (Nivel(piso.Piso.Id) is not Level nivel || doc.GetElement(Id(c.Tipo)) is not FamilySymbol tipo)
        {
            ventana.Enviar(ProtocoloViguetas.Aviso("Elija un tipo de vigueta."));
            return;
        }
        int creadas = CreadorViguetas.Crear(doc, nivel, tipo, propuesta, Ids(piso.CreadasEn(panel)));
        Abrir(piso.Piso.Id, piso.ViguetasComoBorde);
        ventana.Enviar(ProtocoloViguetas.Aviso($"Se crearon {creadas} viguetas en {panel.Id}. Ctrl+Z en Revit las deshace.", bien: true));
    }

    private void Quitar(string id)
    {
        if (piso?.Panel(id) is not Panel panel)
            return;
        int quitadas = CreadorViguetas.Quitar(doc, Ids(piso.CreadasEn(panel)));
        Abrir(piso.Piso.Id, piso.ViguetasComoBorde);
        ventana.Enviar(ProtocoloViguetas.Aviso($"Se quitaron {quitadas} viguetas de {panel.Id}.", bien: true));
    }

    private PropuestaViguetas? Propuesta(PedirPropuesta p) =>
        piso?.Panel(p.Panel) is Panel panel
            ? PlanificadorViguetas.Planificar(panel, p.Direccion, p.EjeAEje, p.Ancho, p.Borradas.ToHashSet())
            : null;

    private Level? Nivel(string id) => doc.GetElement(Id(id)) as Level;

    private static ElementId Id(string id) => long.TryParse(id, out long n) ? new ElementId(n) : ElementId.InvalidElementId;

    private static IEnumerable<ElementId> Ids(IEnumerable<ArmazonPiso> armazon) => armazon.Select(a => Id(a.Id)).ToList();
}
