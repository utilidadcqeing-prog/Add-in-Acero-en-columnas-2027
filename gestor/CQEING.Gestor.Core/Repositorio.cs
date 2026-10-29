namespace CQEING.Gestor.Core;

public static class Repositorio
{
    /// <summary>
    /// Repo público de descargas: solo releases, sin código, para que el repo del código pueda ser privado (issue #197).
    /// Los workflows publican aquí (<c>REPO_RELEASES</c>); una prueba vigila que coincidan.
    /// </summary>
    public const string Nombre = "utilidadcqeing-prog/CQEING-releases";

    public const string ArchivoManifiesto = "manifest.json";
}
