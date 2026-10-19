namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IPreviewDebugImageStorageService
{
    string? SaveDebugCopy(string capturedFilePath, string activityCode);
    void DeleteDebugCopy(string? filePath);
}
