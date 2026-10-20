namespace practicacq.Modelo;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Alturas de los estribos de un tramo, sin Revit: la misma cuenta que hace
/// <c>GeneradorArmado</c> (5 cm al borde del paquete, uno o tres grupos) más
/// la de <c>SetLayoutAsMaximumSpacing</c> (n = ⌈L/s⌉ + 1 posiciones parejas).
/// La usa la vista previa para mostrar lo mismo que se va a generar; el
/// generador la compara con lo que puso Revit y avisa si difiere. Ver
/// <c>docs/planes/2026-10-20-diseno-vista-previa-y-colores.md</c>.
/// </summary>
public static class RepartoEstribos
{
    /// <summary>Del borde del tramo al borde del paquete, en cada punta.</summary>
    public const double SeparacionExtremosMetros = 0.05;

    private const double Tolerancia = 1e-6;

    /// <param name="Nombre">El mismo rótulo que usa <c>GeneradorArmado.CrearGrupo</c>.</param>
    /// <param name="Cantidad">Estribos que pone este grupo (sin el del borde que pone el vecino).</param>
    public sealed record Zona(string Nombre, double ZDesde, double ZHasta, double Espaciado, int Cantidad);

    /// <param name="Alturas">Eje del estribo principal de cada posición, de abajo hacia arriba, en metros.</param>
    /// <param name="Aviso">El mismo aviso que daría el generador para este tramo, o null.</param>
    public sealed record Resultado(IReadOnlyList<double> Alturas, IReadOnlyList<Zona> Zonas, string? Aviso);

    public static Resultado Calcular(double zInferior, double zSuperior, ConfigTramo cfg, PaqueteEstribos.Paquete paquete)
    {
        (double zInf, double zSup) = Limites(zInferior, zSuperior, paquete);
        double altura = zSup - zInf;

        if (altura <= Tolerancia)
            return new Resultado(Array.Empty<double>(), Array.Empty<Zona>(),
                $"no deja altura libre para los {SeparacionExtremosMetros * 100:0} cm de extremo en ambos bordes; se omite.");

        var alturas = new List<double>();
        var zonas = new List<Zona>();

        void Grupo(string nombre, double desde, double largo, double espaciado, bool incluirUltimo = true)
        {
            int cantidad = CantidadEnGrupo(largo, espaciado, incluirUltimo);
            if (cantidad == 0) return;

            double separacion = largo / (Posiciones(largo, espaciado) - 1);
            for (int i = 0; i < cantidad; i++) alturas.Add(desde + i * separacion);
            zonas.Add(new Zona(nombre, desde, desde + largo, espaciado, cantidad));
        }

        if (cfg.Modo == ModoEstribo.Uniforme)
        {
            Grupo("uniforme", zInf, altura, cfg.EspaciadoUniforme);
            return new Resultado(alturas, zonas, null);
        }

        double l1 = cfg.LongitudConfinamiento;
        if (l1 * 2 >= altura - Tolerancia)
        {
            Grupo("confinado (sin zona central)", zInf, altura, cfg.EspaciadoConfinado);
            return new Resultado(alturas, zonas, "la longitud de confinamiento ×2 no deja zona central; se armó confinado de punta a punta.");
        }

        double l2 = altura - 2 * l1;
        Grupo("confinado inferior", zInf, l1, cfg.EspaciadoConfinado, incluirUltimo: false);
        Grupo("zona central", zInf + l1, l2, cfg.EspaciadoNoConfinado, incluirUltimo: false);
        Grupo("confinado superior", zInf + l1 + l2, l1, cfg.EspaciadoConfinado);
        return new Resultado(alturas, zonas, null);
    }

    /// <summary>
    /// Eje del estribo principal de la primera y la última posición del tramo,
    /// en metros: 5 cm al borde del paquete, y el punto medio del paquete
    /// corrido lo que el principal se aleja de él (sección 2 del diseño de
    /// estribos menores del 20-oct). Sin menores da 5 cm más medio diámetro.
    /// La usan este reparto y <c>GeneradorArmado.GenerarTramo</c>.
    /// </summary>
    public static (double ZInf, double ZSup) Limites(double zInferior, double zSuperior, PaqueteEstribos.Paquete paquete) =>
        (zInferior + SeparacionExtremosMetros + paquete.Debajo + paquete.DesplazamientoPrincipal,
         zSuperior - SeparacionExtremosMetros - paquete.Encima + paquete.DesplazamientoPrincipal);

    /// <summary>
    /// Estribos que pone un grupo de largo <paramref name="largo"/> con
    /// espaciado máximo <paramref name="espaciado"/> (mismas unidades los dos):
    /// lo que se supone que hace <c>SetLayoutAsMaximumSpacing</c>. El generador
    /// lo compara con lo que puso Revit.
    /// </summary>
    public static int CantidadEnGrupo(double largo, double espaciado, bool incluirUltimo)
    {
        if (largo <= Tolerancia || espaciado <= Tolerancia) return 0;
        int posiciones = Posiciones(largo, espaciado);
        return incluirUltimo ? posiciones : posiciones - 1;
    }

    private static int Posiciones(double largo, double espaciado) =>
        (int)Math.Ceiling(largo / espaciado - 1e-9) + 1;
}
