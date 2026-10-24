namespace TransitShelf.Core.Settings;

/// <summary>
/// 用户可调项（见 PRD FR-72 / FR-74 / FR-75；落盘格式见 ShelfStateCodec）。
///
/// **取值一律给区间而不是档位**：上限与过期时长改成自由输入（2026-10-08），
/// 所以这里的职责是"把用户可能乱输的值收进安全区间"，而不是枚举。
/// </summary>
public sealed class ShelfSettings
{
    /// <summary>临时区条目上限的合法区间（用户可自由输入，见 FR-72）。</summary>
    public const int CapacityMin = 1;

    public const int CapacityMax = 99;

    /// <summary>过期时长的合法区间（小时）。上限 30 天，再长就失去"临时区"的意义了。</summary>
    public const int TimeToLiveHoursMin = 1;

    public const int TimeToLiveHoursMax = 24 * 30;

    /// <summary>临时区条目上限，默认 9。</summary>
    public int Capacity { get; set; } = 9;

    /// <summary>未钉住条目的存活时长（小时），默认 24。</summary>
    public int TimeToLiveHours { get; set; } = 24;

    /// <summary>
    /// 退出程序时是否清空未钉住条目（FR-74，默认关闭）。
    ///
    /// 关闭时沿用既有约定"退出不清理、条目留到下次启动"（技术框架设计 5.5）；
    /// 开启后每次退出都会丢弃现场——这是隐私与便利的取舍，所以做成可选而非默认。
    /// 钉住项在任何情况下都不清（与"清空只清未钉住"的语义一致，见 4.4）。
    /// </summary>
    public bool ClearOnExit { get; set; }

    /// <summary>
    /// 是否启用工作区（FR-75，默认关闭）。关闭时主面板只有临时区，保持现有行为。
    /// </summary>
    public bool EnableWorkspace { get; set; }

    /// <summary>自动收录下载目录。</summary>
    public bool WatchDownloads { get; set; }

    /// <summary>自动收录微信接收目录。</summary>
    public bool WatchWeChat { get; set; }

    /// <summary>额外的监听目录。</summary>
    public List<string> ExtraWatchDirectories { get; set; } = [];

    /// <summary>
    /// 配色方案名（见 App 层 PaletteService：Frappe / RosePineDawn）。
    /// 这里只存名字，不认识配色本身——Core 不依赖任何界面资源。
    /// </summary>
    public string Palette { get; set; } = "Frappe";

    /// <summary>
    /// 收起胶囊上次停留的显示器设备名（FR-05）。设备名只用于找回显示器，
    /// 不把显示器分辨率或 DPI 当成永久配置；显示器拔掉后会回退到当前可用屏幕。
    /// </summary>
    public string? WindowMonitor { get; set; }

    /// <summary>收起胶囊左上角的物理像素坐标（FR-05）。</summary>
    public int? WindowLeftPx { get; set; }

    /// <summary>收起胶囊左上角的物理像素坐标（FR-05）。</summary>
    public int? WindowTopPx { get; set; }

    /// <summary>实际生效的过期时长：用户可能手填越界值，这里兜底。</summary>
    public TimeSpan TimeToLive => TimeSpan.FromHours(Math.Clamp(TimeToLiveHours, TimeToLiveHoursMin, TimeToLiveHoursMax));

    /// <summary>实际生效的上限。0 或负数会让 ShelfService.Capacity 的兜底逻辑把临时区清空，故在此钳制。</summary>
    public int SafeCapacity => Math.Clamp(Capacity, CapacityMin, CapacityMax);
}
