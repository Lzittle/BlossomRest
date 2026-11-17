using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TransitShelf.App.Views;
using TransitShelf.Core;
using TransitShelf.Core.Localization;
using TransitShelf.Core.Persistence;
using TransitShelf.Core.Settings;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (Mutex.TryOpenExisting(@"Local\TransitShelf.SingleInstance", out var existing))
        {
            existing.Dispose();
            throw new InvalidOperationException("Exit Blossom Rest before running isolated UI verification.");
        }
        var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine("workbench", "verification", "bilingual-" + Guid.NewGuid().ToString("n")));
        var allowed = Path.GetFullPath("workbench/verification") + Path.DirectorySeparatorChar;
        if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Use an isolated verification directory.");
        for (var directory = new DirectoryInfo(root); directory is not null && directory.FullName.Length >= allowed.Length - 1; directory = directory.Parent)
        {
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new ArgumentException("Verification directories must not be junctions or symbolic links.");
        }
        Directory.CreateDirectory(root);
        Console.WriteLine("REPORT: " + root);
        Environment.SetEnvironmentVariable("TRANSITSHELF_DATA_ROOT", root);
        var stateFile = Path.Combine(root, "state.json");
        var resume = args.Length > 1 && args[1] == "resume";
        const string userGroup = "项目资料";
        const string userText = "这是用户原文；语言切换不能修改。";
        if (!resume)
        {
            if (File.Exists(stateFile)) throw new InvalidOperationException("Fresh fixture already exists.");
            var sample = Path.Combine(root, "Project notes.txt");
            File.WriteAllText(sample, "Sample file for the English UI preview.");
            var items = new[]
            {
                new ShelfItem { Id = "file", Kind = ItemKind.File, DisplayName = "Project notes.txt", Path = sample, CreatedAt = DateTimeOffset.Now, Pinned = true },
                new ShelfItem { Id = "text", Kind = ItemKind.Text, DisplayName = "Meeting notes", Text = userText, CreatedAt = DateTimeOffset.Now },
            };
            var groups = new[] { new ShelfGroup { Id = "group", Name = userGroup, CreatedAt = DateTimeOffset.Now } };
            new ItemStore(stateFile).Save(items, groups, new ShelfSettings { Language = "en", EnableWorkspace = true });
        }
        Console.WriteLine("FIXTURE_ITEMS_BEFORE_START=" + new ItemStore(stateFile).LoadSnapshot().Items.Count);
        var app = new TransitShelf.App.App();
        app.InitializeComponent();
        var completed = false;
        _ = app.Dispatcher.InvokeAsync(async () =>
        {
            var failures = 0;
            void Check(string name, bool ok)
            {
                Console.WriteLine(name + ": " + (ok ? "PASS" : "FAIL"));
                if (!ok) failures++;
            }
            try
            {
                for (var i = 0; i < 200 && (app.MainWindow is not ShelfWindow candidate || !candidate.IsLoaded); i++) await Task.Delay(50);
                if (app.MainWindow is not ShelfWindow main || !main.IsLoaded) throw new InvalidOperationException("Main window did not finish loading.");
                if (!resume)
                {
                    var pill = (Border)main.FindName("PillPanel");
                    var brand = ((Grid)pill.Child).Children.OfType<Image>().Single();
                    Check("brand bitmap decode budget", brand.Source is BitmapSource bitmap && bitmap.PixelWidth == 128 && bitmap.PixelHeight == 128);
                    Capture(main, Path.Combine(root, "shelf-collapsed.en.png"));
                }
                main.ShowPanel();
                await Idle();
                Check(resume ? "persisted English startup" : "English startup title", main.Title == "Blossom Rest");
                Check("English workspace tab", ((Button)main.FindName("WorkspaceTab")).Content?.ToString() == "Workspace");
                Check("English panel title", ((TextBlock)main.FindName("PanelTitle")).Text == "Shelf");
                Check("English selection action", ((Button)main.FindName("MultiSelectButton")).Content?.ToString() == "Select");
                var first = new ItemStore(stateFile).LoadSnapshot();
                Console.WriteLine("FIXTURE_ITEMS_ON_DISK=" + first.Items.Count + " UI=" + ((ListBox)main.FindName("ItemList")).Items.Count);
                Console.WriteLine("DATA_ROOT_MATCH=" + (TransitShelf.Platform.AppPaths.StateFile == stateFile));
                Check("original group name preserved", first.Groups.Single().Name == userGroup);
                Check("original text preserved", first.Items.Single(i => i.Id == "text").Text == userText);
                if (!resume)
                {
                    var title = (TextBlock)main.FindName("PanelTitle");
                    var actions = (Panel)main.FindName("HeaderActions");
                    var titleRight = title.TranslatePoint(new Point(title.ActualWidth, 0), main).X;
                    var actionsLeft = actions.TranslatePoint(new Point(0, 0), main).X;
                    Check("English header avoids overlap", actionsLeft >= titleRight + 4);
                    await Task.Delay(3500);
                    Capture(main, Path.Combine(root, "shelf-items.en.png"));
                    Check("English tray first open", OpenTray(app).Any(text => text.StartsWith("Settings", StringComparison.Ordinal)));
                    var settings = new SettingsWindow(main) { ShowActivated = false, Owner = main };
                    settings.Show();
                    await Idle();
                    Check("English settings title", settings.Title == "Blossom Rest · Settings");
                    Check("English settings hint", ((TextBlock)settings.FindName("RangeHint")).Text.StartsWith("Allowed ranges:"));
                    Capture(settings, Path.Combine(root, "settings.en.png"));
                    ((Button)settings.FindName("ChineseLanguageButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Idle();
                    Check("live switch to Chinese", main.Title == "中转站" && settings.Title == "中转站 · 设置");
                    Check("Chinese dynamic panel", ((TextBlock)main.FindName("PanelTitle")).Text == "临时区");
                    Check("Chinese tray updates on open", OpenTray(app).Any(text => text.StartsWith("设置", StringComparison.Ordinal)));
                    ((Button)settings.FindName("EnglishLanguageButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Idle();
                    Check("live switch back to English", main.Title == "Blossom Rest" && settings.Title == "Blossom Rest · Settings");
                    Check("English dynamic filter", ((Button)main.FindName("FilterTypeButton")).Content?.ToString() == "All types");
                    ((Button)main.FindName("MultiSelectButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Idle();
                    var batch = (Panel)main.FindName("BatchBar");
                    Check("English selection count", ((TextBlock)main.FindName("BatchCountText")).Text == "0 selected");
                    Check("English batch fits panel", batch.TranslatePoint(new Point(batch.ActualWidth, 0), main).X <= main.ActualWidth);
                    ((Button)main.FindName("BatchSelectAllButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Idle();
                    Check("English select all count", ((TextBlock)main.FindName("BatchCountText")).Text == "2 selected");
                    var clipboard = new TransitShelf.Platform.ClipboardService();
                    for (var attempt = 0; !clipboard.CopyText("SENTINEL_BILINGUAL"); attempt++)
                    {
                        if (attempt >= 20) throw new InvalidOperationException("Clipboard stayed busy while setting the test sentinel.");
                        await Task.Delay(100);
                    }
                    batch.Children.OfType<Button>().First(button => button.Content?.ToString() == "Copy").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Idle();
                    var copied = await ReadClipboard();
                    Check("English copy keeps original text", copied.Contains(userText));
                    Check("English copy keeps source path", copied.Contains(Path.Combine(root, "Project notes.txt")));
                    Check("English copy status", ((TextBlock)main.FindName("StatusText")).Text.StartsWith("Copied "));
                    batch.Children.OfType<Button>().First(button => button.Content?.ToString() == "Copy text").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Idle();
                    Check("English text-only copy", await ReadClipboard() == userText);
                    settings.Close();
                    await Task.Delay(900);
                    var saved = new ItemStore(stateFile).LoadSnapshot();
                    Check("English choice saved", saved.Settings.Language == "en");
                    Check("group unchanged after both switches", saved.Groups.Single().Name == userGroup);
                    Check("text unchanged after both switches", saved.Items.Single(i => i.Id == "text").Text == userText);
                }
                Console.WriteLine("UI_CHECKS_FAILED=" + failures);
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine(ex);
            }
            finally { completed = true; app.Shutdown(failures == 0 ? 0 : 1); }
        });
        var result = app.Run();
        if (!completed) throw new InvalidOperationException("The application exited before UI verification completed.");
        return result;
    }

    private static IReadOnlyList<string> OpenTray(TransitShelf.App.App app)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var tray = typeof(TransitShelf.App.App).GetField("_tray", flags)!.GetValue(app)!;
        var menu = tray.GetType().GetProperty("ContextMenuStrip")!.GetValue(tray)!;
        var opening = new System.ComponentModel.CancelEventArgs(true);
        menu.GetType().GetMethod("OnOpening", flags)!.Invoke(menu, [opening]);
        if (opening.Cancel) throw new InvalidOperationException("Tray opening was cancelled after population.");
        return ((System.Collections.IEnumerable)menu.GetType().GetProperty("Items")!.GetValue(menu)!)
            .Cast<object>().Select(item => item.GetType().GetProperty("Text")!.GetValue(item)?.ToString() ?? string.Empty).ToArray();
    }

    private static async Task<string> ReadClipboard()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try { return Clipboard.GetText(); }
            catch (System.Runtime.InteropServices.COMException) { await Task.Delay(200); }
        }
        throw new InvalidOperationException("Clipboard stayed busy while reading the test result.");
    }

    private static async Task Idle()
    {
        await Task.Delay(250);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * 2), (int)Math.Ceiling(window.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
