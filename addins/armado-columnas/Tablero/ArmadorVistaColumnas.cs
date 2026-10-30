namespace practicacq.Tablero;

using System;
using System.Collections.Generic;
using System.Linq;
using practicacq.Modelo;

/// <summary>
/// Arma lo que la página dibuja de una columna con las mismas cuentas que la
/// vista previa y el generador: <see cref="SeccionPlanta"/>,
/// <see cref="PlantaArmado"/>, <see cref="RepartoEstribos"/> y
/// <see cref="PlanTraslapos"/>. No hace ninguna cuenta nueva: si un número
/// de la página está mal, está mal en el generador.
/// </summary>
public static class ArmadorVistaColumnas
{
    /// <summary>
    /// Lo que el acero longitudinal queda por encima de la cara inferior de la
    /// zapata (issue #26). La zapata no se lee: su cara inferior se deduce de
    /// ahí, solo para dibujarla.
    /// </summary>
    private const double SobreFondoZapata = 0.075;

    private const string MotivoFueraDeRango = "Esa barra ya no existe con la cantidad de barras actual.";

    public static ColumnaVista Columna(
        string id, ColumnaContexto columna, string ubicacion, OpcionesColumna opciones,
        Func<string?, TipoBarra?> barraDe, IEnumerable<string> avisosExtra)
    {
        List<ConfigTramo?> tramos = opciones.Estribos.ATramos(columna);
        var avisos = columna.Avisos.Concat(avisosExtra).ToList();

        SeccionVista seccion = Seccion(columna, opciones, tramos, barraDe);
        List<ZonaVista> zonas = Zonas(columna, tramos, opciones.Longitudinal, barraDe, avisos);
        List<EstriboMenor> menores = opciones.Longitudinal.EstribosMenores;

        bool conZapata = columna.IdZapata is not null || columna.ZInferiorLongitudinal < columna.ZInferior - 1e-6;
        return new ColumnaVista(
            id, columna.Marca, ubicacion, columna.BaseX, columna.BaseY, opciones.RecubrimientoElegido,
            M(columna.ZSuperior), M(columna.ZInferiorLongitudinal),
            conZapata ? new ZapataVista(M(columna.ZInferiorLongitudinal - SobreFondoZapata), M(columna.ZInferior), 0) : null,
            columna.Niveles.Select(n => new NivelVista(n.Nombre, M(n.Cota))).ToList(),
            columna.Tramos.Select(t => new TramoVista(
                t.Indice, t.Tipo, M(t.ZInferior), M(t.ZSuperior), opciones.Estribos.Excepciones.ContainsKey(t.Indice))).ToList(),
            seccion,
            zonas,
            Traslapos(columna, opciones.Longitudinal, barraDe),
            avisos,
            new ResumenVista(
                seccion.Barras.Count,
                zonas.Sum(z => z.Cantidad),
                menores.Count(p => p.Forma != FormaMenor.Cerrado),
                menores.Count(p => p.Forma == FormaMenor.Cerrado),
                columna.Tramos.Count(t => t.Tipo == TipoTramo.Libre)),
            RecubrimientosRevit(columna));
    }

    /// <summary>
    /// Los recubrimientos que la columna tiene en Revit, en metros: el de la columna y el de cada tramo libre o nudo
    /// (el de contra zapata es el de la cimentación). La página los ofrece a un clic (decisión 15 del plan).
    /// </summary>
    public static IReadOnlyList<double> RecubrimientosRevit(ColumnaContexto columna) => columna.Tramos
        .Where(t => t.Tipo != TipoTramo.ContraZapata)
        .Select(t => t.Recubrimiento)
        .Prepend(columna.Recubrimiento)
        .Where(r => r > 0)
        .Select(r => Math.Round(r, 3))
        .Distinct()
        .OrderBy(r => r)
        .ToList();

    // ------------------------------------------------------------- sección

    private static SeccionVista Seccion(
        ColumnaContexto columna, OpcionesColumna opciones, List<ConfigTramo?> tramos, Func<string?, TipoBarra?> barraDe)
    {
        double ladoX = columna.BaseX, ladoY = columna.BaseY, recubrimiento = opciones.RecubrimientoElegido;
        ConfigLongitudinal cfg = opciones.Longitudinal;
        ConfigTramo estribo = opciones.Estribos.TramosLibres;

        TipoBarra? barraEstribo = barraDe(estribo.TipoBarraNombre);
        double dEstribo = barraEstribo?.DiametroMm ?? 0;
        double doblez = barraEstribo is null ? 0 : PlantaArmado.Doblez(barraEstribo, dEstribo);

        // Las longitudinales se corren con el estribo más grueso de la columna, como en GeneradorLongitudinal.
        double estriboMasGrueso = tramos.Select(t => barraDe(t?.TipoBarraNombre)?.DiametroMm ?? 0).DefaultIfEmpty(0).Max();

        double e = recubrimiento + dEstribo / 2000;
        var vistaEstribo = new EstriboVista(
            Cm(e), Cm(e), Cm(ladoX - e), Cm(ladoY - e),
            barraEstribo is null
                ? Array.Empty<double[]>()
                : Puntos(PlantaArmado.EstriboCerrado(recubrimiento, ladoX - recubrimiento, recubrimiento, ladoY - recubrimiento,
                    dEstribo / 1000, doblez, PlantaArmado.Cola135(dEstribo))),
            Color(dEstribo), Cm(dEstribo / 1000));

        double? Diametro(PosicionBarra p) => barraDe(cfg.BarraDe(p))?.DiametroMm;
        if (Diametro(PosicionBarra.Esquina) is null || Diametro(PosicionBarra.CaraX) is null || Diametro(PosicionBarra.CaraY) is null)
            return new SeccionVista(Cm(ladoX), Cm(ladoY), Cm(recubrimiento), vistaEstribo, Array.Empty<BarraVista>(),
                cfg.EstribosMenores.Select((p, i) => Invalida(i, p, "Elige las barras de las esquinas y de las caras.")).ToList());

        IReadOnlyList<SeccionPlanta.BarraEnPlanta> enPlanta = SeccionPlanta.Calcular(
            ladoX, ladoY, recubrimiento, estriboMasGrueso, cfg, p => Diametro(p)!.Value, doblez * 1000);

        // Cada intermedia con su lado y su lugar en la fila o columna, como las cuenta EstriboMenor.
        var barras = new List<(SeccionPlanta.BarraEnPlanta Barra, BarraVista Vista)>();
        foreach (SeccionPlanta.BarraEnPlanta b in enPlanta)
        {
            (string posicion, EjeCaja? eje, int? indice) = b.Posicion switch
            {
                PosicionBarra.CaraX => (b.Y < ladoY / 2 ? "abajo" : "arriba", (EjeCaja?)EjeCaja.Columna,
                    (int?)enPlanta.Count(o => o.Posicion == PosicionBarra.CaraX && (o.Y < ladoY / 2) == (b.Y < ladoY / 2) && o.X < b.X - 1e-9)),
                PosicionBarra.CaraY => (b.X < ladoX / 2 ? "izquierda" : "derecha", EjeCaja.Fila,
                    enPlanta.Count(o => o.Posicion == PosicionBarra.CaraY && (o.X < ladoX / 2) == (b.X < ladoX / 2) && o.Y < b.Y - 1e-9)),
                _ => ("esquina", null, null),
            };
            barras.Add((b, new BarraVista(Cm(b.X), Cm(b.Y), b.DiametroMm, Color(b.DiametroMm), Numero(b.DiametroMm), posicion, eje, indice)));
        }

        (GanchoGrapa.Pieza Inicio, GanchoGrapa.Pieza Fin)? BarrasDe(EjeCaja eje, int k)
        {
            List<SeccionPlanta.BarraEnPlanta> dos = barras.Where(x => x.Vista.Eje == eje && x.Vista.Indice == k)
                .OrderBy(x => eje == EjeCaja.Fila ? x.Barra.X : x.Barra.Y).Select(x => x.Barra).ToList();
            if (dos.Count != 2) return null;
            return (new GanchoGrapa.Pieza(dos[0].X, dos[0].Y, dos[0].DiametroMm / 2000, 0),
                    new GanchoGrapa.Pieza(dos[1].X, dos[1].Y, dos[1].DiametroMm / 2000, 0));
        }

        var menores = new List<MenorVista>();
        for (int i = 0; i < cfg.EstribosMenores.Count; i++)
        {
            EstriboMenor p = cfg.EstribosMenores[i];
            List<(GanchoGrapa.Pieza Inicio, GanchoGrapa.Pieza Fin)?> rango = p.Desde < 0 || p.Hasta < p.Desde
                ? new() { null }
                : Enumerable.Range(p.Desde, p.Hasta - p.Desde + 1).Select(k => BarrasDe(p.Eje, k)).ToList();
            TipoBarra? barra = barraDe(p.TipoBarraNombre) ?? barraEstribo;
            if (rango.Any(r => r is null) || barra is null)
            {
                menores.Add(Invalida(i, p, barra is null ? "Elige la barra del estribo." : MotivoFueraDeRango));
                continue;
            }

            double d = barra.DiametroMm;
            IReadOnlyList<(double X, double Y)> puntos = PlantaArmado.Menor(
                p, k => BarrasDe(p.Eje, k)!.Value, (ladoX / 2, ladoY / 2), d, PlantaArmado.Doblez(barra, d));
            string? motivo = ChoquesSeccion.CerradoImposible(
                p, barra, rango.SelectMany(r => new[] { r!.Value.Inicio, r.Value.Fin }), Donde(p));
            menores.Add(new MenorVista(
                i, p.Forma, p.Eje, p.Desde, p.Hasta, Color(d), Puntos(puntos),
                p.Forma == FormaMenor.Cerrado ? null : new[] { new[] { Cm(rango[0]!.Value.Inicio.X), Cm(rango[0]!.Value.Inicio.Y) }, new[] { Cm(rango[0]!.Value.Fin.X), Cm(rango[0]!.Value.Fin.Y) } },
                Cm(d / 1000), motivo is null, motivo));
        }

        return new SeccionVista(Cm(ladoX), Cm(ladoY), Cm(recubrimiento), vistaEstribo, barras.Select(x => x.Vista).ToList(), menores);
    }

    private static MenorVista Invalida(int indice, EstriboMenor p, string motivo) =>
        new(indice, p.Forma, p.Eje, p.Desde, p.Hasta, Color(0), Array.Empty<double[]>(), null, 0, false, motivo);

    /// <summary>"fila 2", "columnas 1 a 3": como lo nombra <see cref="ChoquesSeccion.CerradoImposible"/>.</summary>
    private static string Donde(EstriboMenor p)
    {
        string eje = p.Eje == EjeCaja.Fila ? "fila" : "columna";
        return p.Desde == p.Hasta ? $"{eje} {p.Desde + 1}" : $"{eje}s {p.Desde + 1} a {p.Hasta + 1}";
    }

    // ------------------------------------------------------------- estribos

    private static List<ZonaVista> Zonas(
        ColumnaContexto columna, List<ConfigTramo?> tramos, ConfigLongitudinal longitudinal,
        Func<string?, TipoBarra?> barraDe, List<string> avisos)
    {
        var zonas = new List<ZonaVista>();
        foreach (Tramo tramo in columna.Tramos)
        {
            if (tramo.Tipo == TipoTramo.ContraZapata || tramos[tramo.Indice] is not ConfigTramo cfg)
            {
                zonas.Add(new ZonaVista(tramo.Indice, M(tramo.ZInferior), M(tramo.ZSuperior), "zapata", null, 0));
                continue;
            }

            double cadaPorDefecto = cfg.Modo == ModoEstribo.Uniforme ? cfg.EspaciadoUniforme : cfg.EspaciadoConfinado;
            if (barraDe(cfg.TipoBarraNombre)?.DiametroMm is not double d)
            {
                zonas.Add(new ZonaVista(tramo.Indice, M(tramo.ZInferior), M(tramo.ZSuperior), Zona(tramo, "uniforme"), cadaPorDefecto, 0));
                continue;
            }

            // En los nudos no van grapas ni cerrados, como en GeneradorArmado.
            List<EstriboMenor> piezas = tramo.Tipo == TipoTramo.Libre ? longitudinal.MenoresParaGenerar() : new List<EstriboMenor>();
            PaqueteEstribos.Paquete paquete = PaqueteEstribos.Calcular(piezas, d, p => barraDe(p.TipoBarraNombre)?.DiametroMm ?? d);
            RepartoEstribos.Resultado reparto = RepartoEstribos.Calcular(tramo.ZInferior, tramo.ZSuperior, cfg, paquete);
            if (reparto.Aviso is not null) avisos.Add($"Tramo {tramo.Indice + 1}: {reparto.Aviso}");

            if (reparto.Zonas.Count == 0)
                zonas.Add(new ZonaVista(tramo.Indice, M(tramo.ZInferior), M(tramo.ZSuperior), Zona(tramo, "uniforme"), cadaPorDefecto, 0));
            foreach (RepartoEstribos.Zona z in reparto.Zonas)
                zonas.Add(new ZonaVista(tramo.Indice, M(z.ZDesde), M(z.ZHasta), Zona(tramo, z.Nombre), z.Espaciado, z.Cantidad));
        }
        return zonas;
    }

    /// <summary>De los rótulos de <see cref="RepartoEstribos"/> a los de la página.</summary>
    private static string Zona(Tramo tramo, string nombre) =>
        tramo.Tipo == TipoTramo.DentroDeViga ? "nudo"
        : nombre == "uniforme" ? "uniforme"
        : nombre == "zona central" ? "central"
        : "confinada";

    // ------------------------------------------------------------- traslapos

    private static TraslaposVista Traslapos(ColumnaContexto columna, ConfigLongitudinal cfg, Func<string?, TipoBarra?> barraDe)
    {
        // Mismo anillo que el generador: las posiciones pares son A y las impares B.
        List<double?> diametros = cfg.Anillo().Select(p => barraDe(cfg.BarraDe(p))?.DiametroMm).ToList();
        if (diametros.Count == 0 || diametros.Any(d => d is null))
            return new TraslaposVista(Array.Empty<PlanVista>(), M(columna.ZInferiorLongitudinal), M(columna.ZSuperior));

        List<double> a = diametros.Where((_, i) => i % 2 == 0).Select(d => d!.Value).ToList();
        List<double> b = diametros.Where((_, i) => i % 2 == 1).Select(d => d!.Value).ToList();
        PlanTraslapos.Resultado plan = PlanTraslapos.Calcular(
            PlanTraslapos.Columna.De(columna), a, b,
            new PlanTraslapos.Opciones(cfg.LongitudComercialBarra, cfg.RedondeoVarillas, cfg.SolapeMayorDeLosDos));

        PlanVista Vista(PlanTraslapos.PlanTipo p, List<double> delGrupo) => new(
            p.Grupo, Numero(p.DiametroMm), Color(p.DiametroMm),
            delGrupo.Count(d => Math.Abs(d - p.DiametroMm) < 1e-6),
            M(p.SolapeMinimo ?? 0), p.EnTramosDelOtro,
            p.Piezas.Select(x => new TrozoVista(M(x.ZDesde), M(x.ZHasta), M(x.LargoCorte))).ToList(),
            p.Traslapos.Select(x => new TrozoVista(M(x.ZInferior), M(x.ZSuperior), M(x.Largo))).ToList(),
            !p.TieneSolucion, p.Motivo);

        return new TraslaposVista(
            plan.A.OrderByDescending(p => p.DiametroMm).Select(p => Vista(p, a))
                .Concat(plan.B.OrderByDescending(p => p.DiametroMm).Select(p => Vista(p, b))).ToList(),
            M(columna.ZInferiorLongitudinal), M(columna.ZSuperior));
    }

    // ------------------------------------------------------------- formato

    /// <summary>"#5", o el diámetro si la barra no está en el cuadro.</summary>
    public static string Numero(double diametroMm) =>
        CuadroVarillas.PorDiametro(diametroMm) is { } v ? $"#{v.Numero}" : $"Ø {diametroMm:0.#} mm";

    /// <summary>El color de los filtros de vista de Revit para ese diámetro (<see cref="ColoresVarilla"/>).</summary>
    public static string Color(double diametroMm)
    {
        ColoresVarilla.ColorRgb c = ColoresVarilla.ColorDe(diametroMm);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static double[][] Puntos(IReadOnlyList<(double X, double Y)> puntos) =>
        puntos.Select(p => new[] { Cm(p.X), Cm(p.Y) }).ToArray();

    /// <summary>De metros a centímetros, con dos decimales.</summary>
    private static double Cm(double metros) => Math.Round(metros * 100, 2);

    /// <summary>Metros con tres decimales (al milímetro).</summary>
    private static double M(double metros) => Math.Round(metros, 3);
}
