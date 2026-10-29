using CQEING.Compartido;
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class ActualizacionObligatoriaTests : IDisposable
{
    private readonly CarpetaTemporal _tmp = new();
    private readonly string _revit;

    /// <summary>Clave propia de cada prueba: nunca la del usuario real, que bloquearía sus add-ins.</summary>
    private readonly string _registro = $@"Software\CQEING-Pruebas-{Guid.NewGuid():N}";

    public ActualizacionObligatoriaTests() => _revit = _tmp.Combinar("roaming", "Autodesk", "Revit", "Addins", "2026");

    public void Dispose()
    {
        _tmp.Dispose();
        if (OperatingSystem.IsWindows())
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_registro, throwOnMissingSubKey: false);
    }

    [Fact]
    public void El_registro_guarda_la_version_mas_alta_y_borrar_el_archivo_no_desbloquea()
    {
        string carpeta = _tmp.Combinar("CQEING");
        var vigas = new InstalacionAddin("acero-vigas", "1.1.0", "2026");
        ActualizacionObligatoria.Escribir(carpeta, "2026", new Dictionary<string, string> { ["acero-vigas"] = "1.2.0" }, _registro);

        File.Delete(Path.Combine(carpeta, ActualizacionObligatoria.NombreArchivo("2026")));

        Assert.Contains("(1.2.0)", ActualizacionObligatoria.Pendiente(vigas, carpeta, _revit, _registro));
    }

    [Fact]
    public void En_el_registro_la_version_nunca_baja()
    {
        string carpeta = _tmp.Combinar("CQEING");
        ActualizacionObligatoria.Escribir(carpeta, "2026", new Dictionary<string, string> { ["acero-vigas"] = "1.2.0", ["gestor"] = "0.3.1" }, _registro);
        ActualizacionObligatoria.Escribir(carpeta, "2026", new Dictionary<string, string> { ["acero-vigas"] = "1.1.0" }, _registro);

        IReadOnlyDictionary<string, string> ultimas = ActualizacionObligatoria.Leer(carpeta, "2026", _registro);

        Assert.Equal("1.2.0", ultimas["acero-vigas"]);
        Assert.Equal("0.3.1", ultimas["gestor"]); // volver a un gestor viejo no lo borra
        Assert.Equal("1.1.0", ActualizacionObligatoria.Leer(carpeta, "2026")["acero-vigas"]); // sin registro, solo el archivo
    }

    private static string Dll(string id, string version, string anio) =>
        Path.Combine(@"C:\Users\u\AppData\Local\CQEING\Addins", id, version, anio, "addin.dll");

    [Fact]
    public void Reconoce_la_DLL_que_instalo_el_gestor()
    {
        Assert.Equal(new InstalacionAddin("acero-vigas", "1.0.1", "2026"), ActualizacionObligatoria.Instalacion(Dll("acero-vigas", "1.0.1", "2026")));
    }

    [Theory]
    [InlineData(@"C:\repo\addins\acero-vigas\bin\Debug\2026\CQEING.Vigas.dll")] // compilación de desarrollo
    [InlineData(@"C:\Users\u\AppData\Local\CQEING\Addins\acero-vigas\ultima\2026\addin.dll")] // versión sin formato
    [InlineData(@"C:\Users\u\AppData\Local\Otra\Addins\acero-vigas\1.0.0\2026\addin.dll")] // fuera de CQEING
    [InlineData(@"addin.dll")]
    public void Lo_que_no_instalo_el_gestor_nunca_se_bloquea(string dll)
    {
        Assert.Null(ActualizacionObligatoria.Instalacion(dll));
    }

    [Theory]
    [InlineData("1.0.0", "1.1.0", "1.1.0")]
    [InlineData("1.0.0", "1.0.0", null)]
    [InlineData("1.2.0", "1.1.0", null)] // instalada más nueva que la publicada: por ejemplo, una de prueba
    [InlineData("1.1.0-rc.1", "1.1.0", "1.1.0")] // la prerelease va antes que su estable
    [InlineData("1.1.0-rc.2", "1.0.9", null)]
    [InlineData("1.0.0", "basura", null)]
    public void Pendiente_solo_si_la_publicada_es_mas_nueva(string instalada, string publicada, string? esperada)
    {
        string carpeta = _tmp.Combinar("CQEING");
        ActualizacionObligatoria.Escribir(carpeta, "2026", new Dictionary<string, string> { ["acero-vigas"] = publicada });

        string? motivo = ActualizacionObligatoria.Pendiente(new InstalacionAddin("acero-vigas", instalada, "2026"), carpeta, _revit, null);

        if (esperada is null) Assert.Null(motivo);
        else Assert.Contains($"versión nueva de este add-in ({esperada})", motivo);
    }

    [Fact]
    public void Sin_saber_la_ultima_version_deja_usarlo()
    {
        string carpeta = _tmp.Combinar("CQEING");
        var vigas = new InstalacionAddin("acero-vigas", "1.0.0", "2026");
        Assert.Null(ActualizacionObligatoria.Pendiente(vigas, carpeta, _revit, null)); // sin archivo

        ActualizacionObligatoria.Escribir(carpeta, "2026", new Dictionary<string, string> { ["armado-columnas"] = "9.0.0" });
        Assert.Null(ActualizacionObligatoria.Pendiente(vigas, carpeta, _revit, null)); // no aparece en la lista

        File.WriteAllText(Path.Combine(carpeta, ActualizacionObligatoria.NombreArchivo("2026")), "{ dañado");
        Assert.Null(ActualizacionObligatoria.Pendiente(vigas, carpeta, _revit, null)); // archivo dañado
    }

    [Fact]
    public void Cada_anio_de_Revit_tiene_su_lista()
    {
        string carpeta = _tmp.Combinar("CQEING");
        ActualizacionObligatoria.Escribir(carpeta, "2027", new Dictionary<string, string> { ["acero-vigas"] = "2.0.0" });

        Assert.Null(ActualizacionObligatoria.Pendiente(new InstalacionAddin("acero-vigas", "1.0.0", "2026"), carpeta, _revit, null));
        Assert.Contains("(2.0.0)", ActualizacionObligatoria.Pendiente(new InstalacionAddin("acero-vigas", "1.0.0", "2027"), carpeta, _revit, null));
    }

    /// <summary>Escribe el .addin del componente del gestor apuntando a <paramref name="dll"/>.</summary>
    private void ComplementoGestor(string dll)
    {
        Directory.CreateDirectory(_revit);
        File.WriteAllText(Path.Combine(_revit, ActualizacionObligatoria.ArchivoComplementoGestor),
            $"<RevitAddIns><AddIn Type=\"Application\"><Assembly>{dll}</Assembly></AddIn></RevitAddIns>");
    }

    private static string DllGestor(string version) =>
        Path.Combine(@"C:\Users\u\AppData\Local\CQEING\Gestor\revit", version, "2026", "CQEING.GestorRevit.dll");

    [Theory]
    [InlineData("0.2.2", true)]
    [InlineData("0.3.0", false)]
    [InlineData("0.4.0", false)]
    public void Con_el_gestor_desactualizado_no_se_usa_ningun_add_in(string gestorInstalado, bool bloquea)
    {
        string carpeta = _tmp.Combinar("CQEING");
        ActualizacionObligatoria.Escribir(carpeta, "2026", new Dictionary<string, string> { ["acero-vigas"] = "1.0.0", ["gestor"] = "0.3.0" });
        ComplementoGestor(DllGestor(gestorInstalado));

        string? motivo = ActualizacionObligatoria.Pendiente(new InstalacionAddin("acero-vigas", "1.0.0", "2026"), carpeta, _revit, null);

        if (bloquea) Assert.Contains("versión nueva del gestor de add-ins (0.3.0)", motivo);
        else Assert.Null(motivo);
    }

    [Fact]
    public void Sin_el_componente_del_gestor_no_esta_al_dia_y_uno_de_desarrollo_no_se_juzga()
    {
        Assert.False(ActualizacionObligatoria.GestorAlDia(_revit, "0.3.0")); // borrarlo no sirve para esquivar la regla

        ComplementoGestor(@"C:\repo\addins\gestor-revit\bin\Debug\2026\CQEING.GestorRevit.dll");
        Assert.True(ActualizacionObligatoria.GestorAlDia(_revit, "0.3.0"));

        File.WriteAllText(Path.Combine(_revit, ActualizacionObligatoria.ArchivoComplementoGestor), "<<< no es xml");
        Assert.True(ActualizacionObligatoria.GestorAlDia(_revit, "0.3.0"));
    }

    [Fact]
    public void Escribir_reemplaza_la_lista_anterior_sin_dejar_el_temporal()
    {
        string carpeta = _tmp.Combinar("CQEING");
        ActualizacionObligatoria.Escribir(carpeta, "2026", new Dictionary<string, string> { ["acero-vigas"] = "1.0.0" });
        ActualizacionObligatoria.Escribir(carpeta, "2026", new Dictionary<string, string> { ["acero-vigas"] = "1.1.0" });

        Assert.Equal("1.1.0", ActualizacionObligatoria.Leer(carpeta, "2026")["acero-vigas"]);
        Assert.Equal(new[] { ActualizacionObligatoria.NombreArchivo("2026") }, Directory.GetFiles(carpeta).Select(Path.GetFileName));
    }
}
