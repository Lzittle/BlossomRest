using TransitShelf.Platform;
using Xunit;

namespace TransitShelf.Tests;

/// <summary>
/// 深浅主题跟随系统（FR-73）：只测注册表值的纯解析。
/// 真正的主题切换依赖用户桌面环境，不放进单元测试，靠手工验收。
/// </summary>
public class SystemThemeTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    public void 非零整数表示浅色(int value, bool expected)
        => Assert.Equal(expected, SystemThemeService.IsLightThemeValue(value));

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData(1L)]
    public void 非整数按深色处理(object? value)
        => Assert.False(SystemThemeService.IsLightThemeValue(value));
}
