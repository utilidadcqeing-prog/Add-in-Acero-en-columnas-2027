using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CQEING_CuadroCantidades.Core.Application.Interfaces;

namespace CQEING_CuadroCantidades.Services;

public sealed class ImageCacheBypassHelper : IImageCacheBypassHelper
{
    public ImageSource? LoadImage(string path)
    {
        path = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                if (bitmap.CanFreeze)
                {
                    bitmap.Freeze();
                }
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageCacheBypassHelper] Error loading image from URL '{path}': {ex}");
                return null;
            }
        }

        if (!File.Exists(path))
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheBypassHelper] Image file not found: {path}");
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var frame = BitmapFrame.Create(
                stream,
                BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            frame.Freeze();
            return frame;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheBypassHelper] Error loading image '{path}': {ex}");
            return null;
        }
    }

    public ImageSource? LoadImage(byte[] imageBytes)
    {
        if (imageBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(imageBytes);
            var frame = BitmapFrame.Create(
                stream,
                BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            frame.Freeze();
            return frame;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheBypassHelper] Error loading image from DB bytes: {ex}");
            return null;
        }
    }

    public ImageSource? LoadImageFromBase64(string imageDataBase64)
    {
        if (string.IsNullOrWhiteSpace(imageDataBase64))
        {
            return null;
        }

        try
        {
            return LoadImage(Convert.FromBase64String(imageDataBase64));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageCacheBypassHelper] Error decoding image from DB: {ex.Message}");
            return null;
        }
    }

    private static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Trim('"');
    }
}
