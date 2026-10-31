using TransitShelf.Platform;
using Xunit;

namespace TransitShelf.Tests;

/// <summary>
/// 开机自启（FR-73 / ADR D34）：这里只测纯逻辑的命令格式。
/// 真正的注册表读写依赖用户环境（组策略、安全软件），不放进单元测试，靠手工验收。
/// </summary>
public class StartupCommandTests
{
    [Theory]
    [InlineData(@"C:\Users\<user>\Apps\TransitShelf.exe", "\"C:\\Users\\<user>\\Apps\\TransitShelf.exe\"")]
    [InlineData(@"  C:\Users\<user>\Apps\TransitShelf.exe  ", "\"C:\\Users\\<user>\\Apps\\TransitShelf.exe\"")]
    public void 启动命令为带引号的路径(string path, string expected)
        => Assert.Equal(expected, StartupService.BuildCommand(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空路径返回空命令(string? path)
        => Assert.Equal(string.Empty, StartupService.BuildCommand(path));
}
