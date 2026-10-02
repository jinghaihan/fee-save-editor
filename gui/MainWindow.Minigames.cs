using System.Globalization;
using Avalonia.Controls;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record MinigameChoice(string Id, string Name);
    public sealed record MinigameDisplayRow(string Name, string Value);
    public bool CanReadMinigames { get; private set; }
    private IReadOnlyList<MinigameRecord> _minigameRecords = [];

    private void LoadMinigames()
    {
        CanReadMinigames = false;
        MinigameInput.IsEnabled = false;
        _minigameRecords = [];
        MinigameRecords.ItemsSource = null;
        if (Save is null || !CanEditMain) return;
        try
        {
            _minigameRecords = Save.ReadMinigameRecords();
            CanReadMinigames = true;
            MinigameInput.IsEnabled = true;
            RefreshMinigameRecords();
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("MinigamesUnavailable", error.Message);
        }
    }

    private void RefreshMinigameLanguage()
    {
        string? selected = (MinigameInput.SelectedItem as MinigameChoice)?.Id;
        MinigameInput.ItemsSource = MinigameCatalog.Groups.Select(group =>
            new MinigameChoice(group.Id, group.Name(UiLanguage.Current))).ToArray();
        MinigameInput.SelectedItem = MinigameInput.Items.Cast<MinigameChoice>().FirstOrDefault(row => row.Id == selected)
            ?? MinigameInput.Items[0];
        RefreshMinigameRecords();
    }

    private void RefreshMinigameRecords()
    {
        if (MinigameInput.SelectedItem is not MinigameChoice selected) return;
        var group = MinigameCatalog.Groups.Single(row => row.Id == selected.Id);
        MinigameValueTitle.Text = UiLanguage.Get(selected.Id == "Fishing" ? "Caught" : "BestScore");
        var keys = group.Records.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        MinigameRecords.ItemsSource = _minigameRecords.Where(row => keys.Contains(row.Definition.Key))
            .Select(row => new MinigameDisplayRow(row.Definition.Name(UiLanguage.Current),
                row.Value.ToString("N0", CultureInfo.InvariantCulture))).ToArray();
    }

    private void Minigame_Changed(object? sender, SelectionChangedEventArgs e) => RefreshMinigameRecords();
}
