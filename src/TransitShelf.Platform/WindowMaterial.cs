using TransitShelf.Platform.Interop;

namespace TransitShelf.Platform;

/// <summary>
/// 浮窗的窗口材质（毛玻璃）。
///
/// **当前未启用**（ADR D21）：亚克力会把桌面透上来，把配色的饱和度拉灰，
/// 所以面板改成不透明实色，界面层不再调用这里。代码保留是因为它本身没问题，
/// 将来若要做"可选材质"，重新接上就行；届时记得配色的 SurfaceBrush 要带 alpha。
///
/// 三条路径按优先级尝试：
/// 1. Windows 11 build 22621+ 的系统背景材质（Desktop Acrylic）——模糊窗口后面的真实内容，含其他窗口；
/// 2. Windows 10 1809+ 的 SetWindowCompositionAttribute 亚克力模糊——同一效果，但接口未公开文档；
/// 3. 都不支持时返回 <see cref="Kind.None"/>，由界面层回退到实色背景。
///
/// 注意：调用前窗口必须是非分层窗口（WPF 的 AllowsTransparency 必须为 false），否则材质不会生效。
/// **本项目现在不满足这个前提**：浮窗已改为透明窗口（收起态要做正圆，见 ShelfWindow.xaml），
/// 所以将来要启用材质时，得先决定是放弃正圆、还是换一条不依赖系统材质的实现路线。
/// </summary>
public static class WindowMaterial
{
    public enum Kind
    {
        /// <summary>系统背景材质不可用，应回退到实色背景。</summary>
        None = 0,

        /// <summary>Windows 11 系统桌面亚克力。</summary>
        Acrylic = 1,

        /// <summary>旧系统的亚克力模糊回退路径。</summary>
        LegacyBlur = 2,
    }

    /// <summary>按优先级为窗口应用材质，返回实际生效的那一种。</summary>
    /// <param name="abgrTint">仅回退路径使用的 ABGR 着色（例如 0xB3463430 对应 RGB #303446）。</param>
    public static Kind Apply(IntPtr hwnd, uint abgrTint)
    {
        if (NativeMethods.TryApplyAcrylicBackdrop(hwnd))
        {
            return Kind.Acrylic;
        }

        return NativeMethods.TryApplyLegacyAcrylicBlur(hwnd, abgrTint)
            ? Kind.LegacyBlur
            : Kind.None;
    }
}
