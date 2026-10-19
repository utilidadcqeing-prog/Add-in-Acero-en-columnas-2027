using System.Windows.Media;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IImageCacheBypassHelper
{
    ImageSource? LoadImage(string path);
    ImageSource? LoadImage(byte[] imageBytes);
    ImageSource? LoadImageFromBase64(string imageDataBase64);
}
