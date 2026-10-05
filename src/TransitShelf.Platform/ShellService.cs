using System.Diagnostics;

namespace TransitShelf.Platform;

/// <summary>调用系统能力打开文件、进入文件夹、在资源管理器中定位。</summary>
public sealed class ShellService
{
    /// <summary>用系统默认程序打开文件；文件夹则用资源管理器进入该目录。</summary>
    public void Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>打开父目录并高亮选中该文件（explorer /select）。</summary>
    public void Reveal(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }
}
