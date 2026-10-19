using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

/// <summary>
/// Define los métodos para interactuar con la API Web de presupuestos.
/// </summary>
public interface IApiClient
{
    /// <summary>
    /// Obtiene la base URL de la API.
    /// </summary>
    string BaseUrl { get; set; }

    /// <summary>
    /// Obtiene o establece la clave de API (X-Api-Key) para autenticarse con el backend.
    /// </summary>
    string ApiKey { get; set; }

    /// <summary>
    /// Indica si se debe forzar el modo de simulación (mock).
    /// </summary>
    bool UseMockMode { get; set; }

    /// <summary>
    /// Obtiene la lista de presupuestos disponibles, filtrados opcionalmente por la ruta del documento de Revit.
    /// </summary>
    Task<List<ApiBudget>?> GetBudgetsAsync(string? revitPath = null);

    /// <summary>
    /// Obtiene la estructura de presupuesto (Capítulos, Subcapítulos e Ítems) a partir de su código.
    /// </summary>
    Task<ApiResponseBudgetStructure?> GetBudgetStructureAsync(string budgetCode);

    /// <summary>
    /// Envía y sobreescribe la lista de elementos enlazados a un ítem del presupuesto.
    /// </summary>
    Task<bool> UpdateLinkedElementsAsync(int itemId, IEnumerable<ApiLinkedElementRequest> elements);

    /// <summary>
    /// Obtiene la lista de elementos vinculados previamente a un ítem del presupuesto.
    /// </summary>
    Task<List<ApiLinkedElementResponse>?> GetLinkedElementsAsync(int itemId);

    /// <summary>
    /// Busca el presupuesto vinculado a un modelo de Revit.
    /// </summary>
    Task<ApiBudget?> GetBudgetByRevitModelAsync(string revitModelId);

    /// <summary>
    /// Vincula un presupuesto a un modelo de Revit.
    /// Devuelve el mensaje de error de la API cuando la vinculación falla (p. ej. 409 si el modelo ya está vinculado a otro presupuesto).
    /// </summary>
    Task<ApiOperationResult> LinkBudgetToRevitModelAsync(string budgetCode, string revitModelId);

    /// <summary>
    /// Desvincula un presupuesto de su modelo de Revit.
    /// Devuelve el mensaje de error de la API cuando la desvinculación falla.
    /// </summary>
    Task<ApiOperationResult> UnlinkBudgetFromRevitModelAsync(string budgetCode);

    /// <summary>
    /// Obtiene, en una sola llamada, las imágenes que el add-in de imágenes registró para cada actividad del presupuesto.
    /// Devuelve null si la API no está disponible o no responde con JSON (el llamador no debe tocar lo que ya tiene).
    /// </summary>
    Task<List<ApiImagenesDeActividadResponse>?> GetImagenesActividadAsync(string codigoPresupuesto);
}

public static class IdMapper
{
    public static Guid IntToGuid(int value)
    {
        byte[] bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    public static int GuidToInt(Guid guid)
    {
        byte[] bytes = guid.ToByteArray();
        return BitConverter.ToInt32(bytes, 0);
    }
}

/// <summary>
/// Resultado de una operación contra la API que puede fallar con un mensaje legible para el usuario.
/// </summary>
public sealed record ApiOperationResult(bool Success, string? ErrorMessage = null)
{
    public static ApiOperationResult Ok() => new(true);

    public static ApiOperationResult Fail(string errorMessage) => new(false, errorMessage);
}

public class ApiBudget
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("codigo_presupuesto")]
    public string CodigoPresupuesto { get; set; } = string.Empty;

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [JsonPropertyName("fecha_creacion")]
    public DateTime? FechaCreacion { get; set; }

    [JsonPropertyName("revitModelId")]
    public string? RevitModelId { get; set; }

    [JsonPropertyName("revit_model_id")]
    public string? RevitModelIdSnake { get; set; }

    [JsonIgnore]
    public string? ActualRevitModelId => RevitModelId ?? RevitModelIdSnake;
}

public class ApiResponseBudgetStructure
{
    [JsonPropertyName("codigo_presupuesto")]
    public string CodigoPresupuesto { get; set; } = string.Empty;

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [JsonPropertyName("capitulos")]
    public List<ApiCapitulo> Capitulos { get; set; } = new();
}

public class ApiCapitulo
{
    [JsonPropertyName("codigo_capitulo")]
    public string CodigoCapitulo { get; set; } = string.Empty;

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [JsonPropertyName("subcapitulos")]
    public List<ApiSubcapitulo> Subcapitulos { get; set; } = new();
}

public class ApiSubcapitulo
{
    [JsonPropertyName("codigo_subcapitulo")]
    public string CodigoSubcapitulo { get; set; } = string.Empty;

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<ApiItem> Items { get; set; } = new();
}

public class ApiItem
{
    [JsonPropertyName("id_item")]
    public int IdItem { get; set; }

    [JsonPropertyName("codigo_actividad")]
    public string CodigoActividad { get; set; } = string.Empty;

    [JsonPropertyName("descripcion")]
    public string Descripcion { get; set; } = string.Empty;

    [JsonPropertyName("unidad")]
    public string Unidad { get; set; } = string.Empty;

    [JsonPropertyName("cantidad_obra")]
    public double CantidadObra { get; set; }
}

public class ApiLinkedElementRequest
{
    [JsonPropertyName("revit_unique_id")]
    public string RevitUniqueId { get; set; } = string.Empty;

    [JsonPropertyName("categoria")]
    public string Categoria { get; set; } = string.Empty;

    [JsonPropertyName("nombre_elemento")]
    public string NombreElemento { get; set; } = string.Empty;

    [JsonPropertyName("datos_json")]
    public string DatosJson { get; set; } = string.Empty;

    [JsonPropertyName("urls_imagenes")]
    public List<string>? UrlsImagenes { get; set; }

    [JsonPropertyName("eliminado_en_revit")]
    public bool EliminadoEnRevit { get; set; }
}

public class ApiLinkedElementResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("id_item")]
    public int IdItem { get; set; }

    [JsonPropertyName("revit_unique_id")]
    public string RevitUniqueId { get; set; } = string.Empty;

    [JsonPropertyName("categoria")]
    public string Categoria { get; set; } = string.Empty;

    [JsonPropertyName("nombre_elemento")]
    public string NombreElemento { get; set; } = string.Empty;

    [JsonPropertyName("datos_json")]
    public string DatosJson { get; set; } = string.Empty;

    [JsonPropertyName("urls_imagenes")]
    public List<string>? UrlsImagenes { get; set; }

    [JsonPropertyName("eliminado_en_revit")]
    public bool EliminadoEnRevit { get; set; }
}

/// <summary>
/// Imágenes del add-in de imágenes de una actividad (GET /api/presupuestos/{codigo}/imagenes-actividad).
/// </summary>
public class ApiImagenesDeActividadResponse
{
    [JsonPropertyName("id_item")]
    public int IdItem { get; set; }

    [JsonPropertyName("codigo_actividad")]
    public string CodigoActividad { get; set; } = string.Empty;

    [JsonPropertyName("imagenes")]
    public List<ApiImagenActividadResponse> Imagenes { get; set; } = new();
}

/// <summary>
/// Una imagen registrada por el add-in de imágenes. El <see cref="Codigo"/> es estable;
/// la <see cref="Url"/> cambia cuando la imagen se actualiza (…_v2.png).
/// </summary>
public class ApiImagenActividadResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("codigo")]
    public string Codigo { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [JsonPropertyName("descripcion")]
    public string Descripcion { get; set; } = string.Empty;

    [JsonPropertyName("tipo_vista")]
    public string TipoVista { get; set; } = string.Empty;

    [JsonPropertyName("orden")]
    public int Orden { get; set; }

    [JsonPropertyName("fecha_creacion")]
    public DateTime FechaCreacion { get; set; }
}