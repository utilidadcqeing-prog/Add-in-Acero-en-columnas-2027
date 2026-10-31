using System.IO;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using CQEING.Despiece.Calculo;
using CQEING.Despiece.Revit;

namespace CQEING.Despiece.Interfaz;

/// <summary>Une la página con Revit (#221). Los mensajes de la página se atienden en el hilo de Revit, en <see cref="AccionesRevit"/>.</summary>
internal sealed class PuenteDespiece
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly VentanaDespiece ventana;
    private readonly UIApplication uiapp;
    private readonly AccionesRevit acciones = new();
    private readonly Dictionary<string, object> plantas = [];
    private readonly Dictionary<string, object> previas = [];
    private readonly List<string> pendientes = [];
    private string? plantaEnCurso;
    private bool cerrado;
    private Document? doc;
    private List<VigaContinua> vigas = [];
    private IReadOnlyList<GrupoIguales> grupos = [];

    public PuenteDespiece(VentanaDespiece ventana, UIApplication uiapp)
    {
        this.ventana = ventana;
        this.uiapp = uiapp;
        ventana.MensajeRecibido += json => acciones.Encolar(_ => Atender(json));
        acciones.Fallo += texto => Enviar(new { tipo = "error", texto = $"No se pudo hacer en Revit: {texto}" });
        uiapp.Application.DocumentClosing += AlCerrarDocumento;
        ventana.Closed += (_, _) => Soltar();
    }

    private void Atender(string json)
    {
        using JsonDocument mensaje = JsonDocument.Parse(json);
        JsonElement raiz = mensaje.RootElement;
        Registro.Paso("mensaje de la página: " + json);
        switch (raiz.GetProperty("tipo").GetString())
        {
            case "cerrar":
                ventana.Close();
                break;
            case "iniciar":
                Iniciar();
                break;
            case "verPlanta":
                VerPlanta(raiz.GetProperty("id").GetString()!);
                break;
            case "previa":
                VerPrevia(raiz.GetProperty("grupo").GetInt32(), raiz.GetProperty("negros").GetBoolean());
                break;
        }
    }

    /// <summary>Lee el modelo una vez, al abrir: las plantas, las vigas y cuáles son iguales.</summary>
    private void Iniciar()
    {
        doc = uiapp.ActiveUIDocument?.Document;
        if (doc is null || doc.IsFamilyDocument)
        {
            Enviar(new { tipo = "error", texto = "Abre un proyecto de Revit para usar el despiece." });
            return;
        }
        vigas = LectorDespiece.Leer(doc);
        grupos = AgrupadorIguales.Agrupar(vigas.Select(v => v.Elemento));
        List<ViewPlan> lista = LectorDespiece.Plantas(doc);
        pendientes.Clear();
        pendientes.AddRange(lista.Select(p => p.Id.ToString()));
        plantas.Clear();
        plantaEnCurso = null;
        Enviar(new
        {
            tipo = "modelo",
            plantas = lista.Select(p => new { id = p.Id.ToString(), nombre = p.Name }),
            grupos = grupos.Select((g, i) => new
            {
                indice = i,
                nombre = g.Nombre,
                tieneAcero = g.TieneAcero,
                vigas = g.Elementos.SelectMany(e => Continua(e).Vigas).Select(id => id.ToString()),
                cantidad = g.Elementos.Count,
                filas = Filas(g),
            }),
        });
        GenerarSiguientePlanta();
    }

    private VigaContinua Continua(ElementoLeido e) => vigas.First(v => v.Elemento == e);

    private static IEnumerable<object> Filas(GrupoIguales g) => g.Elementos[0].Barras
        .GroupBy(b => (b.Tipo, b.Cantidad, b.LargoMm))
        .OrderByDescending(f => f.Key.LargoMm)
        .Select(f => new
        {
            c = LectorDespiece.Color(f.Key.Tipo),
            et = LectorDespiece.Etiqueta(f.Key.Tipo, f.Key.Cantidad, f.Key.LargoMm / 304.8),
            d = f.Count() == 1 ? "1 pieza" : $"{f.Count()} piezas",
        });

    /// <summary>
    /// La página pide una planta. Si ya está, se manda; si no, pasa al frente de las que faltan y se genera apenas Revit
    /// termine la que esté haciendo. Las demás se generan de fondo desde que abre la ventana, para que moverse entre
    /// plantas sea inmediato (Juan, 30-oct: "al cambiar entre plantas es un poco lento").
    /// </summary>
    private void VerPlanta(string id)
    {
        if (doc is null)
            return;
        if (plantas.TryGetValue(id, out object? listo))
        {
            Enviar(listo);
            return;
        }
        if (id != plantaEnCurso)
        {
            pendientes.Remove(id);
            pendientes.Insert(0, id);
        }
        GenerarSiguientePlanta();
    }

    /// <summary>Pide a Revit la primera planta de las que faltan; al terminar pide la siguiente, una por turno.</summary>
    private void GenerarSiguientePlanta()
    {
        if (plantaEnCurso is not null || pendientes.Count == 0)
            return;
        plantaEnCurso = pendientes[0];
        pendientes.RemoveAt(0);
        acciones.Encolar(_ =>
        {
            string id = plantaEnCurso!;
            try
            {
                Enviar(GenerarPlanta(id));
            }
            finally
            {
                plantaEnCurso = null;
                GenerarSiguientePlanta();
            }
        });
    }

    private object GenerarPlanta(string id)
    {
        var vista = (ViewPlan)doc!.GetElement(new ElementId(long.Parse(id)));
        PlantaGenerada p = GeneradorPlanta.Generar(doc, vista, ventana.CarpetaPrevia);
        object listo = new { tipo = "planta", id, url = Url(p.Ruta), ancho = p.Ancho, alto = p.Alto, cajas = p.Cajas.Select(c => new { viga = c.Key, x = c.Value.X, y = c.Value.Y, w = c.Value.Ancho, h = c.Value.Alto }) };
        plantas[id] = listo;
        return listo;
    }

    private void VerPrevia(int grupo, bool negros)
    {
        if (doc is null)
            return;
        string clave = $"{grupo}|{negros}";
        if (!previas.TryGetValue(clave, out object? listo))
        {
            PreviaGenerada p = GeneradorPrevia.Generar(doc, Continua(grupos[grupo].Elementos[0]), ventana.CarpetaPrevia, negros);
            listo = new { tipo = "previa", grupo, negros, longitudinal = Url(p.Longitudinal), seccion = Url(p.Seccion) };
            previas[clave] = listo;
        }
        Enviar(listo);
    }

    private static string Url(string ruta) => $"https://{VentanaDespiece.HostPrevia}/{Path.GetFileName(ruta)}";

    private void Enviar(object mensaje) => ventana.Enviar(JsonSerializer.Serialize(mensaje, Json));

    // Los manejadores de eventos de Revit nunca dejan escapar una excepción: Revit los llama desde su lado nativo.
    private void AlCerrarDocumento(object? sender, DocumentClosingEventArgs e)
    {
        try
        {
            if (!cerrado && e.Document.Equals(doc))
                ventana.Close();
        }
        catch (Exception)
        {
            // Cerrar la ventana es una ayuda: nunca debe impedir que se cierre el documento.
        }
    }

    /// <summary>
    /// La ventana se cerró. Corre en un evento de WPF, fuera del contexto de la API de Revit: quitar una suscripción o
    /// soltar el <see cref="ExternalEvent"/> puede lanzar «Revit is currently not within an API context», y una
    /// excepción sin atender en ese hilo cierra Revit entero (30-oct, #221: pasó dos veces). Igual que el tablero de
    /// vigas, se atrapa; y <see cref="cerrado"/> hace que, si la suscripción queda viva, no haga nada.
    /// </summary>
    private void Soltar()
    {
        cerrado = true;
        Registro.Paso("ventana: cerrada");
        try
        {
            uiapp.Application.DocumentClosing -= AlCerrarDocumento;
        }
        catch (Exception e)
        {
            Registro.Paso("ventana: no se pudo quitar la suscripción: " + e.Message);
        }
        try
        {
            acciones.Dispose();
        }
        catch (Exception e)
        {
            Registro.Paso("ventana: no se pudo soltar el evento: " + e.Message);
        }
    }
}
