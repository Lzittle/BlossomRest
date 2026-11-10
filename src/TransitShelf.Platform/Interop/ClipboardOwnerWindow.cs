using System.Runtime.InteropServices;

namespace TransitShelf.Platform.Interop;

/// <summary>剪贴板直写使用本线程的消息窗口，不显示、不激活，也不借用其他应用的窗口。</summary>
internal static class ClipboardOwnerWindow
{
    [ThreadStatic]
    private static IntPtr _handle;

    public static IntPtr GetForCurrentThread()
    {
        if (_handle != IntPtr.Zero) return _handle;
        // HWND_MESSAGE：只接收消息，不参与屏幕布局或焦点。
        _handle = CreateWindowEx(0, "STATIC", null, 0, 0, 0, 0, 0,
            new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return _handle;
    }

    // 所有格式立即交付给系统。窗口保留到所属线程退出，由 Windows 自动销毁；
    // 不在 CloseClipboard 后立即销毁，否则所有者又变为 NULL，新版 OLE 可能读到空格式。

    // CreateWindowExW（user32.dll）：使用系统 STATIC 类创建消息窗口。
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string? title,
        uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

}
