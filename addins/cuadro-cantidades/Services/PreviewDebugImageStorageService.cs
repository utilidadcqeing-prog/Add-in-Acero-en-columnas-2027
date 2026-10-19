using System;
using System.IO;
using CQEING_CuadroCantidades.Core.Application.Interfaces;

namespace CQEING_CuadroCantidades.Services;

public sealed class PreviewDebugImageStorageService : IPreviewDebugImageStorageService
{
    private readonly string _targetDir;

    public PreviewDebugImageStorageService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _targetDir = Path.Combine(appData, "CQEING_CuadroCantidades", "Previews");
    }

    public string? SaveDebugCopy(string capturedFilePath, string activityCode)
    {
        if (string.IsNullOrWhiteSpace(capturedFilePath) || !File.Exists(capturedFilePath))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_targetDir);

            string safeCode = string.Join("_", activityCode.Split(Path.GetInvalidFileNameChars()));
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string fileName = $"Activity_{safeCode}_{timestamp}.png";
            string destPath = Path.Combine(_targetDir, fileName);

            File.Copy(capturedFilePath, destPath, overwrite: true);
            return destPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PreviewDebugImageStorageService] Debug copy skipped: {ex.Message}");
            return null;
        }
    }

    public void DeleteDebugCopy(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PreviewDebugImageStorageService] Error deleting debug copy: {ex.Message}");
        }
    }
}
