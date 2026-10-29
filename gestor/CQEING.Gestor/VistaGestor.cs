using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
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
    private readonly ReinicioRevit _reinicio;
    private readonly RenovadorLicencia _renovador;
    private readonly Func<DateTime> _ahora;
    private InfoLicencia _licencia;
    private string _nombreSolicitante;
    private bool _consultandoLicencia;
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

    /// <param name="reinicio">Cierra y reabre Revit después de instalar, para que cargue la versión nueva.</param>
    /// <param name="renovador">Lee, pide y renueva la licencia de este equipo (issue #195).</param>
    public VistaGestor(GestorAddins gestor, IActualizadorGestor actualizador, ReinicioRevit reinicio, RenovadorLicencia renovador,
        Func<DateTime>? ahora = null)
    {
        _gestor = gestor;
        _actualizador = actualizador;
        _reinicio = reinicio;
        _renovador = renovador;
        _ahora = ahora ?? (() => DateTime.Now);
        _licencia = renovador.Estado();
        _nombreSolicitante = renovador.NombreGuardado ?? string.Empty;
        SolicitarAcceso = new ComandoAsync(SolicitarAccesoAsync,
            () => !Ocupado && !_consultandoLicencia && !string.IsNullOrWhiteSpace(NombreSolicitante));
        CopiarCodigo = new ComandoAsync(CopiarCodigoAsync, () => CodigoEquipo is not null);
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
    public ComandoAsync SolicitarAcceso { get; }
    public ComandoAsync CopiarCodigo { get; }

    /// <summary>Franja de la licencia, encima de las tarjetas. Plan docs/planes/2026-10-28-licencias-hoja-google.md, Tarea 6.</summary>
    public string TextoLicencia => _licencia switch
    {
        { Vigente: true } => $"Licencia vigente hasta el {_licencia.VenceUtc?.ToLocalTime():dd-MM-yyyy} · {_licencia.Nombre}",
        { UltimaRespuesta: EstadoSolicitud.Pendiente } => $"Solicitud enviada. Falta que la aprueben. Código del equipo: {CodigoEquipo}",
        { UltimaRespuesta: EstadoSolicitud.Revocada } => $"El acceso de este equipo fue retirado. Código del equipo: {CodigoEquipo}",
        { UltimaRespuesta: EstadoSolicitud.SinConexion } => $"No se pudo consultar la licencia (sin conexión). Código del equipo: {CodigoEquipo}",
        _ => $"Este equipo no tiene licencia. Código del equipo: {CodigoEquipo}",
    };

    public bool LicenciaVigente => _licencia.Vigente;

    public string? CodigoEquipo => _licencia.CodigoEquipo;

    /// <summary>Sin licencia vigente se muestran «Su nombre» y «Solicitar acceso».</summary>
    public bool PideNombre => !_licencia.Vigente;

    public string NombreSolicitante
    {
        get => _nombreSolicitante;
        set
        {
            if (Cambiar(ref _nombreSolicitante, value)) ComandoAsync.Refrescar();
        }
    }

    /// <summary>
    /// Al abrir la ventana: renueva la licencia si hace falta (núcleo, Decisión 4) sin frenar las tarjetas, porque no usa
    /// <see cref="OcuparAsync"/>. Mientras consulta no se puede «Solicitar acceso», para no mandar dos pedidos a la vez.
    /// </summary>
    public async Task ConsultarLicenciaAsync()
    {
        _consultandoLicencia = true;
        ComandoAsync.Refrescar();
        try
        {
            MostrarLicencia(await Task.Run(() => _renovador.RenovarAsync(null, forzar: false, VersionGestor)));
        }
        finally
        {
            _consultandoLicencia = false;
            ComandoAsync.Refrescar();
        }
    }

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

    /// <summary>Instala (o actualiza, o vuelve a) esa versión y, si ese Revit está abierto, lo reinicia para que la cargue.</summary>
    private Task InstalarAsync(TarjetaAddin tarjeta, VersionPublicada version) => OcuparAsync(async () =>
    {
        if (await InstalarSinReiniciarAsync(tarjeta, version))
            await ReiniciarRevitAsync(tarjeta.Anio);
    });

    private async Task<bool> InstalarSinReiniciarAsync(TarjetaAddin tarjeta, VersionPublicada version)
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
            return true;
        }
        catch (GestorException e)
        {
            tarjeta.Terminar(e.Message, esError: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            tarjeta.Terminar($"No se pudo escribir en el disco: {e.Message}", esError: true);
        }
        return false;
    }

    /// <summary>Instala todas y reinicia Revit una sola vez, al final.</summary>
    private Task ActualizarTodasAsync() => OcuparAsync(async () =>
    {
        bool alguna = false;
        foreach (TarjetaAddin tarjeta in Tarjetas.Where(t => t.Estado is { Accion: AccionTarjeta.Actualizar, PuedeActuar: true }).ToList())
            alguna |= await InstalarSinReiniciarAsync(tarjeta, _resumen!.Addins.Single(a => a.Addin.Id == tarjeta.Id).Addin.Ultima);
        if (alguna && AnioElegido is not null)
            await ReiniciarRevitAsync(AnioElegido.Anio);
    });

    /// <summary>
    /// Si ese Revit está abierto, lo cierra como con la X (Revit pregunta si guardar) y lo vuelve a abrir con su modelo.
    /// Si el usuario cancela, queda abierto. Plan: docs/planes/2026-10-25-gestor-reinicia-revit.md.
    /// </summary>
    private async Task ReiniciarRevitAsync(string anio)
    {
        if (!_reinicio.HayAbierto(anio)) return; // cerrado: la versión nueva se carga cuando el usuario lo abra

        MostrarAviso($"Cerrando Revit {anio} para cargar la versión nueva. Si Revit pregunta, elija si guarda los cambios.", esError: false);
        ResultadoCierre cierre = await _reinicio.CerrarAsync([anio]);
        if (cierre.SiguenAbiertos.Count > 0)
        {
            MostrarAviso($"Revit {anio} sigue abierto. Reinícielo cuando pueda para usar la versión nueva.", esError: false);
            return;
        }
        MostrarAviso(
            _reinicio.Reabrir(cierre.Cerrados)
                ? $"Revit {anio} se está abriendo con la versión nueva. Si Revit pregunta por el add-in, elija «Always Load» (cargar siempre)."
                : $"Revit {anio} se cerró, pero no se pudo volver a abrir. Ábralo usted.",
            esError: false);
    }

    private Task ActualizarGestorAsync() => OcuparAsync(async () =>
    {
        try
        {
            await _actualizador.AplicarAsync(
                new Progress<int>(p =>
                {
                    _progresoGestor = $"Descargando el gestor nuevo… {p} %";
                    Avisar(nameof(TextoAvisoGestor));
                }),
                CerrarRevitParaActualizarGestorAsync);
        }
        catch (Exception e)
        {
            // Lo que falle en Velopack (red, disco) se muestra; los add-ins siguen funcionando con este gestor. Los Revit
            // que se cerraron para actualizar se vuelven a abrir ahora.
            _reinicio.Reabrir(_reinicio.TomarPendientes());
            _progresoGestor = null;
            Avisar(nameof(TextoAvisoGestor));
            MostrarAviso($"No se pudo actualizar el gestor: {e.Message}", esError: true);
        }
    });

    /// <summary>
    /// El gestor nuevo trae otro componente para Revit: se cierran los Revit abiertos y se anotan para que el gestor nuevo
    /// los reabra al arrancar (Program.Main), después de registrar su componente.
    /// </summary>
    private async Task CerrarRevitParaActualizarGestorAsync()
    {
        _progresoGestor = "Cerrando Revit para actualizar. Si Revit pregunta, elija si guarda los cambios.";
        Avisar(nameof(TextoAvisoGestor));
        ResultadoCierre cierre = await _reinicio.CerrarAsync(null);
        _reinicio.GuardarPendientes(cierre.Cerrados);
    }

    private static string VersionGestor => ComplementoEnRevit.VersionDelGestor(typeof(VistaGestor).Assembly);

    /// <summary>Pide acceso con el nombre escrito. Si llega la primera licencia vigente, Revit tiene que reiniciarse para cargar los add-ins.</summary>
    private Task SolicitarAccesoAsync() => OcuparAsync(async () =>
    {
        bool teniaLicencia = _licencia.Vigente;
        InfoLicencia info = await Task.Run(() => _renovador.RenovarAsync(NombreSolicitante, forzar: true, VersionGestor));
        MostrarLicencia(info);
        if (!teniaLicencia && info is { Vigente: true, UltimaRespuesta: EstadoSolicitud.Aprobada })
            MostrarAviso("Licencia recibida. Reinicie Revit para usar los add-ins.", esError: false);
    });

    private Task CopiarCodigoAsync()
    {
        try
        {
            Clipboard.SetText(CodigoEquipo!);
        }
        catch (ExternalException)
        {
            // Otro programa tiene el portapapeles tomado: el código igual se puede leer en la franja.
            MostrarAviso("No se pudo copiar el código. Intente de nuevo.", esError: true);
        }
        return Task.CompletedTask;
    }

    private void MostrarLicencia(InfoLicencia info)
    {
        _licencia = info;
        Avisar(nameof(TextoLicencia));
        Avisar(nameof(LicenciaVigente));
        Avisar(nameof(CodigoEquipo));
        Avisar(nameof(PideNombre));
        ComandoAsync.Refrescar();
    }

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
