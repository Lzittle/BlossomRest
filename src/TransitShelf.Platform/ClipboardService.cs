using System.Windows;
using System.Windows.Media.Imaging;
using TransitShelf.Platform.Interop;

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
    public bool CopyFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return false;
        }

        var files = paths.ToArray();

        // 主通道：WPF 的 DataObject，带 OLE 包装，目标应用兼容性最好。
        if (TrySet(() => Clipboard.SetDataObject(new DataObject(DataFormats.FileDrop, files), true), out var failure))
        {
            return true;
        }

        // 后备通道：绕开 OLE 直接用 Win32 写 CF_HDROP（见 NativeMethods.TrySetClipboardFileDrop）。
        if (NativeMethods.TrySetClipboardFileDrop(files))
        {
            return true;
        }

        Diagnostics.LogFailure("复制文件到剪贴板", failure);
        return false;
    }

    /// <summary>复制纯文本。</summary>
    public bool CopyText(string text)
    {
        if (TrySet(() => Clipboard.SetText(text), out var failure))
        {
            return true;
        }

        if (NativeMethods.TrySetClipboardText(text))
        {
            return true;
        }

        Diagnostics.LogFailure("复制文字到剪贴板", failure);
        return false;
    }

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
            catch (Exception)
            {
                // 剪贴板是跨进程共享资源，失败方式不止一种（被占用、格式不支持、OLE 拒绝……），
                // 这里一律吞掉后重试，绝不让读剪贴板把常驻程序带崩。
            }

            if (attempt < MaxAttempts - 1)
            {
                Thread.Sleep(RetryDelay);
            }
        }

        return ClipboardPayload.Empty;
    }

    /// <summary>
    /// 执行一次剪贴板写入，失败重试。
    ///
    /// **必须捕获一切异常**：剪贴板写入的失败方式不止 COMException 一种。实机遇到过
    /// OleSetClipboard 返回 E_NOTIMPL、被 ThrowExceptionForHR 映射成 NotImplementedException 的情况，
    /// 它既不是 COMException 也不是 ExternalException，从这两个 catch 旁边溜了出去，
    /// 一路逃到 App 的全局兜底被吞掉——用户看到的现象是"点复制没反应"，连失败提示都没有。
    /// 见技术框架设计 6.5「重试耗尽后必须返回失败，不能抛出异常」。
    /// </summary>
    private static bool TrySet(Action action, out Exception? lastFailure)
    {
        lastFailure = null;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                // 记录最后一次失败原因交给上层留痕；重试后仍失败就返回 false。
                lastFailure = ex;
            }

            if (attempt < MaxAttempts - 1)
            {
                Thread.Sleep(RetryDelay);
            }
        }

        return false;
    }
}
