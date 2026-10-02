using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FeeEditor.Gui;

internal static class AboutTests
{
    public static void Run(MainWindow window, string? screenshot = null)
    {
        Check(AboutWindow.GetVersion() == File.ReadAllText("VERSION").Trim(),
            "The About version does not match the root VERSION file.");
        var help = window.FindControl<MenuItem>("HelpMenu")!;
        var menu = window.FindControl<MenuItem>("AboutMenu")!;
        Check(help.IsEnabled && menu.IsEnabled, "About requires a loaded save.");
        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var about = window.OwnedWindows.OfType<AboutWindow>().Single();
        Check(about.IsVisible && !about.CanResize && about.Icon is not null,
            "The About dialog did not open with the expected window settings.");
        Check(about.FindControl<TextBlock>("VersionValue")!.Text == AboutWindow.GetVersion(),
            "The About dialog did not display the application version.");
        Check(Equals(about.FindControl<Button>("ProjectLink")!.Content, "github.com/jinghaihan/fee-save-editor"),
            "The About project link is incorrect.");
        foreach (var (language, title, header, versionLabel) in new[]
        {
            ("en", "About", "Help", "Version:"), ("zh-Hans", "关于", "帮助", "版本："),
            ("en", "About", "Help", "Version:")
        })
        {
            window.SetLanguage(language);
            Dispatcher.UIThread.RunJobs();
            Check(about.Title == title && Equals(menu.Header, title) && Equals(help.Header, header)
                && about.FindControl<TextBlock>("VersionLabel")!.Text == versionLabel,
                "About did not follow the interface language.");
        }
        if (screenshot is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshot))!);
            using var frame = about.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("The About dialog did not render.");
            frame.Save(screenshot, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        about.Close();
        Dispatcher.UIThread.RunJobs();
        Check(window.IsEnabled && !window.OwnedWindows.Any(), "Closing About did not return to the editor.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
