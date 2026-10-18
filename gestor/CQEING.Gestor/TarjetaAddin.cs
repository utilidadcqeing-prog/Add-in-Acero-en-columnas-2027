using CQEING.Gestor.Core;

namespace CQEING.Gestor;

/// <summary>La tarjeta de un add-in en el año de Revit elegido: lo que muestra, su progreso y sus botones.</summary>
public sealed class TarjetaAddin : Observable
{
    private static readonly string[] ColoresIcono = ["#0F6CBD", "#8764B8", "#107C41", "#C239B3", "#CA5010"];

    private readonly Func<TarjetaAddin, VersionPublicada, Task> _instalar;
    private readonly Func<bool> _libre;
    private EstadoAddin _addin;
    private double _progreso;
    private bool _descargando;
    private string? _mensaje;
    private bool _mensajeEsError;

    /// <param name="posicion">Lugar de la tarjeta en la lista: da a cada una un color de icono distinto.</param>
    /// <param name="instalar">Instala esa versión en el año de esta tarjeta (lo hace la ventana).</param>
    /// <param name="libre">No hay otra operación en curso: si la hay, los botones quedan deshabilitados.</param>
    public TarjetaAddin(EstadoAddin addin, string anio, bool sinConexion, int posicion, Func<TarjetaAddin, VersionPublicada, Task> instalar, Func<bool> libre)
    {
        _addin = addin;
        _instalar = instalar;
        _libre = libre;
        Anio = anio;
        Iniciales = CalcularIniciales(addin.Addin.Nombre);
        ColorIcono = ColoresIcono[posicion % ColoresIcono.Length];
        Estado = EstadoTarjeta.Calcular(addin, anio, sinConexion);
        Versiones = CrearVersiones(sinConexion);
        Principal = new ComandoAsync(() => _instalar(this, _addin.Addin.Ultima), () => _libre() && Estado.PuedeActuar);
    }

    public string Id => _addin.Addin.Id;
    public string Anio { get; }
    public string Nombre => _addin.Addin.Nombre;
    public string Descripcion => _addin.Addin.Descripcion;
    public string Iniciales { get; }
    public string ColorIcono { get; }

    public EstadoTarjeta Estado { get; private set; }
    public bool EsAlDia => Estado.Accion == AccionTarjeta.AlDia;
    public bool MostrarBoton => !EsAlDia;
    public bool HayNuevaVersion => Estado.NuevaVersion is not null;
    public bool HayNota => Estado.Nota is not null;
    public IReadOnlyList<OpcionVersion> Versiones { get; private set; }
    public ComandoAsync Principal { get; }

    public string NombreAccesibleBoton => $"{Estado.TextoBoton} {Nombre}";
    public string NombreAccesibleVersiones => $"Más versiones de {Nombre}";

    /// <summary>De 0 a 100.</summary>
    public double Progreso
    {
        get => _progreso;
        set
        {
            if (Cambiar(ref _progreso, value)) Avisar(nameof(TextoProgreso));
        }
    }

    public string TextoProgreso => $"{Progreso:0} %";

    public bool Descargando
    {
        get => _descargando;
        private set => Cambiar(ref _descargando, value);
    }

    /// <summary>Resultado de la última operación: «Listo…» o el error, en la misma tarjeta.</summary>
    public string? Mensaje
    {
        get => _mensaje;
        private set
        {
            if (Cambiar(ref _mensaje, value)) Avisar(nameof(HayMensaje));
        }
    }

    public bool HayMensaje => Mensaje is not null;

    public bool MensajeEsError
    {
        get => _mensajeEsError;
        private set => Cambiar(ref _mensajeEsError, value);
    }

    /// <summary>Después de instalar o de releer el disco: la misma tarjeta muestra el estado nuevo.</summary>
    public void Actualizar(EstadoAddin addin, bool sinConexion)
    {
        _addin = addin;
        Estado = EstadoTarjeta.Calcular(addin, Anio, sinConexion);
        Versiones = CrearVersiones(sinConexion);
        Avisar(string.Empty);
    }

    public void EmpezarDescarga()
    {
        Mensaje = null;
        Progreso = 0;
        Descargando = true;
    }

    public void Terminar(string mensaje, bool esError)
    {
        Descargando = false;
        MensajeEsError = esError;
        Mensaje = mensaje;
    }

    private List<OpcionVersion> CrearVersiones(bool sinConexion) =>
        _addin.Addin.Versiones.Select(version =>
        {
            OpcionVersion? opcion = null;
            opcion = new OpcionVersion(version, _addin, Anio, sinConexion,
                new ComandoAsync(() => _instalar(this, version), () => _libre() && opcion!.PuedeUsar));
            return opcion;
        }).ToList();

    /// <summary>«Armado de columnas» → «AC»: las dos primeras palabras largas (sin «de», «por»…).</summary>
    private static string CalcularIniciales(string nombre)
    {
        string[] palabras = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string iniciales = string.Concat(palabras.Where(p => p.Length > 3).Take(2).Select(p => char.ToUpperInvariant(p[0])));
        return iniciales.Length > 0 ? iniciales : nombre[..1].ToUpperInvariant();
    }
}
