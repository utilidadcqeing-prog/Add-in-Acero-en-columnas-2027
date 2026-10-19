using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Application.Interfaces;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Implementación del cliente API HTTP para la comunicación con la plataforma web.
/// </summary>
public sealed class ApiClient : IApiClient
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly JsonSerializerOptions _jsonOptions;

    /// <inheritdoc />
    public string BaseUrl { get; set; } = EnvLoader.Get("API_URL");

    /// <inheritdoc />
    public string ApiKey { get; set; } = EnvLoader.Get("API_KEY");

    /// <inheritdoc />
    public bool UseMockMode { get; set; } = false;

    /// <summary>
    /// Inicializa una nueva instancia de la clase <see cref="ApiClient"/>.
    /// </summary>
    public ApiClient()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    /// <inheritdoc />
    private class ApiBudgetResponseWrapper
    {
        [JsonPropertyName("data")]
        public List<ApiBudgetDto>? Data { get; set; }
    }

    private class ApiBudgetDto
    {
        [JsonPropertyName("codigoPresupuesto")]
        public string CodigoPresupuesto { get; set; } = string.Empty;

        [JsonPropertyName("nombre")]
        public string Nombre { get; set; } = string.Empty;

        [JsonPropertyName("fecha")]
        public string? Fecha { get; set; }

        [JsonPropertyName("revitModelId")]
        public string? RevitModelId { get; set; }

        [JsonPropertyName("revit_model_id")]
        public string? RevitModelIdSnake { get; set; }
    }

    private static int GetDeterministicHash(string text)
    {
        if (text == null)
            return 0;
        int hash = 23;
        foreach (char c in text)
        {
            hash = hash * 31 + c;
        }
        return hash;
    }

    /// <inheritdoc />
    public async Task<List<ApiBudget>?> GetBudgetsAsync(string? revitPath = null)
    {
        if (UseMockMode)
        {
            return GetMockBudgets();
        }

        try
        {
            string url = $"{BaseUrl.TrimEnd('/')}/api/presupuestos";
            if (!string.IsNullOrWhiteSpace(revitPath))
            {
                url += $"?revit_path={Uri.EscapeDataString(revitPath)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", ApiKey);

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                string trimmed = content.Trim();
                if (trimmed.StartsWith("["))
                {
                    return JsonSerializer.Deserialize<List<ApiBudget>>(content, _jsonOptions);
                }
                else if (trimmed.StartsWith("{"))
                {
                    var wrapper = JsonSerializer.Deserialize<ApiBudgetResponseWrapper>(
                        content,
                        _jsonOptions
                    );
                    if (wrapper?.Data != null)
                    {
                        var result = new List<ApiBudget>();
                        foreach (var dto in wrapper.Data)
                        {
                            DateTime? createdDate = null;
                            if (DateTime.TryParse(dto.Fecha, out var parsedDate))
                            {
                                createdDate = parsedDate;
                            }

                            int id = GetDeterministicHash(dto.CodigoPresupuesto);

                             result.Add(
                                 new ApiBudget
                                 {
                                     Id = id,
                                     CodigoPresupuesto = dto.CodigoPresupuesto,
                                     Nombre = dto.Nombre,
                                     FechaCreacion = createdDate,
                                     RevitModelId = dto.RevitModelId,
                                     RevitModelIdSnake = dto.RevitModelIdSnake,
                                 }
                             );
                        }
                        return result;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ApiClient] Error in GetBudgetsAsync: {ex.Message}"
            );
        }

        // Sin conexión no se inventan datos: null indica al llamador que la API no está disponible
        return null;
    }

    /// <inheritdoc />
    public async Task<ApiResponseBudgetStructure?> GetBudgetStructureAsync(string budgetCode)
    {
        if (UseMockMode)
        {
            return GetMockBudgetStructure(budgetCode);
        }

        try
        {
            string url = $"{BaseUrl.TrimEnd('/')}/api/presupuestos/{budgetCode}/estructura";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", ApiKey);

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<ApiResponseBudgetStructure>(
                    content,
                    _jsonOptions
                );
            }
            else
            {
                var content = await response.Content.ReadAsStringAsync();
                var logMsg =
                    $"[GetBudgetStructureAsync] Failed for budgetCode: {budgetCode}. Status: {response.StatusCode}. URL: {url}. Response: {content}{Environment.NewLine}";
                System.Diagnostics.Debug.WriteLine(logMsg);
                try
                {
                    System.IO.File.AppendAllText(
                        "D:\\Addins\\CQEING_CuadroCantidades\\sync_error.log",
                        $"[{DateTime.Now}] {logMsg}"
                    );
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            var logMsg =
                $"[GetBudgetStructureAsync] Exception for budgetCode: {budgetCode}. Error: {ex.Message}. StackTrace: {ex.StackTrace}{Environment.NewLine}";
            System.Diagnostics.Debug.WriteLine(logMsg);
            try
            {
                System.IO.File.AppendAllText(
                    "D:\\Addins\\CQEING_CuadroCantidades\\sync_error.log",
                    $"[{DateTime.Now}] {logMsg}"
                );
            }
            catch { }
        }

        // Sin conexión no se inventan datos: null indica al llamador que la API no está disponible
        return null;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateLinkedElementsAsync(
        int itemId,
        IEnumerable<ApiLinkedElementRequest> elements
    )
    {
        if (UseMockMode)
        {
            return true;
        }

        try
        {
            string url = $"{BaseUrl.TrimEnd('/')}/api/presupuestos/items/{itemId}/elementos";
            var json = JsonSerializer.Serialize(elements, _jsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Api-Key", ApiKey);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var logMsg =
                    $"[UpdateLinkedElementsAsync] Failed for itemId: {itemId}. Status: {response.StatusCode}. URL: {url}. Request: {json}. Response: {content}{Environment.NewLine}";
                System.Diagnostics.Debug.WriteLine(logMsg);
                try
                {
                    System.IO.File.AppendAllText(
                        "D:\\Addins\\CQEING_CuadroCantidades\\sync_error.log",
                        $"[{DateTime.Now}] {logMsg}"
                    );
                }
                catch { }
            }
            else
            {
                var logMsg =
                    $"[UpdateLinkedElementsAsync] Success for itemId: {itemId}. URL: {url}. Request: {json}{Environment.NewLine}";
                System.Diagnostics.Debug.WriteLine(logMsg);
                try
                {
                    System.IO.File.AppendAllText(
                        "D:\\Addins\\CQEING_CuadroCantidades\\sync_error.log",
                        $"[{DateTime.Now}] {logMsg}"
                    );
                }
                catch { }
            }
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            var logMsg =
                $"[UpdateLinkedElementsAsync] Exception for itemId: {itemId}. Error: {ex.Message}. StackTrace: {ex.StackTrace}{Environment.NewLine}";
            System.Diagnostics.Debug.WriteLine(logMsg);
            try
            {
                System.IO.File.AppendAllText(
                    "D:\\Addins\\CQEING_CuadroCantidades\\sync_error.log",
                    $"[{DateTime.Now}] {logMsg}"
                );
            }
            catch { }
            // Un fallo de red no es un envío exitoso: false conserva la actividad como pendiente
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<List<ApiLinkedElementResponse>?> GetLinkedElementsAsync(int itemId)
    {
        if (UseMockMode)
        {
            return GetMockLinkedElements(itemId);
        }

        try
        {
            string url = $"{BaseUrl.TrimEnd('/')}/api/presupuestos/items/{itemId}/elementos";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", ApiKey);

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var list = JsonSerializer.Deserialize<List<ApiLinkedElementResponse>>(
                    content,
                    _jsonOptions
                );
                if (list != null)
                {
                    return list.Where(el => el.RevitUniqueId != "placeholder-empty-activity").ToList();
                }
                return list;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ApiClient] Error in GetLinkedElementsAsync: {ex.Message}"
            );
        }

        // Sin conexión no se inventan datos: null indica al llamador que la API no está disponible
        return null;
    }

    /// <inheritdoc />
    public async Task<ApiBudget?> GetBudgetByRevitModelAsync(string revitModelId)
    {
        if (UseMockMode)
        {
            return null;
        }

        try
        {
            string url = $"{BaseUrl.TrimEnd('/')}/api/presupuestos/by-revit-model/{Uri.EscapeDataString(revitModelId)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", ApiKey);

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                string trimmed = content.Trim();
                ApiBudgetDto? dto = null;

                using (var doc = JsonDocument.Parse(trimmed))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("data", out var dataProp))
                    {
                        dto = JsonSerializer.Deserialize<ApiBudgetDto>(dataProp.GetRawText(), _jsonOptions);
                    }
                    else
                    {
                        dto = JsonSerializer.Deserialize<ApiBudgetDto>(trimmed, _jsonOptions);
                    }
                }

                if (dto != null)
                {
                    DateTime? createdDate = null;
                    if (DateTime.TryParse(dto.Fecha, out var parsedDate))
                    {
                        createdDate = parsedDate;
                    }
                    int id = GetDeterministicHash(dto.CodigoPresupuesto);
                    return new ApiBudget
                    {
                        Id = id,
                        CodigoPresupuesto = dto.CodigoPresupuesto,
                        Nombre = dto.Nombre,
                        FechaCreacion = createdDate,
                        RevitModelId = dto.RevitModelId,
                        RevitModelIdSnake = dto.RevitModelIdSnake,
                    };
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApiClient] Error in GetBudgetByRevitModelAsync: {ex.Message}");
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<ApiOperationResult> LinkBudgetToRevitModelAsync(string budgetCode, string revitModelId)
    {
        if (UseMockMode)
        {
            return ApiOperationResult.Ok();
        }

        try
        {
            string url = $"{BaseUrl.TrimEnd('/')}/api/presupuestos/{Uri.EscapeDataString(budgetCode)}/revit-model";
            var body = new { revitModelId = revitModelId };
            var json = JsonSerializer.Serialize(body, _jsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Api-Key", ApiKey);

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return ApiOperationResult.Ok();
            }

            var content = await response.Content.ReadAsStringAsync();
            string? apiMessage = TryReadApiMessage(content);
            string fallback = response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? $"El presupuesto \"{budgetCode}\" no existe en la API."
                : $"La API respondió {(int)response.StatusCode} ({response.ReasonPhrase}).";

            return ApiOperationResult.Fail(apiMessage ?? fallback);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApiClient] Error in LinkBudgetToRevitModelAsync: {ex.Message}");
            return ApiOperationResult.Fail($"No se pudo conectar con la API: {ex.Message}");
        }
    }

    /// <summary>
    /// Extrae el campo <c>message</c> de un cuerpo de error JSON de la API, si existe.
    /// </summary>
    private static string? TryReadApiMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<ApiOperationResult> UnlinkBudgetFromRevitModelAsync(string budgetCode)
    {
        if (UseMockMode)
        {
            return ApiOperationResult.Ok();
        }

        try
        {
            string url = $"{BaseUrl.TrimEnd('/')}/api/presupuestos/{Uri.EscapeDataString(budgetCode)}/revit-model";
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Add("X-Api-Key", ApiKey);

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return ApiOperationResult.Ok();
            }

            var content = await response.Content.ReadAsStringAsync();
            string? apiMessage = TryReadApiMessage(content);
            string fallback = response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? $"El presupuesto \"{budgetCode}\" no existe en la API."
                : $"La API respondió {(int)response.StatusCode} ({response.ReasonPhrase}).";

            return ApiOperationResult.Fail(apiMessage ?? fallback);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApiClient] Error in UnlinkBudgetFromRevitModelAsync: {ex.Message}");
            return ApiOperationResult.Fail($"No se pudo conectar con la API: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<List<ApiImagenesDeActividadResponse>?> GetImagenesActividadAsync(string codigoPresupuesto)
    {
        if (UseMockMode)
        {
            return new List<ApiImagenesDeActividadResponse>();
        }

        try
        {
            string url = $"{BaseUrl.TrimEnd('/')}/api/presupuestos/{Uri.EscapeDataString(codigoPresupuesto)}/imagenes-actividad";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", ApiKey);

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            // Un backend sin el endpoint puede devolver HTML o un objeto de error: se trata como "no disponible"
            if (response.IsSuccessStatusCode && content.TrimStart().StartsWith("["))
            {
                return JsonSerializer.Deserialize<List<ApiImagenesDeActividadResponse>>(content, _jsonOptions);
            }

            System.Diagnostics.Debug.WriteLine(
                $"[ApiClient] GetImagenesActividadAsync sin datos para {codigoPresupuesto}. Status: {response.StatusCode}."
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApiClient] Error in GetImagenesActividadAsync: {ex.Message}");
        }

        // Sin conexión no se inventan datos: null indica al llamador que la API no está disponible
        return null;
    }

    private List<ApiBudget> GetMockBudgets()
    {
        return new List<ApiBudget>
        {
            new ApiBudget
            {
                Id = 1,
                CodigoPresupuesto = "PROY_001",
                Nombre = "Presupuesto Estructura Edificio (Simulado)",
                FechaCreacion = DateTime.UtcNow.AddDays(-2),
            },
        };
    }

    private ApiResponseBudgetStructure GetMockBudgetStructure(string budgetCode)
    {
        return new ApiResponseBudgetStructure
        {
            CodigoPresupuesto = budgetCode,
            Nombre = "Presupuesto Estructura Edificio (Simulado)",
            Capitulos = new List<ApiCapitulo>
            {
                new ApiCapitulo
                {
                    CodigoCapitulo = "C003",
                    Nombre = "ESTRUCTURA",
                    Subcapitulos = new List<ApiSubcapitulo>
                    {
                        new ApiSubcapitulo
                        {
                            CodigoSubcapitulo = "C003_S001",
                            Nombre = "ESTRUCTURA CONCRETO",
                            Items = new List<ApiItem>
                            {
                                new ApiItem
                                {
                                    IdItem = 1,
                                    CodigoActividad = "C003_S001_A001",
                                    Descripcion = "Concreto de zapatas de cimentación",
                                    Unidad = "M3",
                                    CantidadObra = 12.50,
                                },
                                new ApiItem
                                {
                                    IdItem = 2,
                                    CodigoActividad = "C003_S001_A002",
                                    Descripcion = "Concreto para vigas de cimentación",
                                    Unidad = "M3",
                                    CantidadObra = 8.40,
                                },
                            },
                        },
                    },
                },
            },
        };
    }

    private List<ApiLinkedElementResponse> GetMockLinkedElements(int itemId)
    {
        return new List<ApiLinkedElementResponse>
        {
            new ApiLinkedElementResponse
            {
                Id = "e81d7ad7-1bcf-46d5-a8ad-15de5b2938cf",
                IdItem = itemId,
                RevitUniqueId = "8b51d8e1-d36c-487c-bd5f-682136a6cfb1-00049281",
                Categoria = "Structural Foundations",
                NombreElemento = "Zapata Rectangular: 1.20 x 1.20 x 0.40",
                DatosJson = "{\"Volumen\": 0.576, \"Area\": 1.44}",
                EliminadoEnRevit = false,
            },
        };
    }
}
