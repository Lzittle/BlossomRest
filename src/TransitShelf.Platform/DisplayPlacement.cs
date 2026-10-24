namespace TransitShelf.Platform;

/// <summary>
/// 显示器工作区内的窗口位置计算（FR-05）。坐标使用 Win32 的物理像素，
/// 因而不会把 125% / 150% / 200% 缩放误当成另一套逻辑坐标。
/// </summary>
public static class DisplayPlacement
{
    /// <summary>
    /// 将窗口左上角限制在工作区内。工作区可能有负坐标（左侧显示器），
    /// 窗口比工作区更大时贴到工作区左上角，保证仍能找回窗口。
    /// </summary>
    public static (int Left, int Top) ClampToWorkArea(
        int desiredLeft,
        int desiredTop,
        int windowWidth,
        int windowHeight,
        int workLeft,
        int workTop,
        int workRight,
        int workBottom)
    {
        var maxLeft = Math.Max(workLeft, workRight - Math.Max(0, windowWidth));
        var maxTop = Math.Max(workTop, workBottom - Math.Max(0, windowHeight));
        return (
            Math.Clamp(desiredLeft, workLeft, maxLeft),
            Math.Clamp(desiredTop, workTop, maxTop));
    }
}
