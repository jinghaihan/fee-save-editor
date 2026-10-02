using Avalonia.Controls;
using Avalonia.Interactivity;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record AchievementRow(string Id, string Label);
    public sealed record AchievementCategoryChoice(AchievementCategory? Category, string Label);
    public bool CanEditAchievements { get; private set; }
    private IReadOnlyList<AchievementProgress> _achievements = [];
    private string? _selectedAchievement;
    private bool _refreshingAchievements;

    public void ShowAchievements()
    {
        MainPanel.IsVisible = ItemsPanel.IsVisible = RosterPanel.IsVisible = EmblemsPanel.IsVisible = SupportsPanel.IsVisible = InspectorPanel.IsVisible = false;
        AchievementsPanel.IsVisible = true;
        ApplyMainButton.IsVisible = false;
        MainNavigation.SelectedIndex = 5;
        RefreshAchievementRecords();
        RefreshPageTitle();
    }

    private void LoadAchievements()
    {
        _refreshingAchievements = true;
        CanEditAchievements = false;
        _achievements = [];
        _selectedAchievement = null;
        AchievementSearch.Clear();
        AchievementCategoryInput.SelectedIndex = 0;
        if (Save is not null && Save.Kind == SaveKind.Game && Save.Sections.Any(row => row.Name == "USER"))
        {
            try { _achievements = Save.ReadAchievements(); CanEditAchievements = true; }
            catch (Exception error) when (IsFileError(error)) { ShowMessage("AchievementsUnavailable", error.Message); }
        }
        _refreshingAchievements = false;
        RefreshAchievementRecords();
    }

    private static string AchievementCategoryName(AchievementCategory category) => UiLanguage.Get("AchievementCategory" + category);
    private static string AchievementStatusName(AchievementStatus status) => status switch
    {
        AchievementStatus.None => UiLanguage.Get("NotAchieved"),
        AchievementStatus.Completed => UiLanguage.Get("RewardClaimed"),
        _ => UiLanguage.Get("RewardAvailable")
    };

    private void RefreshAchievementLanguage()
    {
        if (AchievementCategoryInput is null) return;
        _refreshingAchievements = true;
        var category = (AchievementCategoryInput.SelectedItem as AchievementCategoryChoice)?.Category;
        AchievementCategoryChoice[] choices = [new(null, UiLanguage.Get("AllCategories")),
            .. Enum.GetValues<AchievementCategory>().Select(value => new AchievementCategoryChoice(value, AchievementCategoryName(value)))];
        AchievementCategoryInput.ItemsSource = choices;
        AchievementCategoryInput.SelectedItem = choices.Single(row => row.Category == category);
        _refreshingAchievements = false;
        RefreshAchievementRecords();
    }

    private void RefreshAchievementRecords()
    {
        if (AchievementList is null) return;
        _refreshingAchievements = true;
        AchievementCompletion.Text = CanEditAchievements ? $"{_achievements.Count(row => row.Achieved)}/{_achievements.Count}" : "";
        UnlockAllAchievementsButton.IsEnabled = CanEditAchievements && _achievements.Any(row => !row.Achieved);
        string query = AchievementSearch.Text?.Trim() ?? "";
        var category = (AchievementCategoryInput.SelectedItem as AchievementCategoryChoice)?.Category;
        var rows = _achievements.Where(row => category is null || row.Definition.Category == category)
            .Select(row => new AchievementRow(row.Definition.Id, $"{row.Definition.Name(UiLanguage.Current)} · {AchievementStatusName(row.Status)}"))
            .Where(row => row.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        AchievementList.ItemsSource = rows;
        AchievementList.SelectedItem = rows.FirstOrDefault(row => row.Id == _selectedAchievement) ?? rows.FirstOrDefault();
        _selectedAchievement = (AchievementList.SelectedItem as AchievementRow)?.Id;
        LoadAchievementEditor();
        _refreshingAchievements = false;
    }

    private void LoadAchievementEditor()
    {
        var row = _achievements.FirstOrDefault(value => value.Definition.Id == _selectedAchievement);
        AchievementForm.IsEnabled = row is not null;
        AchievementNameValue.Text = row?.Definition.Name(UiLanguage.Current) ?? "";
        AchievementCategoryValue.Text = row is null ? "" : AchievementCategoryName(row.Definition.Category);
        AchievementStatusValue.Text = row is null ? "" : AchievementStatusName(row.Status);
        AchievementRewardValue.Text = row?.Definition.Reward.ToString() ?? "";
        UnlockAchievementButton.IsEnabled = row is { Achieved: false };
    }

    public bool UnlockAchievements(bool all)
    {
        if (!CanEditAchievements || Save is null) return false;
        string? id = null;
        if (!all)
        {
            if (_selectedAchievement is null) return false;
            id = _selectedAchievement;
        }
        try
        {
            Save = Save.WithUnlockedAchievements(id);
            _achievements = Save.ReadAchievements();
            RefreshAchievementRecords();
            RefreshOverview();
            RefreshSections();
            Message.IsVisible = false;
            return true;
        }
        catch (Exception error) when (IsFileError(error)) { ShowMessage("EditFailed", error.Message); return false; }
    }

    private void AchievementFilter_Changed(object? sender, SelectionChangedEventArgs e) { if (!_refreshingAchievements) RefreshAchievementRecords(); }
    private void AchievementSearch_Changed(object? sender, TextChangedEventArgs e) { if (!_refreshingAchievements) RefreshAchievementRecords(); }
    private void AchievementList_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingAchievements) return;
        _selectedAchievement = (AchievementList.SelectedItem as AchievementRow)?.Id;
        LoadAchievementEditor();
    }
    private void UnlockAchievement_Click(object? sender, RoutedEventArgs e) => UnlockAchievements(false);
    private void UnlockAllAchievements_Click(object? sender, RoutedEventArgs e) => UnlockAchievements(true);
}
