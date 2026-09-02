namespace practicacq;

using System;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using practicacq.Logica;
using practicacq.Modelo;

/// <summary>
/// Comprueba la fase 2 sobre el modelo: selecciona una columna y enseña los
/// tramos que el lector sacó de ella.
///
/// No es una de las dos ventanas de la especificación ni pretende serlo: es el
/// banco de pruebas del lector, porque sin poder ver los tramos no hay forma de
/// dar la fase por buena. Cuando la ventana real exista, esto puede irse.
///
/// Solo lee. No abre transacción y no toca el modelo.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class ComandoContexto : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument? uidoc = commandData.Application.ActiveUIDocument;

        if (uidoc?.Document is null)
        {
            message = "Abre un proyecto antes de ejecutar el comando.";
            return Result.Failed;
        }

        Reference referencia;
        try
        {
            referencia = uidoc.Selection.PickObject(
                ObjectType.Element, new FiltroColumnas(), "Selecciona una columna estructural");
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }

        try
        {
            ColumnaContexto ctx = new LectorContexto(uidoc.Document).Leer(referencia.ElementId);
            Mostrar(ctx);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }

    private static void Mostrar(ColumnaContexto ctx)
    {
        var texto = new StringBuilder();

        texto.AppendLine(
            $"Sección {ctx.BaseX:0.00} × {ctx.BaseY:0.00} m · " +
            $"recubrimiento {ctx.Recubrimiento * 100:0.#} cm");
        texto.AppendLine(
            $"De {ctx.ZInferior:0.00} m a {ctx.ZSuperior:0.00} m " +
            $"({ctx.ZSuperior - ctx.ZInferior:0.00} m de alto)");
        texto.AppendLine();
        texto.AppendLine($"{ctx.Tramos.Count} sólidos:");

        foreach (Tramo t in ctx.Tramos)
            texto.AppendLine(
                $"  {t.Indice} / {ctx.Tramos.Count - 1}   " +
                $"{t.ZInferior,7:0.00} → {t.ZSuperior,7:0.00}   " +
                $"{t.Altura,5:0.00} m   {Etiqueta(t.Tipo)}");

        if (ctx.Niveles.Count > 0)
        {
            texto.AppendLine();
            texto.AppendLine("Niveles: " + string.Join(
                " · ", ctx.Niveles.Select(n => $"{n.Nombre} {n.Cota:+0.00;-0.00}")));
        }

        var dialogo = new TaskDialog($"Contexto de {ctx.Marca}")
        {
            MainInstruction = $"Columna {ctx.Marca}",
            MainContent = texto.ToString()
        };

        if (ctx.Avisos.Count > 0)
            dialogo.ExpandedContent = string.Join(Environment.NewLine, ctx.Avisos);

        dialogo.Show();
    }

    private static string Etiqueta(TipoTramo tipo) => tipo switch
    {
        TipoTramo.DentroDeViga => "dentro de viga",
        TipoTramo.ContraZapata => "contra zapata",
        _ => "libre"
    };
}
