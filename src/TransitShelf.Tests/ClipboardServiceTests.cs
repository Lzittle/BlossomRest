using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;
using TransitShelf.Platform;
using TransitShelf.Platform.Interop;

namespace TransitShelf.Tests;

/// <summary>
/// 剪贴板通道的回归测试（FR-20）。
///
/// 为什么必须"写进去再读回来"：这条链路曾经静默失败——点条目复制文件时 OleSetClipboard 返回 E_NOTIMPL，
/// 被映射成 NotImplementedException 从重试保护的 catch(COMException) / catch(ExternalException) 旁边溜走，
/// 一路逃到 App 的全局兜底被吞掉，界面上连"复制失败"都不提示。
/// 只断言"调用没抛异常"是抓不住这个 bug 的，所以这里是真写、真读、真比对。
///
/// 注意：这些用例会临时改写真实剪贴板，跑完按原内容恢复（截图等非文本格式可能恢复不了）。
/// 只覆盖文件/路径通道（也就是出问题的那条）。文字通道没有写进来：实测在本机，
/// 连 WPF 自己的 Clipboard.SetText 和裸 CF_UNICODETEXT 写入都会被系统拒绝（CLIPBRD_E_CANT_OPEN），
/// 与我们的代码无关，放进单元测试只会变成环境相关的红灯；文字复制由手工验收覆盖。
/// </summary>
public class ClipboardServiceTests
{
    /// <summary>剪贴板可能被别的程序（微信、输入法、剪贴板工具）占住，给几秒钟的重试窗口。</summary>
    private const int ClipboardTimeoutMs = 6000;

    private static readonly string[] SamplePaths =
    [
        @"C:\Users\<user>\Downloads\季度报告.pptx",
        @"C:\Users\<user>\xwechat_files\wxid_demo\msg\file\数据表.xlsx",
    ];

    [Fact]
    public void 按DROPFILES格式组装宽字符路径列表()
    {
        var buffer = NativeMethods.BuildFileDropBuffer(SamplePaths);

        Assert.Equal(20u, BitConverter.ToUInt32(buffer, 0)); // pFiles：文件列表的字节偏移
        Assert.Equal(1, BitConverter.ToInt32(buffer, 16));   // fWide：列表是 UTF-16

        var list = Encoding.Unicode.GetString(buffer, 20, buffer.Length - 20);
        Assert.Equal(string.Concat(SamplePaths.Select(path => path + "\0")) + "\0", list);
    }

    [Fact]
    public void 复制文件后剪贴板里能原样读回路径列表()
    {
        RunSta(() => WithClipboardPreserved(() =>
        {
            var service = new ClipboardService();

            AssertClipboardAccepts(() => service.CopyFiles(SamplePaths), "复制文件");

            var pasted = ReadClipboard(() => Clipboard.GetFileDropList().Cast<string>().ToArray());
            Assert.Equal(SamplePaths, pasted);
        }));
    }

    [Fact]
    public void 后备通道不经过OLE也能把路径写进剪贴板()
    {
        RunSta(() => WithClipboardPreserved(() =>
        {
            AssertClipboardAccepts(() => NativeMethods.TrySetClipboardFileDrop(SamplePaths), "写入 CF_HDROP");

            var pasted = ReadClipboard(() => Clipboard.GetFileDropList().Cast<string>().ToArray());
            Assert.Equal(SamplePaths, pasted);
        }));
    }

    [Fact]
    public void 混合复制同时保留文件列表和文字内容()
    {
        RunSta(() => WithClipboardPreserved(() =>
        {
            var service = new ClipboardService();
            const string text = "多选文字\n第二行";

            AssertClipboardAccepts(() => service.CopyMixed(SamplePaths, text), "复制混合内容");

            Assert.Equal(SamplePaths, ReadClipboard(() => Clipboard.GetFileDropList().Cast<string>().ToArray()));
            Assert.Equal(text, ReadClipboard(Clipboard.GetText));
        }));
    }

    /// <summary>
    /// 重试到超时为止：剪贴板被别的程序短暂占用是常态，不该判成失败；
    /// 超过窗口仍写不进去，说明这条通道真的坏了（或环境把它长期占住了）。
    /// </summary>
    private static void AssertClipboardAccepts(Func<bool> attempt, string what)
    {
        var deadline = Environment.TickCount64 + ClipboardTimeoutMs;
        while (true)
        {
            if (attempt())
            {
                return;
            }

            Assert.True(
                Environment.TickCount64 < deadline,
                $"{what}在 {ClipboardTimeoutMs} ms 内一直失败：剪贴板被其他程序长期占用，或写入通道有问题");
            Thread.Sleep(150);
        }
    }

    /// <summary>
    /// 读取也要重试：写入成功后，剪贴板的所有权从 OLE 交回系统的一瞬间仍可能被占用，
    /// 直接读会抛出 <c>CLIPBRD_E_CANT_OPEN</c>。这类失败与写入重试同理，属于环境噪声，
    /// 不该让「写进去了但读不回来」变成假红灯；超过窗口仍读不到才判失败。
    /// </summary>
    private static T ReadClipboard<T>(Func<T> read)
    {
        var deadline = Environment.TickCount64 + ClipboardTimeoutMs;
        Exception? lastFailure = null;
        while (true)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                lastFailure = ex;
            }

            Assert.True(
                Environment.TickCount64 < deadline,
                $"读取剪贴板在 {ClipboardTimeoutMs} ms 内一直失败：{lastFailure?.Message}");
            Thread.Sleep(150);
        }
    }

    /// <summary>WPF 的剪贴板 API 只能在 STA 线程上调用，xunit 默认跑在 MTA 线程池上。</summary>
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "剪贴板测试超时");
        failure?.Throw();
    }

    /// <summary>测试不应当把用户正在用的剪贴板内容顶掉，跑完尽量还原。</summary>
    private static void WithClipboardPreserved(Action body)
    {
        IDataObject? saved = null;
        try
        {
            saved = Clipboard.GetDataObject();
        }
        catch (Exception)
        {
            // 读不到就当剪贴板本来是空的
        }

        try
        {
            body();
        }
        finally
        {
            if (saved is not null)
            {
                try
                {
                    Clipboard.SetDataObject(saved, true);
                }
                catch (Exception)
                {
                    // 还原失败不影响测试结论
                }
            }
        }
    }
}
