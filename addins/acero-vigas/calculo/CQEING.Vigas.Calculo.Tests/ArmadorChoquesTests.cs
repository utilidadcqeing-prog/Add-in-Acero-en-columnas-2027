using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>Los choques en el alzado del tablero (paso 3 del plan de choques de 22-oct): dónde va el punto y qué dice.</summary>
public class ArmadorChoquesTests
{
    private static readonly UbicacionPortico U = new(1, 0, 0, 0, 3000, 3500, 300, 500, [new("101", 0, 9000)]);

    private static PiezaChocada Pieza(string id, TipoTrazo tipo, string? tramo = null, int numero = 5) => new(id, tipo, tramo, numero);

    [Fact]
    public void LaGrapaQueSeMeteEnUnaBarraVaSobreLaBarra()
    {
        var c = new Choque(new Punto3(2350, 40, 3060), Pieza("E1", TipoTrazo.Grapa, "A-B", 3), Pieza("S2", TipoTrazo.Barra), 4.86, 8);

        ChoqueVista v = ArmadorChoques.Vista(c, U);

        Assert.Equal((2.35, "S2", 8, 4.9), (v.X, v.Elemento, v.Veces, v.SeMeteMm));
        Assert.Equal("La grapa E1 de A-B (#3) se mete 4.9 mm en la barra S2 (#5) · ×8 estribos", v.Texto);
        Assert.Equal("Prueba la grapa en C o en S, otro gancho o mover la barra S2", v.Pista);
    }

    [Fact]
    public void ElEstriboDelVoladizoContraUnBaston()
    {
        var c = new Choque(new Punto3(12952, 48, 3439), Pieza("E", TipoTrazo.Estribo, "Voladizo D", 3), Pieza("BS4", TipoTrazo.Baston), 2.58, 8);

        ChoqueVista v = ArmadorChoques.Vista(c, U);

        Assert.Equal("BS4", v.Elemento);
        Assert.Equal("El estribo E del voladizo D (#3) se mete 2.6 mm en el bastón BS4 (#5) · ×8 estribos", v.Texto);
        Assert.Equal("Prueba otro gancho o mover el bastón BS4", v.Pista);
    }

    [Fact]
    public void ElBastonQueSeMeteEnUnaBarraVaSobreElBaston()
    {
        var c = new Choque(new Punto3(1000, 0, 0), Pieza("BS1", TipoTrazo.Baston), Pieza("S1", TipoTrazo.Barra), 6, 1);

        ChoqueVista v = ArmadorChoques.Vista(c, U);

        Assert.Equal("BS1", v.Elemento);
        Assert.Equal("El bastón BS1 (#5) se mete 6.0 mm en la barra S1 (#5)", v.Texto);
        Assert.Equal("Mueve o acorta el bastón BS1", v.Pista);
    }

    [Fact]
    public void LaXEsLaDelPorticoAunqueVayaEnOtraDireccion()
    {
        // pórtico a lo largo de +Y desde (100, 200): el punto (130, 1700) queda a 1.50 m del eje A
        UbicacionPortico u = U with { Ux = 0, Uy = 1, OrigenX = 100, OrigenY = 200 };
        var c = new Choque(new Punto3(130, 1700, 0), Pieza("S1", TipoTrazo.Barra), Pieza("S2", TipoTrazo.Barra), 1, 1);

        Assert.Equal(1.5, ArmadorChoques.Vista(c, u).X, 6);
    }

    [Fact]
    public void LosDeLaMaquetaVanDeIzquierdaADerechaSobreSusBastones()
    {
        // H6: la cola del 135° contra el bastón de arriba junto a la esquina
        PorticoLeido p = PlanificadorAceroTests.Portico();
        PlanAcero plan = PlanificadorAcero.Planificar("P", p, Tablero.OpcionesTablero.PorDefecto, Reglas.Generales)!;

        IReadOnlyList<ChoqueVista> choques = ArmadorChoques.Armar(plan, p.Ubicacion!, Reglas.Generales);

        Assert.Equal(["BS1", "BS2", "BS2", "BS3", "BS3", "BS4", "BS4"], choques.Select(c => c.Elemento));
        Assert.Equal(choques.Select(c => c.X).Order(), choques.Select(c => c.X));
        Assert.Equal(0.6548, choques[0].X, 4);
        Assert.Equal("El estribo E de A-B (#3) se mete 2.6 mm en el bastón BS1 (#5) · ×8 estribos", choques[0].Texto);
    }
}
