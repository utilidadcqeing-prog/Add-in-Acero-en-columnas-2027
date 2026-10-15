using System.Globalization;
using System.Text.RegularExpressions;

namespace CQEING.Gestor.Core;

/// <summary>Versión X.Y.Z de un add-in, con sufijo opcional -rc.N (prerelease, anterior a X.Y.Z).</summary>
public sealed record VersionAddin(int Mayor, int Menor, int Parche, int? Rc) : IComparable<VersionAddin>
{
    private static readonly Regex Patron = new(@"^([0-9]{1,9})\.([0-9]{1,9})\.([0-9]{1,9})(?:-rc\.([0-9]{1,9}))?$", RegexOptions.CultureInvariant);

    public bool EsEstable => Rc is null;

    public static VersionAddin? Interpretar(string? texto)
    {
        Match m = Patron.Match(texto ?? string.Empty);
        if (!m.Success) return null;

        static int Numero(Group g) => int.Parse(g.Value, CultureInfo.InvariantCulture);
        return new VersionAddin(Numero(m.Groups[1]), Numero(m.Groups[2]), Numero(m.Groups[3]), m.Groups[4].Success ? Numero(m.Groups[4]) : null);
    }

    public int CompareTo(VersionAddin? otra)
    {
        if (otra is null) return 1;
        int c = Mayor.CompareTo(otra.Mayor);
        if (c == 0) c = Menor.CompareTo(otra.Menor);
        if (c == 0) c = Parche.CompareTo(otra.Parche);
        if (c != 0) return c;
        return (Rc, otra.Rc) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            var (a, b) => a.Value.CompareTo(b.Value),
        };
    }

    public override string ToString() => Rc is null ? $"{Mayor}.{Menor}.{Parche}" : $"{Mayor}.{Menor}.{Parche}-rc.{Rc}";
}
