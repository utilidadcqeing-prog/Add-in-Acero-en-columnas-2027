using CQEING.Gestor.Tests.Apoyo;

namespace CQEING.Gestor.Tests;

public class VistaGestorTests : IDisposable
{
    private readonly Entorno _e = new("2026");

    public void Dispose() => _e.Dispose();

    private VistaGestor Vista => _e.Vista;

    private Task Buscar() => Vista.BuscarNovedades.EjecutarAsync();

    private static Task Usar(TarjetaAddin tarjeta, string version) =>
        ((ComandoAsync)tarjeta.Versiones.Single(v => v.Version == version).Usar).EjecutarAsync();

    [Fact]
    public async Task Al_abrir_muestra_una_tarjeta_por_addin_y_cuando_se_reviso()
    {
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.0.0");
        _e.GitHub.Publicar("imagenes", "Imágenes por actividad", "1.0.0");

        await Buscar();

        Assert.Equal(new[] { "Armado de columnas", "Imágenes por actividad" }, Vista.Tarjetas.Select(t => t.Nombre));
        Assert.Equal(new[] { "AC", "IA" }, Vista.Tarjetas.Select(t => t.Iniciales));
        Assert.NotEqual(Vista.Tarjetas[0].ColorIcono, Vista.Tarjetas[1].ColorIcono);
        Assert.All(Vista.Tarjetas, t => Assert.Equal(AccionTarjeta.Instalar, t.Estado.Accion));
        Assert.Equal("Revisado hoy a las 09:05", Vista.TextoRevision);
        Assert.True(Vista.HayUnAnio);
        Assert.False(Vista.HayVariosAnios);
        Assert.Equal(string.Empty, Vista.TextoResumen); // equipo limpio: no dice «Todo al día»
        Assert.False(Vista.HayAviso);
        Assert.False(Vista.Ocupado);
    }

    [Fact]
    public async Task Instalar_muestra_Listo_en_la_tarjeta_y_no_vuelve_a_consultar_GitHub()
    {
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.0.0");
        await Buscar();
        TarjetaAddin tarjeta = _e.Tarjeta("armado-columnas");

        await tarjeta.Principal.EjecutarAsync();

        Assert.Equal("Listo. Reinicie Revit 2026 para usar la versión 1.0.0.", tarjeta.Mensaje);
        Assert.False(tarjeta.MensajeEsError);
        Assert.False(tarjeta.Descargando);
        // Sin la ventana, Progress<T> avisa desde otro hilo y el último aviso puede llegar después: no se compara el %.
        Assert.Equal(AccionTarjeta.AlDia, tarjeta.Estado.Accion);
        Assert.Equal("Instalada 1.0.0", tarjeta.Estado.TextoInstalada);
        Assert.Equal("Todo al día", Vista.TextoResumen);
        Assert.Equal(1, _e.GitHub.ConsultasDeReleases);
        Assert.True(File.Exists(_e.Rutas.ArchivoAddin("armado-columnas", "2026")));
    }

    [Fact]
    public async Task Actualizar_y_volver_a_la_version_anterior_desde_el_menu()
    {
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.0.0");
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.1.0", "Corrige ganchos.");
        await Buscar();
        await Usar(_e.Tarjeta("armado-columnas"), "1.0.0");
        TarjetaAddin tarjeta = _e.Tarjeta("armado-columnas");

        Assert.Equal(AccionTarjeta.Actualizar, tarjeta.Estado.Accion);
        Assert.Equal("Nueva versión 1.1.0", tarjeta.Estado.NuevaVersion);
        Assert.Equal("1 actualización disponible", Vista.TextoResumen);
        Assert.True(Vista.AnioElegido!.TieneNovedades);

        await tarjeta.Principal.EjecutarAsync();
        Assert.Equal("Instalada 1.1.0", tarjeta.Estado.TextoInstalada);
        Assert.Equal("Volver a esta", tarjeta.Versiones.Single(v => v.Version == "1.0.0").TextoAccion);

        await Usar(tarjeta, "1.0.0");
        Assert.Equal("Instalada 1.0.0", tarjeta.Estado.TextoInstalada);
        Assert.Equal("Listo. Reinicie Revit 2026 para usar la versión 1.0.0.", tarjeta.Mensaje);
        Assert.Equal(1, _e.GitHub.ConsultasDeReleases);
    }

    [Fact]
    public async Task Mientras_instala_los_demas_botones_quedan_deshabilitados()
    {
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.0.0");
        _e.GitHub.Publicar("imagenes", "Imágenes por actividad", "1.0.0");
        await Buscar();
        _e.GitHub.Freno = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task instalando = _e.Tarjeta("armado-columnas").Principal.EjecutarAsync();

        Assert.True(Vista.Ocupado);
        Assert.True(_e.Tarjeta("armado-columnas").Descargando);
        Assert.False(Vista.BuscarNovedades.CanExecute(null));
        Assert.False(_e.Tarjeta("armado-columnas").Principal.CanExecute(null));
        Assert.False(_e.Tarjeta("imagenes").Principal.CanExecute(null));
        Assert.False(_e.Tarjeta("imagenes").Versiones[0].Usar.CanExecute(null));

        _e.GitHub.Freno.SetResult();
        await instalando;

        Assert.False(Vista.Ocupado);
        Assert.True(Vista.BuscarNovedades.CanExecute(null));
        Assert.True(_e.Tarjeta("imagenes").Principal.CanExecute(null));
    }

    [Fact]
    public async Task Sin_conexion_y_sin_lista_guardada_muestra_el_error_en_rojo()
    {
        _e.GitHub.SinRed = true;

        await Buscar();

        Assert.True(Vista.AvisoEsError);
        Assert.StartsWith("No hay conexión con GitHub.", Vista.Aviso);
        Assert.Contains("Buscar novedades", Vista.Aviso);
        Assert.Equal("No se pudo revisar", Vista.TextoRevision);
        Assert.Empty(Vista.Tarjetas);
        Assert.True(Vista.BuscarNovedades.CanExecute(null)); // se puede reintentar
    }

    [Fact]
    public async Task Sin_conexion_con_lista_guardada_avisa_en_gris_y_no_ofrece_descargar()
    {
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.0.0");
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.1.0");
        await Buscar();
        await Usar(_e.Tarjeta("armado-columnas"), "1.0.0");

        _e.GitHub.SinRed = true;
        await Buscar();
        TarjetaAddin tarjeta = _e.Tarjeta("armado-columnas");

        Assert.False(Vista.AvisoEsError);
        Assert.Contains("lista guardada", Vista.Aviso);
        Assert.Contains("Solo puede volver a versiones que ya están en este equipo.", Vista.Aviso);
        Assert.Equal("Sin conexión: se muestra la lista guardada", Vista.TextoRevision);
        Assert.Equal(AccionTarjeta.Actualizar, tarjeta.Estado.Accion);
        Assert.False(tarjeta.Principal.CanExecute(null));
    }

    [Fact]
    public async Task Un_error_al_instalar_se_ve_en_la_tarjeta_y_no_cambia_nada()
    {
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.0.0");
        await Buscar();
        _e.GitHub.SinRed = true;
        TarjetaAddin tarjeta = _e.Tarjeta("armado-columnas");

        await tarjeta.Principal.EjecutarAsync();

        Assert.True(tarjeta.MensajeEsError);
        Assert.StartsWith("No se pudo descargar el paquete.", tarjeta.Mensaje);
        Assert.False(tarjeta.Descargando);
        Assert.Equal("No instalada", tarjeta.Estado.TextoInstalada);
        Assert.False(Vista.Ocupado);
    }

    [Fact]
    public async Task Actualizar_todas_aparece_con_dos_o_mas_y_las_deja_al_dia()
    {
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.0.0");
        _e.GitHub.Publicar("imagenes", "Imágenes por actividad", "1.0.0");
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.1.0");
        _e.GitHub.Publicar("imagenes", "Imágenes por actividad", "1.1.0");
        await Buscar();
        await Usar(_e.Tarjeta("armado-columnas"), "1.0.0");
        Assert.False(Vista.PuedeActualizarTodas); // una sola actualización: no hace falta
        await Usar(_e.Tarjeta("imagenes"), "1.0.0");

        Assert.True(Vista.PuedeActualizarTodas);
        Assert.Equal("2 actualizaciones disponibles", Vista.TextoResumen);
        await Vista.ActualizarTodas.EjecutarAsync();

        Assert.All(Vista.Tarjetas, t => Assert.Equal("Instalada 1.1.0", t.Estado.TextoInstalada));
        Assert.Equal("Todo al día", Vista.TextoResumen);
        Assert.False(Vista.PuedeActualizarTodas);
    }

    [Fact]
    public async Task Aviso_del_gestor_nuevo_se_aplica_o_se_cierra()
    {
        _e.GitHub.PublicarGestor("0.2.0");
        _e.Actualizador.Nueva = "0.2.0";

        await Buscar();

        Assert.Equal("gestor-v0.2.0", _e.Actualizador.Recibido?.ToString());
        Assert.True(Vista.MostrarAvisoGestor);
        Assert.Equal("Hay una versión nueva del gestor (0.2.0).", Vista.TextoAvisoGestor);
        await Vista.ActualizarGestor.EjecutarAsync();
        Assert.True(_e.Actualizador.Aplicado);

        await Vista.CerrarAvisoGestor.EjecutarAsync();
        Assert.False(Vista.MostrarAvisoGestor);
    }

    [Fact]
    public async Task Sin_addins_publicados_lo_dice()
    {
        await Buscar();

        Assert.Equal("Todavía no hay add-ins publicados.", Vista.TextoVacio);
    }
}

public class VistaGestorVariosAniosTests : IDisposable
{
    private readonly Entorno _e = new("2026", "2027");

    public void Dispose() => _e.Dispose();

    [Fact]
    public async Task Elige_el_anio_con_novedades_y_al_cambiarlo_rehace_las_tarjetas()
    {
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.0.0", anios: ["2026", "2027"]);
        await _e.Vista.BuscarNovedades.EjecutarAsync();
        Assert.Equal(new[] { "2026", "2027" }, _e.Vista.Anios.Select(a => a.Anio));
        Assert.Equal("2027", _e.Vista.AnioElegido!.Anio); // sin novedades: el más nuevo

        _e.Vista.AnioElegido = _e.Vista.Anios.Single(a => a.Anio == "2026");
        await ((ComandoAsync)_e.Tarjeta("armado-columnas").Versiones.Single().Usar).EjecutarAsync();
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.1.0", anios: ["2026", "2027"]);
        _e.Vista.AnioElegido = _e.Vista.Anios.Single(a => a.Anio == "2027");
        await _e.Vista.BuscarNovedades.EjecutarAsync();

        Assert.True(_e.Vista.HayVariosAnios);
        Assert.Equal("2027", _e.Vista.AnioElegido!.Anio); // se respeta lo que eligió el usuario
        Assert.True(_e.Vista.Anios.Single(a => a.Anio == "2026").TieneNovedades);
        Assert.False(_e.Vista.Anios.Single(a => a.Anio == "2027").TieneNovedades);
        Assert.Equal(AccionTarjeta.Instalar, _e.Tarjeta("armado-columnas").Estado.Accion);

        _e.Vista.AnioElegido = _e.Vista.Anios.Single(a => a.Anio == "2026");
        Assert.Equal("2026", _e.Tarjeta("armado-columnas").Anio);
        Assert.Equal(AccionTarjeta.Actualizar, _e.Tarjeta("armado-columnas").Estado.Accion);
    }

    [Fact]
    public async Task La_primera_vez_elige_el_anio_que_tiene_actualizaciones()
    {
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.0.0", anios: ["2026", "2027"]);
        await _e.Vista.BuscarNovedades.EjecutarAsync();
        _e.Vista.AnioElegido = _e.Vista.Anios.Single(a => a.Anio == "2026");
        await ((ComandoAsync)_e.Tarjeta("armado-columnas").Versiones.Single().Usar).EjecutarAsync();
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.1.0", anios: ["2026", "2027"]);

        var otraVentana = new VistaGestor(_e.Gestor, _e.Actualizador);
        await otraVentana.BuscarNovedades.EjecutarAsync();

        Assert.Equal("2026", otraVentana.AnioElegido!.Anio);
    }
}
