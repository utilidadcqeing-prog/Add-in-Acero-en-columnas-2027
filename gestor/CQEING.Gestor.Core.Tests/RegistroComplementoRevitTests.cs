using System.Xml.Linq;
using CQEING.Gestor.Core.Tests.Apoyo;

namespace CQEING.Gestor.Core.Tests;

public class RegistroComplementoRevitTests : IDisposable
{
    private const string Dll = "CQEING.GestorRevit.dll";

    private readonly CarpetaTemporal _tmp = new();
    private readonly RutasGestor _rutas;
    private readonly RegistroComplementoRevit _registro;

    public RegistroComplementoRevitTests()
    {
        _rutas = new RutasGestor(_tmp.Combinar("local"), _tmp.Combinar("roaming"));
        _registro = new RegistroComplementoRevit(_rutas);
    }

    public void Dispose() => _tmp.Dispose();

    /// <summary>La carpeta del gestor instalado: una subcarpeta por año de Revit con el componente compilado.</summary>
    private string Paquete(params string[] anios)
    {
        string paquete = _tmp.Combinar("gestor-instalado");
        if (Directory.Exists(paquete)) Directory.Delete(paquete, recursive: true); // cada versión del gestor trae el suyo
        foreach (string anio in anios)
        {
            string carpeta = Path.Combine(paquete, anio);
            Directory.CreateDirectory(Path.Combine(carpeta, "Resources"));
            File.WriteAllText(Path.Combine(carpeta, Dll), $"dll {anio}");
            File.WriteAllText(Path.Combine(carpeta, "Resources", "icono32.png"), "png");
            File.WriteAllText(Path.Combine(carpeta, RutasGestor.NombreArchivoComplemento), Datos.XmlAddin(Dll));
        }
        return paquete;
    }

    private string DllDelAddin(string anio) =>
        XDocument.Load(_rutas.ArchivoComplemento(anio)).Descendants("Assembly").Single().Value.Trim();

    [Fact]
    public void Registra_cada_anio_con_su_carpeta_y_un_addin_de_ruta_absoluta()
    {
        ResultadoRegistroComplemento r = _registro.Registrar(Paquete("2026", "2027"), "0.2.0", ["2026", "2027"]);

        Assert.Equal(new[] { "2026", "2027" }, r.Anios);
        Assert.Empty(r.Problemas);
        foreach (string anio in new[] { "2026", "2027" })
        {
            string carpeta = _rutas.CarpetaComplemento("0.2.0", anio);
            Assert.Equal(Path.Combine(carpeta, Dll), DllDelAddin(anio));
            Assert.Equal($"dll {anio}", File.ReadAllText(Path.Combine(carpeta, Dll)));
            Assert.True(File.Exists(Path.Combine(carpeta, "Resources", "icono32.png")));
        }
        Assert.EndsWith(Path.Combine("Addins", "2026", "CQEING.zz-gestor-revit.addin"), _rutas.ArchivoComplemento("2026"));
    }

    [Fact]
    public void Solo_registra_los_anios_de_Revit_instalados_que_el_paquete_trae()
    {
        _registro.Registrar(Paquete("2026", "2027"), "0.2.0", ["2026"]);
        Assert.True(File.Exists(_rutas.ArchivoComplemento("2026")));
        Assert.False(File.Exists(_rutas.ArchivoComplemento("2027")));

        ResultadoRegistroComplemento sinAnio = new RegistroComplementoRevit(_rutas).Registrar(Paquete("2026"), "0.2.1", ["2026", "2027"]);

        Assert.Equal(new[] { "2026" }, sinAnio.Anios);
        Assert.Empty(sinAnio.Problemas); // un año que el paquete no trae no es un error
        Assert.False(File.Exists(_rutas.ArchivoComplemento("2027")));
    }

    [Fact]
    public void Nunca_cuenta_como_un_addin_instalado_del_gestor()
    {
        _registro.Registrar(Paquete("2026"), "0.2.0", ["2026"]);

        Assert.Empty(LectorEstado.Leer(_rutas));
    }

    [Fact]
    public void Una_version_nueva_va_a_otra_carpeta_y_el_addin_apunta_a_ella()
    {
        _registro.Registrar(Paquete("2026"), "0.2.0", ["2026"]);
        _registro.Registrar(Paquete("2026"), "0.2.1", ["2026"]);

        Assert.Equal(Path.Combine(_rutas.CarpetaComplemento("0.2.1", "2026"), Dll), DllDelAddin("2026"));
        Assert.True(Directory.Exists(_rutas.CarpetaComplemento("0.2.0", "2026"))); // la anterior se conserva
    }

    [Fact]
    public void Conserva_la_version_actual_y_la_anterior_y_retira_las_demas()
    {
        foreach (string version in new[] { "0.2.0", "0.2.1", "0.2.2" })
            _registro.Registrar(Paquete("2026"), version, ["2026"]);

        Assert.False(Directory.Exists(_rutas.CarpetaComplemento("0.2.0", "2026")));
        Assert.True(Directory.Exists(_rutas.CarpetaComplemento("0.2.1", "2026")));
        Assert.True(Directory.Exists(_rutas.CarpetaComplemento("0.2.2", "2026")));
        Assert.Empty(Directory.GetDirectories(_rutas.CarpetaComplementoRevit, "*.borrar-*"));
    }

    [Fact]
    public void Una_version_que_Revit_tiene_abierta_no_rompe_nada_ni_se_borra_a_medias()
    {
        _registro.Registrar(Paquete("2026"), "0.2.0", ["2026"]);
        string dllEnUso = Path.Combine(_rutas.CarpetaComplemento("0.2.0", "2026"), Dll);
        using (new FileStream(dllEnUso, FileMode.Open, FileAccess.Read, FileShare.Read)) // Revit la tiene cargada
        {
            _registro.Registrar(Paquete("2026"), "0.2.1", ["2026"]);
            ResultadoRegistroComplemento r = _registro.Registrar(Paquete("2026"), "0.2.2", ["2026"]);

            Assert.Empty(r.Problemas);
            Assert.Equal("dll 2026", File.ReadAllText(dllEnUso)); // sigue entera
            Assert.Equal(Path.Combine(_rutas.CarpetaComplemento("0.2.2", "2026"), Dll), DllDelAddin("2026"));
        }

        _registro.Registrar(Paquete("2026"), "0.2.2", ["2026"]); // al abrir el gestor otra vez, ya se puede retirar
        Assert.False(Directory.Exists(_rutas.CarpetaComplemento("0.2.0", "2026")));
    }

    [Fact]
    public void Registrar_otra_vez_repara_un_addin_borrado_sin_volver_a_copiar()
    {
        _registro.Registrar(Paquete("2026"), "0.2.0", ["2026"]);
        string marca = Path.Combine(_rutas.CarpetaComplemento("0.2.0", "2026"), "marca.txt");
        File.WriteAllText(marca, "no se debe recrear");
        File.Delete(_rutas.ArchivoComplemento("2026"));

        _registro.Registrar(Paquete("2026"), "0.2.0", ["2026"]);

        Assert.True(File.Exists(_rutas.ArchivoComplemento("2026")));
        Assert.True(File.Exists(marca));
    }

    [Fact]
    public void Un_paquete_danado_se_informa_y_los_otros_anios_siguen()
    {
        string paquete = Paquete("2026", "2027");
        File.WriteAllText(Path.Combine(paquete, "2027", RutasGestor.NombreArchivoComplemento), "<<< no es xml");

        ResultadoRegistroComplemento r = _registro.Registrar(paquete, "0.2.0", ["2026", "2027"]);

        Assert.Equal(new[] { "2026" }, r.Anios);
        Assert.Contains("Revit 2027", Assert.Single(r.Problemas));
        Assert.False(File.Exists(_rutas.ArchivoComplemento("2027")));
    }

    /// <summary>
    /// Revit carga los .addin en orden alfabético y cada add-in agrega su panel al final de la pestaña CQEING. Para que el
    /// panel «Gestor» quede a la derecha de todos, su archivo tiene que ordenar después del de cada add-in del repositorio.
    /// </summary>
    [Fact]
    public void El_archivo_del_gestor_ordena_despues_del_de_todos_los_addins_del_repositorio()
    {
        string? raiz = AppContext.BaseDirectory;
        while (raiz is not null && !File.Exists(Path.Combine(raiz, "CQEING-Addins.sln")))
            raiz = Path.GetDirectoryName(raiz);
        Assert.True(raiz is not null, "No se encontró la raíz del repositorio (CQEING-Addins.sln) desde la carpeta de las pruebas.");

        string[] archivos = Directory.GetFiles(Path.Combine(raiz!, "addins"), "addin.json", SearchOption.AllDirectories)
            .Select(json => System.Text.Json.JsonDocument.Parse(File.ReadAllText(json)).RootElement.GetProperty("id").GetString()!)
            .Select(id => $"CQEING.{id}.addin")
            .Where(nombre => nombre != RutasGestor.NombreArchivoComplemento)
            .ToArray();

        Assert.NotEmpty(archivos);
        Assert.All(archivos, nombre => Assert.True(
            string.Compare(nombre, RutasGestor.NombreArchivoComplemento, StringComparison.OrdinalIgnoreCase) < 0,
            $"{nombre} ordena después del archivo del gestor: su panel quedaría a la derecha del panel «Gestor»."));
    }

    [Fact]
    public void Quitar_borra_el_componente_y_deja_los_addins_de_los_usuarios()
    {
        _registro.Registrar(Paquete("2026", "2027"), "0.2.0", ["2026", "2027"]);
        string deUsuario = Path.Combine(_rutas.CarpetaAddinsRevit("2026"), "CQEING.armado-columnas.addin");
        File.WriteAllText(deUsuario, Datos.XmlAddin(@"C:\x\practicacq.dll"));

        _registro.Quitar();

        Assert.False(File.Exists(_rutas.ArchivoComplemento("2026")));
        Assert.False(File.Exists(_rutas.ArchivoComplemento("2027")));
        Assert.False(Directory.Exists(_rutas.CarpetaComplementoRevit));
        Assert.True(File.Exists(deUsuario));
    }

    [Fact]
    public void Quitar_sin_haber_registrado_nada_no_falla()
    {
        _registro.Quitar();

        Assert.False(Directory.Exists(_rutas.CarpetaComplementoRevit));
    }

    [Fact]
    public void Una_version_del_gestor_invalida_se_informa_y_no_lanza()
    {
        ResultadoRegistroComplemento r = _registro.Registrar(Paquete("2026"), "0.2.0+abc123", ["2026"]);

        Assert.Empty(r.Anios);
        Assert.Single(r.Problemas);
    }
}
