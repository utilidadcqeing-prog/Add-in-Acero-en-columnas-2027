namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using practicacq.Modelo;

/// <summary>
/// Con qué se generó cada columna (#180), guardado DENTRO del modelo con
/// Extensible Storage, igual que <see cref="AlmacenConfiguracionesRevit"/>: un
/// <see cref="DataStorage"/> con un solo campo de texto que lleva el JSON de
/// todas las columnas.
///
/// La clave es el <c>UniqueId</c> de la columna (el elemento de abajo de la
/// pila, el mismo id que usa la ventana) y no una entidad sobre la columna:
/// una columna copiada en Revit no tiene que heredar una configuración que no
/// es la suya.
///
/// Se escribe en la misma transacción que genera el acero
/// (<see cref="ManejadorGeneracionArmado"/>); si la generación se deshace, esto
/// también.
///
/// API usada, toda pública y documentada: <c>SchemaBuilder</c>,
/// <c>Schema.Lookup</c>, <c>Entity</c>, <c>Element.GetEntity</c>/<c>SetEntity</c>,
/// <c>DataStorage.Create</c> y <c>Element.UniqueId</c>.
/// </summary>
public sealed class RegistroGeneradosRevit : IRegistroGenerados
{
    /// <summary>Fijo y para siempre, como el de las configuraciones.</summary>
    private static readonly Guid IdEsquema = new("3F1A7C52-6B0E-4D2A-9E8B-71C4D5A2F0E3");

    private const string NombreEsquema = "CQEING_ColumnasGeneradas";
    private const string NombreCampo = "Json";

    private readonly Document _doc;
    private readonly Dictionary<string, ColumnaGenerada> _porUniqueId;

    public RegistroGeneradosRevit(Document doc)
    {
        _doc = doc;
        _porUniqueId = LeerDelDocumento(doc);
    }

    public ColumnaGenerada? Leer(string idColumna) =>
        UniqueIdDe(_doc, idColumna) is string clave && _porUniqueId.TryGetValue(clave, out ColumnaGenerada? g) ? g : null;

    /// <summary>
    /// Suma al modelo lo que se generó en esta corrida. Dentro de una
    /// transacción abierta. Devuelve el motivo si no se pudo, o null: que falle
    /// esto no puede tirar abajo una generación que salió bien.
    /// </summary>
    public static string? Registrar(Document doc, PlanArmado plan)
    {
        try
        {
            Dictionary<string, ColumnaGenerada> porUniqueId = LeerDelDocumento(doc);
            foreach (ColumnaPlan columna in plan.Columnas)
            {
                if (columna.Tramos.All(t => t is null) || UniqueIdDe(doc, columna.Id) is not string clave) continue;
                porUniqueId.TryGetValue(clave, out ColumnaGenerada? previa);
                porUniqueId[clave] = ColumnaGenerada.Combinar(previa, columna, plan.GeneradoEl);
            }

            Schema esquema = ObtenerOCrearEsquema();
            Element contenedor = BuscarContenedor(doc, esquema) ?? DataStorage.Create(doc);
            contenedor.Name = "CQEING - columnas generadas";

            var entidad = new Entity(esquema);
            entidad.Set(esquema.GetField(NombreCampo), JsonSerializer.Serialize(porUniqueId));
            contenedor.SetEntity(entidad);
            return null;
        }
        catch (Exception ex)
        {
            return GeometriaSeccion.Explicar(ex);
        }
    }

    private static string? UniqueIdDe(Document doc, string idColumna) =>
        long.TryParse(idColumna, out long valor) ? doc.GetElement(new ElementId(valor))?.UniqueId : null;

    private static Dictionary<string, ColumnaGenerada> LeerDelDocumento(Document doc)
    {
        try
        {
            if (Schema.Lookup(IdEsquema) is not Schema esquema || BuscarContenedor(doc, esquema) is not Element contenedor)
                return new Dictionary<string, ColumnaGenerada>();

            string json = contenedor.GetEntity(esquema).Get<string>(esquema.GetField(NombreCampo));
            return string.IsNullOrWhiteSpace(json)
                ? new Dictionary<string, ColumnaGenerada>()
                : JsonSerializer.Deserialize<Dictionary<string, ColumnaGenerada>>(json) ?? new Dictionary<string, ColumnaGenerada>();
        }
        catch
        {
            // Datos de un formato anterior no pueden impedir abrir la ventana ni
            // generar: se empieza de cero.
            return new Dictionary<string, ColumnaGenerada>();
        }
    }

    private static Element? BuscarContenedor(Document doc, Schema esquema) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(DataStorage))
            .Cast<Element>()
            .FirstOrDefault(e => e.GetEntity(esquema).IsValid());

    private static Schema ObtenerOCrearEsquema()
    {
        // Lookup es por sesión de Revit: si otro documento ya lo creó, se reusa.
        if (Schema.Lookup(IdEsquema) is { } existente) return existente;

        var constructor = new SchemaBuilder(IdEsquema);
        constructor.SetSchemaName(NombreEsquema);
        constructor.SetReadAccessLevel(AccessLevel.Public);
        constructor.SetWriteAccessLevel(AccessLevel.Public);
        constructor.AddSimpleField(NombreCampo, typeof(string));

        return constructor.Finish();
    }
}
