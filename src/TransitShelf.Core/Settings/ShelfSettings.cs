namespace TransitShelf.Core.Settings;

/// <summary>用户可调项（见 PRD FR-72 / FR-73；设置界面尚未实现，先落盘）。</summary>
public sealed class ShelfSettings
{
    /// <summary>临时区条目上限。</summary>
    public int Capacity { get; set; } = 9;

    /// <summary>未钉住条目的存活时长（小时）。</summary>
    public int TimeToLiveHours { get; set; } = 24;

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

    public TimeSpan TimeToLive => TimeSpan.FromHours(Math.Clamp(TimeToLiveHours, 1, 24 * 30));
}
