using System.Text.RegularExpressions;

namespace CQEING.Gestor.Core;

/// <summary>Etiqueta de release del monorepo: &lt;id del add-in&gt;-vX.Y.Z[-rc.N]. El gestor se publica con el id «gestor».</summary>
public sealed record EtiquetaRelease(string Id, VersionAddin Version)
{
    public const string IdGestor = "gestor";

    private static readonly Regex Patron = new(@"^(?<id>[a-z0-9]+(?:-[a-z0-9]+)*)-v(?<version>[0-9.]+(?:-rc\.[0-9]+)?)$", RegexOptions.CultureInvariant);

    public static EtiquetaRelease? Interpretar(string? etiqueta)
    {
        Match m = Patron.Match(etiqueta ?? string.Empty);
        if (!m.Success) return null;
        VersionAddin? version = VersionAddin.Interpretar(m.Groups["version"].Value);
        return version is null ? null : new EtiquetaRelease(m.Groups["id"].Value, version);
    }

    public override string ToString() => $"{Id}-v{Version}";
}
