using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TransitShelf.Platform.Interop;

namespace TransitShelf.Platform;

/// <summary>
/// 条目缩略图（FR-52）：图片直接解码到列表需要的小尺寸，视频走系统缩略图取首帧。
/// 结果冻结后可跨线程交给界面；任何失败都返回 null，由界面回退到类型图标。
/// </summary>
public static class ThumbnailProvider
{
    /// <summary>WPF 的 BitmapImage 能直接解码的格式；其余图片（webp / heic 等）交给系统缩略图。</summary>
    private static readonly string[] DecodableImageExtensions =
    [
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico",
    ];

    public static BitmapSource? TryLoad(string? path, int pixelSize)
    {
        if (string.IsNullOrWhiteSpace(path) || pixelSize <= 0 || !File.Exists(path))
        {
            return null;
        }

        if (IsDecodableImage(path))
        {
            var decoded = TryDecodeImage(path, pixelSize);
            if (decoded is not null)
            {
                return decoded;
            }
        }

        return TryShellThumbnail(path, pixelSize);
    }

    private static bool IsDecodableImage(string path) =>
        DecodableImageExtensions.Any(extension =>
            path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static BitmapSource? TryDecodeImage(string path, int pixelSize)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            // 只解到目标尺寸：原图可能是 4K 截图，解全尺寸再缩放会把内存和耗时就吃满。
            image.DecodePixelWidth = pixelSize;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // 缩略图是可选装饰：损坏文件、权限不足都不该影响条目本身。
            return null;
        }
    }

    private static BitmapSource? TryShellThumbnail(string path, int pixelSize)
    {
        IntPtr bitmapHandle = IntPtr.Zero;
        try
        {
            ShellThumbnail.SHCreateItemFromParsingName(
                path,
                IntPtr.Zero,
                typeof(ShellThumbnail.IShellItemImageFactory).GUID,
                out var factory);

            try
            {
                var size = new ShellThumbnail.Size { Width = pixelSize, Height = pixelSize };
                var result = factory.GetImage(
                    size,
                    ShellThumbnail.GetImageFlags.BiggerSizeOk | ShellThumbnail.GetImageFlags.ThumbnailOnly,
                    out bitmapHandle);

                if (result != 0 || bitmapHandle == IntPtr.Zero)
                {
                    return null;
                }

                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapHandle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception)
        {
            // 视频没有缩略图提供者、文件被独占等都会抛 COMException，一律按"没有缩略图"处理。
            return null;
        }
        finally
        {
            if (bitmapHandle != IntPtr.Zero)
            {
                ShellThumbnail.DeleteObject(bitmapHandle);
            }
        }
    }
}
