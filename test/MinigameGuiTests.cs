using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        Check(window.LoadSave(source) && window.CanEditMinigames, "Minigame records were not loaded.");
        window.ShowMinigames();
        var panel = window.FindControl<Grid>("MinigamesPanel")!;
        var main = window.FindControl<Grid>("MainPanel")!;
        var cards = MinigameCatalog.Groups.Select(group => Card(window, group.Id)).ToArray();
        var navigation = window.FindControl<TabStrip>("MainNavigation")!;
        Check(navigation.ItemCount == 8 && navigation.SelectedIndex == 5 && panel.IsVisible && !main.IsVisible,
            "Minigames must have an independent top-level page.");
        navigation.SelectedIndex = 6;
        Check(window.FindControl<Grid>("AchievementsPanel")!.IsVisible && !panel.IsVisible,
            "Achievements must follow Minigames in top-level navigation.");
        navigation.SelectedIndex = 7;
        Check(window.FindControl<Grid>("InspectorPanel")!.IsVisible
            && !window.FindControl<Grid>("AchievementsPanel")!.IsVisible
            && window.FindControl<Control>("PageTitle") is null,
            "Inspector must be the final tab without a duplicated page title.");
        var menu = window.FindControl<MenuItem>("LanguageMenu")!.Parent as Menu;
        Check(menu is not null && menu.ItemCount == 3,
            "Only File, Language and Help should remain in the menu.");
        navigation.SelectedIndex = 5;
        Check(panel.IsVisible && !window.FindControl<Grid>("AchievementsPanel")!.IsVisible,
            "Selecting Minigames must return to the correct page.");
        Check(!main.GetLogicalDescendants().OfType<GlassCard>().Any(card => card.Name?.StartsWith("Minigame", StringComparison.Ordinal) == true),
            "Minigames still appear inside Main.");
        Check(panel.Parent == window.FindControl<ScrollViewer>("MinigamesScroll")
            && cards.All(card => !card.GetLogicalDescendants().OfType<ScrollViewer>().Any()),
            "Minigames must scroll as a whole page, not inside cards.");
        Check(cards.SelectMany(card => card.GetLogicalDescendants().OfType<NumericUpDown>()).Count() == 55
            && cards.SelectMany(card => card.GetLogicalDescendants().OfType<ComboBox>()).Count() == 23,
            "All 78 record fields must be directly available without an activity/record selector.");
        Check(Grid.GetColumnSpan(window.FindControl<StackPanel>("FishingMinigameCards")!) == 2,
            "Fishing must use a full-width table.");
        foreach (var group in MinigameCatalog.Groups)
        {
            var grid = (Grid)((StackPanel)Card(window, group.Id).Content!).Children[1];
            Check(grid.RowDefinitions.Count == group.Records.Length + 1, "A minigame record was omitted.");
            Check(((TextBlock)((StackPanel)Card(window, group.Id).Content!).Children[0]).Text == group.Name("en"),
                "A minigame card has the wrong title.");
        }
        string fishKey = MinigameCatalog.Groups[4].Records[0].Key;
        string wyvernKey = MinigameCatalog.Groups[3].Records[0].Key;
        var value = Number(window, fishKey);
        var sizeInput = Number(window, fishKey + "Size");
        var rank = Rank(window, fishKey);
        Check(value.Maximum == int.MaxValue && sizeInput.Maximum == int.MaxValue && rank.ItemCount == 6,
            "Storage boundaries or fish ranks are incorrect.");
        double width = window.Width, height = window.Height;
        foreach (var size in new[] { (1120d, 780d), (860d, 600d) })
        {
            window.Width = size.Item1; window.Height = size.Item2; Dispatcher.UIThread.RunJobs();
            Check(cards.All(card => card.Bounds.Width > 0 && card.Bounds.Height > 0), "A minigame card collapsed.");
            Check(Math.Abs(cards[0].Bounds.Width - cards[1].Bounds.Width) <= 1,
                "The minigame columns have unequal widths.");
            foreach (var card in cards)
            {
                var rows = (Grid)((StackPanel)card.Content!).Children[1];
                foreach (var row in rows.Children.GroupBy(Grid.GetRow))
                {
                    Control? previous = null;
                    foreach (var control in row.OrderBy(Grid.GetColumn))
                    {
                        var position = control.TranslatePoint(new Point(), rows)!.Value;
                        Check(position.X >= 0 && position.X + control.Bounds.Width <= rows.Bounds.Width + 1,
                            "A record control extends past its card.");
                        if (previous is not null)
                            Check(position.X >= previous.TranslatePoint(new Point(), rows)!.Value.X + previous.Bounds.Width + 23,
                                "Record columns overlap or lose their spacing.");
                        previous = control;
                    }
                }
            }
        }
        window.Width = width; window.Height = height;
        string output = Path.Combine(temporary, "minigame-gui-copy");
        Check(window.SaveCopy(output) && File.ReadAllBytes(output).AsSpan().SequenceEqual(original), "Viewing cards changed Save Copy.");
        value.Text = "42"; sizeInput.Text = "15"; rank.SelectedIndex = 5;
        Number(window, wyvernKey).Text = "40000"; Rank(window, wyvernKey).SelectedIndex = 1;
        foreach (var group in MinigameCatalog.Groups.Take(3)) Number(window, group.Records[0].Key).Text = "2000";
        for (int pass = 0; pass < 3; pass++)
        {
            window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
            Check(Equals(rank.SelectedItem, "巨大") && value.Text == "42" && sizeInput.Text == "15", "Translation lost pending records.");
            window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Check(Equals(rank.SelectedItem, "Giant") && value.Text == "42", "English labels did not recover.");
        }
        window.ShowItems(); window.ShowMinigames();
        Check(value.Text == "42" && Number(window, wyvernKey).Text == "40000", "Navigation lost pending records.");
        byte[] before = window.Save!.Serialize();
        foreach (string invalid in new[] { "1.5", "-1", "2147483648", "abc", "" })
        {
            value.Text = invalid;
            window.SetLanguage("ja"); window.SetLanguage("en");
            Check(value.Text == invalid && !window.ApplyMainValues() && window.Save.Serialize().AsSpan().SequenceEqual(before),
                "Invalid record input was discarded or partially committed.");
        }
        Check(!window.SaveCopy(Path.Combine(temporary, "invalid-minigame-copy")), "An invalid record was saved.");
        value.Text = "42";
        rank.SelectedIndex = -1;
        Check(!window.ApplyMainValues() && window.Save.Serialize().AsSpan().SequenceEqual(before), "Missing rank partly edited the save.");
        rank.SelectedIndex = 5;
        string edited = Path.Combine(temporary, "minigame-gui-edited");
        Check(window.SaveCopy(edited), "Minigame edits were not saved from their independent page.");
        var actual = EngageSave.Load(edited).ReadMinigameRecords().ToDictionary(row => row.Definition.Key, row => row.Values);
        Check(actual[fishKey] == new MinigameValues(42, 5, 15) && actual[wyvernKey] == new MinigameValues(40000, 1)
            && MinigameCatalog.Groups.Take(3).All(group => actual[group.Records[0].Key] == new MinigameValues(2000)),
            "Simultaneous card edits were lost or applied to the wrong record.");
        foreach (var show in new Action[] { window.ShowItems, window.ShowRoster, window.ShowEmblems, window.ShowBondRings, window.ShowSupports, window.ShowAchievements })
        {
            show();
            Check(!panel.IsVisible, "Minigames overlap another page.");
            window.ShowMinigames();
        }
        navigation.SelectedIndex = 0;
        Check(!panel.IsVisible && main.IsVisible, "Minigames overlap Main.");
        string malformed = Path.Combine(temporary, "minigame-gui-malformed");
        File.WriteAllBytes(malformed, MinigameTests.Fixture("duplicate"));
        Check(window.LoadSave(malformed) && window.CanEditMain && !window.CanEditMinigames
            && !value.IsEnabled && value.Value is null && rank.SelectedIndex == -1,
            "Malformed records left stale data or disabled unrelated Main editing.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "GUI testing overwrote the source.");
        Console.WriteLine("Minigame cards: independent tab, all 78 fields, natural sizing, spacing, translations, navigation and atomic Save Copy passed.");
    }

    private static GlassCard Card(MainWindow window, string group) => window.GetLogicalDescendants().OfType<GlassCard>()
        .Single(card => card.Name == "Minigame" + group + "Card");
    private static NumericUpDown Number(MainWindow window, string key) => window.GetLogicalDescendants().OfType<NumericUpDown>()
        .Single(input => Equals(input.Tag, key));
    private static ComboBox Rank(MainWindow window, string key) => window.GetLogicalDescendants().OfType<ComboBox>()
        .Single(input => Equals(input.Tag, key + "Rank"));
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
