using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;
using SukiUI.Controls;

internal static class MinigameGuiTests
{
    public static void Run(MainWindow window, string temporary)
    {
        string source = Path.Combine(temporary, "minigame-gui-source");
        byte[] original = MinigameTests.Fixture();
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source) && window.CanReadMinigames, "Minigame records were not loaded.");
        var picker = window.FindControl<ComboBox>("MinigameInput")!;
        var records = window.FindControl<ItemsControl>("MinigameRecords")!;
        var cards = new[] { "SettingsCard", "ResourcesCard", "MinigamesCard", "DonationsCard" }
            .Select(name => window.FindControl<GlassCard>(name)!).ToArray();
        var main = window.FindControl<Grid>("MainPanel")!;
        Check(main.ColumnDefinitions.Count == 2 && main.RowDefinitions.Count == 2, "Main is not a two-by-two grid.");
        Check(cards.Select(Grid.GetColumn).SequenceEqual(new[] { 0, 1, 0, 1 })
            && cards.Select(Grid.GetRow).SequenceEqual(new[] { 0, 0, 1, 1 }), "Main cards are in the wrong positions.");
        foreach (var group in MinigameCatalog.Groups)
        {
            picker.SelectedItem = picker.Items.Cast<MainWindow.MinigameChoice>().Single(row => row.Id == group.Id);
            Dispatcher.UIThread.RunJobs();
            Check(records.ItemCount == group.Records.Length, "Activity selection has the wrong record count.");
            Check(records.Items.Cast<MainWindow.MinigameDisplayRow>().Select(row => row.Name)
                .SequenceEqual(group.Records.Select(row => row.Name("en"))), "Record labels do not match the selected activity.");
        }
        for (int pass = 0; pass < 3; pass++)
        {
            window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.MinigameChoice)picker.SelectedItem!).Name == "钓鱼"
                && records.Items.Cast<MainWindow.MinigameDisplayRow>().First().Name == "独角红点鲑",
                "Fishing records did not translate.");
            window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.MinigameChoice)picker.SelectedItem!).Id == "Fishing"
                && records.Items.Cast<MainWindow.MinigameDisplayRow>().First().Name == "Charwhal",
                "Activity selection or translated records changed unexpectedly.");
        }
        Check(!cards[2].GetLogicalDescendants().OfType<NumericUpDown>().Any(), "Unverified minigame editing was exposed.");
        double width = window.Width, height = window.Height;
        foreach (var size in new[] { (1120d, 780d), (860d, 600d) })
        {
            window.Width = size.Item1; window.Height = size.Item2; Dispatcher.UIThread.RunJobs();
            Check(cards.All(card => card.Bounds.Width > 0 && card.Bounds.Height > 0), "A Main card collapsed.");
            Check(cards.All(card => Math.Abs(card.Bounds.Width - cards[0].Bounds.Width) <= 1
                && Math.Abs(card.Bounds.Height - cards[0].Bounds.Height) <= 1), "Main cards have unequal dimensions.");
            Check(cards[0].Bounds.Top == cards[1].Bounds.Top && cards[2].Bounds.Top == cards[3].Bounds.Top
                && cards[2].Bounds.Top > cards[0].Bounds.Bottom && cards[1].Bounds.Left > cards[0].Bounds.Right,
                "Main cards overlap or are not aligned.");
        }
        window.Width = width; window.Height = height; Dispatcher.UIThread.RunJobs();
        string output = Path.Combine(temporary, "minigame-gui-copy");
        Check(window.SaveCopy(output) && File.ReadAllBytes(output).AsSpan().SequenceEqual(original), "Viewing records changed Save Copy.");
        string malformed = Path.Combine(temporary, "minigame-gui-malformed");
        File.WriteAllBytes(malformed, MinigameTests.Fixture("duplicate"));
        Check(window.LoadSave(malformed) && window.CanEditMain && !window.CanReadMinigames && !picker.IsEnabled
            && records.ItemCount == 0, "Malformed records left stale data or disabled unrelated editing.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "GUI testing overwrote the source.");
        Console.WriteLine("Main layout: four aligned cards, localized minigame records, lossless copies and independent validation passed.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
