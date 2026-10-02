using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class SupportGuiTests
{
    public static void Run(MainWindow window, string temporary)
    {
        string source = Path.Combine(temporary, "support-fixture");
        byte[] original = SupportTests.Fixture();
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source) && window.CanEditSupports, "Support editing was unavailable for valid records.");
        window.FindControl<TabStrip>("MainNavigation")!.SelectedIndex = 5;
        Dispatcher.UIThread.RunJobs();
        Check(window.FindControl<Grid>("SupportsPanel")!.IsVisible && !window.FindControl<Grid>("EmblemsPanel")!.IsVisible,
            "Support navigation did not hide other panels.");
        var list = window.FindControl<ListBox>("SupportList")!;
        var search = window.FindControl<TextBox>("SupportSearch")!;
        var ranks = window.FindControl<ComboBox>("SupportRankInput")!;
        var points = window.FindControl<NumericUpDown>("SupportPointsInput")!;
        Check(list.ItemCount == 232 && ranks.ItemCount == 4 && points.Value == 0, "Support data was not shown.");
        Check(points.Parent is StackPanel { Parent: StackPanel } && ranks.Parent?.Parent == points.Parent.Parent,
            "Support rank and points must be flat, vertically stacked inputs.");
        var pair = SupportCatalog.Pair(((MainWindow.SupportRow)list.SelectedItem!).Key)!;
        ranks.SelectedIndex = 2;
        Check(points.Value == pair.PointsForRank(SupportRank.B), "Rank selection did not map to this pair's points.");
        points.Text = pair.PointsForRank(SupportRank.A).ToString();
        Check(((MainWindow.SupportRankChoice)ranks.SelectedItem!).Rank == SupportRank.A, "Points did not update the rank.");
        for (int pass = 0; pass < 3; pass++)
        {
            window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.SupportRow)list.Items[0]!).Label.Contains("琉尔"), "Chinese support names were not applied.");
            Check(Equals(window.FindControl<Button>("MaxAllSupportsButton")!.Content, "全部支援满级"), "Chinese batch action failed.");
            window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.SupportRow)list.Items[0]!).Label.Contains("Alear")
                && points.Text == pair.PointsForRank(SupportRank.A).ToString(), "Translation lost pending points or English names.");
        }
        Check(window.ApplySupportValues(), "Applying support rank failed.");
        Check(window.Save!.ReadSupports()[0].Rank == SupportRank.A, "The selected pair was not updated.");
        foreach (string invalid in new[] { "-1", "100", "abc", "", "1.5", "2147483648" })
        {
            byte[] before = window.Save.Serialize(); points.Text = invalid;
            Check(!window.ApplySupportValues() && window.Save.Serialize().AsSpan().SequenceEqual(before),
                "Invalid support points changed the save.");
        }
        ranks.SelectedIndex = 1;
        list.SelectedIndex = 1;
        Check(window.Save.ReadSupports()[0].Rank == SupportRank.C, "Selection change lost a pending edit.");
        Check(window.MaximizeSupports(all: false) && window.Save.ReadSupports()[1].Rank == SupportRank.A
            && window.Save.ReadSupports()[2].Rank == SupportRank.None, "Single maximum affected another pair.");
        ranks.SelectedIndex = 2;
        window.ShowItems();
        string copy = Path.Combine(temporary, "support-pending-copy");
        Check(window.SaveCopy(copy) && EngageSave.Load(copy).ReadSupports()[1].Rank == SupportRank.B,
            "Global save lost a pending support edit from another page.");
        window.ShowSupports();
        search.Text = "no-match"; Dispatcher.UIThread.RunJobs();
        Check(list.ItemCount == 0 && window.MaximizeSupports(all: true), "Filtered batch maximum failed.");
        Check(window.Save.ReadSupports().Where(row => SupportCatalog.Pair(row.Key) is not null)
            .All(row => row.Rank == window.Save.MaximumSupportRank(row.Key)), "Search restricted the batch scope.");
        Check(window.FindControl<TextBlock>("SupportCompletion")!.Text == "231/231", "Completion count is wrong.");
        Check(window.LoadSave(source), "Could not reload the support fixture.");
        window.ShowSupports();
        ranks.SelectedIndex = 1;
        window.ShowEmblems();
        Check(window.Save!.ReadSupports()[0].Rank == SupportRank.C, "Switching to Emblems lost pending support edits.");
        var emblems = window.FindControl<ComboBox>("EmblemChoiceInput")!;
        emblems.SelectedIndex = 2;
        window.FindControl<ListBox>("EmblemRecordList")!.SelectedIndex = 1;
        window.FindControl<ComboBox>("EmblemBondLevelInput")!.SelectedItem = 10;
        window.ShowSupports();
        Check(window.Save.ReadSupports().Single(row => row.Key == EmblemTests.Alear + "PID_ユナカ").Rank == SupportRank.B,
            "Switching to Support did not synchronize a pending Alear bond edit.");
        points.Text = "abc";
        window.ShowEmblems();
        Check(window.FindControl<Grid>("SupportsPanel")!.IsVisible
            && window.FindControl<TabStrip>("MainNavigation")!.SelectedIndex == 5,
            "Invalid support input did not retain the editing page and navigation.");
        points.Text = "0";
        Check(window.MaximizeSupports(all: true), "Reloaded batch maximum failed.");
        search.Clear(); Dispatcher.UIThread.RunJobs();
        list.SelectedItem = list.Items.Cast<MainWindow.SupportRow>().Single(row => row.Key == EmblemTests.Alear + "PID_ユナカ");
        Check(ranks.ItemCount == 5 && ((MainWindow.SupportRankChoice)ranks.SelectedItem!).Rank == SupportRank.APlus,
            "The existing Pact partner did not show A+.");
        Check(points.Value == 99, "The Pact support did not use the game's 99-point value.");
        list.SelectedItem = list.Items.Cast<MainWindow.SupportRow>().Last();
        Check(!window.FindControl<StackPanel>("SupportForm")!.IsEnabled && ((MainWindow.SupportRow)list.SelectedItem!).Label.Contains("PID_unknown"),
            "Unknown supports were editable or blank.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Support GUI overwrote the original save.");
        Console.WriteLine("Support GUI: rank/points mapping, search-independent batch, pending saves, Pact limits and live translation passed.");
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
