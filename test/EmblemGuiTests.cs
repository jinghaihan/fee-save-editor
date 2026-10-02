using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class EmblemGuiTests
{
    public static void Run(MainWindow window, string temporary)
    {
        string source = Path.Combine(temporary, "emblem-fixture");
        byte[] original = EmblemTests.Fixture();
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source) && window.CanEditEmblems && window.CanEditBondRings,
            "Emblem forms did not enable for verified sections.");
        window.FindControl<TabStrip>("MainNavigation")!.SelectedIndex = 3;
        Dispatcher.UIThread.RunJobs();
        Check(window.FindControl<Grid>("EmblemsPanel")!.IsVisible && !window.FindControl<Grid>("RosterPanel")!.IsVisible,
            "Navigation did not show only the Emblems panel.");
        var choices = window.FindControl<ComboBox>("EmblemChoiceInput")!;
        var list = window.FindControl<ListBox>("EmblemRecordList")!;
        var search = window.FindControl<TextBox>("EmblemSearch")!;
        var levels = window.FindControl<ComboBox>("EmblemBondLevelInput")!;
        var exp = window.FindControl<NumericUpDown>("EmblemBondExpInput")!;
        var tabs = window.FindControl<TabStrip>("EmblemTabs")!;
        Check(choices.ItemCount == 3 && list.ItemCount == 2 && (int)levels.SelectedItem! == 1 && exp.Value == 0,
            "The Emblem form did not show the saved values.");
        Check(window.FindControl<StackPanel>("EmblemBondForm")!.Width == 340,
            "The Emblem form is not aligned with existing editor forms.");
        Check(exp.Parent is StackPanel { Parent: StackPanel } && levels.Parent?.Parent == exp.Parent.Parent,
            "Bond level and EXP must be stacked directly in the same form, without a disclosure.");
        levels.SelectedItem = 10;
        Check(exp.Text == "99", "Changing bond level did not update EXP.");
        exp.Text = "108";
        Check((int)levels.SelectedItem! == 10, "Changing EXP did not update bond level.");
        for (int pass = 0; pass < 3; pass++)
        {
            window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.EmblemChoice)choices.Items[0]!).Label == "马尔斯", "Chinese Emblem names were not applied.");
            Check(((MainWindow.EmblemRow)list.Items[0]!).Label.Contains("琉尔"), "Chinese character names were not applied.");
            window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.EmblemChoice)choices.Items[0]!).Label == "Marth" && exp.Text == "108",
                "English switching failed or discarded pending bond EXP.");
        }
        Check(window.ApplyEmblemValues() && window.Save!.ReadEmblems()[0].Bonds[0].Experience == 108,
            "The GUI did not apply partial bond EXP.");
        exp.Text = "120";
        Check((int)levels.SelectedItem! == 12, "EXP did not cross a bond-level threshold.");
        window.ShowItems();
        string copy = Path.Combine(temporary, "emblem-pending-copy");
        Check(window.SaveCopy(copy) && EngageSave.Load(copy).ReadEmblems()[0].Bonds[0].Level == 12,
            "Global save omitted pending Emblem edits from another page.");
        window.ShowEmblems();
        foreach (string invalid in new[] { "-1", "209", "1.5", "abc", "", "2147483648" })
        {
            byte[] before = window.Save!.Serialize();
            exp.Text = invalid;
            Check(!window.ApplyEmblemValues() && window.Save.Serialize().AsSpan().SequenceEqual(before),
                "Invalid bond EXP was clamped or changed the save.");
        }
        exp.Text = "131";
        list.SelectedIndex = 1;
        Check(window.Save!.ReadEmblems()[0].Bonds[0].Level == 13
            && (int)levels.SelectedItem! == 1 && ((MainWindow.EmblemRow)list.SelectedItem!).Key == "PID_ユナカ",
            "Changing selected bond lost edits or displayed the wrong selected character.");
        choices.SelectedIndex = 1;
        levels.SelectedItem = 20;
        Check(window.ApplyEmblemValues() && window.Save!.ReadEmblems()[1].Bonds[0].Level == 20,
            "A DLC bond could not be edited in the GUI.");
        search.Text = "no-match";
        Dispatcher.UIThread.RunJobs();
        Check(list.ItemCount == 0, "Bond search did not filter.");
        search.Clear();
        Dispatcher.UIThread.RunJobs();
        tabs.SelectedIndex = 1;
        var stock = window.FindControl<NumericUpDown>("BondRingStockInput")!;
        Check(list.ItemCount == 2 && stock.Value == 7 && !choices.IsVisible
            && window.FindControl<StackPanel>("BondRingForm")!.IsVisible, "Ring tab did not show stock.");
        stock.Text = "99";
        for (int pass = 0; pass < 2; pass++)
        {
            window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.EmblemRow)list.Items[0]!).Label.Contains("希达"), "Chinese ring names did not apply.");
            window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.EmblemRow)list.Items[0]!).Label.Contains("Caeda") && stock.Text == "99",
                "English ring names or pending stock were lost.");
        }
        Check(window.ApplyEmblemValues() && window.Save!.ReadBondRings()[0].StockCount == 99, "Ring stock edit failed.");
        stock.Text = "100";
        Check(!window.ApplyEmblemValues(), "Ring stock exceeded 99.");
        stock.Text = "3";
        string ringCopy = Path.Combine(temporary, "emblem-ring-copy");
        Check(window.SaveCopy(ringCopy) && EngageSave.Load(ringCopy).ReadBondRings()[0].StockCount == 3,
            "Saving omitted pending ring stock.");
        Check(!window.FindControl<Button>("MaxAllEmblemBondsButton")!.IsVisible, "Bond batch action appeared in ring stock.");
        tabs.SelectedIndex = 0;
        choices.SelectedIndex = 0;
        Check(window.MaximizeEmblemBonds(all: false), "Single maximum failed.");
        Check(window.Save!.ReadEmblems()[0].Bonds[0].Level == 20 && window.Save.ReadEmblems()[0].Bonds[1].Level == 1,
            "Single maximum changed another character.");
        search.Text = "no-match";
        Dispatcher.UIThread.RunJobs();
        Check(window.MaximizeEmblemBonds(all: true) && window.Save.ReadEmblems()[0].Bonds.All(bond => bond.Level == 20),
            "Search excluded characters from batch maximum.");
        Check(window.FindControl<TextBlock>("EmblemCompletion")!.Text == "2/2", "Bond completion count is incorrect.");
        search.Clear(); Dispatcher.UIThread.RunJobs();
        choices.SelectedIndex = 2;
        Check(window.MaximizeEmblemBonds(all: true), "Alear batch maximum failed.");
        list.SelectedIndex = 1;
        Check((int)levels.SelectedItem! == 21 && levels.ItemCount == 5 && exp.Value == 209 && exp.IsReadOnly,
            "The Pact partner did not display its special rank and EXP.");
        Check(window.MaximizeEmblemBonds(all: false) && window.Save.ReadEmblems()[2].Bonds[1].Level == 21,
            "Single maximum downgraded the Pact partner.");
        window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
        Check(Equals(window.FindControl<Button>("MaxAllEmblemBondsButton")!.Content, "全部羁绊满级"), "Batch label did not translate.");
        window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
        Check(Equals(window.FindControl<Button>("MaxAllEmblemBondsButton")!.Content, "Max All Bonds"), "Batch label did not recover.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "GUI tests overwrote the source save.");
        Console.WriteLine("Emblem GUI: linked EXP/level, DLC, translation, selection, bounds and pending saves passed.");
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
