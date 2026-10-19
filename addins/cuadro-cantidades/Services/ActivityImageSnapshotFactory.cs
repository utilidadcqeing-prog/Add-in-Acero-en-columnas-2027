using System;
using System.IO;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Services;

internal static class ActivityImageSnapshotFactory
{
    public static ActivityImageSnapshot CreateFromCapturedFile(
        string capturedFilePath,
        string activityCode,
        DateTime capturedAt,
        string elementIds,
        IPreviewDebugImageStorageService debugImageStorageService)
    {
        byte[] imageBytes = File.ReadAllBytes(capturedFilePath);
        string? debugPath = debugImageStorageService.SaveDebugCopy(capturedFilePath, activityCode);

        return new ActivityImageSnapshot
        {
            ImageDataBase64 = Convert.ToBase64String(imageBytes),
            ContentType = ResolveContentType(capturedFilePath),
            DebugImagePath = debugPath,
            ImagePath = debugPath ?? string.Empty,
            CapturedAt = capturedAt,
            ElementIds = elementIds,
        };
    }

    private static string ResolveContentType(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            _ => "image/png",
        };
    }
}
