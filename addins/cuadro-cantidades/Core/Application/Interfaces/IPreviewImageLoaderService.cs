using System.Windows.Media;
using CQEING_CuadroCantidades.Core.Domain.Entities;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IPreviewImageLoaderService
{
    ImageSource? LoadPreviewImage(ActivityImageSnapshot snapshot);
    ImageSource? LoadPreviewImage(string path);
}
