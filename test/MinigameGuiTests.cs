using Avalonia.Controls;
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
        var picker = window.FindControl<ComboBox>("MinigameInput")!;
        var records = window.FindControl<ComboBox>("MinigameRecordInput")!;
        var value = window.FindControl<NumericUpDown>("MinigameValueInput")!;
        var sizeInput = window.FindControl<NumericUpDown>("MinigameSizeInput")!;
        var rank = window.FindControl<ComboBox>("MinigameRankInput")!;
        var inputs = window.FindControl<StackPanel>("MinigameInputs")!;
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
            Check(records.Items.Count == group.Records.Length, "Activity selection has the wrong record count.");
            Check(records.Items.Cast<MainWindow.MinigameChoice>().Select(row => row.Name)
                .SequenceEqual(group.Records.Select(row => row.Name("en"))), "Record labels do not match the selected activity.");
        }
        for (int pass = 0; pass < 3; pass++)
        {
            window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.MinigameChoice)picker.SelectedItem!).Name == "钓鱼"
                && records.Items.Cast<MainWindow.MinigameChoice>().First().Name == "独角红点鲑",
                "Fishing records did not translate.");
            window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.MinigameChoice)picker.SelectedItem!).Id == "Fishing"
                && records.Items.Cast<MainWindow.MinigameChoice>().First().Name == "Charwhal",
                "Activity selection or translated records changed unexpectedly.");
        }
        Check(value.Maximum == int.MaxValue && sizeInput.Maximum == int.MaxValue && rank.Items.Count == 6,
            "Storage boundaries or fish rank options are incorrect.");
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
        value.Value = 42; value.Text = "42";
        sizeInput.Value = 15; sizeInput.Text = "15";
        rank.SelectedIndex = 5;
        string fishKey = ((MainWindow.MinigameChoice)records.SelectedItem!).Id;
        records.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        records.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
        Check(value.Value == 42 && sizeInput.Value == 15 && rank.SelectedIndex == 5, "Switching records lost pending edits.");
        for (int pass = 0; pass < 3; pass++)
        {
            window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
            Check(Equals(rank.SelectedItem, "巨大") && value.Value == 42 && sizeInput.Value == 15, "Translation lost pending values.");
            window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Check(Equals(rank.SelectedItem, "Giant") && value.Value == 42, "English rank did not recover.");
        }
        picker.SelectedIndex = 3; Dispatcher.UIThread.RunJobs();
        Check(rank.Items.Count == 10 && Equals(rank.Items[1], "SSS") && Equals(rank.Items[9], "F"), "Wyvern rank mapping is incorrect.");
        value.Value = 40000; value.Text = "40000"; rank.SelectedIndex = 1;
        string wyvernKey = ((MainWindow.MinigameChoice)records.SelectedItem!).Id;
        picker.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
        value.Value = 2000; value.Text = "2000";
        string trainingKey = ((MainWindow.MinigameChoice)records.SelectedItem!).Id;
        byte[] before = window.Save!.Serialize();
        value.Text = "1.5";
        picker.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        Check(picker.SelectedIndex == 0 && value.Text == "1.5", "Invalid edits were discarded instead of retaining the record.");
        Check(!window.ApplyMainValues() && window.Save.Serialize().AsSpan().SequenceEqual(before), "Invalid values partially edited Main.");
        value.Text = "2147483648";
        Check(!window.SaveCopy(Path.Combine(temporary, "overflow-copy")), "Overflow values were saved.");
        value.Value = 2000; value.Text = "2000";
        string edited = Path.Combine(temporary, "minigame-gui-edited");
        Check(window.SaveCopy(edited), "Minigame edits were not included in Save Copy.");
        var actual = EngageSave.Load(edited).ReadMinigameRecords().ToDictionary(row => row.Definition.Key, row => row.Values);
        Check(actual[fishKey] == new MinigameValues(42, 5, 15) && actual[wyvernKey] == new MinigameValues(40000, 1)
            && actual[trainingKey] == new MinigameValues(2000), "Edits were lost or applied to the wrong record.");
        Check(window.LoadSave(source), "Source reload failed.");
        string malformed = Path.Combine(temporary, "minigame-gui-malformed");
        File.WriteAllBytes(malformed, MinigameTests.Fixture("duplicate"));
        Check(window.LoadSave(malformed) && window.CanEditMain && !window.CanEditMinigames && !inputs.IsEnabled
            && records.Items.Count == 0, "Malformed records left stale data or disabled unrelated editing.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "GUI testing overwrote the source.");
        Console.WriteLine("Main minigames: four aligned cards, pending edits across selections/languages, rank mappings, atomic validation and Save Copy passed.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
