using System.Globalization;
using Avalonia.Controls;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record MinigameChoice(string Id, string Name);
    public bool CanEditMinigames { get; private set; }
    private readonly Dictionary<string, MinigameValues> _minigameValues = new(StringComparer.Ordinal);
    private string? _minigameRecordKey;
    private bool _refreshingMinigames;

    private void LoadMinigames()
    {
        CanEditMinigames = false;
        MinigameInputs.IsEnabled = false;
        _refreshingMinigames = true;
        try
        {
            _minigameValues.Clear();
            _minigameRecordKey = null;
            MinigameRecordInput.ItemsSource = null;
            MinigameValueInput.Value = MinigameSizeInput.Value = null;
            MinigameRankInput.SelectedIndex = -1;
            if (Save is null || !CanEditMain) return;
            foreach (var row in Save.ReadMinigameRecords()) _minigameValues.Add(row.Definition.Key, row.Values);
            CanEditMinigames = true;
            MinigameInputs.IsEnabled = true;
            RefreshMinigameRecords();
        }
        catch (Exception error) when (IsFileError(error))
        {
            _minigameValues.Clear();
            ShowMessage("MinigamesUnavailable", error.Message);
        }
        finally { _refreshingMinigames = false; }
    }

    private void RefreshMinigameLanguage()
    {
        bool refreshing = _refreshingMinigames;
        _refreshingMinigames = true;
        try
        {
            string? selected = (MinigameInput.SelectedItem as MinigameChoice)?.Id;
            MinigameInput.ItemsSource = MinigameCatalog.Groups.Select(group =>
                new MinigameChoice(group.Id, group.Name(UiLanguage.Current))).ToArray();
            MinigameInput.SelectedItem = MinigameInput.Items.Cast<MinigameChoice>().FirstOrDefault(row => row.Id == selected)
                ?? MinigameInput.Items[0];
            RefreshMinigameRecords(loadValues: false);
        }
        finally { _refreshingMinigames = refreshing; }
    }

    private void RefreshMinigameRecords(bool loadValues = true)
    {
        if (MinigameInput.SelectedItem is not MinigameChoice selected) return;
        var group = MinigameCatalog.Groups.Single(row => row.Id == selected.Id);
        string? key = _minigameRecordKey;
        MinigameRecordInput.ItemsSource = group.Records.Select(row => new MinigameChoice(row.Key, row.Name(UiLanguage.Current))).ToArray();
        MinigameRecordInput.SelectedItem = MinigameRecordInput.Items.Cast<MinigameChoice>().FirstOrDefault(row => row.Id == key)
            ?? MinigameRecordInput.Items[0];
        _minigameRecordKey = ((MinigameChoice)MinigameRecordInput.SelectedItem!).Id;
        MinigameValueTitle.Text = UiLanguage.Get(selected.Id == "Fishing" ? "Caught" : "BestScore");
        MinigameSizeField.IsVisible = selected.Id == "Fishing";
        Grid.SetColumnSpan(MinigameValueField, selected.Id == "Fishing" ? 1 : 2);
        MinigameRankField.IsVisible = selected.Id is "Fishing" or "WyvernRide";
        MinigameRankTitle.Text = UiLanguage.Get(selected.Id == "Fishing" ? "SizeRank" : "BestRank");
        int rank = MinigameRankInput.SelectedIndex;
        MinigameRankInput.ItemsSource = selected.Id == "Fishing"
            ? MinigameCatalog.FishRanks.Select(UiLanguage.Get).ToArray()
            : MinigameCatalog.WyvernRanks.Select(label => label == "None" ? UiLanguage.Get("None") : label).ToArray();
        MinigameRankInput.SelectedIndex = rank;
        if (loadValues && CanEditMinigames) LoadMinigameRecord();
    }

    private void LoadMinigameRecord()
    {
        if (_minigameRecordKey is null || !_minigameValues.TryGetValue(_minigameRecordKey, out var row)) return;
        SetMinigameNumber(MinigameValueInput, row.Value);
        SetMinigameNumber(MinigameSizeInput, row.BestSize);
        MinigameRankInput.SelectedIndex = row.Rank ?? -1;
    }

    private static void SetMinigameNumber(NumericUpDown input, int? value)
    {
        input.Value = value;
        input.Text = value?.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    private MinigameValues ReadMinigameInput()
    {
        int? rank = null, size = null;
        if (MinigameRankField.IsVisible)
        {
            if (MinigameRankInput.SelectedIndex < 0) throw new ArgumentException(UiLanguage.Get("InvalidAmount"));
            rank = MinigameRankInput.SelectedIndex;
        }
        if (MinigameSizeField.IsVisible) size = Amount(MinigameSizeInput);
        return new(Amount(MinigameValueInput), rank, size);
    }

    private Dictionary<string, MinigameValues> ReadMinigameInputs()
    {
        var values = new Dictionary<string, MinigameValues>(_minigameValues, StringComparer.Ordinal);
        if (_minigameRecordKey is not null) values[_minigameRecordKey] = ReadMinigameInput();
        return values;
    }

    private void Minigame_Changed(object? sender, SelectionChangedEventArgs e) => ChangeMinigameSelection(activity: true);
    private void MinigameRecord_Changed(object? sender, SelectionChangedEventArgs e) => ChangeMinigameSelection(activity: false);

    private void ChangeMinigameSelection(bool activity)
    {
        if (_refreshingMinigames || !CanEditMinigames) return;
        _refreshingMinigames = true;
        try
        {
            if (_minigameRecordKey is not null) _minigameValues[_minigameRecordKey] = ReadMinigameInput();
            if (activity) RefreshMinigameRecords();
            else
            {
                _minigameRecordKey = (MinigameRecordInput.SelectedItem as MinigameChoice)?.Id;
                LoadMinigameRecord();
            }
        }
        catch (Exception error) when (IsFileError(error))
        {
            var group = MinigameCatalog.Groups.Single(row => row.Records.Any(record => record.Key == _minigameRecordKey));
            MinigameInput.SelectedItem = MinigameInput.Items.Cast<MinigameChoice>().Single(row => row.Id == group.Id);
            RefreshMinigameRecords(loadValues: false);
            ShowMessage("EditFailed", error.Message);
        }
        finally { _refreshingMinigames = false; }
    }
}
