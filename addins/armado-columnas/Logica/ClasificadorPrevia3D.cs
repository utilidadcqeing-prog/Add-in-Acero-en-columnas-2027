namespace practicacq.Logica;

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using practicacq.Modelo;

/// <summary>
/// Pasa lo que leyó <see cref="SimuladorArmado"/> a <see cref="BarraPrevia"/>:
/// qué pieza es cada barra, y para los filtros, su grupo A/B, su posición, su
/// zona y qué pedazos son gancho o traslapo
/// (docs/planes/2026-10-21-vista-previa-3d-columnas-123.md, paso 3 y decisión 7).
/// Todo sale de la forma real de las barras, no de volver a hacer las cuentas
/// del generador.
/// </summary>
internal static class ClasificadorPrevia3D
{
    /// <summary>1 mm, en metros: holgura para comparar bordes.</summary>
    private const double Holgura = 0.001;

    /// <summary>Holgura para ubicar un estribo menor en la zona de su principal, en metros.</summary>
    private const double HolguraZona = 0.03;

    /// <summary>Una barra leída, en metros, antes de clasificarla.</summary>
    private sealed class Pieza
    {
        public required RebarStyle Estilo { get; init; }
        public required double DiametroMm { get; init; }
        public required List<PedazoBarra> Pedazos { get; set; }

        public IEnumerable<Punto3> Rectos => Pedazos.Where(p => p.Parte != ParteBarra.Gancho).SelectMany(p => p.Puntos);
        public IEnumerable<Punto3> Todos => Pedazos.SelectMany(p => p.Puntos);
    }

    public static List<BarraPrevia> Clasificar(SimuladorArmado.Resultado simulacion, ColumnaPlan plan)
    {
        List<Pieza> piezas = simulacion.Barras
            .SelectMany(r => r.Posiciones.Select(p => new Pieza
            {
                Estilo = r.Estilo,
                DiametroMm = r.DiametroMm,
                Pedazos = p.Curvas
                    .Select(c => new PedazoBarra(c.Puntos.Select(Metros).ToList(), c.EsGancho ? ParteBarra.Gancho : ParteBarra.Recto))
                    .ToList(),
            }))
            .Where(p => p.Rectos.Any())
            .ToList();

        List<Pieza> longitudinales = piezas.Where(EsVertical).ToList();
        List<Pieza> horizontales = piezas.Except(longitudinales).ToList();

        var barras = new List<BarraPrevia>();
        barras.AddRange(ClasificarLongitudinales(longitudinales));
        barras.AddRange(ClasificarTransversales(horizontales, longitudinales, plan, simulacion.Generacion));
        return barras;
    }

    private static Punto3 Metros(XYZ p) => new(GeometriaSeccion.Metros(p.X), GeometriaSeccion.Metros(p.Y), GeometriaSeccion.Metros(p.Z));

    private static bool EsVertical(Pieza p)
    {
        List<Punto3> puntos = p.Rectos.ToList();
        double alto = puntos.Max(q => q.Z) - puntos.Min(q => q.Z);
        double ancho = Math.Max(puntos.Max(q => q.X) - puntos.Min(q => q.X), puntos.Max(q => q.Y) - puntos.Min(q => q.Y));
        return alto > ancho;
    }

    private static (double X, double Y) Planta(Pieza p)
    {
        List<Punto3> puntos = p.Rectos.ToList();
        return (puntos.Average(q => q.X), puntos.Average(q => q.Y));
    }

    // ------------------------------------------------------- longitudinales

    /// <summary>
    /// Agrupa las piezas por posición (en un traslapo la pieza corrida queda
    /// un diámetro al lado, así que se agrupan a menos de 1,5 diámetros) y
    /// recorre las posiciones en el orden del anillo de
    /// <c>GeneradorLongitudinal</c>: la esquina de abajo a la izquierda y de
    /// ahí en sentido antihorario. Las pares son A y las impares B.
    /// </summary>
    private static IEnumerable<BarraPrevia> ClasificarLongitudinales(List<Pieza> piezas)
    {
        if (piezas.Count == 0) yield break;

        var grupos = new List<(double X, double Y, List<Pieza> Piezas)>();
        foreach (Pieza pieza in piezas)
        {
            (double x, double y) = Planta(pieza);
            double radio = 1.5 * pieza.DiametroMm / 1000;
            int i = grupos.FindIndex(g => Math.Sqrt((g.X - x) * (g.X - x) + (g.Y - y) * (g.Y - y)) < radio);
            if (i < 0)
            {
                grupos.Add((x, y, new List<Pieza> { pieza }));
                continue;
            }

            // El centro del grupo queda en el promedio de sus piezas.
            List<Pieza> miembros = grupos[i].Piezas;
            miembros.Add(pieza);
            grupos[i] = (miembros.Average(m => Planta(m).X), miembros.Average(m => Planta(m).Y), miembros);
        }

        double xMin = grupos.Min(g => g.X), xMax = grupos.Max(g => g.X);
        double yMin = grupos.Min(g => g.Y), yMax = grupos.Max(g => g.Y);
        double cx = (xMin + xMax) / 2, cy = (yMin + yMax) / 2;
        double inicio = Math.Atan2(yMin - cy, xMin - cx);
        double Angulo(double x, double y)
        {
            double a = Math.Atan2(y - cy, x - cx) - inicio;
            while (a < -1e-9) a += 2 * Math.PI;
            return a;
        }

        // Cada barra se separa de su cara según su diámetro: las de esquina y
        // las de cara no caen exactamente en la misma línea.
        double tolerancia = piezas.Max(p => p.DiametroMm) / 1000 + Holgura;
        bool EnX(double x) => Math.Abs(x - xMin) < tolerancia || Math.Abs(x - xMax) < tolerancia;
        bool EnY(double y) => Math.Abs(y - yMin) < tolerancia || Math.Abs(y - yMax) < tolerancia;

        List<(double X, double Y, List<Pieza> Piezas)> anillo = grupos.OrderBy(g => Angulo(g.X, g.Y)).ToList();
        for (int indice = 0; indice < anillo.Count; indice++)
        {
            (double x, double y, List<Pieza> miembros) = anillo[indice];
            PosicionBarra posicion = EnX(x) && EnY(y) ? PosicionBarra.Esquina : EnY(y) ? PosicionBarra.CaraX : PosicionBarra.CaraY;
            GrupoTraslapo grupo = indice % 2 == 0 ? GrupoTraslapo.A : GrupoTraslapo.B;

            foreach (Pieza pieza in MarcarTraslapos(miembros))
                yield return new BarraPrevia(ClasePieza.Longitudinal, pieza.DiametroMm, pieza.Pedazos, grupo, posicion);
        }
    }

    /// <summary>Lo que se pisan dos piezas seguidas de la misma posición queda como traslapo en las dos.</summary>
    private static List<Pieza> MarcarTraslapos(List<Pieza> miembros)
    {
        List<Pieza> ordenadas = miembros.OrderBy(p => Polilinea.ZMin(p.Rectos)).ToList();
        var solapes = new List<(double Desde, double Hasta)>();
        for (int i = 0; i + 1 < ordenadas.Count; i++)
        {
            double desde = Polilinea.ZMin(ordenadas[i + 1].Rectos), hasta = Polilinea.ZMax(ordenadas[i].Rectos);
            if (hasta - desde > Holgura) solapes.Add((desde, hasta));
        }

        foreach (Pieza pieza in ordenadas)
        {
            foreach ((double desde, double hasta) in solapes)
            {
                var nuevos = new List<PedazoBarra>();
                foreach (PedazoBarra pedazo in pieza.Pedazos)
                {
                    if (pedazo.Parte != ParteBarra.Recto)
                    {
                        nuevos.Add(pedazo);
                        continue;
                    }

                    (List<List<Punto3>> dentro, List<List<Punto3>> fuera) = Polilinea.PartirPorAltura(pedazo.Puntos, desde, hasta);
                    nuevos.AddRange(dentro.Select(d => new PedazoBarra(d, ParteBarra.Traslapo)));
                    nuevos.AddRange(fuera.Select(f => new PedazoBarra(f, ParteBarra.Recto)));
                }

                pieza.Pedazos = nuevos;
            }
        }

        return ordenadas;
    }

    // ------------------------------------------------------ transversales

    private static IEnumerable<BarraPrevia> ClasificarTransversales(
        List<Pieza> piezas, List<Pieza> longitudinales, ColumnaPlan plan, ResultadoArmado generacion)
    {
        if (piezas.Count == 0) yield break;

        // El principal rodea a todas las longitudinales. Sin longitudinales
        // (traslapos sin solución, por ejemplo), es el estribo más grande.
        Func<Pieza, bool> esPrincipal;
        if (longitudinales.Count > 0)
        {
            List<(double X, double Y)> plantas = longitudinales.Select(Planta).ToList();
            double lxMin = plantas.Min(p => p.X), lxMax = plantas.Max(p => p.X);
            double lyMin = plantas.Min(p => p.Y), lyMax = plantas.Max(p => p.Y);
            esPrincipal = p =>
            {
                List<Punto3> q = p.Rectos.ToList();
                return p.Estilo == RebarStyle.StirrupTie &&
                       q.Min(r => r.X) <= lxMin + Holgura && q.Max(r => r.X) >= lxMax - Holgura &&
                       q.Min(r => r.Y) <= lyMin + Holgura && q.Max(r => r.Y) >= lyMax - Holgura;
            };
        }
        else
        {
            double Area(Pieza p)
            {
                List<Punto3> q = p.Rectos.ToList();
                return (q.Max(r => r.X) - q.Min(r => r.X)) * (q.Max(r => r.Y) - q.Min(r => r.Y));
            }

            double mayor = piezas.Where(p => p.Estilo == RebarStyle.StirrupTie).Select(Area).DefaultIfEmpty(0).Max();
            esPrincipal = p => p.Estilo == RebarStyle.StirrupTie && Area(p) >= 0.9 * mayor;
        }

        foreach (Pieza pieza in piezas)
        {
            double z = pieza.Rectos.Average(p => p.Z);
            yield return new BarraPrevia(
                esPrincipal(pieza) ? ClasePieza.Estribo : ClasePieza.EstriboMenor,
                pieza.DiametroMm, pieza.Pedazos, Zona: Zona(z, pieza.DiametroMm, plan, generacion));
        }
    }

    /// <summary>
    /// La zona del reparto (<see cref="RepartoEstribos"/>, la misma cuenta que
    /// usa el generador) en la que cae la altura <paramref name="z"/>.
    /// </summary>
    private static ZonaEstribo Zona(double z, double diametroMm, ColumnaPlan plan, ResultadoArmado generacion)
    {
        if (plan.Contexto is not ColumnaContexto contexto) return ZonaEstribo.NoConfinada;

        Tramo? tramo = contexto.Tramos.FirstOrDefault(t => z >= t.ZInferior - Holgura && z <= t.ZSuperior + Holgura);
        if (tramo is null || tramo.Indice >= plan.Tramos.Count || plan.Tramos[tramo.Indice] is not ConfigTramo cfg ||
            cfg.Modo == ModoEstribo.Uniforme)
            return ZonaEstribo.NoConfinada;

        PaqueteEstribos.Paquete paquete =
            tramo.IdElementoHost is string host && generacion.PaquetePorTramo.TryGetValue((host, tramo.Indice), out PaqueteEstribos.Paquete? p)
                ? p
                : PaqueteEstribos.Calcular(Array.Empty<EstriboMenor>(), diametroMm, _ => diametroMm);

        RepartoEstribos.Zona? zona = RepartoEstribos.Calcular(tramo.ZInferior, tramo.ZSuperior, cfg, paquete).Zonas
            .FirstOrDefault(zn => z >= zn.ZDesde - HolguraZona && z <= zn.ZHasta + HolguraZona);
        return zona is not null && zona.Nombre.StartsWith("confinado", StringComparison.Ordinal)
            ? ZonaEstribo.Confinada
            : ZonaEstribo.NoConfinada;
    }
}
