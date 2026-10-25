using CQEING.Gestor.Core;

namespace CQEING.Gestor.Tests.Apoyo;

/// <summary>Revit simulados: se cierran al pedirlo salvo los que «el usuario cancela».</summary>
internal sealed class ProcesosFalsos : IProcesosRevit
{
    public List<RevitAbierto> Lista { get; } = [];
    public HashSet<int> Cancelan { get; } = [];
    public List<int> CierresPedidos { get; } = [];
    public List<(string Ejecutable, string? Modelo)> Abiertas { get; } = [];

    public void Agregar(int id, string anio) => Lista.Add(new RevitAbierto(id, anio, $@"C:\Program Files\Autodesk\Revit {anio}\Revit.exe"));

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
        Abiertas.Add((ejecutable, modelo));
        return true;
    }
}
