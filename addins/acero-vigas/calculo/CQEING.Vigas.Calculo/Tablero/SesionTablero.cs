using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>Lo que sale de atender un mensaje: algo para la página o algo que hacer en Revit.</summary>
public abstract record Salida;

/// <summary>Un mensaje JSON para la página (<see cref="ProtocoloTablero.Vista"/> o <see cref="ProtocoloTablero.Aviso"/>).</summary>
public sealed record AJs(string Json) : Salida;

/// <summary>Una acción en Revit sobre las vigas de un pórtico (ids de elemento como texto).</summary>
public sealed record ARevit(AccionRevit Accion, IReadOnlyList<string> Vigas) : Salida;

/// <summary>
/// "Generar en Revit" (#85): los pórticos que sí se generan, con su plan, y los que se saltan, cada uno con su motivo
/// (un texto por línea para el resumen). <paramref name="Reglas"/> son las vigentes, para buscar los tipos de barra.
/// </summary>
public sealed record AGenerar(IReadOnlyList<PorticoAGenerar> Porticos, IReadOnlyList<string> Saltados, Reglas Reglas) : Salida;

/// <summary>Un pórtico para generar: <paramref name="Vigas"/> son las suyas, para borrar lo que el add-in creó antes en ellas.</summary>
public sealed record PorticoAGenerar(string Id, IReadOnlyList<string> Vigas, UbicacionPortico Ubicacion, PlanAcero Plan);

public enum AccionRevit
{
    /// <summary>Dejar seleccionadas las vigas (En vivo).</summary>
    Seleccionar,
    /// <summary>"Ver en Revit": mostrarlas en la vista activa.</summary>
    Mostrar,
    /// <summary>"3D": la vista 3D aislada en ellas.</summary>
    Abrir3d,
    /// <summary>"Seleccionar en Revit": que el usuario elija vigas en el modelo.</summary>
    PedirSeleccion,
}

/// <summary>
/// El estado del tablero de un documento (#83), sin Revit: los pórticos leídos, las opciones de cada uno, el que está
/// abierto, las reglas y si está "En vivo". Atiende los mensajes de la página y dice qué contestar y qué hacer en Revit.
/// <paramref name="nominalesDelModelo"/> da el diámetro nominal en mm de cada tipo de barra del modelo (#85); null = todos los de las
/// reglas.
/// </summary>
public sealed class SesionTablero(Func<ResultadoLectura> leer, AlmacenReglas almacen, string proyecto,
    Func<IEnumerable<double>>? nominalesDelModelo = null)
{
    private readonly Dictionary<string, OpcionesTablero> opciones = [];
    private ResultadoLectura lectura = new([], []);
    private List<PorticoConId> porticos = [];
    private List<string>? elegidos;
    private ReglasCargadas reglas = new(Reglas.Generales, null);
    private string? actual;
    private bool enVivo = true;
    private string proyecto = proyecto;

    /// <summary>Si el tablero sigue la selección de Revit y la marca en Revit ("En vivo con Revit").</summary>
    public bool EnVivo => enVivo;

    /// <summary>El pórtico abierto; null si ninguno.</summary>
    public string? Actual => actual;

    public IReadOnlyList<Salida> Manejar(MensajeTablero m)
    {
        switch (m)
        {
            case Listo:
                reglas = almacen.Cargar();
                Recargar();
                return [Vista()];
            case ElegirPortico(string id):
                if (Buscar(id) is not { } elegido)
                    return [Vista()];
                actual = id;
                if (elegidos is not null && !elegidos.Contains(id))
                    elegidos = null;
                return enVivo ? [Vista(), new ARevit(AccionRevit.Seleccionar, elegido.Vigas)] : [Vista()];
            case Calcular(string id, OpcionesTablero o):
                if (Buscar(id) is null)
                    return [Vista()];
                (opciones[id], actual) = (o, id);
                return [Vista()];
            case Previsualizar(string id, OpcionesTablero o, PedidoEdicion pedido):
                // no guarda las opciones: es solo ver qué pasaría (#84)
                return Buscar(id) is { } p ? [new AJs(ProtocoloTablero.Previa(EditorTablero.Previsualizar(p, o, reglas.Reglas, pedido)))] : [];
            case SeleccionarEnRevit:
                return [new ARevit(AccionRevit.PedirSeleccion, [])];
            case VerEnRevit(string id):
                return Buscar(id) is { } ver ? [new ARevit(AccionRevit.Mostrar, ver.Vigas)] : [];
            case Abrir3d(string id):
                return Buscar(id) is { } en3d ? [new ARevit(AccionRevit.Abrir3d, en3d.Vigas)] : [];
            case CambiarEnVivo(bool activo):
                enVivo = activo;
                return [Vista()];
            case VerTodos:
                elegidos = null;
                return [Vista()];
            case GuardarReglas(VistaReglas pantalla):
                return Guardar(pantalla);
            case Generar(IReadOnlyList<string> ids):
                return [AGenerar(ids)];
            default:
                return [];
        }
    }

    /// <summary>
    /// Arma lo que se genera de cada pórtico con las opciones que tiene en el tablero (o las de base). Se saltan los que ya
    /// no están, los que el tablero no deja generar y los que usan un diámetro sin tipo de barra en el modelo.
    /// </summary>
    private AGenerar AGenerar(IReadOnlyList<string> ids)
    {
        HashSet<int> disponibles = Disponibles().ToHashSet();
        var aGenerar = new List<PorticoAGenerar>();
        var saltados = new List<string>();
        foreach (string id in ids.Distinct())
        {
            if (Buscar(id) is not { } p)
            {
                saltados.Add($"{id}: ya no está en el modelo.");
                continue;
            }
            if (PlanificadorAcero.Planificar(id, p, opciones.GetValueOrDefault(id) ?? OpcionesTablero.PorDefecto, reglas.Reglas) is not { } plan
                || p.Ubicacion is null)
            {
                saltados.Add($"{id}: no se puede generar; revisa los avisos del tablero.");
                continue;
            }
            if (plan.Diametros.Where(d => !disponibles.Contains(d)).ToList() is { Count: > 0 } faltan)
            {
                saltados.Add($"{id}: el modelo no tiene un tipo de barra para el {string.Join(", ", faltan.Select(d => $"#{d}"))}.");
                continue;
            }
            aGenerar.Add(new PorticoAGenerar(id, p.Vigas, p.Ubicacion, plan));
        }
        return new AGenerar(aGenerar, saltados, reglas.Reglas);
    }

    private IReadOnlyList<int> Disponibles() =>
        nominalesDelModelo is null ? reglas.Reglas.PorDiametro.Keys.Order().ToList() : DiametrosDisponibles.De(nominalesDelModelo(), reglas.Reglas);

    /// <summary>
    /// Vuelve a leer el modelo (otro documento, o el mismo con cambios). Las opciones de cada pórtico se conservan
    /// mientras su nombre exista; si el abierto ya no está, se abre el primero (o ninguno).
    /// </summary>
    public void Recargar(string? proyectoNuevo = null)
    {
        proyecto = proyectoNuevo ?? proyecto;
        lectura = leer();
        porticos = ConId(lectura.Porticos);
        foreach (string nombre in opciones.Keys.Where(n => Buscar(n) is null).ToList())
            opciones.Remove(nombre);
        if (actual is null || Buscar(actual) is null)
            actual = porticos.FirstOrDefault()?.Id;
        elegidos = elegidos?.Where(id => Buscar(id) is not null).ToList() is { Count: > 0 } siguen ? siguen : null;
    }

    /// <summary>Todas las vigas de los pórticos leídos (el botón "Todas" de la barra de selección).</summary>
    public IReadOnlyList<string> Vigas => porticos.SelectMany(p => p.Portico.Vigas).ToList();

    /// <summary>
    /// "Seleccionar en Revit" con la barra del add-in: la lista muestra solo los pórticos de <paramref name="vigas"/> y
    /// se abre el primero. Si ninguna está en un pórtico, avisa y no cambia nada.
    /// </summary>
    public IReadOnlyList<Salida> ElegirVigas(IEnumerable<string> vigas)
    {
        var elegidas = vigas.ToHashSet();
        List<string> ids = porticos.Where(p => p.Portico.Vigas.Any(elegidas.Contains)).Select(p => p.Id).ToList();
        if (ids.Count == 0)
            return [new AJs(ProtocoloTablero.Aviso("Esas vigas no están en ningún pórtico del tablero."))];
        (elegidos, actual) = (ids, ids[0]);
        return [Vista()];
    }

    /// <summary>El pórtico al que pertenece la viga <paramref name="idViga"/>, para "En vivo"; null si ninguno.</summary>
    public string? PorticoDeViga(string idViga) => porticos.FirstOrDefault(p => p.Portico.Vigas.Contains(idViga))?.Id;

    /// <summary>
    /// El id de cada pórtico en el tablero: su nombre, o "nombre · nivel" si el mismo eje se repite en varios niveles
    /// ("Pórtico 1" de Piso 1, Piso 2 y Cubierta).
    /// </summary>
    private static List<PorticoConId> ConId(IReadOnlyList<PorticoLeido> leidos)
    {
        HashSet<string> repetidos = leidos.GroupBy(p => p.Nombre).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        return leidos.Select(p => new PorticoConId(repetidos.Contains(p.Nombre) && p.Nivel is not null ? $"{p.Nombre} · {p.Nivel}" : p.Nombre, p,
            repetidos.Contains(p.Nombre) ? p.Nivel : null)).ToList();
    }

    /// <param name="Nivel">El nivel que se muestra en la lista, solo cuando el nombre se repite.</param>
    private sealed record PorticoConId(string Id, PorticoLeido Portico, string? Nivel);

    private IReadOnlyList<Salida> Guardar(VistaReglas pantalla)
    {
        Reglas nuevas = ArmadorVista.ReglasDe(pantalla, reglas.Reglas);
        IReadOnlyList<string> errores = nuevas.Validar();
        if (errores.Count > 0)
            return [new AJs(ProtocoloTablero.Aviso(errores[0]))];
        try
        {
            almacen.Guardar(nuevas);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [new AJs(ProtocoloTablero.Aviso($"No se pudieron guardar las reglas: {e.Message}"))];
        }
        reglas = new ReglasCargadas(nuevas, null);
        return [Vista()];
    }

    private PorticoLeido? Buscar(string id) => porticos.FirstOrDefault(p => p.Id == id)?.Portico;

    /// <summary>La vista de todo el tablero para la página, por ejemplo después de <see cref="Recargar"/>.</summary>
    public AJs Vista()
    {
        PorticoLeido? abierto = actual is null ? null : Buscar(actual);
        VistaPortico? vista = abierto is null
            ? null
            : ArmadorVista.Armar(abierto, opciones.GetValueOrDefault(actual!) ?? OpcionesTablero.PorDefecto, reglas.Reglas) with { Id = actual! };
        var lista = porticos.Select(p => ArmadorVista.EnLista(p.Portico) is var l && p.Nivel is not null
            ? l with { Id = p.Id, Sub = $"{l.Sub} · {p.Nivel}" }
            : l with { Id = p.Id }).ToList();
        return new AJs(ProtocoloTablero.Vista(new VistaTablero(proyecto, lista, vista, ArmadorVista.Reglas(reglas), enVivo, elegidos, Disponibles())));
    }
}
