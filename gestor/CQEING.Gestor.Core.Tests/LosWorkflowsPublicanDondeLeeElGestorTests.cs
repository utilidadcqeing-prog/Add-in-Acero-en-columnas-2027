namespace CQEING.Gestor.Core.Tests;

/// <summary>
/// Vigila que los workflows publiquen en el repo que lee el gestor (issue #197): si <see cref="Repositorio.Nombre"/> y
/// el workflow se separan, el gestor deja de ver las versiones nuevas sin que nada falle al publicar.
/// </summary>
public class LosWorkflowsPublicanDondeLeeElGestorTests
{
    [Theory]
    [InlineData("publicar.yml")]
    [InlineData("publicar-gestor.yml")]
    public void El_workflow_publica_en_el_repo_del_gestor_con_su_token(string workflow)
    {
        string texto = File.ReadAllText(Path.Combine(Raiz(), ".github", "workflows", workflow));

        Assert.Contains($"REPO_RELEASES: {Repositorio.Nombre}", texto);
        Assert.Contains("GH_TOKEN: ${{ secrets.TOKEN_RELEASES }}", texto);
        Assert.Contains("--repo $env:REPO_RELEASES", texto);
    }

    private static string Raiz()
    {
        string? raiz = AppContext.BaseDirectory;
        while (raiz is not null && !File.Exists(Path.Combine(raiz, "CQEING-Addins.sln")))
            raiz = Path.GetDirectoryName(raiz);
        Assert.True(raiz is not null, "No se encontró la raíz del repositorio (CQEING-Addins.sln) desde la carpeta de las pruebas.");
        return raiz!;
    }
}
