using System.Runtime.InteropServices;

namespace TransitShelf.Platform.Interop;

/// <summary>
/// 集中在平台层里的 Win32 互操作。每个签名标注来源 API 与用途。
/// </summary>
public static class NativeMethods
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;

    // DwmSetWindowAttribute（dwmapi.dll）：Windows 11 圆角与背景材质。
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    // DwmSetWindowAttribute：非客户区渲染策略与边框颜色。
    private const int DWMWA_NCRENDERING_POLICY = 2;
    private const int DWMNCRP_DISABLED = 1;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    // DwmSetWindowAttribute（dwmapi.dll）：深色标题栏与系统背景材质。
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2;      // Mica：只取壁纸，不模糊后面的窗口
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Desktop Acrylic：模糊窗口后面的真实内容

    // SetWindowCompositionAttribute（user32.dll，未公开文档）：Windows 10 的模糊回退路径。
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor; // ABGR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    // SetWindowPos（user32.dll）：把窗口放回置顶层顶部。
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    // RegisterHotKey（user32.dll）：全局快捷键。
    public const int WmHotKey = 0x0312;
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// 让窗口不抢焦点（WS_EX_NOACTIVATE）且不出现在 Alt+Tab（WS_EX_TOOLWINDOW）。
    /// 这是"悬浮小框不打断当前输入"的关键。
    /// </summary>
    public static void ApplyNonActivatingToolWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// Windows 11 上启用系统圆角；旧系统调用失败可直接忽略。
    /// **当前未被调用**：浮窗是透明窗口、圆角自绘，再让系统描一圈会沿窗口矩形留下边框和投影
    /// （见 <see cref="TryRemoveWindowFrame"/>）。保留它是因为回退成不透明窗口时还需要。
    /// </summary>
    public static void TryApplyRoundedCorners(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var preference = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    /// <summary>
    /// 去掉系统给窗口画的边框与投影。
    ///
    /// 浮窗是透明窗口、轮廓由 XAML 自绘（收起态是正圆），但系统仍会沿**窗口矩形**描一圈边框并投影；
    /// 在浅色页面上一眼就能看见圆外面套着一个浅灰方块。这里关掉非客户区渲染并把边框颜色设为"无"。
    /// 旧系统不支持这两项属性时调用失败，可忽略。
    /// </summary>
    public static void TryRemoveWindowFrame(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var policy = DWMNCRP_DISABLED;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));

        var none = DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref none, sizeof(int));
    }

    /// <summary>
    /// 让窗口使用系统「桌面亚克力」背景材质（Windows 11 build 22621 及以上）。
    ///
    /// 为什么要 Desktop Acrylic（DWMSBT_TRANSIENTWINDOW）而不是 Mica（DWMSBT_MAINWINDOW）：
    /// 微软文档写明 Background Acrylic 会显示"桌面壁纸以及当前窗口后面的其他窗口"，
    /// 而 Mica 只采样一次壁纸——只有前者能做出毛玻璃观感。
    /// 调用方必须先确认窗口未使用 AllowsTransparency（分层窗口拿不到系统材质）。
    /// 注意：本项目的浮窗当前**就是**透明窗口（收起态要做正圆），因此这里目前不可用。
    /// </summary>
    /// <returns>成功返回 true；旧系统或不支持时返回 false，由调用方回退到实色背景。</returns>
    public static bool TryApplyAcrylicBackdrop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var dark = 1;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        // 关键一步：把"玻璃区域"扩展到整个窗口。
        // WPF 的非分层窗口默认把客户区当作不透明表面，系统材质只能在边框区域透出来；
        // margins = -1 表示四边都按边框处理，整个客户区才成为可与材质合成的玻璃区。
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);

        var backdrop = DWMSBT_TRANSIENTWINDOW;
        return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
    }

    /// <summary>
    /// 系统材质不可用时的回退（Windows 10 1809+）：直接让 DWM 在窗口后面做亚克力模糊。
    /// 该 API 未公开文档，但长期可用；缺点是拖动窗口时模糊会重算，可能发滞。
    /// </summary>
    /// <param name="abgrTint">ABGR 顺序的着色，例如 0xB3463430 对应 RGB #303446。</param>
    public static bool TryApplyLegacyAcrylicBlur(IntPtr hwnd, uint abgrTint)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var policy = new AccentPolicy
        {
            AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags = 2,
            GradientColor = unchecked((int)abgrTint),
            AnimationId = 0,
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, buffer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = buffer,
                SizeOfData = size,
            };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>窗口是否仍处于置顶层。</summary>
    public static bool IsTopmost(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;

    /// <summary>
    /// 重新把窗口压到置顶层顶部，且不激活它（SWP_NOACTIVATE）。
    /// 必要性：本窗口带 WS_EX_NOACTIVATE，永远不激活；而 Windows 在置顶层内部按激活先后排序，
    /// 所以任何别的置顶窗口（例如被"置顶"的微信窗口）都会永久盖住我们。定时调用此方法可抢回位置。
    /// </summary>
    public static void ReassertTopmost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // WPF 可能在样式变化时重写扩展样式，这里顺带把我们的三个样式位补回去。
        ApplyNonActivatingToolWindow(hwnd);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>注册全局快捷键。被占用时返回 false，不抛异常。</summary>
    public static bool TryRegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey) =>
        hwnd != IntPtr.Zero && RegisterHotKey(hwnd, id, modifiers | ModNoRepeat, virtualKey);

    public static void UnregisterHotKeyById(IntPtr hwnd, int id)
    {
        if (hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(hwnd, id);
        }
    }

    /// <summary>
    /// 临时允许/禁止窗口被激活。
    /// 用途：弹出系统文件对话框时必须让窗口能被激活并置于前台，
    /// 否则对话框可能开在其他窗口后面、而 ShowDialog 已经阻塞了主窗口——用户看到的就是"程序卡死"。
    /// </summary>
    public static void SetActivatable(IntPtr hwnd, bool activatable)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, activatable
            ? style & ~WS_EX_NOACTIVATE
            : style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    public static void BringToForeground(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(hwnd);
        }
    }
}
