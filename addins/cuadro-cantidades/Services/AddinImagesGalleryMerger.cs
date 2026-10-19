using System;
using System.Collections.Generic;
using System.Linq;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Services;

/// <summary>
/// Resultado de combinar la galería con las imágenes del add-in de imágenes.
/// </summary>
/// <param name="Snapshots">Galería resultante (las capturas propias, intactas y en su lugar).</param>
/// <param name="Changed">Si algo cambió y hay que guardar la galería.</param>
/// <param name="SelectedUrlChanged">Si cambió la URL de una imagen marcada: hay que volver a publicar la actividad.</param>
public sealed record AddinImagesMergeResult(
    List<ActivityImageSnapshot> Snapshots,
    bool Changed,
    bool SelectedUrlChanged);

/// <summary>
/// Combina la "Galería de Capturas" de una actividad con las imágenes que el add-in de imágenes registró en la API.
/// Las imágenes del add-in se reconocen por su código (estable), no por la URL, que cambia al actualizarlas.
/// </summary>
public static class AddinImagesGalleryMerger
{
    /// <param name="current">Galería actual de la actividad (no se modifica).</param>
    /// <param name="apiImages">Imágenes del add-in para la actividad; null si la API no estuvo disponible.</param>
    public static AddinImagesMergeResult Merge(
        IReadOnlyList<ActivityImageSnapshot> current,
        IReadOnlyList<ApiImagenActividadResponse>? apiImages)
    {
        // Sin respuesta de la API no se sabe qué hay: la galería queda como estaba
        if (apiImages == null)
            return new AddinImagesMergeResult(current.ToList(), false, false);

        var apiByCode = new Dictionary<string, ApiImagenActividadResponse>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in apiImages.Where(i => !string.IsNullOrWhiteSpace(i.Codigo) && !string.IsNullOrWhiteSpace(i.Url)))
            apiByCode[image.Codigo] = image;

        var result = new List<ActivityImageSnapshot>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        bool selectedUrlChanged = false;

        foreach (var snapshot in current)
        {
            if (!snapshot.IsFromImagesAddin)
            {
                result.Add(snapshot);
                continue;
            }

            // Borrada en el add-in de imágenes (o repetida en la galería): se quita
            if (!apiByCode.TryGetValue(snapshot.AddinImageCode!, out var image) || !seenCodes.Add(image.Codigo))
            {
                changed = true;
                continue;
            }

            var updated = FromApi(image, snapshot.IsSelectedForPublication);
            if (!SameContent(snapshot, updated))
            {
                changed = true;
                if (snapshot.IsSelectedForPublication && !string.Equals(PublicUrlOf(snapshot), image.Url, StringComparison.Ordinal))
                    selectedUrlChanged = true;
            }

            result.Add(updated);
        }

        // Nuevas en el add-in de imágenes: llegan sin marcar, como una captura nueva
        foreach (var image in apiByCode.Values.OrderBy(i => i.Orden))
        {
            if (seenCodes.Add(image.Codigo))
            {
                result.Add(FromApi(image, isSelected: false));
                changed = true;
            }
        }

        return new AddinImagesMergeResult(result, changed, selectedUrlChanged);
    }

    private static ActivityImageSnapshot FromApi(ApiImagenActividadResponse image, bool isSelected) => new()
    {
        AddinImageCode = image.Codigo,
        AddinImageName = image.Nombre,
        ImagePath = image.Url,
        PublishedUrl = image.Url,
        // La galería dibuja DebugImagePath antes que ImagePath: vacío, para que nunca muestre una URL vieja
        DebugImagePath = null,
        // La API da la fecha en UTC; las capturas propias usan hora local y se ordenan juntas por fecha
        CapturedAt = image.FechaCreacion.Kind == DateTimeKind.Utc ? image.FechaCreacion.ToLocalTime() : image.FechaCreacion,
        IsSelectedForPublication = isSelected,
    };

    private static string? PublicUrlOf(ActivityImageSnapshot snapshot) =>
        !string.IsNullOrWhiteSpace(snapshot.PublishedUrl) ? snapshot.PublishedUrl : snapshot.ImagePath;

    private static bool SameContent(ActivityImageSnapshot a, ActivityImageSnapshot b) =>
        string.Equals(a.AddinImageCode, b.AddinImageCode, StringComparison.Ordinal)
        && string.Equals(a.AddinImageName, b.AddinImageName, StringComparison.Ordinal)
        && string.Equals(a.ImagePath, b.ImagePath, StringComparison.Ordinal)
        && string.Equals(a.PublishedUrl, b.PublishedUrl, StringComparison.Ordinal)
        && string.IsNullOrEmpty(a.DebugImagePath)
        && a.CapturedAt == b.CapturedAt;
}
