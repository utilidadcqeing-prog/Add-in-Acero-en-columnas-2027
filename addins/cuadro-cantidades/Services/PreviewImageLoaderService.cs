using System;
using System.Windows.Media;
using CQEING_CuadroCantidades.Core.Application.Interfaces;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Services;

public sealed class PreviewImageLoaderService : IPreviewImageLoaderService
{
    private readonly IImageCacheBypassHelper _cacheBypassHelper;

    public PreviewImageLoaderService(IImageCacheBypassHelper cacheBypassHelper)
    {
        _cacheBypassHelper = cacheBypassHelper ?? throw new ArgumentNullException(nameof(cacheBypassHelper));
    }

    public ImageSource? LoadPreviewImage(ActivityImageSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.ImageDataBase64))
        {
            var image = _cacheBypassHelper.LoadImageFromBase64(snapshot.ImageDataBase64);
            if (image != null)
            {
                return image;
            }
        }

        string? legacyPath = !string.IsNullOrWhiteSpace(snapshot.DebugImagePath)
            ? snapshot.DebugImagePath
            : snapshot.ImagePath;

        return string.IsNullOrWhiteSpace(legacyPath)
            ? null
            : _cacheBypassHelper.LoadImage(legacyPath);
    }

    public ImageSource? LoadPreviewImage(string path)
    {
        return _cacheBypassHelper.LoadImage(path);
    }
}
