using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;

namespace CQEING.Vigas.Calculo.Tablero;

/// <summary>Lo que sale de atender un mensaje: algo para la página o algo que hacer en Revit.</summary>
public abstract record Salida;

/// <summary>Un mensaje JSON para la página (<see cref="ProtocoloTablero.Vista"/> o <see cref="ProtocoloTablero.Aviso"/>).</summary>
public sealed record AJs(string Json) : Salida;

/// <summary>Una acción en Revit sobre las vigas de un pórtico (ids de elemento como texto).</summary>
public sealed record ARevit(AccionRevit Accion, IReadOnlyList<string> Vigas) : Salida;

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
/// </summary>
public sealed class SesionTablero(Func<ResultadoLectura> leer, AlmacenReglas almacen, string proyecto)
{
    private readonly Dictionary<string, OpcionesTablero> opciones = [];
    private ResultadoLectura lectura = new([], []);
    private ReglasCargadas reglas = new(Reglas.Generales, null);
    private string? actual;
    private bool enVivo;
    private string proyecto = proyecto;

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
                return enVivo ? [Vista(), new ARevit(AccionRevit.Seleccionar, elegido.Vigas)] : [Vista()];
            case Calcular(string id, OpcionesTablero o):
                if (Buscar(id) is null)
                    return [Vista()];
                (opciones[id], actual) = (o, id);
                return [Vista()];
            case SeleccionarEnRevit:
                return [new ARevit(AccionRevit.PedirSeleccion, [])];
            case VerEnRevit(string id):
                return Buscar(id) is { } ver ? [new ARevit(AccionRevit.Mostrar, ver.Vigas)] : [];
            case Abrir3d(string id):
                return Buscar(id) is { } en3d ? [new ARevit(AccionRevit.Abrir3d, en3d.Vigas)] : [];
            case CambiarEnVivo(bool activo):
                enVivo = activo;
                return [Vista()];
            case GuardarReglas(VistaReglas pantalla):
                return Guardar(pantalla);
            default:
                return [];
        }
    }

    /// <summary>
    /// Vuelve a leer el modelo (otro documento, o el mismo con cambios). Las opciones de cada pórtico se conservan
    /// mientras su nombre exista; si el abierto ya no está, se abre el primero (o ninguno).
    /// </summary>
    public void Recargar(string? proyectoNuevo = null)
    {
        proyecto = proyectoNuevo ?? proyecto;
        lectura = leer();
        foreach (string nombre in opciones.Keys.Where(n => Buscar(n) is null).ToList())
            opciones.Remove(nombre);
        if (actual is null || Buscar(actual) is null)
            actual = lectura.Porticos.FirstOrDefault()?.Nombre;
    }

    /// <summary>El pórtico al que pertenece la viga <paramref name="idViga"/>, para "En vivo"; null si ninguno.</summary>
    public string? PorticoDeViga(string idViga) => lectura.Porticos.FirstOrDefault(p => p.Vigas.Contains(idViga))?.Nombre;

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

    private PorticoLeido? Buscar(string id) => lectura.Porticos.FirstOrDefault(p => p.Nombre == id);

    private AJs Vista()
    {
        PorticoLeido? abierto = actual is null ? null : Buscar(actual);
        VistaPortico? vista = abierto is null
            ? null
            : ArmadorVista.Armar(abierto, opciones.GetValueOrDefault(abierto.Nombre) ?? OpcionesTablero.PorDefecto, reglas.Reglas);
        return new AJs(ProtocoloTablero.Vista(new VistaTablero(proyecto, lectura.Porticos.Select(ArmadorVista.EnLista).ToList(), vista,
            ArmadorVista.Reglas(reglas), enVivo)));
    }
}
