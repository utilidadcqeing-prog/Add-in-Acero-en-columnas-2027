using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using CQEING_CuadroCantidades.UI.Models;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Proporciona métodos para guardar y cargar la estructura del cuadro de cantidades
/// directamente en el modelo de Revit usando DataStorage y ExtensibleStorage.
/// </summary>
public static class BudgetStorageService
{
    /// <summary>
    /// Identificador único (GUID) para el esquema de almacenamiento extensible de Revit.
    /// </summary>
    private static readonly Guid SchemaId = new("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D"); // Guid único para el Schema

    /// <summary>
    /// Nombre del esquema de almacenamiento.
    /// </summary>
    private const string SchemaName = "CQEING_CuadroCantidadesData";

    /// <summary>
    /// Nombre del campo en el que se almacena el JSON.
    /// </summary>
    private const string FieldName = "BudgetDataJson";

    /// <summary>
    /// Guarda el presupuesto actual (cuadro de cantidades) dentro del documento de Revit especificado.
    /// </summary>
    /// <param name="document">Documento de Revit donde se guardarán los datos.</param>
    /// <param name="rows">Colección de filas de presupuesto a almacenar.</param>
    public static void SaveBudget(Document document, IEnumerable<BudgetRow> rows)
    {
        // Iniciar una transacción en Revit para permitir modificaciones en el modelo
        using Transaction transaction = new(document, "Guardar Cuadro de Cantidades");
        transaction.Start();

        // Obtener o crear el esquema y el almacenamiento de datos correspondiente
        Schema schema = GetOrCreateSchema();
        DataStorage storage = GetOrCreateDataStorage(document, schema);

        // Serializar las filas a formato JSON
        string json = JsonSerializer.Serialize(rows);

        // Crear una nueva entidad asociada al esquema y guardar el JSON en ella
        Entity entity = new(schema);
        entity.Set(FieldName, json);

        // Adjuntar la entidad al almacenamiento de Revit
        storage.SetEntity(entity);

        // Confirmar los cambios
        transaction.Commit();
    }

    /// <summary>
    /// Carga el presupuesto (cuadro de cantidades) previamente guardado en el documento de Revit especificado.
    /// </summary>
    /// <param name="document">Documento de Revit desde el cual cargar los datos.</param>
    /// <returns>Una lista de solo lectura que contiene las filas de presupuesto cargadas, o una lista vacía si no existen datos.</returns>
    public static IReadOnlyList<BudgetRow> LoadBudget(Document document)
    {
        // Buscar si el esquema ya existe en la sesión actual de Revit
        Schema? schema = Schema.Lookup(SchemaId);
        if (schema is null)
        {
            return Array.Empty<BudgetRow>();
        }

        // Buscar el almacenamiento de datos existente asociado al esquema
        DataStorage? storage = GetExistingDataStorage(document, schema);
        if (storage is null)
        {
            return Array.Empty<BudgetRow>();
        }

        // Recuperar la entidad del almacenamiento
        Entity entity = storage.GetEntity(schema);
        if (!entity.IsValid())
        {
            return Array.Empty<BudgetRow>();
        }

        // Extraer la cadena JSON del campo correspondiente
        string json = entity.Get<string>(FieldName);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<BudgetRow>();
        }

        try
        {
            // Deserializar la cadena JSON a una lista de filas de presupuesto
            return JsonSerializer.Deserialize<List<BudgetRow>>(json) ?? new List<BudgetRow>();
        }
        catch (JsonException)
        {
            // Retornar vacío en caso de que ocurra algún error al procesar el JSON corrupto
            return Array.Empty<BudgetRow>();
        }
    }

    /// <summary>
    /// Obtiene el esquema de almacenamiento extensible existente o lo crea si no existe.
    /// </summary>
    /// <returns>El esquema <see cref="Schema"/> listo para ser utilizado.</returns>
    private static Schema GetOrCreateSchema()
    {
        Schema? schema = Schema.Lookup(SchemaId);
        if (schema is not null)
        {
            return schema;
        }

        // Si no existe el esquema, se construye definiendo niveles de acceso y agregando un campo de texto simple
        SchemaBuilder builder = new(SchemaId);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.SetSchemaName(SchemaName);
        builder.AddSimpleField(FieldName, typeof(string));

        return builder.Finish();
    }

    /// <summary>
    /// Obtiene el elemento DataStorage existente en el modelo de Revit que contiene el esquema especificado, o crea uno nuevo si no existe.
    /// </summary>
    /// <param name="document">Documento de Revit.</param>
    /// <param name="schema">Esquema de almacenamiento.</param>
    /// <returns>La instancia de <see cref="DataStorage"/> correspondiente.</returns>
    private static DataStorage GetOrCreateDataStorage(Document document, Schema schema)
    {
        DataStorage? existingStorage = GetExistingDataStorage(document, schema);
        if (existingStorage is not null)
        {
            return existingStorage;
        }

        // Crear una nueva instancia de DataStorage en el documento de Revit
        return DataStorage.Create(document);
    }

    /// <summary>
    /// Busca un elemento DataStorage existente en el modelo que ya contenga datos del esquema proporcionado.
    /// </summary>
    /// <param name="document">Documento de Revit.</param>
    /// <param name="schema">Esquema a buscar en el almacenamiento.</param>
    /// <returns>El elemento <see cref="DataStorage"/> si se encuentra; de lo contrario, null.</returns>
    private static DataStorage? GetExistingDataStorage(Document document, Schema schema)
    {
        // Utilizar un recolector filtrado de Revit para buscar elementos de la clase DataStorage
        using FilteredElementCollector collector = new(document);
        return collector
            .OfClass(typeof(DataStorage))
            .Cast<DataStorage>()
            .FirstOrDefault(storage => storage.GetEntity(schema).IsValid());
    }
}
