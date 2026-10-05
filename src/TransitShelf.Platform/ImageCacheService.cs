using System.IO;
using System.Windows.Media.Imaging;

namespace TransitShelf.Platform;

/// <summary>
/// 无源图片的落盘（见技术框架设计 5.2 / 红线 R8）。
/// 剪贴板里的位图一旦被覆盖就永久丢失，因此必须立刻存成缓存 PNG。
/// </summary>
public sealed class ImageCacheService
{
    /// <summary>把位图写成缓存 PNG，返回绝对路径；失败返回 null。</summary>
    public string? SavePng(BitmapSource image)
    {
        try
        {
            AppPaths.EnsureCreated();
            var path = Path.Combine(AppPaths.CacheDirectory, $"clip-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():n}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using var stream = File.Create(path);
            encoder.Save(stream);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
