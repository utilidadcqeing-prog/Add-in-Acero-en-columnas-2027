using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;

namespace practicacq.Imagenes.Revit;

/// <summary>
/// Id del modelo de Revit con el que la API vincula un presupuesto (<c>revit_model_id</c>).
///
/// Copia exacta de la lógica de <c>Services/RevitModelIdentifier.cs</c> del add-in de cantidades:
/// tiene que dar el MISMO texto, porque es el add-in de cantidades el que hace el vínculo y este solo
/// lo compara. Si allá cambia la fórmula, hay que cambiarla aquí también.
/// </summary>
public static class IdentificadorModelo
{
    public static string Obtener(Document doc)
    {
        string baseId = string.Empty;
        string rutaOTitulo = string.Empty;
        bool colaborativoPorArchivo = false;

        if (doc.IsWorkshared)
        {
            try
            {
                if (doc.WorksharingCentralGUID != Guid.Empty)
                    baseId = "CENTRAL_" + doc.WorksharingCentralGUID;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                colaborativoPorArchivo = true;
            }

            try
            {
                ModelPath? central = doc.GetWorksharingCentralModelPath();
                if (central is not null)
                {
                    rutaOTitulo = colaborativoPorArchivo && doc.ProjectInformation is not null
                        ? doc.ProjectInformation.UniqueId
                        : ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
                }
            }
            catch
            {
                // Igual que en cantidades: si no se puede leer la ruta central se usa la del archivo.
            }
        }

        if (string.IsNullOrEmpty(baseId))
            baseId = doc.ProjectInformation is not null ? "PROJECT_" + doc.ProjectInformation.UniqueId : "FALLBACK";

        if (string.IsNullOrEmpty(rutaOTitulo))
            rutaOTitulo = string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName;

        string idFinal = baseId + "_" + Hash(rutaOTitulo);

        if (doc.IsWorkshared)
        {
            string instancia = Hash(string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName);
            return string.IsNullOrEmpty(instancia) ? idFinal : $"{idFinal}:{instancia}";
        }

        return idFinal;
    }

    private static string Hash(string texto)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(texto));
        return Convert.ToHexString(hash)[..16];
    }
}
