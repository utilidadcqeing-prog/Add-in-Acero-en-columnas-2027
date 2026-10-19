using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IRevitImageCaptureService
{
    string CaptureView(Document doc, IReadOnlyCollection<string> revitUniqueIds, string activityCode, string? selectedViewUniqueId = null);
}
