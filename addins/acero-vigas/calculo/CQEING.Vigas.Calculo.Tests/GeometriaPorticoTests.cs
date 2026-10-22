using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Nucleo;
using CQEING.Vigas.Calculo.Vigas;

namespace CQEING.Vigas.Calculo.Tests;

public class GeometriaPorticoTests
{
    private static readonly GeometriaPortico Maqueta = new(Datos.PorticoMaqueta(), recubrimientoPuntas: 50);

    [Fact]
    public void LasLucesVanEntreApoyosYElVoladizoNoEsLuz()
    {
        Assert.Equal(["A-B", "B-C", "C-D"], Maqueta.Luces.Select(l => l.Nombre));
    }

    [Fact]
    public void LasBarrasVanDePuntaAPuntaMenosElRecubrimiento()
    {
        Assert.Equal((-150, 13950), (Maqueta.InicioBarras, Maqueta.FinBarras));
    }

    [Fact]
    public void CapaDelCentro_TraslapaEnLaMitadDeLaLuzYA5cmDeLasCaras()
    {
        Ranura r = Maqueta.Ranura(Maqueta.Luces[0], Referencia.Centro);

        Assert.Equal(new Ranura(250, 4150, 2200, Referencia.Centro), r);
    }

    [Fact]
    public void CapaDeLaCara_EmpiezaOTerminaA5cmDeLaCara()
    {
        Luz bc = Maqueta.Luces[1];

        Assert.Equal(4650, Maqueta.Ranura(bc, Referencia.Inicio).PuntoIdeal);
        Assert.Equal(7650, Maqueta.Ranura(bc, Referencia.Fin).PuntoIdeal);
    }

    [Theory]
    [InlineData(TipoViga.Aerea, Posicion.Superior, Referencia.Centro)]
    [InlineData(TipoViga.Aerea, Posicion.Inferior, Referencia.Inicio)]
    [InlineData(TipoViga.Cimentacion, Posicion.Superior, Referencia.Inicio)]
    [InlineData(TipoViga.Cimentacion, Posicion.Inferior, Referencia.Centro)]
    public void ElTipoDeVigaDecideQueCapaVaAlCentro(TipoViga tipo, Posicion capa, Referencia esperada)
    {
        Assert.Equal(esperada, GeometriaPortico.ReferenciaDe(tipo, capa, Cara.Inicio));
    }

    [Fact]
    public void LaCapaDeLaCara_AlFinalDeLaLuz_SeMideDesdeElFin()
    {
        Assert.Equal(Referencia.Fin, GeometriaPortico.ReferenciaDe(TipoViga.Aerea, Posicion.Inferior, Cara.Final));
    }

    [Fact]
    public void ConVoladizoAlInicio_LasBarrasEmpiezanEnLaPunta()
    {
        var g = new GeometriaPortico(Datos.PorticoMaqueta() with { VoladizoInicio = 1000 }, recubrimientoPuntas: 50);

        Assert.Equal(-1150, g.InicioBarras);
        Assert.Equal(3, g.Luces.Count);
    }

    [Fact]
    public void EnLaPuntaDeUnVoladizo_ElGanchoQuedaPorDentroDelUltimoEstribo()
    {
        Portico p = Datos.PorticoMaqueta() with { VoladizoInicio = 1000, VoladizoFinal = 1000 };

        var g = new GeometriaPortico(p, recubrimientoPuntas: 50, estriboEnVoladizo: 10);

        Assert.Equal(-1140, g.InicioBarras);
        Assert.Equal(new GeometriaPortico(p, 50).FinBarras - 10, g.FinBarras);
    }

    [Fact]
    public void ElEstriboDelVoladizoNoMueveElExtremoQueDescansaEnColumna()
    {
        Portico sinVoladizos = Datos.PorticoMaqueta() with { VoladizoInicio = 0, VoladizoFinal = 0 };

        var con = new GeometriaPortico(sinVoladizos, recubrimientoPuntas: 50, estriboEnVoladizo: 10);
        var sin = new GeometriaPortico(sinVoladizos, recubrimientoPuntas: 50);

        Assert.Equal((sin.InicioBarras, sin.FinBarras), (con.InicioBarras, con.FinBarras));
    }
}
