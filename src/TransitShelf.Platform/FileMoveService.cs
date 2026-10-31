using System.IO;

namespace TransitShelf.Platform;

/// <summary>
/// 把文件或文件夹移到指定目录（FR-23）。目标重名时自动追加「 (2)」「 (3)」……
/// **绝不覆盖已有文件**——移动是少数会真实改动用户文件位置的操作，覆盖等于静默丢数据。
/// </summary>
public sealed class FileMoveService
{
    /// <summary>
    /// 移动成功返回落位路径；源不存在、目标目录不存在、文件夹移入自身内部等情况抛出异常，
    /// 由调用方提示用户。
    /// </summary>
    public MoveOutcome Move(string sourcePath, string targetDirectory)
    {
        var source = Path.GetFullPath(sourcePath);
        var targetDir = Path.GetFullPath(targetDirectory);
        var isFolder = Directory.Exists(source);

        if (!isFolder && !File.Exists(source))
        {
            throw new FileNotFoundException("源文件不存在，可能已被移动或删除", source);
        }

        if (!Directory.Exists(targetDir))
        {
            throw new DirectoryNotFoundException($"目标文件夹不存在：{targetDirectory}");
        }

        if (isFolder && IsInside(targetDir, source))
        {
            throw new IOException("不能把文件夹移动到它自己内部");
        }

        // 已经在目标目录里：不改名、不复制，直接告诉调用方无需操作。
        var parent = Path.GetDirectoryName(source);
        if (parent is not null
            && string.Equals(Path.GetFullPath(parent), targetDir, StringComparison.OrdinalIgnoreCase))
        {
            return new MoveOutcome(source, Moved: false, Renamed: false);
        }

        // Directory.Move 跨盘只能报错；复制后删除整个目录在中途失败时会留下半份数据，
        // 因此明确不支持，让用户改用资源管理器（见技术框架 6.14 / ADR D37）。
        if (isFolder
            && !string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(targetDir), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("文件夹不能跨磁盘移动；请先在资源管理器中移动，再重新添加");
        }

        var name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var desired = Path.Combine(targetDir, name);
        var target = FindFreeTarget(desired, isFolder);

        if (isFolder)
        {
            Directory.Move(source, target);
        }
        else
        {
            File.Move(source, target);
        }

        return new MoveOutcome(target, Moved: true,
            Renamed: !string.Equals(target, desired, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>在目标目录里找第一个空闲名称：name (2).ext、name (3).ext……</summary>
    private static string FindFreeTarget(string desired, bool isFolder)
    {
        if (IsFree(desired))
        {
            return desired;
        }

        var directory = Path.GetDirectoryName(desired)
            ?? throw new IOException("无法确定目标目录");
        var baseName = isFolder ? Path.GetFileName(desired) : Path.GetFileNameWithoutExtension(desired);
        var extension = isFolder ? string.Empty : Path.GetExtension(desired);

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = Path.Combine(directory, $"{baseName} ({suffix}){extension}");
            if (IsFree(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"目标目录重名过多：{Path.GetFileName(desired)}");
    }

    private static bool IsFree(string path) => !File.Exists(path) && !Directory.Exists(path);

    /// <summary>candidate 是否位于 folder 内部（含更深层）。</summary>
    private static bool IsInside(string candidate, string folder)
    {
        var root = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>移动结果：<paramref name="Moved"/> 为 false 表示已在目标目录，<paramref name="Renamed"/> 表示因重名改了名。</summary>
public sealed record MoveOutcome(string Path, bool Moved, bool Renamed);
