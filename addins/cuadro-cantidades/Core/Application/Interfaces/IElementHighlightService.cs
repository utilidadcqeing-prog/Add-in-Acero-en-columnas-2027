using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IElementHighlightService
{
    void ApplyHighlight(View view, IReadOnlyCollection<ElementId> elementIds);
}
