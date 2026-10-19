using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Resumen de la publicación de imágenes de un guardado, para el reporte de diagnóstico.
/// </summary>
public sealed class ImagePublishReport
{
    public int Uploaded { get; set; }
    public int ReusedUrls { get; set; }
    public int FileNotFound { get; set; }
    public int WithoutImage { get; set; }
    public List<string> Details { get; } = new();
}

/// <summary>
/// Resultado de publicar las imágenes de una actividad.
/// </summary>
/// <param name="ImageUrls">URLs publicadas separadas por coma, o null si la actividad no publica imágenes.</param>
/// <param name="IsComplete">False si alguna imagen marcada no se pudo publicar; en ese caso ImageUrls está incompleto.</param>
/// <param name="UpdatedSnapshotsJson">Historial con las URLs nuevas guardadas, o null si no cambió.</param>
public sealed record ActivityImagePublishResult(string? ImageUrls, bool IsComplete, string? UpdatedSnapshotsJson);

/// <summary>
/// Publica en la nube las imágenes marcadas de una actividad.
/// Cada imagen se sube una sola vez: la URL queda guardada en la captura (<see cref="ActivityImageSnapshot.PublishedUrl"/>)
/// y se reutiliza en los guardados siguientes.
/// </summary>
public sealed class ActivityImagePublisher
{
    private const string LogSource = "ActivityImagePublisher";
    private const int MaxPublishedImages = 3;

    private readonly IImageUploadService _uploadService;

    public ActivityImagePublisher(IImageUploadService uploadService)
    {
        _uploadService = uploadService ?? throw new ArgumentNullException(nameof(uploadService));
    }

    public async Task<ActivityImagePublishResult> PublishAsync(BudgetItem item, ImagePublishReport report)
    {
        var snapshots = TryReadSnapshots(item.PreviewImagesJson);
        if (snapshots is { Count: > 0 })
            return await PublishSnapshotsAsync(item.Code, snapshots, report);

        // Sin historial: rutas locales o URLs heredadas en PreviewImagePath
        if (string.IsNullOrWhiteSpace(item.PreviewImagePath))
        {
            report.WithoutImage++;
            return new ActivityImagePublishResult(null, true, null);
        }

        var urls = new List<string>();
        bool isComplete = true;
        foreach (var rawPath in item.PreviewImagePath.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string path = CleanPath(rawPath);
            string? url = IsUrl(path)
                ? Reuse(path, report)
                : await UploadAsync(item.Code, path, null, null, report);

            if (url == null)
                isComplete = false;
            else
                urls.Add(url);
        }

        return new ActivityImagePublishResult(JoinUrls(urls), isComplete, null);
    }

    private async Task<ActivityImagePublishResult> PublishSnapshotsAsync(
        string activityCode,
        List<ActivityImageSnapshot> snapshots,
        ImagePublishReport report)
    {
        // La fuente de verdad es la selección: si el usuario quitó todos los checks, la actividad no publica imágenes
        var selected = snapshots
            .Where(s => s.IsSelectedForPublication)
            .OrderByDescending(s => s.CapturedAt)
            .Take(MaxPublishedImages)
            .ToList();

        if (selected.Count == 0)
        {
            report.WithoutImage++;
            return new ActivityImagePublishResult(null, true, null);
        }

        var urls = new List<string>();
        bool isComplete = true;
        bool snapshotsChanged = false;

        foreach (var snapshot in selected)
        {
            string? url = await PublishSnapshotAsync(activityCode, snapshot, report);
            if (url == null)
            {
                isComplete = false;
                continue;
            }

            urls.Add(url);
            if (snapshot.PublishedUrl != url)
            {
                snapshot.PublishedUrl = url;
                snapshotsChanged = true;
            }
        }

        string? updatedJson = snapshotsChanged ? JsonSerializer.Serialize(snapshots) : null;
        return new ActivityImagePublishResult(JoinUrls(urls), isComplete, updatedJson);
    }

    private async Task<string?> PublishSnapshotAsync(string activityCode, ActivityImageSnapshot snapshot, ImagePublishReport report)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.PublishedUrl))
            return Reuse(snapshot.PublishedUrl!, report);

        string path = CleanPath(!string.IsNullOrWhiteSpace(snapshot.ImagePath) ? snapshot.ImagePath : snapshot.DebugImagePath);
        if (IsUrl(path))
            return Reuse(path, report);

        return await UploadAsync(activityCode, path, snapshot, BuildFileName(activityCode, snapshot), report);
    }

    /// <summary>
    /// Sube el archivo local; si no existe, usa la copia Base64 guardada en la captura.
    /// </summary>
    private async Task<string?> UploadAsync(
        string activityCode,
        string path,
        ActivityImageSnapshot? snapshot,
        string? fileName,
        ImagePublishReport report)
    {
        try
        {
            string? url;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                url = await _uploadService.UploadImageAsync(path);
            }
            else if (!string.IsNullOrEmpty(snapshot?.ImageDataBase64))
            {
                url = await _uploadService.UploadImageAsync(
                    Convert.FromBase64String(snapshot!.ImageDataBase64!),
                    fileName ?? $"{activityCode}.png",
                    snapshot.ContentType);
            }
            else
            {
                report.FileNotFound++;
                string message = $"Actividad {activityCode}: archivo local no encontrado en '{path}' y sin copia de la imagen.";
                report.Details.Add(message);
                ErrorLog.Write(LogSource, message);
                return null;
            }

            if (string.IsNullOrEmpty(url))
            {
                report.Details.Add($"Actividad {activityCode}: la subida no devolvió una URL.");
                return null;
            }

            report.Uploaded++;
            report.Details.Add($"Actividad {activityCode}: subida a Cloudinary -> {url}");
            return url;
        }
        catch (Exception ex)
        {
            report.Details.Add($"Actividad {activityCode}: error de subida: {ex.Message}");
            ErrorLog.Write(LogSource, $"Actividad {activityCode}: no se pudo publicar la imagen '{path}'.", ex);
            return null;
        }
    }

    private static string Reuse(string url, ImagePublishReport report)
    {
        report.ReusedUrls++;
        return url;
    }

    private static List<ActivityImageSnapshot>? TryReadSnapshots(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<ActivityImageSnapshot>>(json!);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(LogSource, "No se pudo leer el historial de capturas.", ex);
            return null;
        }
    }

    private static string BuildFileName(string activityCode, ActivityImageSnapshot snapshot)
    {
        string extension = snapshot.ContentType == "image/jpeg" ? ".jpg" : ".png";
        return $"{activityCode}_{snapshot.CapturedAt:yyyyMMdd_HHmmss}{extension}";
    }

    private static string CleanPath(string? path) => (path ?? string.Empty).Trim().Trim('"');

    private static bool IsUrl(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static string? JoinUrls(List<string> urls) => urls.Count > 0 ? string.Join(",", urls) : null;
}
