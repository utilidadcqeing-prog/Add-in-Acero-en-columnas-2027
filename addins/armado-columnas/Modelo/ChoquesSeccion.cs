namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// Qué piezas de la sección chocan (issue #131): el add-in avisa y deja
/// seguir, nunca corrige ni impide generar. Sin Revit: lo usan la vista
/// previa, "Editar estribos menores" y el aviso antes de generar. Ver
/// docs/planes/2026-10-22-avisos-de-choque-131-diseno.md.
/// Todas las medidas en METROS salvo los diámetros, en milímetros.
/// </summary>
public static class ChoquesSeccion
{
    /// <summary>Por debajo de esto (mm) dos piezas que "se tocan" no cuentan como choque.</summary>
    public const double ToleranciaMm = 1;

    /// <param name="Varilla">Índice de la varilla en el anillo: las piezas de una misma varilla no chocan entre sí.</param>
    /// <param name="ZInf">Desde qué altura existe la pieza; sin límite, toda la columna.</param>
    public sealed record PiezaRedonda(
        double X, double Y, double DiametroMm, int Varilla,
        double ZInf = double.NegativeInfinity, double ZSup = double.PositiveInfinity);

    /// <param name="Motivo">Qué pieza choca con cuál, para leer.</param>
    /// <param name="Marcas">Círculos a rodear en rojo en el dibujo (centro y radio).</param>
    /// <param name="Clave">Qué piezas chocan, sin las medidas: el mismo par en dos tramos es el mismo choque (<see cref="DeLaColumna"/>).</param>
    public sealed record Choque(string Motivo, IReadOnlyList<(double X, double Y, double Radio)> Marcas, string Clave = "");

    /// <summary>
    /// Los choques de todos los tramos de una columna, uno por <see cref="Choque.Clave"/>:
    /// las longitudinales son las mismas en toda la columna, y un tramo sin
    /// estribo las ubica unos milímetros distinto (revisión final del #131).
    /// Manda el primero: pasar antes el tramo de referencia.
    /// </summary>
    public static IReadOnlyList<Choque> DeLaColumna(IEnumerable<Resultado> tramosEnOrden)
    {
        var vistas = new HashSet<string>();
        return tramosEnOrden.SelectMany(r => r.Todos).Where(c => vistas.Add(c.Clave)).ToList();
    }

    /// <summary>
    /// Lo que se dice cuando hay varillas con más de un traslapo: su zona de
    /// traslapo no se revisa (el acomodo de las piezas corridas solo está
    /// medido con uno). Lo usan la vista previa, estribos menores y Generar.
    /// </summary>
    public static string NotaSinRevisar(int varillas) =>
        $"Zona de traslapo no revisada en {varillas} varilla(s): llevan más de un traslapo.";

    /// <summary>
    /// Distancia libre mínima (borde a borde) entre dos longitudinales: la de
    /// la NSR-10 para columnas, 1,5 diámetros de la más gruesa o 4 cm, la
    /// mayor. Decisión de Luciano (22-oct), A CONFIRMAR CON CARLOS: si la
    /// empresa usa otra regla, se cambia acá.
    /// </summary>
    public static double SeparacionMinimaMm(double diametroMm1, double diametroMm2) =>
        Math.Max(1.5 * Math.Max(diametroMm1, diametroMm2), 40);

    /// <summary>
    /// Cada par de piezas de varillas distintas que comparten altura y quedan
    /// más juntas que <see cref="SeparacionMinimaMm"/>: un choque por par.
    /// </summary>
    public static IReadOnlyList<Choque> BarrasJuntas(IReadOnlyList<PiezaRedonda> piezas, Func<int, string> nombreVarilla)
    {
        var choques = new List<Choque>();
        for (int i = 0; i < piezas.Count; i++)
            for (int j = i + 1; j < piezas.Count; j++)
            {
                PiezaRedonda a = piezas[i], b = piezas[j];
                if (a.Varilla == b.Varilla || a.ZSup <= b.ZInf || b.ZSup <= a.ZInf) continue;

                double libreMm = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) * 1000
                                 - (a.DiametroMm + b.DiametroMm) / 2;
                double minimoMm = SeparacionMinimaMm(a.DiametroMm, b.DiametroMm);
                if (libreMm >= minimoMm - ToleranciaMm) continue;

                choques.Add(new Choque(
                    string.Format(Es, "{0} y {1} quedan a {2:0.0} cm libres; se piden {3:0.0} cm.",
                        Mayuscula(nombreVarilla(a.Varilla)), nombreVarilla(b.Varilla), Math.Max(0, libreMm) / 10, minimoMm / 10),
                    new[] { Marca(a), Marca(b) },
                    $"juntas:{Math.Min(a.Varilla, b.Varilla)}-{Math.Max(a.Varilla, b.Varilla)}"));
            }

        return choques;
    }

    /// <param name="Normal">Choques con la sección como es en la mayor parte del tramo.</param>
    /// <param name="Traslapo">Los que aparecen además en la zona de traslapo, con las piezas corridas.</param>
    /// <param name="HayTraslapo">Si alguna varilla lleva un traslapo con pieza corrida.</param>
    /// <param name="PiezasTraslapo">Las piezas de las varillas con traslapo, para dibujar esa zona.</param>
    public sealed record Resultado(
        IReadOnlyList<Choque> Normal, IReadOnlyList<Choque> Traslapo, bool HayTraslapo, IReadOnlyList<PiezaRedonda> PiezasTraslapo,
        int VarillasSinRevisarEnTraslapo = 0)
    {
        public static Resultado Vacio { get; } =
            new(Array.Empty<Choque>(), Array.Empty<Choque>(), false, Array.Empty<PiezaRedonda>());

        public IEnumerable<Choque> Todos => Normal.Concat(Traslapo);
    }

    /// <summary>
    /// Los choques de un tramo, con las mismas entradas que dibuja la vista
    /// previa (<c>PanelVistaPrevia.Mostrar</c>) y la misma cuenta de traslapos
    /// que el generador (<see cref="PlanTraslapos"/> +
    /// <see cref="AcomodoTraslapos.PlanearSeccion"/>), para que lo que se
    /// avisa sea lo que se genera. Sin las tres barras elegidas, vacío.
    /// </summary>
    public static Resultado DeTramo(
        ColumnaContexto columna, Tramo tramo, ConfigTramo cfgTramo, ConfigLongitudinal cfgLong,
        double recubrimiento, Func<string?, TipoBarra?> barraDe, double diametroEstriboPlantaMm)
    {
        double? Diametro(string? nombre) => string.IsNullOrEmpty(nombre) ? null : barraDe(nombre)?.DiametroMm;

        if (Diametro(cfgLong.BarraDe(PosicionBarra.Esquina)) is not double dEsquina ||
            Diametro(cfgLong.BarraDe(PosicionBarra.CaraX)) is not double dCaraX ||
            Diametro(cfgLong.BarraDe(PosicionBarra.CaraY)) is not double dCaraY)
            return Resultado.Vacio;

        double DiametroDe(PosicionBarra p) => p switch
        {
            PosicionBarra.Esquina => dEsquina,
            PosicionBarra.CaraX => dCaraX,
            _ => dCaraY,
        };

        // Lo mismo que PanelVistaPrevia: el tramo contra zapata no lleva
        // estribos, y en los nudos no van menores.
        double? dEstribo = tramo.Tipo == TipoTramo.ContraZapata ? null : Diametro(cfgTramo.TipoBarraNombre);
        double ladoX = columna.BaseX, ladoY = columna.BaseY;
        double doblezEstribo = dEstribo is double dd ? PlantaArmado.Doblez(barraDe(cfgTramo.TipoBarraNombre), dd) : 0;
        IReadOnlyList<SeccionPlanta.BarraEnPlanta> barras = SeccionPlanta.Calcular(
            ladoX, ladoY, recubrimiento, diametroEstriboPlantaMm, cfgLong, DiametroDe, doblezEstribo * 1000);
        string Nombre(int i) => NombreBarra(barras, i);

        var normal = new List<Choque>(BarrasJuntas(
            barras.Select((b, i) => new PiezaRedonda(b.X, b.Y, b.DiametroMm, i)).ToList(), Nombre));

        // Estribos menores, ubicados como los dibuja la vista previa.
        List<EstriboMenor> menores = tramo.Tipo is TipoTramo.DentroDeViga or TipoTramo.ContraZapata
            ? new List<EstriboMenor>()
            : cfgLong.MenoresParaGenerar();
        int columnas = Math.Max(0, cfgLong.BarrasPorCaraX / 2), filas = Math.Max(0, cfgLong.BarrasPorCaraY / 2);
        double insetEsq = SeccionPlanta.Inset(recubrimiento, diametroEstriboPlantaMm / 1000, barras.Max(b => b.DiametroMm) / 1000);
        double XColumna(int c) => SeccionPlanta.PosicionIntermedia(insetEsq, ladoX - insetEsq, columnas, c);
        double YFila(int f) => SeccionPlanta.PosicionIntermedia(insetEsq, ladoY - insetEsq, filas, f);

        // Las dos barras que engancha una pieza en la fila / columna k (índices en el anillo).
        (int Inicio, int Fin) IndicesDe(EjeCaja eje, int k)
        {
            List<int> cara = Enumerable.Range(0, barras.Count)
                .Where(i => eje == EjeCaja.Fila
                    ? barras[i].Posicion == PosicionBarra.CaraY && Math.Abs(barras[i].Y - YFila(k)) < 1e-6
                    : barras[i].Posicion == PosicionBarra.CaraX && Math.Abs(barras[i].X - XColumna(k)) < 1e-6)
                .OrderBy(i => eje == EjeCaja.Fila ? barras[i].X : barras[i].Y)
                .ToList();
            return (cara[0], cara[^1]);
        }

        GanchoGrapa.Pieza Pieza(int i) => new(barras[i].X, barras[i].Y, barras[i].DiametroMm / 2000, 0);
        List<PiezaRedonda> redondas = barras.Select((b, i) => new PiezaRedonda(b.X, b.Y, b.DiametroMm, i)).ToList();

        var grapas = new List<(EstriboMenor Pieza, string Nombre, TipoBarra Barra, IReadOnlyList<int> Abrazadas)>();
        foreach (EstriboMenor p in menores)
        {
            bool fueraDeRango = p.Eje == EjeCaja.Fila ? p.Hasta >= filas : p.Hasta >= columnas;
            if (fueraDeRango || PaqueteEstribos.EstaTapada(p, menores)) continue;
            if (barraDe(p.TipoBarraNombre ?? cfgTramo.TipoBarraNombre) is not { } barraPieza) continue;

            List<int> abrazadas = Enumerable.Range(p.Desde, p.Hasta - p.Desde + 1)
                .SelectMany(k => { (int a, int b) = IndicesDe(p.Eje, k); return new[] { a, b }; })
                .Distinct().ToList();
            string donde = Donde(p);
            string nombrePieza = p.Forma == FormaMenor.Cerrado ? $"el estribo cerrado de las {donde}" : $"la grapa de la {donde}";

            if (p.Forma == FormaMenor.Cerrado &&
                CerradoImposible(p, barraPieza, abrazadas.Select(Pieza), donde) is string imposible)
                normal.Add(new Choque(imposible, abrazadas.Select(i => Marca(redondas[i])).ToList(), $"cerrado:{donde}"));

            IReadOnlyList<(double X, double Y)> eje = PlantaArmado.Menor(
                p, k => { (int a, int b) = IndicesDe(p.Eje, k); return (Pieza(a), Pieza(b)); },
                (ladoX / 2, ladoY / 2), barraPieza.DiametroMm, PlantaArmado.Doblez(barraPieza, barraPieza.DiametroMm));
            if (MenorSobreBarra(eje, barraPieza.DiametroMm, redondas, abrazadas.ToHashSet(), nombrePieza, Nombre) is { } encima)
                normal.Add(encima);

            if (p.Forma != FormaMenor.Cerrado) grapas.Add((p, nombrePieza, barraPieza, abrazadas));
        }

        // La zona de traslapo: dónde quedan las piezas corridas, con la misma
        // cuenta que el generador.
        PlanTraslapos.Resultado plan = PlanTraslapos.Calcular(
            PlanTraslapos.Columna.De(columna),
            barras.Where((_, i) => i % 2 == 0).Select(b => b.DiametroMm),
            barras.Where((_, i) => i % 2 == 1).Select(b => b.DiametroMm),
            new PlanTraslapos.Opciones(cfgLong.LongitudComercialBarra, cfgLong.RedondeoVarillas, cfgLong.SolapeMayorDeLosDos));
        if (!plan.TieneSolucion) return new Resultado(normal, Array.Empty<Choque>(), false, Array.Empty<PiezaRedonda>());

        var pedidos = new List<AcomodoTraslapos.PedidoSeccion>();
        int sinRevisar = 0;
        for (int i = 0; i < barras.Count; i++)
        {
            SeccionPlanta.BarraEnPlanta b = barras[i];
            PlanTraslapos.PlanTipo tipo = plan.De(grupoA: i % 2 == 0, b.DiametroMm);
            bool unTraslapo = tipo.Traslapos.Count == 1 && tipo.Piezas.Count == 2;
            // Con más de un traslapo la varilla va como siempre (el acomodo
            // solo está medido con uno) y su zona no se revisa: se dice.
            if (tipo.Traslapos.Count > 1) sinRevisar++;

            // Las mismas normales que el anillo del generador.
            (double X, double Y) normalBarra = b.Posicion switch
            {
                PosicionBarra.Esquina => b.Y < ladoY / 2 ? (0, 1) : (1, 0),
                PosicionBarra.CaraX => (1, 0),
                _ => (0, 1),
            };

            pedidos.Add(new AcomodoTraslapos.PedidoSeccion(
                b.X, b.Y, b.DiametroMm / 2000, b.DiametroMm / 1000, b.Posicion == PosicionBarra.Esquina,
                ConGancho: columna.IdZapata is not null, Normal: normalBarra, CentroSeccion: (ladoX / 2, ladoY / 2),
                Entera: (columna.ZInferiorLongitudinal, columna.ZSuperior),
                Abajo: unTraslapo ? (tipo.Piezas[0].ZDesde, tipo.Piezas[0].ZHasta) : null,
                Arriba: unTraslapo ? (tipo.Piezas[1].ZDesde, tipo.Piezas[1].ZHasta) : null,
                Limites: (Math.Min(insetEsq, b.X), Math.Max(ladoX - insetEsq, b.X), Math.Min(insetEsq, b.Y), Math.Max(ladoY - insetEsq, b.Y))));
        }

        IReadOnlyList<AcomodoTraslapos.Pieza> ganchos = dEstribo is double de
            ? GanchosDelEstribo(
                PlantaArmado.EstriboCerrado(recubrimiento, ladoX - recubrimiento, recubrimiento, ladoY - recubrimiento,
                    de / 1000, doblezEstribo, PlantaArmado.Cola135(de)),
                de / 1000, columna.ZInferiorLongitudinal, columna.ZSuperior)
            : Array.Empty<AcomodoTraslapos.Pieza>();
        IReadOnlyList<(AcomodoTraslapos.Manera? Manera, (double X, double Y) Eje)> planeadas =
            AcomodoTraslapos.PlanearSeccion(pedidos, ganchos);

        var enTraslapo = new List<PiezaRedonda>();
        var corridas = new List<PiezaRedonda>();
        for (int i = 0; i < barras.Count; i++)
        {
            if (planeadas[i].Manera is { } manera)
            {
                List<PiezaRedonda> deEsta = manera.Piezas
                    .Select(q => new PiezaRedonda(q.X, q.Y, q.Radio * 2000, i, q.ZInf, q.ZSup)).ToList();
                enTraslapo.AddRange(deEsta);
                corridas.AddRange(deEsta);
            }
            else
            {
                enTraslapo.Add(new PiezaRedonda(barras[i].X, barras[i].Y, barras[i].DiametroMm, i,
                    columna.ZInferiorLongitudinal, columna.ZSuperior));
            }
        }

        var yaAvisados = normal.Select(c => c.Motivo).ToHashSet();
        var traslapo = BarrasJuntas(enTraslapo, Nombre)
            .Where(c => !yaAvisados.Contains(c.Motivo))
            .Select(c => c with { Motivo = "En la zona de traslapo: " + Minuscula(c.Motivo), Clave = "traslapo:" + c.Clave })
            .ToList();

        // Las dos piezas del traslapo de una barra que abraza una grapa: si no
        // entran juntas en el doblez, la grapa abraza una y choca con la otra.
        foreach ((EstriboMenor _, string nombrePieza, TipoBarra barraPieza, IReadOnlyList<int> abrazadas) in grapas)
            foreach (int i in abrazadas)
            {
                if (planeadas[i].Manera is not { } manera) continue;
                List<GanchoGrapa.Pieza> dos = manera.Piezas.Select(q => new GanchoGrapa.Pieza(q.X, q.Y, q.Radio, q.ZSup)).ToList();
                if (GanchoGrapa.Elegir(dos, PlantaArmado.Doblez(barraPieza, barraPieza.DiametroMm) / 2).Caben) continue;

                traslapo.Add(new Choque(
                    $"En la zona de traslapo: las dos piezas de {Nombre(i)} no entran juntas en el doblez de {nombrePieza}; " +
                    "abraza una y choca con la otra.",
                    dos.Select(q => (q.X, q.Y, q.Radio)).ToList(), $"doblez:{i}:{nombrePieza}"));
            }

        return new Resultado(normal, traslapo, corridas.Count > 0, corridas, sinRevisar);
    }

    /// <summary>
    /// Los ganchos de 135° del estribo principal como trocitos redondos, para
    /// que las piezas corridas no los atraviesen. El generador los mide en
    /// Revit; acá salen del mismo dibujo de la vista previa
    /// (<see cref="PlantaArmado.EstriboCerrado"/>): el último tramo de 45° de
    /// cada doblez y su cola, en las dos puntas.
    /// </summary>
    private static IReadOnlyList<AcomodoTraslapos.Pieza> GanchosDelEstribo(
        IReadOnlyList<(double X, double Y)> eje, double diametro, double zInf, double zSup)
    {
        // EstriboCerrado arranca y termina con su gancho: 6 puntos del arco de
        // 45° (pasos de 7,5°) más la punta de la cola en cada extremo.
        const int PuntosDelGancho = 8;
        double radio = diametro / 2;
        var trocitos = new List<AcomodoTraslapos.Pieza>();
        foreach (IEnumerable<(double X, double Y)> punta in new[] { eje.Take(PuntosDelGancho), eje.Skip(Math.Max(0, eje.Count - PuntosDelGancho)) })
        {
            List<(double X, double Y)> puntos = punta.ToList();
            for (int i = 1; i < puntos.Count; i++)
            {
                double largo = Math.Sqrt(Math.Pow(puntos[i].X - puntos[i - 1].X, 2) + Math.Pow(puntos[i].Y - puntos[i - 1].Y, 2));
                int pasos = Math.Max(1, (int)Math.Ceiling(largo / (radio / 2)));
                for (int k = 0; k <= pasos; k++)
                {
                    double t = (double)k / pasos;
                    trocitos.Add(new AcomodoTraslapos.Pieza(
                        puntos[i - 1].X + t * (puntos[i].X - puntos[i - 1].X), puntos[i - 1].Y + t * (puntos[i].Y - puntos[i - 1].Y),
                        radio - ToleranciaMm / 1000, zInf, zSup));
                }
            }
        }

        return trocitos;
    }

    /// <summary>"fila 2", "columnas 1 a 3": como en "Editar estribos menores".</summary>
    private static string Donde(EstriboMenor p)
    {
        string eje = p.Eje == EjeCaja.Fila ? "fila" : "columna";
        return p.Desde == p.Hasta ? $"{eje} {p.Desde + 1}" : $"{eje}s {p.Desde + 1} a {p.Hasta + 1}";
    }

    private static string Minuscula(string texto) => texto.Length == 0 ? texto : char.ToLower(texto[0], Es) + texto[1..];

    /// <summary>
    /// Una grapa o un estribo menor (su eje, de <see cref="PlantaArmado.Menor"/>,
    /// con el grosor de su barra) que se mete en planta en una longitudinal
    /// que no es de las que abraza. Las longitudinales son continuas en
    /// altura, así que todo cruce en planta es un choque. Marca esa barra; si
    /// pisa varias, la primera.
    /// </summary>
    public static Choque? MenorSobreBarra(
        IReadOnlyList<(double X, double Y)> eje, double diametroMm, IReadOnlyList<PiezaRedonda> barras,
        ISet<int> abrazadas, string nombrePieza, Func<int, string> nombreVarilla)
    {
        foreach (PiezaRedonda barra in barras)
        {
            if (abrazadas.Contains(barra.Varilla)) continue;

            double distancia = double.PositiveInfinity;
            for (int i = 1; i < eje.Count; i++)
                distancia = Math.Min(distancia, DistanciaASegmento(barra.X, barra.Y, eje[i - 1], eje[i]));

            double metidaMm = (diametroMm + barra.DiametroMm) / 2 - distancia * 1000;
            if (metidaMm <= ToleranciaMm) continue;

            return new Choque(
                string.Format(Es, "{0} pasa encima de {1}, que no abraza (se meten {2:0.0} cm).",
                    Mayuscula(nombrePieza), nombreVarilla(barra.Varilla), metidaMm / 10),
                new[] { Marca(barra) }, $"encima:{nombrePieza}:{barra.Varilla}");
        }

        return null;
    }

    /// <summary>
    /// Por qué un estribo cerrado no se puede doblar con su barra -- su lado
    /// corto, entre los ejes de su propia barra, más angosto que
    /// <see cref="EstriboMenor.AnchoMinimoCerradoMm"/> --, o null si se puede.
    /// Con la misma caja que se dibuja y que crea el generador, por fuera de
    /// las barras que abraza (#132). Antes vivía en VentanaEstribosMenores.
    /// </summary>
    /// <param name="donde">Cómo se nombra su lugar: "fila 2", "columnas 1 a 3".</param>
    public static string? CerradoImposible(EstriboMenor pieza, TipoBarra barra, IEnumerable<GanchoGrapa.Pieza> abrazadas, string donde)
    {
        if (pieza.Forma != FormaMenor.Cerrado) return null;

        double diametro = barra.DiametroMm / 1000;
        (double x1, double x2, double y1, double y2) = EstriboMenor.CajaQueAbraza(
            abrazadas, diametro, PlantaArmado.Doblez(barra, barra.DiametroMm) / 2);
        double anchoMm = (Math.Min(x2 - x1, y2 - y1) - diametro) * 1000;
        double minimoMm = EstriboMenor.AnchoMinimoCerradoMm(barra);
        if (anchoMm >= minimoMm) return null;

        return string.Format(Es,
            "El estribo cerrado de las {0} mide {1:0.0} cm de ancho (entre los ejes de su barra), y con «{2}» necesita al menos {3:0.0} cm para doblarse: " +
            "dibújalo entre barras más separadas o usa una barra más delgada.",
            donde, anchoMm / 10, barra.Nombre, minimoMm / 10);
    }

    private static double DistanciaASegmento(double px, double py, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, largo2 = dx * dx + dy * dy;
        double t = largo2 < 1e-18 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / largo2, 0, 1);
        double qx = a.X + t * dx - px, qy = a.Y + t * dy - py;
        return Math.Sqrt(qx * qx + qy * qy);
    }

    /// <summary>
    /// Cómo se nombra la barra <paramref name="indice"/> en los avisos:
    /// "la esquina inferior izquierda", o "la barra 2 de la cara inferior"
    /// (las caras de abajo y de arriba se cuentan de izquierda a derecha; las
    /// de los costados, de abajo hacia arriba; las esquinas no cuentan).
    /// </summary>
    public static string NombreBarra(IReadOnlyList<SeccionPlanta.BarraEnPlanta> barras, int indice)
    {
        double cx = (barras.Min(b => b.X) + barras.Max(b => b.X)) / 2;
        double cy = (barras.Min(b => b.Y) + barras.Max(b => b.Y)) / 2;
        SeccionPlanta.BarraEnPlanta barra = barras[indice];

        switch (barra.Posicion)
        {
            case PosicionBarra.Esquina:
                return $"la esquina {(barra.Y < cy ? "inferior" : "superior")} {(barra.X < cx ? "izquierda" : "derecha")}";
            case PosicionBarra.CaraX:
            {
                bool abajo = barra.Y < cy;
                int n = barras.Count(b => b.Posicion == PosicionBarra.CaraX && (b.Y < cy) == abajo && b.X < barra.X) + 1;
                return $"la barra {n} de la cara {(abajo ? "inferior" : "superior")}";
            }
            default:
            {
                bool izquierda = barra.X < cx;
                int n = barras.Count(b => b.Posicion == PosicionBarra.CaraY && (b.X < cx) == izquierda && b.Y < barra.Y) + 1;
                return $"la barra {n} de la cara {(izquierda ? "izquierda" : "derecha")}";
            }
        }
    }

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-CO");

    private static (double X, double Y, double Radio) Marca(PiezaRedonda p) => (p.X, p.Y, p.DiametroMm / 2000);

    private static string Mayuscula(string texto) => texto.Length == 0 ? texto : char.ToUpper(texto[0], Es) + texto[1..];
}
