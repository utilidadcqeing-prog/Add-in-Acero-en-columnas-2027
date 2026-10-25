using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using CQEING.Gestor.Core;

namespace CQEING.GestorRevit;

/// <summary>
/// Anota qué modelo tiene activo este Revit (<see cref="SesionRevit"/>), para que el gestor lo vuelva a abrir cuando
/// reinicia Revit después de una actualización. Plan: docs/planes/2026-10-25-gestor-reinicia-revit.md, decisión 4.
/// </summary>
internal sealed class ModeloActivo
{
    private readonly RutasGestor _rutas = RutasGestor.DelUsuario();
    private readonly int _idProceso = Environment.ProcessId;
    private string? _anotado;

    public void Empezar(UIControlledApplication app)
    {
        app.ViewActivated += AlActivarVista;
        app.ControlledApplication.DocumentClosing += AlCerrarModelo;
    }

    public void Terminar(UIControlledApplication app)
    {
        app.ViewActivated -= AlActivarVista;
        app.ControlledApplication.DocumentClosing -= AlCerrarModelo;
        Anotar(null);
    }

    private void AlActivarVista(object? sender, ViewActivatedEventArgs e) => Anotar(SePuedeReabrir(e.Document));

    private void AlCerrarModelo(object? sender, DocumentClosingEventArgs e)
    {
        if (_anotado is not null && string.Equals(e.Document.PathName, _anotado, StringComparison.OrdinalIgnoreCase))
            Anotar(null);
    }

    /// <summary>Solo un archivo normal que existe en disco y sin trabajo compartido; si no, el gestor reabre solo Revit.</summary>
    private static string? SePuedeReabrir(Document documento) =>
        !documento.IsWorkshared && !string.IsNullOrEmpty(documento.PathName) && File.Exists(documento.PathName)
            ? documento.PathName
            : null;

    private void Anotar(string? modelo)
    {
        if (modelo == _anotado) return;
        _anotado = modelo;
        SesionRevit.Anotar(_rutas, _idProceso, modelo);
    }
}
