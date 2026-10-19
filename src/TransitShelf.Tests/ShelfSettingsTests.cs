using TransitShelf.Core.Settings;

namespace TransitShelf.Tests;

/// <summary>
/// 设置项（FR-72 / FR-74 / FR-75，见技术框架设计 8.6.1 / ADR D27）。
///
/// 这里只测**纯逻辑**：取值钳制与持久化往返。
/// 独立设置窗口的开关交互要真实消息泵与 Dispatcher，自动化价值低，
/// 靠手工验收 + XAML 良构性静态检查（见进度与待办第六节）。
/// </summary>
public class ShelfSettingsTests
{
    // ── 区间钳制（FR-72：上限与时长都改成自由输入，所以钳制成了唯一的护栏）──────

    [Theory]
    [InlineData(0, 1)]      // 0 小时会被抬到下限 1，否则"立即过期"会让用户以为条目丢了
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(24, 24)]
    [InlineData(720, 720)]
    [InlineData(10000, 720)] // 上限 30 天，超出按 30 天算
    public void 过期时长会被钳制在1小时到30天之间(int hours, double expected)
    {
        var settings = new ShelfSettings { TimeToLiveHours = hours };

        Assert.Equal(expected, settings.TimeToLive.TotalHours);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 1)]
    [InlineData(9, 9)]
    [InlineData(99, 99)]
    [InlineData(100000, 99)]
    public void 上限会被钳制在1到99之间(int capacity, int expected)
    {
        var settings = new ShelfSettings { Capacity = capacity };

        Assert.Equal(expected, settings.SafeCapacity);
    }

    /// <summary>
    /// SafeCapacity 存在的理由：手输 0 会让 <c>ShelfService.Capacity</c> 的兜底逻辑
    /// 把整个临时区清空。这条测试守着"任何输入都不会清空全部"这个底线。
    /// </summary>
    [Fact]
    public void 上限至少为1()
    {
        var shelf = new TransitShelf.Core.ShelfService { Capacity = 0 };

        Assert.Equal(1, shelf.Capacity);
    }

    [Fact]
    public void 区间常量自身必须自洽()
    {
        Assert.True(ShelfSettings.CapacityMin < ShelfSettings.CapacityMax);
        Assert.True(ShelfSettings.TimeToLiveHoursMin < ShelfSettings.TimeToLiveHoursMax);

        // 默认值必须落在区间内，否则用户一打开设置框看到的就是"非法值"
        var settings = new ShelfSettings();
        Assert.InRange(settings.Capacity, ShelfSettings.CapacityMin, ShelfSettings.CapacityMax);
        Assert.InRange(settings.TimeToLiveHours, ShelfSettings.TimeToLiveHoursMin, ShelfSettings.TimeToLiveHoursMax);
    }

    // ── 默认值 ──────────────────────────────────────────────────────

    [Fact]
    public void 默认值与文档一致()
    {
        var settings = new ShelfSettings();

        Assert.Equal(9, settings.Capacity);
        Assert.Equal(24, settings.TimeToLiveHours);
        Assert.False(settings.WatchDownloads);   // 红线 R6：自动采集默认关闭
        Assert.False(settings.WatchWeChat);
    }

    /// <summary>
    /// 两个新增开关必须**默认关闭**：
    /// 退出清空是丢弃现场的不可逆动作（FR-74），工作区没实现完（FR-75）。
    /// 默认开等于让用户第一次启动就丢掉已有列表。
    /// </summary>
    [Fact]
    public void 退出清空与工作区默认关闭()
    {
        var settings = new ShelfSettings();

        Assert.False(settings.ClearOnExit);
        Assert.False(settings.EnableWorkspace);
    }

    // ── 持久化往返 ──────────────────────────────────────────────────

    [Fact]
    public void 清理策略能往返序列化()
    {
        var settings = new ShelfSettings
        {
            Capacity = 30,
            TimeToLiveHours = 168,
            WatchDownloads = true,
            Palette = "RosePineDawn",
            ClearOnExit = true,
            EnableWorkspace = true,
        };

        var json = TransitShelf.Core.Persistence.ShelfStateCodec.Serialize([], settings);
        var (_, _, loaded) = TransitShelf.Core.Persistence.ShelfStateCodec.DeserializeSnapshot(json);

        Assert.Equal(30, loaded.Capacity);
        Assert.Equal(168, loaded.TimeToLiveHours);
        Assert.True(loaded.WatchDownloads);
        Assert.Equal("RosePineDawn", loaded.Palette);
        Assert.True(loaded.ClearOnExit);
        Assert.True(loaded.EnableWorkspace);
    }

    /// <summary>
    /// 旧版 state.json 里没有 clearOnExit / enableWorkspace 字段（2026-10-08 之前）。
    /// 反序列化必须让它们取到属性默认值，而不是留在 CLR 默认的 false 之外出现别的行为——
    /// 这条测试锁住"老配置文件升级后行为不变"。
    /// </summary>
    [Fact]
    public void 旧配置文件缺少新字段时取默认值()
    {
        const string legacy = """
        {
          "items": [],
          "settings": {
            "capacity": 15,
            "timeToLiveHours": 6,
            "watchDownloads": true,
            "watchWeChat": false,
            "extraWatchDirectories": [],
            "palette": "Frappe"
          }
        }
        """;

        var (_, _, settings) = TransitShelf.Core.Persistence.ShelfStateCodec.DeserializeSnapshot(legacy);

        Assert.Equal(15, settings.Capacity);
        Assert.Equal(6, settings.TimeToLiveHours);
        Assert.True(settings.WatchDownloads);
        Assert.False(settings.ClearOnExit);
        Assert.False(settings.EnableWorkspace);
    }
}
