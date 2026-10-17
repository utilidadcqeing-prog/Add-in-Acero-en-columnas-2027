using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CQEING.Vigas.Calculo.Modelo;

namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>Un mensaje de la página del tablero a C#. En JSON, <c>tipo</c> dice cuál es (<c>"listo"</c>, <c>"calcular"</c>…).</summary>
public abstract record MensajeTablero;

/// <summary>La página cargó: pide la lista de pórticos y el primero abierto.</summary>
public sealed record Listo() : MensajeTablero;

public sealed record ElegirPortico(string Id) : MensajeTablero;

/// <summary>Cambió algo de los pasos 1 a 3: recalcular <paramref name="Id"/> con <paramref name="Opciones"/>.</summary>
public sealed record Calcular(string Id, OpcionesTablero Opciones) : MensajeTablero;

/// <summary>"Seleccionar en Revit": elegir vigas en el modelo para abrir su pórtico.</summary>
public sealed record SeleccionarEnRevit() : MensajeTablero;

public sealed record VerEnRevit(string Id) : MensajeTablero;

public sealed record Abrir3d(string Id) : MensajeTablero;

public sealed record CambiarEnVivo(bool Activo) : MensajeTablero;

/// <summary>"Ver todos": la lista vuelve a mostrar todos los pórticos después de elegir vigas en Revit.</summary>
public sealed record VerTodos() : MensajeTablero;

/// <summary>"Guardar y bloquear" en la pantalla Reglas, con los textos de sus campos.</summary>
public sealed record GuardarReglas(VistaReglas Reglas) : MensajeTablero;

/// <summary>Un mensaje que no se entiende (<paramref name="Tipo"/> vacío si ni siquiera trae tipo): se ignora.</summary>
public sealed record Desconocido(string Tipo) : MensajeTablero;

/// <summary>
/// Lee y escribe los mensajes del tablero (#83). JSON en camelCase con los enums como texto; los textos van sin
/// escapar los acentos (viajan por <c>PostWebMessageAsJson</c>, no dentro de HTML).
/// </summary>
public static class ProtocoloTablero
{
    public static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Nunca lanza: un JSON roto o incompleto da <see cref="Desconocido"/>.</summary>
    public static MensajeTablero Leer(string json)
    {
        JsonObject? nodo;
        try
        {
            nodo = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return new Desconocido("");
        }
        string? tipo = nodo?["tipo"] is JsonValue v && v.TryGetValue(out string? t) ? t : null;
        if (nodo is null || tipo is null)
            return new Desconocido("");
        try
        {
            return tipo switch
            {
                "listo" => new Listo(),
                "elegirPortico" => new ElegirPortico(Texto(nodo, "id")),
                "calcular" => Opciones(nodo["opciones"]) is { } o ? new Calcular(Texto(nodo, "id"), o) : new Desconocido(tipo),
                "seleccionarEnRevit" => new SeleccionarEnRevit(),
                "verEnRevit" => new VerEnRevit(Texto(nodo, "id")),
                "abrir3d" => new Abrir3d(Texto(nodo, "id")),
                "cambiarEnVivo" => new CambiarEnVivo(nodo["activo"]?.GetValue<bool>() ?? false),
                "verTodos" => new VerTodos(),
                "guardarReglas" => nodo["reglas"]?.Deserialize<VistaReglas>(Json) is { PorDiametro: not null } r
                    ? new GuardarReglas(r) : new Desconocido(tipo),
                _ => new Desconocido(tipo),
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return new Desconocido(tipo);
        }
    }

    /// <summary><c>{"tipo":"vista","vista":…}</c>.</summary>
    public static string Vista(VistaTablero v) => JsonSerializer.Serialize(new { tipo = "vista", vista = v }, Json);

    /// <summary><c>{"tipo":"aviso","texto":…}</c>: la página lo muestra como el aviso de la maqueta.</summary>
    public static string Aviso(string texto) => JsonSerializer.Serialize(new { tipo = "aviso", texto }, Json);

    private static string Texto(JsonObject nodo, string clave) => nodo[clave]?.GetValue<string>() ?? "";

    /// <summary>
    /// Las opciones de la página; lo que no trae sale de <see cref="OpcionesTablero.PorDefecto"/>. Sin barras arriba o
    /// abajo no hay nada que calcular: null.
    /// </summary>
    private static OpcionesTablero? Opciones(JsonNode? nodo)
    {
        OpcionesTablero? o = nodo?.Deserialize<OpcionesTablero>(Json);
        if (o?.Superior is not { Count: > 0 } || o.Inferior is not { Count: > 0 })
            return null;
        OpcionesTablero d = OpcionesTablero.PorDefecto;
        return o with
        {
            Bastones = o.Bastones ?? d.Bastones,
            Estribos = o.Estribos ?? d.Estribos,
            TramosMarcados = o.TramosMarcados ?? d.TramosMarcados,
        };
    }
}
