namespace CQEING.Gestor.Core.Tests.Apoyo;

/// <summary>Revit simulados: se cierran al pedirlo salvo los que «el usuario cancela».</summary>
internal sealed class ProcesosFalsos : IProcesosRevit
{
    public List<RevitAbierto> Lista { get; } = [];
    public HashSet<int> Cancelan { get; } = [];
    public List<int> CierresPedidos { get; } = [];
    public List<(string Ejecutable, string? Modelo)> Abiertas { get; } = [];
    public bool AbrirFalla { get; set; }

    public RevitAbierto Agregar(int id, string anio)
    {
        var revit = new RevitAbierto(id, anio, $@"C:\Program Files\Autodesk\Revit {anio}\Revit.exe");
        Lista.Add(revit);
        return revit;
    }

    public IReadOnlyList<RevitAbierto> Abiertos() => Lista.ToList();

    public void PedirCierre(int idProceso) => CierresPedidos.Add(idProceso);

    public Task<bool> EsperarCierreAsync(int idProceso, TimeSpan espera, CancellationToken ct)
    {
        if (Cancelan.Contains(idProceso)) return Task.FromResult(false);
        Lista.RemoveAll(r => r.IdProceso == idProceso);
        return Task.FromResult(true);
    }

    public bool Abrir(string ejecutable, string? modelo)
    {
        if (AbrirFalla) return false;
        Abiertas.Add((ejecutable, modelo));
        return true;
    }
}
