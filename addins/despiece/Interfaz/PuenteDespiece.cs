using System.IO;
using System.Text.Json;
using System.Windows.Threading;
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

    /// <summary>Corre un trabajo del add-in: lo que cambie en el documento mientras tanto no cuenta como cambio del usuario.</summary>
    private bool trabajando;

    /// <summary>Espera a que el usuario termine: una sola orden de Revit puede ser varias transacciones seguidas.</summary>
    private readonly DispatcherTimer esperaCambios = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public PuenteDespiece(VentanaDespiece ventana, UIApplication uiapp)
    {
        this.ventana = ventana;
        this.uiapp = uiapp;
        ventana.MensajeRecibido += json => Encolar(() => Atender(json));
        acciones.Fallo += texto => Enviar(new { tipo = "error", texto = $"No se pudo hacer en Revit: {texto}" });
        esperaCambios.Tick += (_, _) =>
        {
            esperaCambios.Stop();
            Encolar(Refrescar);
        };
        uiapp.Application.DocumentClosing += AlCerrarDocumento;
        uiapp.Application.DocumentChanged += AlCambiarDocumento;
        ventana.Closed += (_, _) => Soltar();
    }

    private void Encolar(Action trabajo) => acciones.Encolar(_ =>
    {
        trabajando = true;
        try
        {
            trabajo();
        }
        finally
        {
            trabajando = false;
        }
    });

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
                VerPrevia(raiz);
                break;
            case "crearVistas":
                CrearVistas(raiz.GetProperty("grupo").GetInt32(), raiz.GetProperty("negros").GetBoolean(), Movimientos(raiz));
                break;
            case "ver3d":
                Ver3D(raiz.GetProperty("grupo").GetInt32());
                break;
            case "irAVista":
                IrAVista(raiz.GetProperty("grupo").GetInt32(), raiz.GetProperty("seccion").GetBoolean());
                break;
        }
    }

    /// <summary>Lo que la página dice que se movió: <c>{ "397193": { dz, tx, tz } }</c>, en metros.</summary>
    private static Dictionary<string, Movimiento> Movimientos(JsonElement raiz)
    {
        var movimientos = new Dictionary<string, Movimiento>();
        foreach (JsonProperty p in raiz.GetProperty("movimientos").EnumerateObject())
            movimientos[p.Name] = new Movimiento(p.Value.GetProperty("dz").GetDouble(), p.Value.GetProperty("tx").GetDouble(), p.Value.GetProperty("tz").GetDouble());
        return movimientos;
    }

    private void CrearVistas(int grupo, bool negros, Dictionary<string, Movimiento> movimientos)
    {
        if (doc is null)
            return;
        GrupoIguales g = grupos[grupo];
        VistasCreadas v = GeneradorPrevia.Crear(doc, Continua(g.Elementos[0]), g.Nombre, negros, new DespieceMovible(movimientos, false));
        Enviar(new { tipo = "vistas", grupo, longitudinal = v.Longitudinal, seccion = v.Seccion, yaExistian = v.YaExistian });
    }

    private void Ver3D(int grupo)
    {
        if (doc is null || grupo >= grupos.Count)
            return;
        Enviar(new { tipo = "modelo3d", grupo, modelo = Modelo3D.Leer(doc, Continua(grupos[grupo].Elementos[0])) });
    }

    /// <summary>Abre en Revit la vista larga o la sección del grupo. La ventana se minimiza porque queda encima de Revit y taparía la vista.</summary>
    private void IrAVista(int grupo, bool seccion)
    {
        if (doc is null || grupo >= grupos.Count)
            return;
        if (GeneradorPrevia.VistaCreada(doc, grupos[grupo].Nombre, seccion) is not { } vista)
        {
            Enviar(new { tipo = "error", texto = "Esa vista ya no existe en el proyecto." });
            return;
        }
        uiapp.ActiveUIDocument.RequestViewChange(vista);
        ventana.WindowState = System.Windows.WindowState.Minimized;
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
        plantaEnCurso = null;
        Cargar(false);
    }

    /// <summary>
    /// El modelo cambió en Revit: se lee de nuevo y se rehacen las imágenes. La página sigue mostrando las de antes hasta
    /// que lleguen las nuevas. Una planta ya en la cola se deja correr: al terminar sigue con las pendientes.
    /// </summary>
    private void Refrescar()
    {
        if (doc is null || !doc.IsValidObject)
            return;
        Registro.Paso("modelo cambió: refrescando");
        Cargar(true);
    }

    private void Cargar(bool refresco)
    {
        vigas = LectorDespiece.Leer(doc!);
        grupos = AgrupadorIguales.Agrupar(vigas.Select(v => v.Elemento));
        List<ViewPlan> lista = LectorDespiece.Plantas(doc!);
        pendientes.Clear();
        pendientes.AddRange(lista.Select(p => p.Id.ToString()));
        plantas.Clear();
        previas.Clear();
        Enviar(new
        {
            tipo = "modelo",
            refresco,
            plantas = lista.Select(p => new { id = p.Id.ToString(), nombre = p.Name }),
            grupos = grupos.Select((g, i) => new
            {
                indice = i,
                nombre = g.Nombre,
                tieneAcero = g.TieneAcero,
                vistaCreada = GeneradorPrevia.VistaCreada(doc!, g.Nombre) is not null,
                vigas = g.Elementos.SelectMany(e => Continua(e).Vigas).Select(id => id.ToString()),
                cantidad = g.Elementos.Count,
                filas = Filas(g),
            }),
        });
        GenerarSiguientePlanta();
    }

    private VigaContinua Continua(ElementoLeido e) => vigas.First(v => v.Elemento == e);

    private static IEnumerable<object> Filas(GrupoIguales g) => g.Elementos[0].Barras
        .GroupBy(b => (b.Tipo, b.Cantidad, b.LargoMm, b.RectoMm))
        .OrderByDescending(f => f.Key.LargoMm)
        .Select(f => new
        {
            c = LectorDespiece.Color(f.Key.Tipo),
            et = LectorDespiece.Etiqueta(f.Key.Tipo, f.Key.Cantidad, f.Key.LargoMm / 304.8, f.Key.RectoMm / 304.8),
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
        Encolar(() =>
        {
            string id = plantaEnCurso!;
            try
            {
                if (GenerarPlanta(id) is { } listo)
                    Enviar(listo);
            }
            finally
            {
                plantaEnCurso = null;
                GenerarSiguientePlanta();
            }
        });
    }

    /// <summary>Null si la planta ya no existe: la pudieron borrar mientras esperaba su turno.</summary>
    private object? GenerarPlanta(string id)
    {
        if (doc!.GetElement(new ElementId(long.Parse(id))) is not ViewPlan vista)
            return null;
        PlantaGenerada p = GeneradorPlanta.Generar(doc, vista, ventana.CarpetaPrevia);
        object listo = new { tipo = "planta", id, url = Url(p.Ruta), ancho = p.Ancho, alto = p.Alto, cajas = p.Cajas.Select(c => new { viga = c.Key, x = c.Value.X, y = c.Value.Y, w = c.Value.Ancho, h = c.Value.Alto }) };
        plantas[id] = listo;
        return listo;
    }

    /// <summary>
    /// La vista previa que pide la página. <c>separar</c>: la del paso 2, con lo movible aparte para arrastrarlo; si no, la
    /// imagen real con los movimientos aplicados. La clave la pone la página, y dice todo lo que distingue una imagen de otra.
    /// </summary>
    private void VerPrevia(JsonElement raiz)
    {
        int grupo = raiz.GetProperty("grupo").GetInt32();
        if (doc is null || grupo >= grupos.Count)
            return;
        string clave = raiz.GetProperty("clave").GetString()!;
        if (!previas.TryGetValue(clave, out object? listo))
        {
            var movible = new DespieceMovible(Movimientos(raiz), raiz.GetProperty("separar").GetBoolean());
            PreviaGenerada p = GeneradorPrevia.Generar(doc, Continua(grupos[grupo].Elementos[0]), ventana.CarpetaPrevia, raiz.GetProperty("negros").GetBoolean(), movible);
            listo = new { tipo = "previa", grupo, clave, capa = p.Capa, longitudinal = Imagen(p.Longitudinal), seccion = Imagen(p.Seccion) };
            previas[clave] = listo;
        }
        Enviar(listo);
    }

    /// <summary>La página necesita el tamaño para acercarla y moverla (<c>Visor</c> en pasos.js).</summary>
    private static object Imagen(string ruta)
    {
        (int ancho, int alto) = ExportadorImagen.Tamano(ruta);
        return new { url = Url(ruta), ancho, alto };
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
    /// Lo que cambia las imágenes: vigas, acero, columnas, ejes o niveles, o cualquier cosa borrada (de lo borrado ya no se
    /// puede saber qué era). Aquí no se puede tocar el modelo: solo se pide el refresco. Elegir o mirar no dispara este
    /// evento, ni las vistas temporales del add-in, que se hacen con <see cref="trabajando"/>.
    /// </summary>
    private void AlCambiarDocumento(object? sender, DocumentChangedEventArgs e)
    {
        try
        {
            if (cerrado || trabajando || !e.GetDocument().Equals(doc))
                return;
            var relevantes = new ElementMulticategoryFilter(CategoriasDelModelo);
            if (e.GetDeletedElementIds().Count == 0 && e.GetAddedElementIds(relevantes).Count == 0 && e.GetModifiedElementIds(relevantes).Count == 0)
                return;
            esperaCambios.Stop();
            esperaCambios.Start();
        }
        catch (Exception ex)
        {
            Registro.Paso("cambio del modelo: no se pudo revisar: " + ex.Message);
        }
    }

    private static readonly BuiltInCategory[] CategoriasDelModelo =
    [
        BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Rebar, BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_Columns, BuiltInCategory.OST_Grids, BuiltInCategory.OST_Levels, BuiltInCategory.OST_Viewers,
    ];

    /// <summary>
    /// La ventana se cerró. Corre en un evento de WPF, fuera del contexto de la API de Revit: quitar una suscripción o
    /// soltar el <see cref="ExternalEvent"/> puede lanzar «Revit is currently not within an API context», y una
    /// excepción sin atender en ese hilo cierra Revit entero (30-oct, #221: pasó dos veces). Igual que el tablero de
    /// vigas, se atrapa; y <see cref="cerrado"/> hace que, si la suscripción queda viva, no haga nada.
    /// </summary>
    private void Soltar()
    {
        cerrado = true;
        esperaCambios.Stop();
        Registro.Paso("ventana: cerrada");
        try
        {
            uiapp.Application.DocumentClosing -= AlCerrarDocumento;
            uiapp.Application.DocumentChanged -= AlCambiarDocumento;
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
