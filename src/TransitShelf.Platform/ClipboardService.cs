using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TransitShelf.Platform;

/// <summary>剪贴板当前内容的快照。</summary>
public sealed record ClipboardPayload(string? Text, IReadOnlyList<string> Files, BitmapSource? Image)
{
    public static ClipboardPayload Empty { get; } = new(null, [], null);
}

/// <summary>剪贴板读写。剪贴板可能被其他进程占用，因此统一带重试。</summary>
public sealed class ClipboardService
{
    // 剪贴板读写都在 UI 线程上，重试总时长必须短：5×50ms 会让界面明显卡顿。
    private const int MaxAttempts = 4;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(30);

    /// <summary>复制文件路径列表（CF_HDROP）。粘贴到资源管理器、微信、AI 会话均可用。</summary>
    public bool CopyFiles(IReadOnlyList<string> paths) =>
        TrySet(() => Clipboard.SetDataObject(new DataObject(DataFormats.FileDrop, paths.ToArray()), true));

    /// <summary>复制纯文本。</summary>
    public bool CopyText(string text) => TrySet(() => Clipboard.SetText(text));

    /// <summary>复制单条路径文本（用于"复制路径"）。</summary>
    public bool CopyPath(string path) => CopyText(path);

    /// <summary>
    /// 读取剪贴板内容：优先文件，其次文字，最后图片（FR-13）。
    /// 读不到或被占用时返回空快照，不抛异常。
    /// </summary>
    public ClipboardPayload Read()
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    if (files.Count > 0)
                    {
                        return new ClipboardPayload(null, files.Cast<string>().ToArray(), null);
                    }
                }

                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return new ClipboardPayload(text, [], null);
                    }
                }

                if (Clipboard.ContainsImage())
                {
                    // 取快照：剪贴板里的位图在下次复制后就没了，必须立即冻结。
                    if (Clipboard.GetImage() is { } image)
                    {
                        image.Freeze();
                        return new ClipboardPayload(null, [], image);
                    }
                }

                return ClipboardPayload.Empty;
            }
            catch (COMException)
            {
                // 剪贴板被占用，重试
            }
            catch (ExternalException)
            {
                // 同上
            }

            if (attempt < MaxAttempts - 1)
            {
                Thread.Sleep(RetryDelay);
            }
        }

        return ClipboardPayload.Empty;
    }

    private static bool TrySet(Action action)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                action();
                return true;
            }
            catch (COMException)
            {
                // 剪贴板被其他进程占用（CLIPBRD_E_CANT_OPEN 等），重试后放弃。
            }
            catch (ExternalException)
            {
                // 同上；这里绝不能让异常逃出去，否则整个常驻程序会退出。
            }

            if (attempt < MaxAttempts - 1)
            {
                Thread.Sleep(RetryDelay);
            }
        }

        return false;
    }
}
