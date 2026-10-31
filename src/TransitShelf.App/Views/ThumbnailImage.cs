using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TransitShelf.Platform;

namespace TransitShelf.App.Views;

/// <summary>
/// 懒加载缩略图（FR-52）。只在条目被界面实际渲染时读取；
/// 容器被回收或滚出视野（Unloaded）时立刻释放，不在内存里留住位图。
/// </summary>
public sealed class ThumbnailImage : Image
{
    /// <summary>
    /// 解码目标边长。条目里显示 32 px，按最高 200% 缩放取 64 px；
    /// 再大只会浪费内存（一张 64 × 64 的位图约 16 KB）。
    /// </summary>
    private const int PixelSize = 64;

    public static readonly DependencyProperty SourcePathProperty = DependencyProperty.Register(
        nameof(SourcePath),
        typeof(string),
        typeof(ThumbnailImage),
        new PropertyMetadata(null, OnSourcePathChanged));

    private CancellationTokenSource? _pending;
    private string? _loadingPath;

    public ThumbnailImage()
    {
        IsHitTestVisible = false;
        Stretch = Stretch.UniformToFill;

        // 折叠面板或虚拟化回收都会触发 Unloaded；重新出现时靠 Loaded 再加载。
        Loaded += (_, _) => BeginLoad(SourcePath);
        Unloaded += (_, _) => Release();
    }

    public string? SourcePath
    {
        get => (string?)GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    private static void OnSourcePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ThumbnailImage)d).BeginLoad(e.NewValue as string);

    private void BeginLoad(string? path)
    {
        // 同一路径正在加载或已有结果时不重复解码（属性变化与 Loaded 都会走到这里）。
        if (path == _loadingPath && (_pending is not null || Source is not null))
        {
            return;
        }

        Release();

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _loadingPath = path;
        var pending = new CancellationTokenSource();
        _pending = pending;
        LoadAsync(path, pending);
    }

    private async void LoadAsync(string path, CancellationTokenSource pending)
    {
        try
        {
            var thumbnail = await Task.Run(() => ThumbnailProvider.TryLoad(path, PixelSize));

            // 期间可能已经被回收或换了路径，结果宁可丢掉也不能把旧图贴到新条目上。
            if (pending.IsCancellationRequested || pending != _pending)
            {
                return;
            }

            Source = thumbnail;
        }
        catch (Exception)
        {
            // 缩略图是可选装饰，失败就保持空图。
        }
    }

    private void Release()
    {
        _pending?.Cancel();
        _pending = null;
        _loadingPath = null;
        Source = null;
    }
}
