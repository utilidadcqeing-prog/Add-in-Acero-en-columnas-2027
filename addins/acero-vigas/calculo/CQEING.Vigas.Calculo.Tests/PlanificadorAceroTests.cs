using CQEING.Vigas.Calculo.Generacion;
using CQEING.Vigas.Calculo.Modelo;
using CQEING.Vigas.Calculo.Porticos;
using CQEING.Vigas.Calculo.Seccion;
using CQEING.Vigas.Calculo.Tablero;

namespace CQEING.Vigas.Calculo.Tests;

/// <summary>
/// El plan de lo que se crea en Revit (#85): una pieza por barra física, en su puesto del corte, con el traslapo al lado
/// (decisión 2 del plan docs/planes/2026-10-19-generar-acero-vigas-85.md). El pórtico es el de la maqueta, 300 × 500.
/// </summary>
public class PlanificadorAceroTests
{
    /// <summary>Las vigas van de cara a cara de las columnas de 0.40, y la última hasta la punta del voladizo.</summary>
    internal static PorticoLeido Portico(TipoViga tipo = TipoViga.Aerea) => ArmadorVistaTests.Portico1(tipo: tipo) with
    {
        Ubicacion = new UbicacionPortico(1, 0, 0, 0, 3000, 3500, 300, 500,
            [new("101", -200, 4200), new("102", 4600, 7700), new("103", 8100, 12100), new("104", 12500, 14000)]),
    };

    private static PlanAcero Planificar(OpcionesTablero? o = null, PorticoLeido? p = null) =>
        PlanificadorAcero.Planificar("Pórtico 1", p ?? Portico(), o ?? OpcionesTablero.PorDefecto, Reglas.Generales)!;

    private static PorticoCalculado Calcular(OpcionesTablero? o = null, PorticoLeido? p = null) =>
        CalculoPortico.Calcular(p ?? Portico(), o ?? OpcionesTablero.PorDefecto, Reglas.Generales)!;

    private static List<BarraEnSeccion> DelCorte(PorticoCalculado c, Posicion capa) =>
        (capa == Posicion.Superior ? c.Estribos.Seccion.Superior : c.Estribos.Seccion.Inferior).Filas.SelectMany(f => f).ToList();

    [Fact]
    public void UnaPiezaPorBarraFisica()
    {
        PorticoCalculado c = Calcular();

        PlanAcero plan = Planificar();

        Assert.Equal(c.Vigas.Barras.Count() * 3, plan.Barras.Count);
        Assert.Equal("Pórtico 1", plan.Portico);
        Assert.All(c.Vigas.Barras, b => Assert.Equal(3, plan.Barras.Count(x => x.Pieza == b.Id)));
    }

    [Fact]
    public void VariasFilas_CadaPiezaEnSuPuestoDelCorte()
    {
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(7, 5)] };
        PorticoCalculado c = Calcular(o);
        List<BarraEnSeccion> corte = DelCorte(c, Posicion.Superior);

        List<PiezaLongitudinal> s1 = Planificar(o).Barras.Where(b => b.Pieza == "S1").ToList();

        Assert.Equal(7, s1.Count);
        Assert.True(corte.Select(b => b.Y).Distinct().Count() > 1, "7 #5 en 300 de ancho tienen que ir en dos filas");
        Assert.Equal(corte.Select(b => (b.X, b.Y, b.Diametro)), s1.Select(p => (p.XCorte, p.YCorte, p.Diametro)));
    }

    [Fact]
    public void DosDiametros_CadaPiezaConElDeSuBarra()
    {
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(3, 5), new GrupoBarras(2, 4)] };

        List<PiezaLongitudinal> s1 = Planificar(o).Barras.Where(b => b.Pieza == "S1").ToList();

        Assert.Equal(3, s1.Count(p => p.Diametro == 5));
        Assert.Equal(2, s1.Count(p => p.Diametro == 4));
        Assert.Equal([3, 4, 5], Planificar(o).Diametros.Order()); // el #3 es el de los estribos
    }

    [Fact]
    public void GanchosDelBorde_SoloEnLasPuntasDelPortico()
    {
        PorticoCalculado c = Calcular();
        List<string> arriba = c.Vigas.Superior!.Barras.Select(b => b.Id).ToList();

        PlanAcero plan = Planificar();

        Assert.All(plan.Barras.Where(p => p.Pieza == arriba[0]), p => Assert.Equal(TipoGancho.De90, p.GanchoInicio));
        Assert.All(plan.Barras.Where(p => p.Pieza == arriba[^1]), p => Assert.Equal(TipoGancho.De90, p.GanchoFin));
        if (arriba.Count > 1)
            Assert.All(plan.Barras.Where(p => p.Pieza == arriba[0]), p => Assert.Equal(TipoGancho.Sin, p.GanchoFin));
        Assert.All(plan.Barras, p => Assert.Equal(c.Vigas.Barras.Single(b => b.Id == p.Pieza) is var b ? (b.Desde, b.Largo) : default, (p.Desde, p.Largo)));
    }

    /// <summary>
    /// Carlos (21-oct, min 01:03:07): "son elementos sólidos que no se pueden traslapar". Medido en Revit: con la fila 2
    /// debajo de las esquinas, las dos filas llegaban a la misma punta y el gancho de la fila 1 bajaba atravesando la barra
    /// de la fila 2. Ahora la de la fila 2 termina antes, con todo su gancho por dentro del arranque del de la fila 1: un
    /// radio de doblado más un diámetro (#3: 57 / 2 + 9.5 = 38 mm) en cada punta con gancho. Su largo baja lo mismo.
    /// </summary>
    [Fact]
    public void FilaDos_SuGanchoVaPorDentroDelDeLaFilaUno()
    {
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(3, 5), new GrupoBarras(2, 3, Fila: 2)] };
        PorticoCalculado c = Calcular(o);
        List<PiezaLongitudinal> arriba = Planificar(o).Barras.Where(p => p.Capa == Posicion.Superior).ToList();
        const int anidado = 38;

        int anidadas = 0;
        foreach (Barra b in c.Vigas.Superior!.Barras)
        {
            List<PiezaLongitudinal> fila1 = arriba.Where(p => p.Pieza == b.Id && p.Diametro == 5).ToList();
            Assert.All(fila1, p => Assert.Equal((b.Desde, b.Hasta, b.Largo), (p.Desde, p.Hasta, p.Largo)));
            foreach (PiezaLongitudinal p in arriba.Where(p => p.Pieza == b.Id && p.Diametro == 3))
            {
                // Solo la que queda debajo de una #5 (se tocan en X) se anida; si el traslapo al lado las separa, sus
                // ganchos van en planos distintos y no se cruzan.
                bool debajo = fila1.Any(f => Math.Abs(f.XCorte - p.XCorte) < (15.9 + 9.5) / 2);
                int alInicio = debajo && b.TipoInicio != TipoGancho.Sin ? anidado : 0, alFin = debajo && b.TipoFin != TipoGancho.Sin ? anidado : 0;
                ReglaDiametro r3 = Reglas.Generales.PorDiametro[3];
                int largo = b.Hasta - alFin - (b.Desde + alInicio) + Reglas.LargoGancho(r3, b.TipoInicio) + Reglas.LargoGancho(r3, b.TipoFin);
                Assert.Equal((b.Desde + alInicio, b.Hasta - alFin, largo), (p.Desde, p.Hasta, p.Largo));
                anidadas += alInicio + alFin > 0 ? 1 : 0;
            }
        }
        Assert.True(anidadas >= 2, "las dos #3 de debajo de las esquinas en la punta con gancho");
    }

    /// <summary>
    /// Con dos diámetros en la capa, cada pieza lleva el largo con los ganchos de su diámetro: con el de la barra del
    /// tablero (#5), las #4 de la fila 2 salían "0.06 m más cortas en Revit" (21-oct).
    /// </summary>
    [Fact]
    public void DosDiametros_CadaPiezaConElLargoDeSusGanchos()
    {
        var o = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(3, 5), new GrupoBarras(2, 4)] };
        ReglaDiametro r4 = Reglas.Generales.PorDiametro[4];

        List<PiezaLongitudinal> de4 = Planificar(o).Barras.Where(p => p.Capa == Posicion.Superior && p.Diametro == 4).ToList();

        Assert.NotEmpty(de4);
        Assert.Contains(de4, p => p.GanchoInicio != TipoGancho.Sin || p.GanchoFin != TipoGancho.Sin);
        Assert.All(de4, p => Assert.Equal(p.Hasta - p.Desde + Reglas.LargoGancho(r4, p.GanchoInicio) + Reglas.LargoGancho(r4, p.GanchoFin), p.Largo));
    }

    [Fact]
    public void TraslapoAlLado_LaSegundaBarraSeCorreUnDiametroHaciaElCentro()
    {
        PorticoCalculado c = Calcular();
        List<string> arriba = c.Vigas.Superior!.Barras.Select(b => b.Id).ToList();
        Assert.True(arriba.Count >= 2, "el pórtico de la maqueta traslapa arriba");
        List<BarraEnSeccion> corte = DelCorte(c, Posicion.Superior);
        const double d5 = 15.9;

        PlanAcero plan = Planificar();

        List<PiezaLongitudinal> primera = plan.Barras.Where(p => p.Pieza == arriba[0]).ToList();
        List<PiezaLongitudinal> segunda = plan.Barras.Where(p => p.Pieza == arriba[1]).ToList();
        Assert.Equal(corte.Select(b => b.X), primera.Select(p => p.XCorte));
        Assert.Equal(corte.Select(b => b.X < 150 ? b.X + d5 : b.X > 150 ? b.X - d5 : b.X + d5), segunda.Select(p => p.XCorte));
        Assert.Equal(corte.Select(b => b.Y), segunda.Select(p => p.YCorte));
        if (arriba.Count >= 3)
            Assert.Equal(primera.Select(p => p.XCorte), plan.Barras.Where(p => p.Pieza == arriba[2]).Select(p => p.XCorte));
    }

    /// <summary>El pórtico de la maqueta con vigas de 25 de ancho, como las VIG_25X35 del proyecto de prueba.</summary>
    private static PorticoLeido Angosto() => Portico() with { Tramos = Portico().Tramos.Select(t => t with { Ancho = 250 }).ToList() };

    [Fact]
    public void TraslapoEnVigaAngosta_LaDelMedioNoCabeAlLadoYVaAbajo()
    {
        // 3 #5 en 250: al lado, la del medio quedaría a 19.8 mm libres de una corrida (Carlos: entonces va abajo).
        PorticoCalculado c = Calcular(p: Angosto());
        List<string> arriba = c.Vigas.Superior!.Barras.Select(b => b.Id).ToList();
        List<BarraEnSeccion> corte = DelCorte(c, Posicion.Superior).OrderBy(b => b.X).ToList();
        Assert.Equal(3, corte.Count);
        const double d5 = 15.9;

        List<PiezaLongitudinal> segunda = Planificar(p: Angosto()).Barras.Where(p => p.Pieza == arriba[1]).OrderBy(p => p.XCorte).ToList();

        Assert.Equal((corte[0].X + d5, corte[0].Y), (segunda[0].XCorte, segunda[0].YCorte));
        Assert.Equal((corte[1].X, corte[1].Y - d5), (segunda[1].XCorte, segunda[1].YCorte));
        Assert.Equal((corte[2].X - d5, corte[2].Y), (segunda[2].XCorte, segunda[2].YCorte));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void TraslapoEnVigaAngosta_NingunaBarraSeMontaEnOtra(int cantidad)
    {
        // Con 4 #5 en 250 las dos del medio se corrían una hacia la otra y quedaban a 14 mm de centro a centro.
        var o = OpcionesTablero.PorDefecto with { Inferior = [new GrupoBarras(cantidad, 5)] };
        PorticoCalculado c = Calcular(o, Angosto());
        List<string> abajo = c.Vigas.Inferior!.Barras.Select(b => b.Id).ToList();
        Assert.True(abajo.Count >= 2, "el pórtico de la maqueta traslapa abajo");

        PlanAcero plan = Planificar(o, Angosto());

        // En el traslapo están las dos barras: cada pieza solo puede tocar a su pareja (la del mismo puesto del corte).
        List<PiezaLongitudinal> primera = plan.Barras.Where(p => p.Pieza == abajo[0]).ToList();
        List<PiezaLongitudinal> segunda = plan.Barras.Where(p => p.Pieza == abajo[1]).ToList();
        for (int i = 0; i < segunda.Count; i++)
        {
            foreach (PiezaLongitudinal otra in primera.Where((_, k) => k != i).Concat(segunda.Where((_, k) => k != i)))
            {
                double centros = Math.Sqrt(Math.Pow(segunda[i].XCorte - otra.XCorte, 2) + Math.Pow(segunda[i].YCorte - otra.YCorte, 2));
                Assert.True(centros >= 15.9 - 1e-6, $"{segunda[i].XCorte:F1} y {otra.XCorte:F1} quedan a {centros:F1} mm de centro a centro");
            }
        }
    }

    [Fact]
    public void SinTraslapo_NadaSeCorre()
    {
        // Un pórtico de una luz de 3 m: cada capa sale de una sola varilla.
        var portico = new Portico([Datos.Columna("A", 0), Datos.Columna("B", 3000)]);
        PorticoLeido p = ArmadorVistaTests.Portico1(portico) with
        {
            Ubicacion = new UbicacionPortico(1, 0, 0, 0, 3000, 3500, 300, 500, [new("101", 200, 2800)]),
        };
        PorticoCalculado c = Calcular(p: p);
        Assert.Single(c.Vigas.Superior!.Barras);

        PlanAcero plan = Planificar(p: p);

        Assert.Equal(DelCorte(c, Posicion.Superior).Select(b => b.X), plan.Barras.Where(b => b.Capa == Posicion.Superior).Select(b => b.XCorte));
    }

    [Fact]
    public void Bastones_EnElPuestoQueLesDaElCorte()
    {
        PorticoCalculado c = Calcular();
        Assert.NotEmpty(c.Vigas.Bastones);

        PlanAcero plan = Planificar();

        foreach (Baston b in c.Vigas.Bastones)
        {
            UbicacionBastones u = UbicadorBastones.Ubicar(c.Seccion, b.Capa, [b.Grupo]);
            List<PiezaLongitudinal> piezas = plan.Bastones.Where(p => p.Pieza == b.Id).ToList();
            Assert.Equal(u.Barras.Select(x => (x.Barra.X, x.Barra.Y)), piezas.Select(p => (p.XCorte, p.YCorte)));
            Assert.All(piezas, p => Assert.Equal((b.Desde, b.Hasta, b.TipoInicio, b.TipoFin, b.Capa), (p.Desde, p.Hasta, p.GanchoInicio, p.GanchoFin, p.Capa)));
        }
        Assert.Empty(plan.BastonesSinLugar);
    }

    [Fact]
    public void Cimentacion_LosBastonesDeLosApoyosVanAbajo()
    {
        PlanAcero plan = Planificar(p: Portico(TipoViga.Cimentacion));

        Assert.Contains(plan.Bastones, b => b.Pieza.StartsWith("BI") && b.Capa == Posicion.Inferior);
        Assert.All(plan.Bastones, b => Assert.Equal(b.Pieza.StartsWith("BS") ? Posicion.Superior : Posicion.Inferior, b.Capa));
    }

    [Fact]
    public void Host_LaVigaDelPuntoMedio_OLaMasCercanaSiCaeEnUnApoyo()
    {
        PlanAcero plan = Planificar();

        // El bastón de arriba en B queda centrado en el eje (4400), dentro de la columna: las vigas 101 y 102 están a
        // 200 mm; con empate, la primera.
        PiezaLongitudinal enB = plan.Bastones.First(b => b.Capa == Posicion.Superior && (b.Desde + b.Hasta) / 2 == 4400);
        Assert.Equal("101", enB.Host);
        Assert.All(plan.Barras, b => Assert.Contains(b.Host, new[] { "101", "102", "103", "104" }));
    }

    [Fact]
    public void Estribos_UnGrupoPorCorridaConLasCarasDelCorte()
    {
        PorticoCalculado c = Calcular();

        PlanAcero plan = Planificar();

        foreach (EstriboEnPortico e in c.Estribos.Estribos)
        {
            List<GrupoEstribos> grupos = plan.Estribos.Where(g => g.Pieza == e.Id && g.Tramo == e.Tramo).ToList();
            EstriboEnSeccion caras = c.Estribos.EnSeccion.Single(s => s.Id == e.Id);
            // Por zonas (hallazgo E6): las mismas posiciones, y en una luz las dos zonas con la misma cantidad.
            List<double> rehechas = grupos.SelectMany(g => Enumerable.Range(0, g.Corrida.Cantidad).Select(k => g.Corrida.Primero + k * g.Corrida.Separacion)).ToList();
            Assert.Equal(e.Posiciones.Count, rehechas.Count);
            Assert.All(e.Posiciones.Zip(rehechas), z => Assert.Equal(z.First, z.Second, 1));
            if (!e.Tramo.StartsWith("Voladizo") && grupos.Count == 3)
                Assert.Equal(grupos[0].Corrida.Cantidad, grupos[2].Corrida.Cantidad);
            Assert.All(grupos, g => Assert.Equal((e.Tipo, e.Diametro, e.Gancho, caras.Izquierda, caras.Derecha, caras.Abajo, caras.Arriba),
                (g.Tipo, g.Diametro, g.Gancho, g.Izquierda, g.Derecha, g.Abajo, g.Arriba)));
        }
        Assert.Equal(c.Estribos.Cantidad, plan.Estribos.Sum(g => g.Corrida.Cantidad));
        Assert.Contains(3, plan.Diametros);
    }

    [Fact]
    public void Estribos_ElHostEsLaVigaDeSuTramo()
    {
        PlanAcero plan = Planificar();

        Assert.All(plan.Estribos.Where(g => g.Tramo == "A-B"), g => Assert.Equal("101", g.Host));
        Assert.All(plan.Estribos.Where(g => g.Tramo == "C-D"), g => Assert.Equal("103", g.Host));
        Assert.All(plan.Estribos.Where(g => g.Tramo.StartsWith("Voladizo")), g => Assert.Equal("104", g.Host));
    }

    [Fact]
    public void Estribos_ElAdicionalAbiertoEsUnaRama()
    {
        var adicional = new EstriboAdicional(TipoEstribo.Abierto, [new PuntoBarra(Posicion.Superior, 1, 2), new PuntoBarra(Posicion.Inferior, 1, 2)]);
        var o = OpcionesTablero.PorDefecto with { Estribos = new ConfigEstribos(Adicionales: [adicional]) };

        List<GrupoEstribos> e1 = Planificar(o).Estribos.Where(g => g.Pieza == "E1").ToList();

        Assert.NotEmpty(e1);
        Assert.All(e1, g => Assert.Equal((TipoEstribo.Abierto, g.Izquierda), (g.Tipo, g.Derecha)));
        // Hallazgo E13: la rama sabe qué barras abraza (la del medio de arriba y la del medio de abajo, ya alineadas).
        PorticoCalculado c = Calcular(o);
        BarraEnSeccion medioArriba = c.Estribos.Seccion.Superior.Filas[0][1], medioAbajo = c.Estribos.Seccion.Inferior.Filas[0][1];
        Assert.All(e1, g => Assert.Equal(new Rama(medioArriba.X, medioArriba.Y, medioAbajo.Y, 15.9), g.Rama));
        Assert.All(Planificar(o).Estribos.Where(g => g.Pieza == "E"), g => Assert.Null(g.Rama));
    }

    /// <summary>
    /// Carlos (21-oct, min 01:03:07): "son elementos sólidos que no se pueden traslapar". El adicional va pegado al lado
    /// del cerrado, un diámetro de estribo más allá, y no en su mismo plano (ahí sus ganchos cruzaban el cerrado).
    /// </summary>
    [Fact]
    public void Estribos_ElAdicionalVaPegadoAlCerradoYNoEnSuPlano()
    {
        var adicional = new EstriboAdicional(TipoEstribo.Abierto, [new PuntoBarra(Posicion.Superior, 1, 2), new PuntoBarra(Posicion.Inferior, 1, 2)]);
        var o = OpcionesTablero.PorDefecto with { Estribos = new ConfigEstribos(Adicionales: [adicional]) };
        PlanAcero plan = Planificar(o);
        double d = Reglas.Generales.PorDiametro[3].Diametro;

        IEnumerable<Corrida> cerrado = plan.Estribos.Where(g => g.Pieza == "E").Select(g => g.Corrida);
        IEnumerable<Corrida> abierto = plan.Estribos.Where(g => g.Pieza == "E1").Select(g => g.Corrida);

        Assert.Equal(cerrado.Select(c => c with { Primero = c.Primero + d }), abierto);
    }

    [Fact]
    public void NoSePlanifica_SiNoSePuedeGenerarOSinUbicacion()
    {
        var noCaben = OpcionesTablero.PorDefecto with { Superior = [new GrupoBarras(30, 8)] };

        Assert.Null(PlanificadorAcero.Planificar("Pórtico 1", Portico(), noCaben, Reglas.Generales));
        Assert.Null(PlanificadorAcero.Planificar("Pórtico 1", ArmadorVistaTests.Portico1(), OpcionesTablero.PorDefecto, Reglas.Generales));
    }
}
