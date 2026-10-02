using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FeeEditor.Gui;

internal static class ReviewScreenshots
{
    public static void Run(MainWindow window, string directory, string output)
    {
        string source = Path.Combine(directory, "Manual0");
        byte[] original = File.ReadAllBytes(source);
        if (!window.LoadSave(source)) throw new InvalidOperationException("Cannot load the review save.");
        Directory.CreateDirectory(output);
        window.Width = 1232;
        window.Height = 880;
        foreach (var (page, language, show) in new (string, string, Action)[]
        {
            ("main", "en", () => window.FindControl<Avalonia.Controls.Primitives.TabStrip>("MainNavigation")!.SelectedIndex = 0),
            ("roster", "en", window.ShowRoster), ("emblems", "en", window.ShowEmblems),
            ("quantity-items", "en", window.ShowItems),
            ("bond-rings", "en", window.ShowBondRings), ("achievements", "en", window.ShowAchievements),
            ("roster", "ja", window.ShowRoster), ("achievements", "ko", window.ShowAchievements),
            ("main", "zh-Hans", () => window.FindControl<Avalonia.Controls.Primitives.TabStrip>("MainNavigation")!.SelectedIndex = 0)
        })
        {
            window.SetLanguage(language);
            show();
            if (page == "quantity-items")
            {
                window.FindControl<Avalonia.Controls.Primitives.TabStrip>("ItemPages")!.SelectedIndex = 1;
            }
            if (page == "roster") window.FindControl<Avalonia.Controls.Primitives.TabStrip>("RosterTabs")!.SelectedIndex = 0;
            if (page == "achievements") window.FindControl<ComboBox>("AchievementStatusFilter")!.SelectedIndex = 1;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 600)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }
            string path = Path.Combine(output, $"{page}-{language}.png");
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame.");
            frame.Save(path, PngBitmapEncoderOptions.Default);
            Console.WriteLine($"Screenshot: {Path.GetFullPath(path)}");
        }
        if (!window.Save!.Serialize().AsSpan().SequenceEqual(original) || !File.ReadAllBytes(source).AsSpan().SequenceEqual(original))
            throw new InvalidOperationException("Review screenshots changed save data.");
        window.SetLanguage("en");
    }
}
