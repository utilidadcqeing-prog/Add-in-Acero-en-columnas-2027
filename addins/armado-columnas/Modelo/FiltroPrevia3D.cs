namespace practicacq.Modelo;

using System.Collections.Generic;
using System.Linq;

/// <summary>Qué partes de las barras se pintan.</summary>
public enum ModoPartes
{
    Todo,
    SoloGanchos,
    SoloTraslapos,
}

/// <summary>
/// Los filtros de la vista previa 3D que pidió Carlos (#123): qué barras se
/// pintan. Dentro de un grupo las opciones se suman (#4 o #6); entre grupos
/// se cruzan (#6 y tipo A → solo las #6 tipo A). Un grupo que no aplica a
/// una barra no la oculta: A/B y posición son solo de las longitudinales, y
/// la zona, solo de los estribos (docs/planes/2026-10-21-vista-previa-3d-columnas-123.md,
/// decisión 7). Sin Revit: lo arma la ventana y lo aplica el manejador.
/// </summary>
public sealed class FiltroPrevia3D
{
    public HashSet<ClasePieza> Clases { get; set; } = new() { ClasePieza.Longitudinal, ClasePieza.Estribo, ClasePieza.EstriboMenor };

    public HashSet<GrupoTraslapo> Grupos { get; set; } = new() { GrupoTraslapo.A, GrupoTraslapo.B };

    public HashSet<PosicionBarra> Posiciones { get; set; } = new() { PosicionBarra.Esquina, PosicionBarra.CaraX, PosicionBarra.CaraY };

    public HashSet<ZonaEstribo> Zonas { get; set; } = new() { ZonaEstribo.Confinada, ZonaEstribo.NoConfinada };

    /// <summary>
    /// Números de varilla que NO se pintan (-1 = diámetro que no es de la
    /// tabla). Al revés que los demás grupos a propósito: un diámetro que la
    /// ventana no conocía se ve, en vez de desaparecer sin aviso.
    /// </summary>
    public HashSet<int> NumerosOcultos { get; set; } = new();

    public ModoPartes Partes { get; set; } = ModoPartes.Todo;

    /// <summary>Si no es null, todo se recorta a esa altura (el tramo activo), en metros.</summary>
    public (double ZDesde, double ZHasta)? SoloAltura { get; set; }

    public FiltroPrevia3D Copiar() => new()
    {
        Clases = new(Clases), Grupos = new(Grupos), Posiciones = new(Posiciones), Zonas = new(Zonas),
        NumerosOcultos = new(NumerosOcultos), Partes = Partes, SoloAltura = SoloAltura,
    };

    public static int NumeroDe(double diametroMm) => ColoresVarilla.NumeroDe(diametroMm) ?? -1;

    public bool Muestra(BarraPrevia b) =>
        Clases.Contains(b.Clase) &&
        !NumerosOcultos.Contains(NumeroDe(b.DiametroMm)) &&
        (b.Grupo is not GrupoTraslapo g || Grupos.Contains(g)) &&
        (b.Posicion is not PosicionBarra p || Posiciones.Contains(p)) &&
        (b.Zona is not ZonaEstribo z || Zonas.Contains(z));

    /// <summary>Los pedazos a pintar, cada uno con el diámetro de su barra.</summary>
    public IEnumerable<(IReadOnlyList<Punto3> Puntos, double DiametroMm)> Aplicar(IEnumerable<BarraPrevia> barras)
    {
        foreach (BarraPrevia barra in barras.Where(Muestra))
        {
            foreach (PedazoBarra pedazo in barra.Pedazos)
            {
                if (Partes == ModoPartes.SoloGanchos && pedazo.Parte != ParteBarra.Gancho) continue;
                if (Partes == ModoPartes.SoloTraslapos && pedazo.Parte != ParteBarra.Traslapo) continue;

                if (SoloAltura is not { } rango)
                {
                    yield return (pedazo.Puntos, barra.DiametroMm);
                    continue;
                }

                foreach (List<Punto3> dentro in Polilinea.PartirPorAltura(pedazo.Puntos, rango.ZDesde, rango.ZHasta).Dentro)
                    yield return (dentro, barra.DiametroMm);
            }
        }
    }

    // ------------------------------------------------------------ atajos

    public static FiltroPrevia3D Todo() => new();

    public static FiltroPrevia3D SoloLongitudinales() => new() { Clases = new() { ClasePieza.Longitudinal } };

    public static FiltroPrevia3D SoloEstribos() => new() { Clases = new() { ClasePieza.Estribo, ClasePieza.EstriboMenor } };

    public static FiltroPrevia3D SoloGanchos() => new() { Partes = ModoPartes.SoloGanchos };

    /// <summary>Los traslapos son de las longitudinales: el resto no se pinta.</summary>
    public static FiltroPrevia3D SoloTraslapos() => new()
    {
        Clases = new() { ClasePieza.Longitudinal }, Partes = ModoPartes.SoloTraslapos,
    };
}
