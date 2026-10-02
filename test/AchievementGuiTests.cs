using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class AchievementGuiTests
{
    public static void Run(MainWindow window, string temporary)
    {
        byte[] original = AchievementTests.Fixture();
        string source = Path.Combine(temporary, "achievement-gui-source");
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source) && window.CanEditAchievements, "Achievement panel was not enabled.");
        var donations = window.FindControl<StackPanel>("DonationInputs")!;
        var settings = window.FindControl<StackPanel>("SettingsInputs")!;
        var donationCard = donations.GetLogicalAncestors().First(control => control.GetType().Name == "GlassCard");
        var settingsCard = settings.GetLogicalAncestors().First(control => control.GetType().Name == "GlassCard");
        Check(donationCard != settingsCard, "Donations are still inside Game Settings.");
        window.ShowAchievements();
        var panel = window.FindControl<Grid>("AchievementsPanel")!;
        Check(panel.IsVisible && !window.FindControl<Grid>("MainPanel")!.IsVisible, "Achievement navigation overlapped Main.");
        var list = window.FindControl<ListBox>("AchievementList")!;
        var search = window.FindControl<TextBox>("AchievementSearch")!;
        var category = window.FindControl<ComboBox>("AchievementCategoryInput")!;
        var status = window.FindControl<TextBox>("AchievementStatusValue")!;
        Check(list.ItemCount == 765 && category.ItemCount == 6 && status.Text == "Not Achieved", "Achievement controls are incomplete.");
        Check(window.FindControl<TextBox>("AchievementRewardValue")!.Text == "30", "Reward preview differs from the catalog.");
        Check(window.UnlockAchievements(false) && window.Save!.ReadAchievements()[0].RewardAvailable, "Single achievement unlock failed.");
        Check(status.Text == "Reward Available" && !window.FindControl<Button>("UnlockAchievementButton")!.IsEnabled,
            "Achieved item remained unlockable.");
        category.SelectedIndex = 1;
        search.Text = "VANDER";
        Dispatcher.UIThread.RunJobs();
        Check(list.ItemCount > 0 && list.Items.Cast<MainWindow.AchievementRow>()
            .All(row => row.Label.Contains("VANDER", StringComparison.OrdinalIgnoreCase)), $"Achievement search is not case insensitive: {list.ItemCount} results.");
        search.Clear();
        Dispatcher.UIThread.RunJobs();
        string selected = ((MainWindow.AchievementRow)list.SelectedItem!).Id;
        for (int pass = 0; pass < 3; pass++)
        {
            window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.AchievementRow)list.SelectedItem!).Id == selected && status.Text == "奖励待领取"
                && window.FindControl<TextBlock>("AchievementNameValue")!.Text!.Contains("凡德雷"), "Achievement fields did not translate together.");
            window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Check(status.Text == "Reward Available" && category.SelectedIndex == 1
                && window.FindControl<TextBlock>("AchievementNameValue")!.Text!.Contains("Vander"), "English or category selection did not recover.");
        }
        category.SelectedIndex = 2;
        Check(list.ItemCount == 167, "Category filter returned the wrong entries.");
        search.Text = "no-match";
        Dispatcher.UIThread.RunJobs();
        Check(list.ItemCount == 0 && !window.FindControl<StackPanel>("AchievementForm")!.IsEnabled,
            "Empty search left a stale achievement editable.");
        var before = window.Save!.ReadMainValues();
        Check(window.UnlockAchievements(true) && window.Save.ReadAchievements().All(row => row.Achieved),
            "Batch achievement unlock obeyed the search/category filter.");
        Check(window.Save.ReadAchievements()[3].RewardClaimed && window.Save.ReadMainValues() == before,
            "Batch unlock reset claimed rewards or awarded fragments.");
        Check(window.FindControl<TextBlock>("AchievementCompletion")!.Text == "765/765"
            && !window.FindControl<Button>("UnlockAllAchievementsButton")!.IsEnabled, "Completion and batch state are stale.");
        string output = Path.Combine(temporary, "achievement-gui-copy");
        Check(window.SaveCopy(output) && EngageSave.Load(output).ReadAchievements().All(row => row.Achieved), "Save Copy lost achievement edits.");
        foreach (var show in new Action[] { window.ShowItems, window.ShowRoster, window.ShowEmblems, window.ShowSupports })
        {
            show();
            Check(!panel.IsVisible, "Achievement panel overlaps another page.");
            window.ShowAchievements();
            Check(panel.IsVisible, "Cannot return to achievements.");
        }
        window.FindControl<TabStrip>("MainNavigation")!.SelectedIndex = 0;
        Check(!panel.IsVisible, "Main did not hide achievements.");
        string malformed = Path.Combine(temporary, "achievement-gui-malformed");
        File.WriteAllBytes(malformed, AchievementTests.Fixture("overflow"));
        Check(window.LoadSave(malformed) && !window.CanEditAchievements && window.CanEditMain,
            "Malformed achievements disabled unrelated Main fields.");
        Check(list.ItemCount == 0 && status.Text == "" && !window.UnlockAchievements(true), "Malformed achievements left stale state.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Achievement UI overwrote the source.");
        Console.WriteLine("Achievement GUI: categories/search, single/batch scopes, translations, cards/navigation and reward-safe copies passed.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
