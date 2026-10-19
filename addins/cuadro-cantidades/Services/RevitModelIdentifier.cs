using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;
using System.Security.Cryptography;
using System.Text;

namespace CQEING_CuadroCantidades.Services
{
    /// <summary>
    /// Proveedor de identificación única persistente para modelos de Revit.
    /// </summary>
    public static class RevitModelIdentifier
    {
        private static string GetInstanceGuid(Document doc)
        {
            string currentPath = string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName;
            return GetDeterministicHash(currentPath);
        }

        public static string BuildRevitModelId(string centralGuid, string instanceGuid)
        {
            if (string.IsNullOrEmpty(instanceGuid)) return centralGuid;
            return $"{centralGuid}:{instanceGuid}";
        }

        public static bool IsSameOrCompatibleModelId(string linkedModelId, string activeModelId)
        {
            if (string.IsNullOrEmpty(linkedModelId)) return false;
            if (linkedModelId == activeModelId) return true;
            
            // Compatibilidad hacia atrás: si la BD tiene ABC-123 y el activo es ABC-123:111-111
            if (activeModelId.StartsWith(linkedModelId + ":")) return true;
            
            return false;
        }

        /// <summary>
        /// Obtiene un identificador único persistente para el documento de Revit activo.
        /// </summary>
        public static string GetModelUniqueId(Document doc)
        {
            if (doc == null) return string.Empty;

            string baseId = string.Empty;
            string pathOrTitle = string.Empty;

            // 1. Si el modelo es colaborativo, intentar usar el GUID del modelo central
            bool isFileBasedWorkshared = false;
            if (doc.IsWorkshared)
            {
                try
                {
                    if (doc.WorksharingCentralGUID != Guid.Empty)
                    {
                        baseId = "CENTRAL_" + doc.WorksharingCentralGUID.ToString();
                    }
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    // Es un modelo colaborativo basado en archivos (file-based)
                    isFileBasedWorkshared = true;
                }

                try
                {
                    var centralPath = doc.GetWorksharingCentralModelPath();
                    if (centralPath != null)
                    {
                        if (isFileBasedWorkshared && doc.ProjectInformation != null)
                        {
                            pathOrTitle = doc.ProjectInformation.UniqueId;
                        }
                        else
                        {
                            pathOrTitle = ModelPathUtils.ConvertModelPathToUserVisiblePath(centralPath);
                        }
                    }
                }
                catch { }
            }

            // 2. Si no es colaborativo o falló el paso 1, usar el UniqueId de la información del proyecto
            if (string.IsNullOrEmpty(baseId) && doc.ProjectInformation != null)
            {
                baseId = "PROJECT_" + doc.ProjectInformation.UniqueId;
            }
            else if (string.IsNullOrEmpty(baseId))
            {
                baseId = "FALLBACK";
            }

            if (string.IsNullOrEmpty(pathOrTitle))
            {
                pathOrTitle = string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName;
            }

            string pathHash = GetDeterministicHash(pathOrTitle);
            string finalBaseId = baseId + "_" + pathHash;

            // 3. Añadir el InstanceGuid SOLO si es un modelo colaborativo
            if (doc.IsWorkshared)
            {
                string instanceGuid = GetInstanceGuid(doc);
                return BuildRevitModelId(finalBaseId, instanceGuid);
            }

            return finalBaseId;
        }

        private static string GetDeterministicHash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                byte[] hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
            }
        }
    }
}
