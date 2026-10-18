using System.Globalization;
using System.Windows.Input;
using CQEING.Gestor.Core;

namespace CQEING.Gestor;

/// <summary>Una fila del menú «⋯» de una tarjeta: una versión publicada del add-in en el año de Revit elegido.</summary>
public sealed class OpcionVersion
{
    private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-ES");

    public OpcionVersion(VersionPublicada version, EstadoAddin estado, string anio, bool sinConexion, ICommand usar)
    {
        Publicada = version;
        Usar = usar;
        Version = version.Version.ToString();
        Fecha = version.Manifiesto.PublicadoEn.ToLocalTime().ToString("d MMM yyyy", Espanol);

        VersionAddin? instalada = VersionAddin.Interpretar(estado.Instalada(anio));
        EnUso = instalada is not null && version.Version.CompareTo(instalada) == 0;
        EnEsteEquipo = estado.EstaEnDisco(anio, Version);
        bool tienePaquete = version.Manifiesto.PaqueteParaRevit(anio) is not null;
        PuedeUsar = !EnUso && tienePaquete && (EnEsteEquipo || !sinConexion);

        TextoAccion = EnUso ? "En uso"
            : !tienePaquete ? $"No está para Revit {anio}"
            : !PuedeUsar ? "Necesita conexión"
            : instalada is not null && version.Version.CompareTo(instalada) < 0 ? "Volver a esta"
            : "Instalar esta";
    }

    public VersionPublicada Publicada { get; }
    public ICommand Usar { get; }
    public string Version { get; }
    public string Fecha { get; }
    public bool EnUso { get; }
    public bool EnEsteEquipo { get; }
    public bool PuedeUsar { get; }
    public string TextoAccion { get; }
    public string NombreAccesible => $"Versión {Version}, {TextoAccion}";
}
