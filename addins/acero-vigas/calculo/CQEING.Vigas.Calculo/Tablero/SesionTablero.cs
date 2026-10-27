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

/// <summary>
/// La vista previa 3D del acero (el botón "3D"): el pórtico con su plan, como lo crearía "Generar", para dibujarlo en la
/// vista 3D sin crear nada en el modelo. <paramref name="Portico"/> null = quitarla.
/// </summary>
public sealed record APrevia3d(PorticoAGenerar? Portico, Reglas Reglas) : Salida;

/// <summary>
/// Un pórtico para generar: <paramref name="Vigas"/> son las suyas, para borrar lo que el add-in creó antes en ellas.
/// <paramref name="Configuracion"/> es lo que se guarda en cada viga (<see cref="ConfiguracionGuardada"/>); vacía en la
/// vista previa 3D, que no guarda nada.
/// </summary>
public sealed record PorticoAGenerar(string Id, IReadOnlyList<string> Vigas, UbicacionPortico Ubicacion, PlanAcero Plan, string Configuracion = "");

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
/// reglas. <paramref name="configuracionDeViga"/> da lo que guardó una generación anterior en la viga con ese id
/// (<see cref="ConfiguracionGuardada"/>), o null; sin ella no se recupera nada. <paramref name="dobladosDelModelo"/> da
/// el diámetro y los doblados de los tipos de barra del modelo, para que los choques del alzado sean los de la vista
/// previa 3D (H3 del plan de choques de 22-oct); null = los de las reglas.
/// </summary>
public sealed class SesionTablero(Func<ResultadoLectura> leer, AlmacenReglas almacen, string proyecto,
    Func<IEnumerable<double>>? nominalesDelModelo = null, Func<string, string?>? configuracionDeViga = null,
    Func<Reglas, IReadOnlyDictionary<int, DobladoBarra>>? dobladosDelModelo = null)
{
    private readonly Dictionary<string, OpcionesTablero> opciones = [];

    /// <summary>El estado de la página con que se generó cada pórtico (recuperado de sus vigas o mandado al generar).</summary>
    private readonly Dictionary<string, string> estados = [];
    private ResultadoLectura lectura = new([], []);
    private List<PorticoConId> porticos = [];

    /// <summary>La vista en planta de lo leído y los sistemas de viguetas iguales que salen de ella (planta, 27-oct).</summary>
    private PlantaVista planta = new([]);
    private IReadOnlyDictionary<string, IReadOnlyList<string>> grupos = new Dictionary<string, IReadOnlyList<string>>();
    private List<string>? elegidos;
    private ReglasCargadas reglas = new(Reglas.Generales, null);
    private string? actual;
    private string? en3d;
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
                // un sistema de viguetas lleva el mismo acero en todas (Carlos; planta, 27-oct)
                foreach (string otra in Miembros(id).Where(m => m != id))
                    opciones[otra] = o;
                // la vista previa 3D sigue el cambio si está en el pórtico o en otra del sistema
                return en3d is not null && Miembros(id).Contains(en3d) ? [Vista(), Previa3d(en3d)] : [Vista()];
            case Previsualizar(string id, OpcionesTablero o, PedidoEdicion pedido):
                // no guarda las opciones: es solo ver qué pasaría (#84)
                return Buscar(id) is { } p ? [new AJs(ProtocoloTablero.Previa(EditorTablero.Previsualizar(p, o, reglas.Reglas, pedido)))] : [];
            case PedirArreglos(string id, OpcionesTablero o, int n, int choque):
                // como la vista previa, no guarda las opciones
                return Buscar(id) is { } conChoque
                    ? [new AJs(ProtocoloTablero.Arreglos(new ArreglosVista(n, choque,
                        ArreglosChoque.Buscar(id, conChoque, o, reglas.Reglas, dobladosDelModelo?.Invoke(reglas.Reglas), choque))))]
                    : [];
            case SeleccionarEnRevit:
                return [new ARevit(AccionRevit.PedirSeleccion, [])];
            case VerEnRevit(string id):
                return Buscar(id) is { } ver ? [new ARevit(AccionRevit.Mostrar, ver.Vigas)] : [];
            case Abrir3d(string id):
                if (Buscar(id) is not { } vigas3d)
                    return [];
                en3d = id;
                return [new ARevit(AccionRevit.Abrir3d, vigas3d.Vigas), Previa3d(id)];
            case CambiarEnVivo(bool activo):
                enVivo = activo;
                return [Vista()];
            case VerTodos:
                elegidos = null;
                return [Vista()];
            case ElegirVarios(var ids, string abrir):
                List<string> validos = ids.Where(id => Buscar(id) is not null).Distinct().ToList();
                if (validos.Count == 0)
                    return [Vista()];
                elegidos = validos.Count > 1 ? validos : null;
                actual = validos.Contains(abrir) ? abrir : validos[0];
                return enVivo
                    ? [Vista(), new ARevit(AccionRevit.Seleccionar, validos.SelectMany(id => Buscar(id)!.Vigas).ToList())]
                    : [Vista()];
            case GuardarReglas(VistaReglas pantalla):
                return Guardar(pantalla);
            case Generar(IReadOnlyList<string> ids, var estadosPagina, bool forzar):
                AGenerar generar = AGenerar(ConSusSistemas(ids), EstadosDelSistema(ids, estadosPagina));
                // Decisión 8 del plan de choques: avisa los choques de todos los que se generan, no solo del abierto. No
                // bloquea: "Generar de todos modos" vuelve con forzar.
                if (!forzar && ConChoques(generar) is { Count: > 0 } conChoques)
                    return [new AJs(ProtocoloTablero.Choques(conChoques))];
                // Lo generado ya se ve de verdad: la vista previa encima estorbaría.
                if (en3d is null)
                    return [generar];
                en3d = null;
                return [new APrevia3d(null, reglas.Reglas), generar];
            default:
                return [];
        }
    }

    /// <summary>
    /// Arma lo que se genera de cada pórtico con las opciones que tiene en el tablero (o las de base). Se saltan los que ya
    /// no están, los que el tablero no deja generar y los que usan un diámetro sin tipo de barra en el modelo. Cada uno
    /// lleva su configuración para guardarla en sus vigas: sus opciones y el estado de la página (el que manda ahora o, si
    /// no lo manda, el que ya tenía).
    /// </summary>
    private AGenerar AGenerar(IReadOnlyList<string> ids, IReadOnlyDictionary<string, string>? estadosPagina)
    {
        foreach ((string id, string estado) in estadosPagina ?? new Dictionary<string, string>())
            estados[id] = estado;
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
            OpcionesTablero o = Opciones(id);
            if (PlanificadorAcero.Planificar(id, p, o, reglas.Reglas) is not { } plan || p.Ubicacion is null)
            {
                saltados.Add($"{id}: no se puede generar; revisa los avisos del tablero.");
                continue;
            }
            if (plan.Diametros.Where(d => !disponibles.Contains(d)).ToList() is { Count: > 0 } faltan)
            {
                saltados.Add($"{id}: el modelo no tiene un tipo de barra para el {string.Join(", ", faltan.Select(d => $"#{d}"))}.");
                continue;
            }
            aGenerar.Add(new PorticoAGenerar(id, p.Vigas, p.Ubicacion, plan, ConfiguracionGuardada.Escribir(o, estados.GetValueOrDefault(id))));
        }
        return new AGenerar(aGenerar, saltados, reglas.Reglas);
    }

    /// <summary>
    /// Los pórticos del sistema de viguetas de <paramref name="id"/> (él incluido), en su orden; solo él si no está en
    /// ninguno (planta, 27-oct).
    /// </summary>
    private IReadOnlyList<string> Miembros(string id) => grupos.GetValueOrDefault(id) ?? [id];

    /// <summary>Los ids con los demás miembros del sistema de cada vigueta, sin repetir y en su orden.</summary>
    private List<string> ConSusSistemas(IReadOnlyList<string> ids) => ids.SelectMany(Miembros).Distinct().ToList();

    /// <summary>
    /// Los estados de la página con el del miembro de un sistema copiado a los demás que no traen uno propio: el del
    /// abierto si viene, si no el primero que venga. Sin estados, null (como antes).
    /// </summary>
    private IReadOnlyDictionary<string, string>? EstadosDelSistema(IReadOnlyList<string> ids, IReadOnlyDictionary<string, string>? estadosPagina)
    {
        if (estadosPagina is null)
            return null;
        var todos = new Dictionary<string, string>(estadosPagina);
        foreach (string id in ids.Where(grupos.ContainsKey))
        {
            IReadOnlyList<string> miembros = Miembros(id);
            string? fuente = actual is not null && miembros.Contains(actual) && estadosPagina.ContainsKey(actual)
                ? actual
                : miembros.FirstOrDefault(estadosPagina.ContainsKey);
            if (fuente is null)
                continue;
            foreach (string m in miembros.Where(m => !estadosPagina.ContainsKey(m)))
                todos[m] = estadosPagina[fuente];
        }
        return todos;
    }

    /// <summary>Los pórticos de <paramref name="generar"/> con choques, con cuántos puntos tiene cada uno, en su orden.</summary>
    private List<PorticoConChoques> ConChoques(AGenerar generar)
    {
        IReadOnlyDictionary<int, DobladoBarra>? doblados = dobladosDelModelo?.Invoke(reglas.Reglas);
        return generar.Porticos
            .Select(p => new PorticoConChoques(p.Id, ArmadorChoques.Detectar(p.Plan, p.Ubicacion, reglas.Reglas, doblados).Count))
            .Where(p => p.Cantidad > 0).ToList();
    }

    /// <summary>La vista previa del pórtico con sus opciones; sin plan (no se puede generar) queda vacía.</summary>
    private APrevia3d Previa3d(string id)
    {
        PorticoAGenerar? p = Buscar(id) is { Ubicacion: { } u } portico
            && PlanificadorAcero.Planificar(id, portico, Opciones(id), reglas.Reglas) is { } plan
            ? new PorticoAGenerar(id, portico.Vigas, u, plan)
            : null;
        return new APrevia3d(p, reglas.Reglas);
    }

    // sin nada elegido, las de arranque de lo que se detectó: viga o vigueta (#942)
    private OpcionesTablero Opciones(string id) =>
        opciones.GetValueOrDefault(id) ?? OpcionesTablero.Para(Buscar(id)?.Elemento ?? Elemento.Viga);

    private IReadOnlyList<int> Disponibles() =>
        nominalesDelModelo is null ? reglas.Reglas.PorDiametro.Keys.Order().ToList() : DiametrosDisponibles.De(nominalesDelModelo(), reglas.Reglas);

    /// <summary>
    /// Vuelve a leer el modelo (otro documento, o el mismo con cambios). Las opciones de cada pórtico se conservan
    /// mientras su nombre exista; si el abierto ya no está, se abre el primero (o ninguno). Un pórtico sin opciones en
    /// esta sesión toma las que guardó una generación anterior en sus vigas, si las hay.
    /// </summary>
    public void Recargar(string? proyectoNuevo = null)
    {
        proyecto = proyectoNuevo ?? proyecto;
        en3d = null; // la vista previa es del documento anterior: quien recarga la quita en Revit
        lectura = leer();
        porticos = ConId(lectura.Porticos);
        planta = ArmadorPlanta.Armar(porticos.Select(p => (p.Id, p.Portico)), lectura.Modelo);
        grupos = ArmadorPlanta.Grupos(planta);
        foreach (string nombre in opciones.Keys.Where(n => Buscar(n) is null).ToList())
            opciones.Remove(nombre);
        foreach (string nombre in estados.Keys.Where(n => Buscar(n) is null).ToList())
            estados.Remove(nombre);
        foreach (PorticoConId p in porticos.Where(p => !opciones.ContainsKey(p.Id)))
            if (Guardada(p.Portico) is ({ } o, var estado))
            {
                opciones[p.Id] = o;
                if (estado is not null)
                    estados[p.Id] = estado;
            }
        if (actual is null || Buscar(actual) is null)
            actual = porticos.FirstOrDefault()?.Id;
        elegidos = elegidos?.Where(id => Buscar(id) is not null).ToList() is { Count: > 0 } siguen ? siguen : null;
    }

    /// <summary>La configuración de la primera viga del pórtico que tenga una que se entienda (decisión 2 del plan).</summary>
    private (OpcionesTablero Opciones, string? Estado)? Guardada(PorticoLeido p) =>
        configuracionDeViga is null ? null
            : p.Vigas.Select(configuracionDeViga).OfType<string>().Select(ConfiguracionGuardada.Leer).FirstOrDefault(c => c is not null);

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
        VistaPortico? vista = null;
        if (abierto is not null)
        {
            // el mismo plan que crea "Generar": los bastones se ven con su largo en Revit y de él salen los choques
            PlanAcero? plan = abierto.Ubicacion is null ? null : PlanificadorAcero.Planificar(actual!, abierto, Opciones(actual!), reglas.Reglas);
            vista = ConChoques(abierto, plan, ArmadorVista.Armar(abierto, Opciones(actual!), reglas.Reglas, plan)
                with { Id = actual!, Estado = estados.GetValueOrDefault(actual!) });
        }
        var lista = porticos.Select(p => ArmadorVista.EnLista(p.Portico) is var l && p.Nivel is not null
            ? l with { Id = p.Id, Sub = $"{l.Sub} · {p.Nivel}", Grupo = GrupoDe(p.Id) }
            : l with { Id = p.Id, Grupo = GrupoDe(p.Id) }).ToList();
        return new AJs(ProtocoloTablero.Vista(new VistaTablero(proyecto, lista, vista, ArmadorVista.Reglas(reglas), enVivo, elegidos, Disponibles(),
            planta)));
    }

    /// <summary>El texto del sistema de viguetas del pórtico en la planta; vacío si no está en ninguno.</summary>
    private string GrupoDe(string id) =>
        grupos.ContainsKey(id)
            ? planta.Niveles.SelectMany(n => n.Elementos).First(e => e.Portico == id).Grupo
            : "";

    /// <summary>
    /// Los puntos donde las barras del pórtico abierto se tocan, sobre el mismo plan que crea "Generar". Van solo como
    /// puntos en el alzado, sin aviso en la lista (Juan, 22-oct). Sin ubicación en el modelo o sin plan (no se puede
    /// generar) no se revisan.
    /// </summary>
    private VistaPortico ConChoques(PorticoLeido p, PlanAcero? plan, VistaPortico v) =>
        p.Ubicacion is { } u && plan is not null
            ? v with { Choques = ArmadorChoques.Armar(plan, u, reglas.Reglas, dobladosDelModelo?.Invoke(reglas.Reglas)) }
            : v;
}
