using System.IO;

namespace TransitShelf.Platform;

/// <summary>把磁盘上的文件改成条目当前的显示名（FR-40 / FR-50 的落地动作）。</summary>
public sealed class FileRenameService
{
    /// <summary>返回重命名后的完整路径；目标已存在等情况抛出异常，由调用方提示用户。</summary>
    public string Rename(string path, string newName)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new IOException(Loc.Text("TA7DF79DE82A8"));

        var target = Path.Combine(directory, newName);
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            throw new IOException(Loc.Format("TD2A4B5890F65", newName));
        }

        File.Move(path, target);
        return target;
    }
}
