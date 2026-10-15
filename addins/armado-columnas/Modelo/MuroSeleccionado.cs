namespace practicacq.Modelo;

using Autodesk.Revit.DB;

public class MuroSeleccionado
{
    public MuroSeleccionado(Wall muro, Document doc)
    {
        Muro = muro;
        Id = muro.Id.Value;
        Nivel = doc.GetElement(muro.LevelId) is Level nivel ? nivel.Name : "(sin nivel)";
        Tipo = muro.WallType?.Name ?? "(sin tipo)";
    }

    public Wall Muro { get; }
    public long Id { get; }
    public string Nivel { get; }
    public string Tipo { get; }
}
