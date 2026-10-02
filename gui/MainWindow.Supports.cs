using Avalonia.Controls;
using Avalonia.Interactivity;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record SupportRow(string Key, string Label);
    public sealed record SupportRankChoice(SupportRank Rank, string Label);
    public bool CanEditSupports { get; private set; }
    private string? _selectedSupport;
    private bool _refreshingSupports;
    private CharacterSupport? SelectedSupport => CanEditSupports && Save is not null
        ? Save.ReadSupports().FirstOrDefault(row => row.Key == _selectedSupport) : null;

    public void ShowSupports()
    {
        if (HasPendingEmblemValues() && !ApplyEmblemValues())
        {
            MainNavigation.SelectedIndex = 3;
            return;
        }
        MainPanel.IsVisible = ItemsPanel.IsVisible = RosterPanel.IsVisible = EmblemsPanel.IsVisible = InspectorPanel.IsVisible = false;
        SupportsPanel.IsVisible = true;
        AchievementsPanel.IsVisible = false;
        MainNavigation.SelectedIndex = 4;
        RefreshSupportRecords(preserveEditor: HasPendingSupportValues());
        RefreshPageTitle();
    }

    private static string SupportRankName(SupportRank rank) => rank switch
    {
        SupportRank.None => UiLanguage.Get("None"),
        SupportRank.APlus => "A+",
        _ => rank.ToString()
    };
    private static string SupportName(CharacterSupport support)
    {
        var pair = SupportCatalog.Pair(support.Key);
        return pair is null ? support.Key : $"{BondPersonName(pair.FirstPersonId)} — {BondPersonName(pair.SecondPersonId)}";
    }

    private void LoadSupports()
    {
        _refreshingSupports = true;
        CanEditSupports = false;
        _selectedSupport = null;
        SupportSearch.Clear();
        SupportRankInput.ItemsSource = Array.Empty<SupportRankChoice>();
        SupportPointsInput.Value = null;
        _refreshingSupports = false;
        if (Save is not null && Save.Kind == SaveKind.Game && Save.Sections.Any(row => row.Name == "UREL"))
        {
            try { Save.ReadSupports(); CanEditSupports = true; }
            catch (Exception error) when (IsFileError(error)) { ShowMessage("SupportsUnavailable", error.Message); }
        }
        RefreshSupportRecords(preserveEditor: false);
    }

    private void RefreshSupportRecords(bool preserveEditor)
    {
        if (SupportList is null) return;
        _refreshingSupports = true;
        var supports = CanEditSupports && Save is not null ? Save.ReadSupports() : [];
        var known = supports.Where(row => SupportCatalog.Pair(row.Key) is not null).ToArray();
        int complete = known.Count(row => row.Rank >= Save!.MaximumSupportRank(row.Key));
        SupportCompletion.Text = CanEditSupports ? $"{complete}/{known.Length}" : "";
        MaxAllSupportsButton.IsEnabled = known.Length != 0;
        string query = SupportSearch.Text?.Trim() ?? "";
        var filtered = supports.Select(row => new SupportRow(row.Key, $"{SupportName(row)} · {SupportRankName(row.Rank)}"))
            .Where(row => row.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        SupportList.ItemsSource = filtered;
        SupportList.SelectedItem = filtered.FirstOrDefault(row => row.Key == _selectedSupport);
        if (!preserveEditor)
        {
            SupportList.SelectedItem ??= filtered.FirstOrDefault();
            _selectedSupport = (SupportList.SelectedItem as SupportRow)?.Key;
            LoadSupportEditor();
        }
        _refreshingSupports = false;
    }

    private void RefreshSupportRanks(SupportRank? selected)
    {
        var support = SelectedSupport;
        var pair = support is null ? null : SupportCatalog.Pair(support.Key);
        var maximum = pair is null ? SupportRank.A : Save!.MaximumSupportRank(support!.Key);
        var choices = Enumerable.Range(0, (int)maximum + 1)
            .Select(rank => new SupportRankChoice((SupportRank)rank, SupportRankName((SupportRank)rank))).ToList();
        if (selected is SupportRank saved && saved > maximum) choices.Add(new(saved, SupportRankName(saved)));
        SupportRankInput.ItemsSource = choices;
        SupportRankInput.SelectedItem = choices.FirstOrDefault(row => row.Rank == selected);
    }

    private void LoadSupportEditor()
    {
        var support = SelectedSupport;
        var pair = support is null ? null : SupportCatalog.Pair(support.Key);
        SupportForm.IsEnabled = pair is not null && support!.Rank <= Save!.MaximumSupportRank(support.Key);
        RefreshSupportRanks(support?.Rank);
        SupportPointsInput.Maximum = pair is null || support is null ? 99 : Math.Max(support.Points, pair.MaximumPoints(support.Rank));
        SupportPointsInput.Minimum = Math.Min(0, support?.Points ?? 0);
        SupportPointsInput.Value = support?.Points;
    }

    private bool HasPendingSupportValues() => SelectedSupport is { } support
        && (SupportRankInput.SelectedItem is not SupportRankChoice choice || choice.Rank != support.Rank
            || SupportPointsInput.Text != support.Points.ToString());

    public bool ApplySupportValues()
    {
        if (Save is null || SelectedSupport is not { } support) return false;
        if (!HasPendingSupportValues()) return true;
        try
        {
            var choice = SupportRankInput.SelectedItem as SupportRankChoice
                ?? throw new ArgumentException(UiLanguage.Get("InvalidAmount"));
            Save = Save.WithSupport(support.Key, choice.Rank, Amount(SupportPointsInput));
            RefreshAfterSupportEdit();
            return true;
        }
        catch (Exception error) when (IsFileError(error)) { ShowMessage("EditFailed", error.Message); return false; }
    }

    public bool MaximizeSupports(bool all)
    {
        if (Save is null || !CanEditSupports) return false;
        try
        {
            string? key = null;
            if (!all) key = SelectedSupport?.Key ?? throw new ArgumentException(UiLanguage.Get("SelectSupport"));
            Save = Save.WithMaximumSupports(key);
            RefreshAfterSupportEdit();
            return true;
        }
        catch (Exception error) when (IsFileError(error)) { ShowMessage("EditFailed", error.Message); return false; }
    }

    private void RefreshAfterSupportEdit()
    {
        RefreshOverview();
        RefreshSections();
        RefreshSupportRecords(preserveEditor: false);
        RefreshEmblemRecords(preserveEditor: false);
        Message.IsVisible = false;
    }

    private void RefreshSupportLanguage()
    {
        _refreshingSupports = true;
        var selected = (SupportRankInput.SelectedItem as SupportRankChoice)?.Rank;
        RefreshSupportRanks(selected);
        _refreshingSupports = false;
        RefreshSupportRecords(preserveEditor: true);
    }

    private void SupportRank_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingSupports || SupportRankInput.SelectedItem is not SupportRankChoice choice
            || SelectedSupport is not { } support || SupportCatalog.Pair(support.Key) is not { } pair) return;
        _refreshingSupports = true;
        SupportPointsInput.Minimum = 0;
        SupportPointsInput.Maximum = pair.MaximumPoints(choice.Rank);
        SupportPointsInput.Value = pair.PointsForRank(choice.Rank);
        _refreshingSupports = false;
    }

    private void UpdateSupportRank()
    {
        if (_refreshingSupports || SelectedSupport is not { } support || SupportCatalog.Pair(support.Key) is not { } pair
            || SupportRankInput.SelectedItem is not SupportRankChoice choice || choice.Rank == SupportRank.APlus
            || !int.TryParse(SupportPointsInput.Text, out int points) || points is < 0 or > 99) return;
        _refreshingSupports = true;
        var rank = pair.RankForPoints(points);
        SupportRankInput.SelectedItem = SupportRankInput.ItemsSource!.Cast<SupportRankChoice>().First(row => row.Rank == rank);
        SupportPointsInput.Maximum = pair.MaximumPoints(rank);
        _refreshingSupports = false;
    }

    private void SupportList_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingSupports || SupportList.SelectedItem is not SupportRow row || row.Key == _selectedSupport) return;
        if (HasPendingSupportValues() && !ApplySupportValues()) { RefreshSupportRecords(preserveEditor: true); return; }
        _selectedSupport = row.Key;
        RefreshSupportRecords(preserveEditor: false);
    }
    private void SupportSearch_Changed(object? sender, TextChangedEventArgs e)
    { if (!_refreshingSupports) RefreshSupportRecords(preserveEditor: true); }
    private void ApplySupport_Click(object? sender, RoutedEventArgs e) => ApplySupportValues();
    private void MaxSupport_Click(object? sender, RoutedEventArgs e) => MaximizeSupports(all: false);
    private void MaxAllSupports_Click(object? sender, RoutedEventArgs e) => MaximizeSupports(all: true);
}
