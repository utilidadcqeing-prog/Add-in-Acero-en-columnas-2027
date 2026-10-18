using System.Collections.ObjectModel;
using System.IO;
using CQEING.Gestor.Core;

namespace CQEING.Gestor;

/// <param name="Novedades">Cuántos add-ins tienen actualización en ese año (el punto del selector).</param>
public sealed record OpcionAnio(string Anio, int Novedades)
{
    public string Etiqueta => $"Revit {Anio}";
    public bool TieneNovedades => Novedades > 0;
    public string NombreAccesible => TieneNovedades ? $"{Etiqueta}, con novedades" : Etiqueta;
}

/// <summary>
/// Modelo de la ventana. Consulta GitHub solo al abrir y con «Buscar novedades» (nunca en segundo plano: sin
/// token, cada consulta descuenta del cupo de 60 por hora). Después de instalar, relee solo el disco.
/// Todo lo pesado corre fuera del hilo de la ventana y, mientras hay una operación, los botones se deshabilitan.
/// </summary>
public sealed class VistaGestor : Observable
{
    private readonly GestorAddins _gestor;
    private readonly IActualizadorGestor _actualizador;
    private readonly Func<DateTime> _ahora;
    private ResumenGestor? _resumen;
    private OpcionAnio? _anioElegido;
    private int _operaciones;
    private bool _buscando;
    private string _textoRevision = "Buscando novedades…";
    private string? _aviso;
    private bool _avisoEsError;
    private string? _gestorNuevo;
    private bool _avisoGestorCerrado;
    private string? _progresoGestor;

    public VistaGestor(GestorAddins gestor, IActualizadorGestor actualizador, Func<DateTime>? ahora = null)
    {
        _gestor = gestor;
        _actualizador = actualizador;
        _ahora = ahora ?? (() => DateTime.Now);
        BuscarNovedades = new ComandoAsync(BuscarNovedadesAsync, () => !Ocupado);
        ActualizarTodas = new ComandoAsync(ActualizarTodasAsync, () => !Ocupado && PuedeActualizarTodas);
        ActualizarGestor = new ComandoAsync(ActualizarGestorAsync, () => !Ocupado && MostrarAvisoGestor);
        CerrarAvisoGestor = new ComandoAsync(() =>
        {
            _avisoGestorCerrado = true;
            Avisar(nameof(MostrarAvisoGestor));
            return Task.CompletedTask;
        });
    }

    public ComandoAsync BuscarNovedades { get; }
    public ComandoAsync ActualizarTodas { get; }
    public ComandoAsync ActualizarGestor { get; }
    public ComandoAsync CerrarAvisoGestor { get; }

    public ObservableCollection<TarjetaAddin> Tarjetas { get; } = [];

    public IReadOnlyList<OpcionAnio> Anios { get; private set; } = [];
    public bool HayVariosAnios => Anios.Count > 1;

    /// <summary>Con un solo año instalado no hace falta selector: se muestra como texto.</summary>
    public bool HayUnAnio => Anios.Count == 1;

    public OpcionAnio? AnioElegido
    {
        get => _anioElegido;
        set
        {
            if (value is null || !Cambiar(ref _anioElegido, value)) return;
            ConstruirTarjetas();
            AvisarResumen();
        }
    }

    public bool Ocupado => _operaciones > 0;

    /// <summary>Consultando GitHub (barra fina bajo el encabezado).</summary>
    public bool Buscando
    {
        get => _buscando;
        private set => Cambiar(ref _buscando, value);
    }

    public string TextoRevision
    {
        get => _textoRevision;
        private set => Cambiar(ref _textoRevision, value);
    }

    public int Actualizaciones => AnioElegido?.Novedades ?? 0;
    public bool PuedeActualizarTodas => Actualizaciones >= 2;

    /// <summary>«Todo al día» solo si hay algo instalado en ese año: en un equipo limpio no se dice nada.</summary>
    public string TextoResumen => _resumen is null || AnioElegido is null ? string.Empty
        : Actualizaciones switch
        {
            0 => _resumen.Addins.Any(a => a.Instalada(AnioElegido.Anio) is not null) ? "Todo al día" : string.Empty,
            1 => "1 actualización disponible",
            var n => $"{n} actualizaciones disponibles",
        };

    /// <summary>Texto cuando no hay tarjetas que mostrar.</summary>
    public string? TextoVacio => _resumen is null ? null
        : _resumen.AniosRevit.Count == 0 ? "No se encontró Revit instalado en este equipo."
        : _resumen.Addins.Count == 0 ? "Todavía no hay add-ins publicados."
        : null;

    public bool HayTextoVacio => TextoVacio is not null;

    /// <summary>Franja de aviso: gris para «lista guardada» y releases omitidas, roja para errores.</summary>
    public string? Aviso
    {
        get => _aviso;
        private set
        {
            if (Cambiar(ref _aviso, value)) Avisar(nameof(HayAviso));
        }
    }

    public bool HayAviso => Aviso is not null;

    public bool AvisoEsError
    {
        get => _avisoEsError;
        private set => Cambiar(ref _avisoEsError, value);
    }

    public bool MostrarAvisoGestor => _gestorNuevo is not null && !_avisoGestorCerrado;

    public string TextoAvisoGestor => _progresoGestor ?? $"Hay una versión nueva del gestor ({_gestorNuevo}).";

    private async Task BuscarNovedadesAsync()
    {
        await OcuparAsync(async () =>
        {
            Buscando = true;
            TextoRevision = "Buscando novedades…";
            try
            {
                ResumenGestor resumen = await Task.Run(() => _gestor.ConsultarAsync());
                Mostrar(resumen);
                TextoRevision = resumen.DesdeCache ? "Sin conexión: se muestra la lista guardada" : $"Revisado hoy a las {_ahora():HH:mm}";
                _gestorNuevo = await _actualizador.BuscarAsync(resumen.UltimoGestor);
                Avisar(nameof(MostrarAvisoGestor));
                Avisar(nameof(TextoAvisoGestor));
            }
            catch (GestorException e)
            {
                TextoRevision = "No se pudo revisar";
                MostrarAviso($"{e.Message} Intente de nuevo con «Buscar novedades».", esError: true);
            }
            finally
            {
                Buscando = false;
            }
        });
    }

    private Task InstalarAsync(TarjetaAddin tarjeta, VersionPublicada version) => OcuparAsync(async () =>
    {
        string anio = tarjeta.Anio;
        tarjeta.EmpezarDescarga();
        var progreso = new Progress<double>(p => tarjeta.Progreso = p * 100);
        try
        {
            // El instalador extrae el ZIP y calcula el SHA-256 de forma síncrona: fuera del hilo de la ventana.
            await Task.Run(() => _gestor.InstalarAsync(version, anio, CancellationToken.None, progreso));
            ReleerDisco();
            tarjeta.Terminar($"Listo. Reinicie Revit {anio} para usar la versión {version.Version}.", esError: false);
        }
        catch (GestorException e)
        {
            tarjeta.Terminar(e.Message, esError: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            tarjeta.Terminar($"No se pudo escribir en el disco: {e.Message}", esError: true);
        }
    });

    private Task ActualizarTodasAsync() => OcuparAsync(async () =>
    {
        foreach (TarjetaAddin tarjeta in Tarjetas.Where(t => t.Estado is { Accion: AccionTarjeta.Actualizar, PuedeActuar: true }).ToList())
            await InstalarAsync(tarjeta, _resumen!.Addins.Single(a => a.Addin.Id == tarjeta.Id).Addin.Ultima);
    });

    private Task ActualizarGestorAsync() => OcuparAsync(async () =>
    {
        try
        {
            await _actualizador.AplicarAsync(new Progress<int>(p =>
            {
                _progresoGestor = $"Descargando el gestor nuevo… {p} %";
                Avisar(nameof(TextoAvisoGestor));
            }));
        }
        catch (Exception e)
        {
            // Lo que falle en Velopack (red, disco) se muestra; los add-ins siguen funcionando con este gestor.
            _progresoGestor = null;
            Avisar(nameof(TextoAvisoGestor));
            MostrarAviso($"No se pudo actualizar el gestor: {e.Message}", esError: true);
        }
    });

    /// <summary>Cuenta las operaciones en curso (Actualizar todas encadena varias) y refresca los botones.</summary>
    private async Task OcuparAsync(Func<Task> accion)
    {
        _operaciones++;
        Avisar(nameof(Ocupado));
        ComandoAsync.Refrescar();
        try
        {
            await accion();
        }
        finally
        {
            _operaciones--;
            Avisar(nameof(Ocupado));
            ComandoAsync.Refrescar();
        }
    }

    private void Mostrar(ResumenGestor resumen)
    {
        _resumen = resumen;
        string? anioPrevio = _anioElegido?.Anio;
        CalcularAnios();
        // Se respeta el año que el usuario eligió; la primera vez, el más nuevo con novedades (o el más nuevo).
        _anioElegido = Anios.FirstOrDefault(o => o.Anio == anioPrevio) ?? Anios.LastOrDefault(o => o.TieneNovedades) ?? Anios.LastOrDefault();
        ConstruirTarjetas();

        List<string> avisos = [.. resumen.Avisos];
        if (resumen.DesdeCache) avisos.Add("Solo puede volver a versiones que ya están en este equipo.");
        MostrarAviso(avisos.Count == 0 ? null : string.Join(" ", avisos), esError: false);
        AvisarResumen();
    }

    /// <summary>Después de instalar: mismo catálogo, estado nuevo del disco, sin consultar GitHub.</summary>
    private void ReleerDisco()
    {
        if (_resumen is null) return;
        _resumen = _gestor.ReleerEstado(_resumen);
        string? anio = _anioElegido?.Anio;
        CalcularAnios();
        _anioElegido = Anios.FirstOrDefault(o => o.Anio == anio);
        foreach (TarjetaAddin tarjeta in Tarjetas)
            tarjeta.Actualizar(_resumen.Addins.Single(a => a.Addin.Id == tarjeta.Id), _resumen.DesdeCache);
        AvisarResumen();
    }

    private void CalcularAnios() =>
        Anios = _resumen!.AniosRevit
            .Select(anio => new OpcionAnio(anio, _resumen.Addins.Count(a =>
                EstadoTarjeta.Calcular(a, anio, _resumen.DesdeCache).Accion == AccionTarjeta.Actualizar)))
            .ToList();

    private void ConstruirTarjetas()
    {
        Tarjetas.Clear();
        if (_resumen is null || _anioElegido is null) return;
        foreach (EstadoAddin addin in _resumen.Addins)
            Tarjetas.Add(new TarjetaAddin(addin, _anioElegido.Anio, _resumen.DesdeCache, Tarjetas.Count, InstalarAsync, () => !Ocupado));
    }

    private void MostrarAviso(string? texto, bool esError)
    {
        AvisoEsError = esError;
        Aviso = texto;
    }

    private void AvisarResumen()
    {
        Avisar(nameof(Anios));
        Avisar(nameof(HayVariosAnios));
        Avisar(nameof(HayUnAnio));
        Avisar(nameof(AnioElegido));
        Avisar(nameof(Actualizaciones));
        Avisar(nameof(PuedeActualizarTodas));
        Avisar(nameof(TextoResumen));
        Avisar(nameof(TextoVacio));
        Avisar(nameof(HayTextoVacio));
    }
}
