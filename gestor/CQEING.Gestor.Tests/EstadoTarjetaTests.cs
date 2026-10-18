using CQEING.Gestor.Core;
using System.Windows.Input;

namespace CQEING.Gestor.Tests;

public class EstadoTarjetaTests
{
    private static readonly ICommand SinAccion = new ComandoAsync(() => Task.CompletedTask);

    private static VersionPublicada V(string version, params string[] anios) => new(
        new Manifiesto(1, "armado-columnas", "Armado de columnas", "Estribos y columnetas.", version,
            new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), "Corrige ganchos.\nSegunda línea.", [],
            (anios.Length == 0 ? ["2026"] : anios).Select(a => new PaqueteManifiesto(a, "net8.0-windows", $"p-{a}.zip", new string('a', 64))).ToList()),
        VersionAddin.Interpretar(version)!,
        new Dictionary<string, Uri>());

    /// <param name="versiones">De la más nueva a la más vieja.</param>
    private static EstadoAddin Estado(VersionPublicada[] versiones, string? instalada = null, params string[] enDisco) => new(
        new EntradaCatalogo("armado-columnas", "Armado de columnas", "Estribos y columnetas.", versiones),
        instalada is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["2026"] = instalada },
        new Dictionary<string, IReadOnlyList<string>> { ["2026"] = enDisco });

    [Fact]
    public void Sin_instalar_ofrece_Instalar()
    {
        EstadoTarjeta t = EstadoTarjeta.Calcular(Estado([V("1.0.0")]), "2026", sinConexion: false);

        Assert.Equal(AccionTarjeta.Instalar, t.Accion);
        Assert.Equal("Instalar", t.TextoBoton);
        Assert.True(t.PuedeActuar);
        Assert.Equal("No instalada", t.TextoInstalada);
        Assert.Null(t.NuevaVersion);
    }

    [Fact]
    public void Con_una_version_nueva_ofrece_Actualizar_con_la_primera_linea_de_las_notas()
    {
        EstadoTarjeta t = EstadoTarjeta.Calcular(Estado([V("1.1.0"), V("1.0.0")], "1.0.0", "1.0.0"), "2026", sinConexion: false);

        Assert.Equal(AccionTarjeta.Actualizar, t.Accion);
        Assert.True(t.PuedeActuar);
        Assert.Equal("Instalada 1.0.0", t.TextoInstalada);
        Assert.Equal("Nueva versión 1.1.0", t.NuevaVersion);
        Assert.Equal("Corrige ganchos.", t.Nota);
    }

    [Fact]
    public void Con_la_ultima_instalada_queda_Al_dia_sin_boton()
    {
        EstadoTarjeta t = EstadoTarjeta.Calcular(Estado([V("1.1.0"), V("1.0.0")], "1.1.0", "1.1.0"), "2026", sinConexion: false);

        Assert.Equal(AccionTarjeta.AlDia, t.Accion);
        Assert.False(t.PuedeActuar);
        Assert.Null(t.Nota);
    }

    [Fact]
    public void Sin_paquete_para_su_Revit_y_sin_instalar_no_esta_disponible()
    {
        EstadoTarjeta t = EstadoTarjeta.Calcular(Estado([V("1.0.0", "2027")]), "2026", sinConexion: false);

        Assert.Equal(AccionTarjeta.NoDisponible, t.Accion);
        Assert.False(t.PuedeActuar);
        Assert.Equal("Todavía no está disponible para Revit 2026.", t.Nota);
    }

    [Fact]
    public void Sin_paquete_para_su_Revit_pero_instalada_queda_al_dia_y_lo_explica()
    {
        EstadoTarjeta t = EstadoTarjeta.Calcular(Estado([V("2.0.0", "2027"), V("1.0.0")], "1.0.0", "1.0.0"), "2026", sinConexion: false);

        Assert.Equal(AccionTarjeta.AlDia, t.Accion);
        Assert.Equal("La versión 2.0.0 todavía no está para Revit 2026.", t.Nota);
    }

    [Fact]
    public void Sin_conexion_solo_se_puede_si_la_version_ya_esta_en_disco()
    {
        EstadoTarjeta falta = EstadoTarjeta.Calcular(Estado([V("1.1.0"), V("1.0.0")], "1.0.0", "1.0.0"), "2026", sinConexion: true);
        EstadoTarjeta enDisco = EstadoTarjeta.Calcular(Estado([V("1.1.0"), V("1.0.0")], "1.0.0", "1.0.0", "1.1.0"), "2026", sinConexion: true);

        Assert.False(falta.PuedeActuar);
        Assert.StartsWith("Sin conexión", falta.Nota);
        Assert.True(enDisco.PuedeActuar);
    }

    [Fact]
    public void El_menu_de_versiones_dice_cual_esta_en_uso_y_a_cual_se_puede_volver()
    {
        EstadoAddin estado = Estado([V("1.2.0"), V("1.1.0"), V("1.0.0"), V("0.9.0", "2027")], "1.1.0", "1.1.0", "1.0.0");

        OpcionVersion Opcion(int i, bool sinConexion = false) => new(estado.Addin.Versiones[i], estado, "2026", sinConexion, SinAccion);

        Assert.Equal(("Instalar esta", true), (Opcion(0).TextoAccion, Opcion(0).PuedeUsar));
        Assert.Equal(("En uso", false), (Opcion(1).TextoAccion, Opcion(1).PuedeUsar));
        Assert.True(Opcion(1).EnUso);
        Assert.Equal(("Volver a esta", true), (Opcion(2).TextoAccion, Opcion(2).PuedeUsar));
        Assert.True(Opcion(2).EnEsteEquipo);
        Assert.Equal(("No está para Revit 2026", false), (Opcion(3).TextoAccion, Opcion(3).PuedeUsar));
        Assert.Equal(("Necesita conexión", false), (Opcion(0, sinConexion: true).TextoAccion, Opcion(0, sinConexion: true).PuedeUsar));
        Assert.True(Opcion(2, sinConexion: true).PuedeUsar); // está en disco
        Assert.Equal("1 oct 2026", Opcion(0).Fecha);
    }
}
