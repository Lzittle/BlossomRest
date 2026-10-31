using System.IO;
using TransitShelf.Platform;

namespace TransitShelf.Tests;

/// <summary>
/// FR-23「移动到文件夹」的平台层回归测试。重点不是"能移"，而是**绝不覆盖同名文件**、
/// 以及文件夹移到自身内部这类必须提前拒绝的情况（见技术框架 6.14 / ADR D37）。
/// </summary>
public class FileMoveServiceTests
{
    private readonly FileMoveService _mover = new();

    [Fact]
    public void 移动文件后目标出现且内容不变()
    {
        var root = NewSandbox();
        try
        {
            var from = Directory.CreateDirectory(Path.Combine(root, "from")).FullName;
            var to = Directory.CreateDirectory(Path.Combine(root, "to")).FullName;
            var source = Path.Combine(from, "报告.txt");
            File.WriteAllText(source, "内容");

            var outcome = _mover.Move(source, to);

            Assert.True(outcome.Moved);
            Assert.False(outcome.Renamed);
            Assert.False(File.Exists(source));
            Assert.Equal(Path.Combine(to, "报告.txt"), outcome.Path);
            Assert.Equal("内容", File.ReadAllText(outcome.Path));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void 目标重名时追加序号且不覆盖已有文件()
    {
        var root = NewSandbox();
        try
        {
            var from = Directory.CreateDirectory(Path.Combine(root, "from")).FullName;
            var to = Directory.CreateDirectory(Path.Combine(root, "to")).FullName;
            var source = Path.Combine(from, "报告.txt");
            var existing = Path.Combine(to, "报告.txt");
            File.WriteAllText(source, "新内容");
            File.WriteAllText(existing, "旧内容");

            var outcome = _mover.Move(source, to);

            Assert.True(outcome.Moved);
            Assert.True(outcome.Renamed);
            Assert.Equal(Path.Combine(to, "报告 (2).txt"), outcome.Path);
            Assert.Equal("旧内容", File.ReadAllText(existing));
            Assert.Equal("新内容", File.ReadAllText(outcome.Path));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void 移动文件夹时连同内部文件一起移动()
    {
        var root = NewSandbox();
        try
        {
            var from = Directory.CreateDirectory(Path.Combine(root, "from")).FullName;
            var to = Directory.CreateDirectory(Path.Combine(root, "to")).FullName;
            var source = Directory.CreateDirectory(Path.Combine(from, "资料")).FullName;
            File.WriteAllText(Path.Combine(source, "a.txt"), "a");

            var outcome = _mover.Move(source, to);

            Assert.True(outcome.Moved);
            Assert.False(Directory.Exists(source));
            Assert.True(Directory.Exists(outcome.Path));
            Assert.Equal("a", File.ReadAllText(Path.Combine(outcome.Path, "a.txt")));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void 文件夹重名时同样追加序号()
    {
        var root = NewSandbox();
        try
        {
            var from = Directory.CreateDirectory(Path.Combine(root, "from")).FullName;
            var to = Directory.CreateDirectory(Path.Combine(root, "to")).FullName;
            var existing = Directory.CreateDirectory(Path.Combine(to, "资料")).FullName;
            var source = Directory.CreateDirectory(Path.Combine(from, "资料")).FullName;

            var outcome = _mover.Move(source, to);

            Assert.True(outcome.Renamed);
            Assert.Equal(Path.Combine(to, "资料 (2)"), outcome.Path);
            Assert.True(Directory.Exists(existing));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void 已在目标目录时不改动文件()
    {
        var root = NewSandbox();
        try
        {
            var from = Directory.CreateDirectory(Path.Combine(root, "from")).FullName;
            var source = Path.Combine(from, "报告.txt");
            File.WriteAllText(source, "内容");

            var outcome = _mover.Move(source, from);

            Assert.False(outcome.Moved);
            Assert.Equal(source, outcome.Path);
            Assert.True(File.Exists(source));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void 禁止把文件夹移动到自身内部()
    {
        var root = NewSandbox();
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "资料")).FullName;
            var inner = Directory.CreateDirectory(Path.Combine(source, "内部")).FullName;

            Assert.Throws<IOException>(() => _mover.Move(source, inner));
            Assert.True(Directory.Exists(source));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void 源文件不存在时抛错且目标目录不受影响()
    {
        var root = NewSandbox();
        try
        {
            var to = Directory.CreateDirectory(Path.Combine(root, "to")).FullName;

            Assert.Throws<FileNotFoundException>(() => _mover.Move(Path.Combine(root, "不存在.txt"), to));
            Assert.Empty(Directory.EnumerateFileSystemEntries(to));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void 目标文件夹不存在时抛错()
    {
        var root = NewSandbox();
        try
        {
            var source = Path.Combine(root, "报告.txt");
            File.WriteAllText(source, "内容");

            Assert.Throws<DirectoryNotFoundException>(
                () => _mover.Move(source, Path.Combine(root, "不存在的目录")));
            Assert.True(File.Exists(source));
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string NewSandbox()
    {
        var root = Path.Combine(Path.GetTempPath(), "TransitShelfMoveTests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响断言结果。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
