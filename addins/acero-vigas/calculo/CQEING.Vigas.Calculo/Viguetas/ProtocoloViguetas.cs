using System.Text.Json;
using System.Text.Json.Serialization;
using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Viguetas;

/// <summary>Un mensaje de la página de «Crear viguetas» (#951).</summary>
public abstract record MensajeViguetas;

public sealed record ListoViguetas : MensajeViguetas;

public sealed record ElegirPiso(string Id, bool ViguetasComoBorde) : MensajeViguetas;

/// <summary>Pide la propuesta de un panel. Medidas en mm; <paramref name="Borradas"/> son ids de <see cref="Vigueta"/>.</summary>
public sealed record PedirPropuesta(string Panel, Direccion Direccion, double EjeAEje, double Ancho, IReadOnlyList<string> Borradas) : MensajeViguetas;

public sealed record CrearViguetas(PedirPropuesta Propuesta, string Tipo) : MensajeViguetas;

public sealed record QuitarCreadas(string Panel) : MensajeViguetas;

public sealed record CerrarViguetas : MensajeViguetas;

public sealed record DesconocidoViguetas : MensajeViguetas;

/// <summary>
/// Lee y escribe los mensajes de «Crear viguetas», con las mismas opciones de JSON que el tablero
/// (<see cref="ProtocoloTablero.Json"/>). Los puntos viajan como [x, y] en mm.
/// </summary>
public static class ProtocoloViguetas
{
    /// <summary>Nunca lanza: un JSON roto da <see cref="DesconocidoViguetas"/>.</summary>
    public static MensajeViguetas Leer(string json)
    {
        Entrada? e;
        try
        {
            e = JsonSerializer.Deserialize<Entrada>(json, ProtocoloTablero.Json);
        }
        catch (JsonException)
        {
            return new DesconocidoViguetas();
        }
        if (e is null)
            return new DesconocidoViguetas();
        PedirPropuesta Pedido() => new(e.Panel ?? "", e.Direccion ?? Direccion.X, e.EjeAEje ?? 0, e.Ancho ?? 0, e.Borradas ?? []);
        return e.Tipo switch
        {
            "listo" => new ListoViguetas(),
            "piso" when e.Id is not null => new ElegirPiso(e.Id, e.ViguetasComoBorde ?? false),
            "propuesta" when e.Panel is not null => Pedido(),
            "crear" when e.Panel is not null && e.TipoVigueta is not null => new CrearViguetas(Pedido(), e.TipoVigueta),
            "quitar" when e.Panel is not null => new QuitarCreadas(e.Panel),
            "cerrar" => new CerrarViguetas(),
            _ => new DesconocidoViguetas(),
        };
    }

    public static string Pisos(string documento, IEnumerable<PisoResumen> pisos, IEnumerable<TipoArmazon> tipos) =>
        Escribir(new { tipo = "pisos", documento, pisos, tipos });

    public static string Piso(PisoViguetas p) => Escribir(new
    {
        tipo = "piso",
        id = p.Piso.Id,
        nombre = p.Piso.Nombre,
        cota = p.Piso.Cota,
        viguetasComoBorde = p.ViguetasComoBorde,
        armazon = p.Piso.Armazon.Select(a => new { id = a.Id, desde = Xy(a.Desde), hasta = Xy(a.Hasta), ancho = a.Ancho, tipo = a.NombreTipo, esVigueta = a.EsVigueta, creada = a.Creada }),
        columnas = p.Piso.Columnas.Select(c => new { id = c.Id, min = Xy(c.Min), max = Xy(c.Max) }),
        paneles = p.Paneles.Select(x => new { id = x.Id, eje = x.Eje.Select(Xy), libre = x.Libre.Select(Xy), creadas = p.CreadasEn(x).Count }),
    });

    public static string Propuesta(PropuestaViguetas? p, string panel) => p is null
        ? Escribir(new { tipo = "propuesta", panel, propuesta = (object?)null })
        : Escribir(new
        {
            tipo = "propuesta",
            panel,
            propuesta = new
            {
                direccion = p.Direccion,
                ancho = p.Ancho,
                libre = p.Libre,
                cantidad = p.Reparto.Cantidad,
                ejeAEje = p.Reparto.EjeAEje,
                libreExtremos = p.Reparto.LibreExtremos,
                libreMaximo = p.Reparto.LibreMaximo,
                viguetas = p.Viguetas.Select(v => new
                {
                    id = v.Id, cota = v.Cota, desde = Xy(p.Punto(v.Cota, v.Desde)), hasta = Xy(p.Punto(v.Cota, v.Hasta)),
                    largo = v.Largo, tipo = v.Tipo, borrada = v.Borrada,
                }),
                tipos = p.Tipos,
            },
        });

    public static string Aviso(string texto, bool bien = false) => Escribir(new { tipo = "aviso", texto, bien });

    private static double[] Xy(Punto p) => [Math.Round(p.X, 1), Math.Round(p.Y, 1)];

    private static string Escribir(object o) => JsonSerializer.Serialize(o, ProtocoloTablero.Json);

    private sealed class Entrada
    {
        public string? Tipo { get; set; }
        public string? Id { get; set; }
        public bool? ViguetasComoBorde { get; set; }
        public string? Panel { get; set; }
        public Direccion? Direccion { get; set; }
        public double? EjeAEje { get; set; }
        public double? Ancho { get; set; }
        public List<string>? Borradas { get; set; }
        public string? TipoVigueta { get; set; }
    }
}
