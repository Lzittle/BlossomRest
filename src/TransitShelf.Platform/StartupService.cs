using Microsoft.Win32;

namespace TransitShelf.Platform;

/// <summary>
/// 开机自启（FR-73）：写当前用户的 Run 项，系统「设置 → 应用 → 启动」里能看到同一项，
/// 用户也可以在那边直接关闭。只操作 HKCU，不请求管理员权限，失败返回 false 不抛出。
/// </summary>
public static class StartupService
{
    /// <summary>HKCU 的 Run 项。用当前用户而不是 HKLM，避免提权。</summary>
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>启动项显示名，同时用于识别和清理自己写入的项。</summary>
    private const string ValueName = "BlossomRest";

    /// <summary>是否已经登记自启（不校验指向的路径是否仍存在，避免把"待修复"误报成未开启）。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string command && command.Length > 0;
        }
        catch (Exception)
        {
            // 注册表读取受组策略限制时按"未开启"处理，界面降级而不是报错。
            return false;
        }
    }

    /// <summary>登记自启并指向当前 exe。重复开启会覆盖旧路径（移动安装位置后可用一次开关修复）。</summary>
    public static bool Enable(string? exePath)
    {
        var command = BuildCommand(exePath);
        if (command.Length == 0)
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            key.SetValue(ValueName, command);
            return true;
        }
        catch (Exception)
        {
            // 安全软件拦截或组策略禁止写入：由调用方提示用户，不抛出。
            return false;
        }
    }

    /// <summary>删除自己写入的启动项；本来就不存在也算成功（幂等）。</summary>
    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 启动命令：路径加引号。不加引号时，`C:\Program Files\...` 这类带空格的路径会被
    /// Windows 拆成"程序 + 参数"，开机时只会弹一个找不到文件的错误。
    /// </summary>
    public static string BuildCommand(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return string.Empty;
        }

        return $"\"{exePath.Trim()}\"";
    }
}
