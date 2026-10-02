using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class RosterStatGuiTests
{
    public static void Run(MainWindow window, string temporary)
    {
        string source = Path.Combine(temporary, "personal-stats-fixture");
        byte[] original = RosterTests.Fixture(baseStrength: 100);
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source), "Could not load the personal stats GUI fixture.");
        window.ShowRoster();
        var tabs = window.FindControl<TabStrip>("RosterTabs")!;
        var single = window.FindControl<Button>("MaximizeRosterStatsButton")!;
        var all = window.FindControl<Button>("MaximizeAllRosterStatsButton")!;
        Check(!single.IsVisible && all.IsEnabled, "Maximum buttons have the wrong initial scope.");
        tabs.SelectedIndex = 1;
        Check(single.IsVisible && single.IsEnabled, "Stats did not show its single-character maximum button.");
        var strength = Input(window, RosterStat.Strength);
        Check(strength.Value == 100 && Preview(window, RosterStat.Strength).Text == "Class: 42 / 42",
            "The GUI displayed the capped value instead of personal storage.");
        Check(window.ApplyRosterStats() && window.Save!.Serialize().AsSpan().SequenceEqual(original),
            "Applying unchanged personal stats flattened overflow.");
        strength = Input(window, RosterStat.Strength);
        strength.Text = "99";
        Dispatcher.UIThread.RunJobs();
        Check(Preview(window, RosterStat.Strength).Text == "Class: 42 / 42", "Personal preview did not update live.");
        window.SetLanguage("zh-Hans");
        Check(Preview(window, RosterStat.Strength).Text == "职业：42 / 42", "The preview did not translate.");
        window.SetLanguage("en");
        Check(strength.Text == "99" && Preview(window, RosterStat.Strength).Text == "Class: 42 / 42",
            "Language switching lost personal edits or left a translated preview.");
        string copy = Path.Combine(temporary, "personal-stats-copy");
        Check(window.SaveCopy(copy) && EngageSave.Load(copy).ReadRoster()[0].Stats[1].PersonalValue == 99,
            "Save Copy lost a personal edit hidden behind the class cap.");
        Check(window.LoadSave(source), "Could not reset the personal stats fixture.");
        strength = Input(window, RosterStat.Strength);
        strength.Text = "-6";
        Dispatcher.UIThread.RunJobs();
        Check(Preview(window, RosterStat.Strength).Text == "Class: 0 / 42" && window.ApplyRosterStats(),
            "A valid signed personal value failed to preview or save.");
        Check(window.Save!.ReadRoster()[0].Stats[1].PersonalValue == -6, "Signed personal storage was not preserved.");
        foreach (string invalid in new[] { "-7", "128", "1.5", "abc", "" })
        {
            byte[] before = window.Save!.Serialize();
            Input(window, RosterStat.Strength).Text = invalid;
            Check(!window.ApplyRosterStats() && window.Save.Serialize().AsSpan().SequenceEqual(before),
                "An invalid personal value was accepted or partially applied.");
        }
        Check(window.LoadSave(source), "Could not reset the maximum-button fixture.");
        single.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var first = window.Save!.ReadRoster()[0];
        int strengthTarget = RosterStats.MaximumPersonalValues(first.PersonHash, first.Progress.Gender)[1];
        Check(first.Stats[1].PersonalValue == strengthTarget && strengthTarget < 100
            && window.Save.ReadRoster()[1].Stats[1].PersonalValue == 3, "The single maximum changed the wrong scope.");
        var classChoice = window.FindControl<ComboBox>("RosterClass")!;
        classChoice.SelectedItem = classChoice.Items.OfType<MainWindow.ClassChoice>().Single(choice => choice.Definition.Id == "JID_ソードマスター");
        Check(Preview(window, RosterStat.Strength).Text == "Class: 41 / 41", "Class selection did not update the stat preview.");
        Check(window.ChangeRosterClass() && window.Save.ReadRoster()[0].Stats.Take(9).All(stat => stat.Value == stat.Maximum),
            "Reclassing a maximized character lost capped attributes.");
        var search = window.FindControl<TextBox>("RosterSearch")!;
        search.Text = "no matches";
        Dispatcher.UIThread.RunJobs();
        Check(window.FindControl<ListBox>("RosterList")!.ItemCount == 0 && all.IsEnabled, "Search disabled the global maximum.");
        all.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(window.Save.ReadRoster()[1].Stats.Take(9).All(stat => stat.Value == stat.Maximum),
            "The global maximum depended on the visible search results.");
        search.Clear();
        Dispatcher.UIThread.RunJobs();
        Check(Input(window, RosterStat.Strength).Value == strengthTarget, "Refreshing after maximum lost the exact target.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "The GUI overwrote the original save.");
        Check(window.LoadSave(source), "Could not restore the GUI fixture.");
        Console.WriteLine("Personal stats GUI: live previews, signed input, class/language switching, pending saves and maximum scopes passed.");
    }

    private static NumericUpDown Input(MainWindow window, RosterStat stat) =>
        window.GetLogicalDescendants().OfType<NumericUpDown>().Single(input => input.Name == $"RosterPersonal{stat}");

    private static TextBlock Preview(MainWindow window, RosterStat stat) =>
        window.GetLogicalDescendants().OfType<TextBlock>().Single(text => text.Name == $"RosterPreview{stat}");

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
