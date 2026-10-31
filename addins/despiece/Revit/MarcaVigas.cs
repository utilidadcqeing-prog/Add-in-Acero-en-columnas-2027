using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;

namespace CQEING.Despiece.Revit;

/// <summary>Lee la marca invisible que el add-in de vigas deja en cada barra (<c>MarcaAcero</c>): S1 e I1 son corridas; BS1 y BI1, bastones.</summary>
internal static class MarcaVigas
{
    private static readonly Guid IdEsquema = new("4f6c2a91-8d3e-4b57-a0e2-7c19d5b8f346");
    private const string CampoPieza = "Pieza";

    /// <summary>Una barra sin marca, modelada a mano, no se da por bastón.</summary>
    public static bool EsBaston(Rebar barra)
    {
        if (Schema.Lookup(IdEsquema) is not { } esquema)
            return false;
        Entity entidad = barra.GetEntity(esquema);
        return entidad.IsValid() && entidad.Get<string>(CampoPieza).StartsWith('B');
    }
}
