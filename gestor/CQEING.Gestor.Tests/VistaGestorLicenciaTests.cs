using CQEING.Gestor.Core;
using CQEING.Gestor.Tests.Apoyo;

namespace CQEING.Gestor.Tests;

/// <summary>La franja de la licencia en la ventana del gestor (issue #195, plan 2026-10-28-licencias-hoja-google.md, Tarea 6).</summary>
public class VistaGestorLicenciaTests : IDisposable
{
    private const string Codigo = LicenciasFalso.Codigo;
    private static readonly DateTime Ahora = Entorno.AhoraUtc;

    private readonly Entorno _e = new("2026");

    public void Dispose() => _e.Dispose();

    private VistaGestor Vista => _e.Vista;

    private string ArchivoLicencia => Path.Combine(_e.CarpetaLicencia, "licencia.json");

    private static string Vence(DateTime emitidaUtc) => emitidaUtc.AddDays(3).ToLocalTime().ToString("dd-MM-yyyy");

    [Fact]
    public async Task Al_abrir_con_licencia_vigente_muestra_hasta_cuando_y_el_nombre()
    {
        DateTime emitida = Ahora.AddHours(-1);
        _e.GuardarLicencia(emitida, "Santiago Bula");

        await Vista.ConsultarLicenciaAsync();

        Assert.Equal($"Licencia vigente hasta el {Vence(emitida)} · Santiago Bula", Vista.TextoLicencia);
        Assert.Equal(Codigo, Vista.CodigoEquipo);
        Assert.False(Vista.PideNombre);
        Assert.Equal(0, _e.Licencias.Pedidos); // emitida hace menos de 12 h: no consulta (Decisión 4)
    }

    [Fact]
    public void Sin_licencia_pide_nombre_y_no_deja_solicitar_con_el_nombre_vacio()
    {
        Assert.Equal($"Este equipo no tiene licencia. Código del equipo: {Codigo}", Vista.TextoLicencia);
        Assert.True(Vista.PideNombre);
        Assert.Equal(string.Empty, Vista.NombreSolicitante);
        Assert.False(Vista.SolicitarAcceso.CanExecute(null));

        Vista.NombreSolicitante = "   ";
        Assert.False(Vista.SolicitarAcceso.CanExecute(null));

        Vista.NombreSolicitante = "Yina Londoño";
        Assert.True(Vista.SolicitarAcceso.CanExecute(null));
        Assert.True(Vista.CopiarCodigo.CanExecute(null));
    }

    [Fact]
    public async Task Solicitar_acceso_pendiente_muestra_el_texto_de_pendiente()
    {
        _e.Licencias.Pendiente();
        Vista.NombreSolicitante = "Yina Londoño";

        await Vista.SolicitarAcceso.EjecutarAsync();

        Assert.Equal($"Solicitud enviada. Falta que la aprueben. Código del equipo: {Codigo}", Vista.TextoLicencia);
        Assert.True(Vista.PideNombre);
        Assert.False(Vista.HayAviso); // pedir no es recibir: no se pide reiniciar Revit
        Assert.Equal(1, _e.Licencias.Pedidos);
        Assert.Equal("Yina Londoño", _e.Renovador.NombreGuardado);
        Assert.False(Vista.Ocupado);
    }

    [Fact]
    public async Task Solicitar_acceso_aprobado_muestra_vigente_y_pide_reiniciar_Revit()
    {
        _e.Licencias.Aprobado(Ahora, "Yina Londoño");
        Vista.NombreSolicitante = "Yina Londoño";

        await Vista.SolicitarAcceso.EjecutarAsync();

        Assert.Equal($"Licencia vigente hasta el {Vence(Ahora)} · Yina Londoño", Vista.TextoLicencia);
        Assert.False(Vista.PideNombre);
        Assert.Equal("Licencia recibida. Reinicie Revit para usar los add-ins.", Vista.Aviso);
        Assert.False(Vista.AvisoEsError);
        Assert.True(File.Exists(ArchivoLicencia));
    }

    [Fact]
    public async Task Con_licencia_previa_una_aprobada_no_pide_reiniciar_Revit()
    {
        _e.GuardarLicencia(Ahora.AddHours(-1), "Yina Londoño");
        await Vista.ConsultarLicenciaAsync();
        _e.Licencias.Aprobado(Ahora, "Yina Londoño");
        Vista.NombreSolicitante = "Yina Londoño";

        await Vista.SolicitarAcceso.EjecutarAsync();

        Assert.False(Vista.HayAviso);
    }

    [Fact]
    public async Task Sin_conexion_y_sin_licencia_muestra_el_texto_sin_conexion()
    {
        _e.Licencias.SinRed();

        await Vista.ConsultarLicenciaAsync();

        Assert.Equal($"No se pudo consultar la licencia (sin conexión). Código del equipo: {Codigo}", Vista.TextoLicencia);
        Assert.True(Vista.PideNombre);
    }

    [Fact]
    public async Task Sin_conexion_con_licencia_vigente_la_conserva()
    {
        DateTime emitida = Ahora.AddHours(-13);
        _e.GuardarLicencia(emitida, "Santiago Bula");
        _e.Licencias.SinRed();

        await Vista.ConsultarLicenciaAsync();

        Assert.Equal(1, _e.Licencias.Pedidos);
        Assert.Equal($"Licencia vigente hasta el {Vence(emitida)} · Santiago Bula", Vista.TextoLicencia);
        Assert.False(Vista.PideNombre);
    }

    [Fact]
    public async Task Revocada_muestra_el_texto_de_acceso_retirado()
    {
        _e.GuardarLicencia(Ahora.AddHours(-13), "Santiago Bula");
        _e.Licencias.Revocado();

        await Vista.ConsultarLicenciaAsync();

        Assert.Equal($"El acceso de este equipo fue retirado. Código del equipo: {Codigo}", Vista.TextoLicencia);
        Assert.True(Vista.PideNombre);
        Assert.False(File.Exists(ArchivoLicencia));
    }

    [Fact]
    public async Task Al_abrir_la_licencia_no_ocupa_la_ventana()
    {
        _e.GitHub.Publicar("armado-columnas", "Armado de columnas", "1.0.0");

        Task licencia = Vista.ConsultarLicenciaAsync();
        await Vista.BuscarNovedades.EjecutarAsync();
        await licencia;

        Assert.Single(Vista.Tarjetas);
        Assert.Equal(1, _e.GitHub.ConsultasDeReleases);
        Assert.Equal(1, _e.Licencias.Pedidos);
    }
}
