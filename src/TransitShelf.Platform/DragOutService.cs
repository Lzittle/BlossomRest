using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using TransitShelf.Core;

namespace TransitShelf.Platform;

/// <summary>
/// 出站拖放（见技术框架设计 6.4）。
/// 按条目类型挂载对应格式：文件与文件夹走 CF_HDROP，文字走 CF_UNICODETEXT，
/// 图片文件额外附带位图与 PNG 两份，由目标应用自取。
/// </summary>
public sealed class DragOutService
{
    private const string PngFormat = "PNG";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp",
    };

    /// <summary>开始拖拽；返回目标应用实际执行的拖放效果。</summary>
    public DragDropEffects StartDrag(DependencyObject source, ShelfItem item)
    {
        var data = BuildData(item);
        return data is null ? DragDropEffects.None : DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
    }

    private static DataObject? BuildData(ShelfItem item)
    {
        if (item.Kind == ItemKind.Text)
        {
            if (string.IsNullOrEmpty(item.Text))
            {
                return null;
            }

            var textData = new DataObject();
            textData.SetData(DataFormats.UnicodeText, item.Text);
            return textData;
        }

        // 文件与文件夹：无源图片用缓存 PNG 兜底。
        var path = item.Path ?? item.CachePath;
        if (path is null || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return null;
        }

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { path });

        if (File.Exists(path) && ImageExtensions.Contains(Path.GetExtension(path)))
        {
            TryAddImageFormats(data, path);
        }

        return data;
    }

    private static void TryAddImageFormats(DataObject data, string path)
    {
        try
        {
            var frame = BitmapFrame.Create(
                new Uri(path),
                BitmapCreateOptions.None,
                BitmapCacheOption.OnLoad);

            data.SetData(DataFormats.Bitmap, frame);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(frame);
            var buffer = new MemoryStream();
            encoder.Save(buffer);
            buffer.Position = 0;
            data.SetData(PngFormat, buffer);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            // 附加位图格式失败不影响文件本身的拖出
        }
    }
}
