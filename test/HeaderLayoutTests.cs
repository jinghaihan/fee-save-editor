using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FeeEditor.Gui;

internal static class HeaderLayoutTests
{
    public static void Run(MainWindow window)
    {
        var layout = window.FindControl<Grid>("WindowLayout")!;
        var header = window.FindControl<Grid>("Header")!;
        var navigation = window.FindControl<TabStrip>("MainNavigation")!;
        var content = window.FindControl<ScrollViewer>("MainScroll")!;
        Check(layout.Margin == new Thickness(0, 0, 0, 24)
            && header.Margin == default && navigation.FontSize == 13,
            "The header must use the FETH template without page margins.");
        foreach (string name in new[]
        {
            "MainScroll", "ItemsPanel", "RosterPanel", "EmblemsPanel", "BondRingsPanel",
            "SupportsPanel", "AchievementsPanel", "InspectorPanel", "Message"
        })
            Check(window.FindControl<Control>(name)!.Margin == new Thickness(24, 0),
                $"{name}: content margins must remain separate from the header.");

        double width = window.Width, height = window.Height;
        foreach (var size in new[] { (1120d, 780d), (860d, 600d) })
        {
            window.Width = size.Item1;
            window.Height = size.Item2;
            Dispatcher.UIThread.RunJobs();
            var headerPosition = header.TranslatePoint(new Point(), layout)!.Value;
            var contentPosition = content.TranslatePoint(new Point(), layout)!.Value;
            Check(headerPosition == new Point() && Math.Abs(header.Bounds.Width - layout.Bounds.Width) <= 1,
                "The header is inset from the top or sides of the window.");
            Check(Math.Abs(contentPosition.Y - header.Bounds.Height - 24) <= 1
                && contentPosition.X == 24
                && Math.Abs(content.Bounds.Width - layout.Bounds.Width + 48) <= 1,
                "Adjusting the header changed the page's spacing or width.");
        }
        window.Width = width;
        window.Height = height;
        Dispatcher.UIThread.RunJobs();
        Console.WriteLine("Header layout: FETH-aligned top/sides and preserved content margins at both window sizes passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
