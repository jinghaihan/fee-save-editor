using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record RosterRow(int Index, string Label);
    public bool CanEditRoster { get; private set; }
    private int? _selectedCharacter;
    private int? _selectedCharacterItem;
    private bool _refreshingRoster;
    private readonly Dictionary<RosterStat, (TextBlock Label, NumericUpDown Input)> _rosterStats = new();
    private RosterCharacter? SelectedCharacter => CanEditRoster && Save is not null
        ? Save.ReadRoster().FirstOrDefault(character => character.Index == _selectedCharacter) : null;

    public void ShowRoster()
    {
        MainPanel.IsVisible = ItemsPanel.IsVisible = InspectorPanel.IsVisible = false;
        RosterPanel.IsVisible = true;
        ApplyMainButton.IsVisible = false;
        MainNavigation.SelectedIndex = 2;
        RefreshPageTitle();
    }

    private void LoadRoster()
    {
        CanEditRoster = false;
        _selectedCharacter = null;
        _selectedCharacterItem = null;
        RosterGeneralForm.IsEnabled = RosterStatsForm.IsEnabled = RosterItemsForm.IsEnabled = false;
        RosterList.ItemsSource = Array.Empty<RosterRow>();
        RosterItemsList.ItemsSource = Array.Empty<InventoryRow>();
        RosterName.Clear();
        RosterClass.Clear();
        RosterLevel.Value = RosterExperience.Value = RosterSkillPoints.Value = null;
        RosterStatsInputs.Children.Clear();
        _rosterStats.Clear();
        RosterSearch.Clear();
        if (Save is null || Save.Kind != SaveKind.Game)
            return;
        try
        {
            Save.ReadRoster();
            CanEditRoster = true;
            RefreshRoster(selectEditor: true);
        }
        catch (Exception error) when (IsFileError(error))
        {
            // A missing/unsupported UNIT section must not disable other supported panels.
            if (Save.Sections.Any(section => section.Name == "UNIT"))
                ShowMessage("RosterUnavailable", error.Message);
        }
    }

    private static string CharacterName(RosterCharacter character) =>
        RosterCatalog.Person(character.PersonHash)?.Name(UiLanguage.Current)
        ?? $"{UiLanguage.Get("UnknownCharacter")} (0x{character.PersonHash:X8})";

    private void RefreshRoster(bool selectEditor)
    {
        if (!CanEditRoster || Save is null)
            return;
        string query = RosterSearch.Text?.Trim() ?? "";
        var rows = Save.ReadRoster().Where(character => character.Force is not UnitForce.Enemy and not UnitForce.Temporary)
            .Select(character => new RosterRow(character.Index, $"{CharacterName(character)} · Lv. {character.Values.Level}"))
            .Where(row => row.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _refreshingRoster = true;
        RosterList.ItemsSource = rows;
        RosterList.SelectedItem = rows.FirstOrDefault(row => row.Index == _selectedCharacter) ?? rows.FirstOrDefault();
        _refreshingRoster = false;
        int? selected = (RosterList.SelectedItem as RosterRow)?.Index;
        bool changed = _selectedCharacter != selected;
        _selectedCharacter = selected;
        if (selectEditor || changed)
            SelectCharacter();
    }

    private void SelectCharacter()
    {
        var character = SelectedCharacter;
        RosterGeneralForm.IsEnabled = RosterStatsForm.IsEnabled = RosterItemsForm.IsEnabled = character is not null;
        if (character is null)
        {
            RosterName.Clear();
            RosterClass.Clear();
            RosterLevel.Value = RosterExperience.Value = RosterSkillPoints.Value = null;
            RosterStatsInputs.Children.Clear();
            _rosterStats.Clear();
            RosterItemsList.ItemsSource = Array.Empty<InventoryRow>();
            return;
        }
        _refreshingRoster = true;
        RefreshCharacterNames(character);
        var job = RosterCatalog.Class(character.ClassHash);
        RosterLevel.Maximum = job?.MaxLevel ?? 255;
        RosterLevel.IsEnabled = RosterExperience.IsEnabled = job is not null;
        RosterLevel.Text = character.Values.Level.ToString();
        RosterExperience.Text = character.Values.Experience.ToString();
        RosterSkillPoints.Text = character.Values.SkillPoints.ToString();
        RosterStatsInputs.Children.Clear();
        _rosterStats.Clear();
        foreach (var stat in character.Stats.Where(stat => stat.Stat != RosterStat.Sight))
        {
            var label = new TextBlock { Text = UiLanguage.Get(stat.Stat.ToString()) };
            var input = new NumericUpDown { Minimum = stat.Stat == RosterStat.HP ? 1 : 0,
                Maximum = stat.Maximum ?? 255, Increment = 1, FormatString = "0", Height = 42,
                HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = stat.Maximum.HasValue, Value = stat.Value };
            input.Text = stat.Value.ToString();
            var field = new StackPanel { Spacing = 8 };
            field.Children.Add(label);
            field.Children.Add(input);
            RosterStatsInputs.Children.Add(field);
            _rosterStats.Add(stat.Stat, (label, input));
        }
        _selectedCharacterItem = null;
        RefreshCharacterItems(selectEditor: true);
        _refreshingRoster = false;
    }

    private void RefreshCharacterNames(RosterCharacter character)
    {
        RosterName.Text = CharacterName(character);
        RosterClass.Text = RosterCatalog.Class(character.ClassHash)?.Name(UiLanguage.Current)
            ?? $"{UiLanguage.Get("UnknownClass")} (0x{character.ClassHash:X8})";
    }

    private void RefreshRosterLanguage()
    {
        if (!CanEditRoster)
            return;
        RefreshRoster(selectEditor: false);
        if (SelectedCharacter is not { } character)
            return;
        RefreshCharacterNames(character);
        foreach (var (stat, field) in _rosterStats)
            field.Label.Text = UiLanguage.Get(stat.ToString());
        RefreshCharacterItems(selectEditor: false);
        RefreshCharacterItemChoices((RosterItemType.SelectedItem as ItemChoice)?.Definition.Hash);
    }

    public bool ApplyRosterValues()
    {
        return EditRoster(save =>
        {
            var character = SelectedCharacter ?? throw new ArgumentException("Select a character.");
            var values = new RosterValue(Amount(RosterLevel), Amount(RosterExperience), Amount(RosterSkillPoints));
            return save.WithRosterValues(character.Index, values);
        }, refresh: false);
    }

    public bool ApplyRosterStats() => EditRoster(save =>
    {
        var character = SelectedCharacter ?? throw new ArgumentException("Select a character.");
        foreach (var (stat, field) in _rosterStats.Where(pair => pair.Value.Input.IsEnabled))
            save = save.WithRosterStat(character.Index, stat, Amount(field.Input));
        return save;
    }, refresh: false);

    private bool EditRoster(Func<EngageSave, EngageSave> edit, bool refresh)
    {
        if (!CanEditRoster || Save is null || SelectedCharacter is null)
            return false;
        try
        {
            Save = edit(Save);
            RefreshRoster(selectEditor: refresh);
            RefreshOverview();
            RefreshSections();
            Message.IsVisible = false;
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("EditFailed", error.Message);
            return false;
        }
    }

    private void RefreshCharacterItems(bool selectEditor)
    {
        var character = SelectedCharacter;
        if (character is null)
            return;
        var rows = character.Items.Select(slot => new InventoryRow(slot.Slot,
            slot.Item is null ? UiLanguage.Get("EmptySlot") : ItemLabel(slot.Item))).ToArray();
        bool refreshing = _refreshingRoster;
        _refreshingRoster = true;
        RosterItemsList.ItemsSource = rows;
        RosterItemsList.SelectedItem = rows.FirstOrDefault(row => row.Slot == _selectedCharacterItem) ?? rows.FirstOrDefault();
        _selectedCharacterItem = (RosterItemsList.SelectedItem as InventoryRow)?.Slot;
        if (selectEditor)
            SelectCharacterItem();
        _refreshingRoster = refreshing;
    }

    private void SelectCharacterItem()
    {
        var item = SelectedCharacter?.Items.FirstOrDefault(slot => slot.Slot == _selectedCharacterItem)?.Item;
        RefreshCharacterItemChoices(item?.ItemHash);
        SetCharacterItemRanges(ItemCatalog.Find(item?.ItemHash ?? 0));
        RosterItemUses.Text = (item?.Uses ?? 1).ToString();
        RosterItemRefine.Text = (item?.RefineLevel ?? 0).ToString();
        bool reserved = item is not null && RosterCatalog.Item(item.ItemHash) is { EngageOnly: true };
        RosterItemType.IsEnabled = RosterItemSearch.IsEnabled = !reserved;
        DeleteRosterItemButton.IsEnabled = item is not null && !reserved;
        if (reserved)
            ApplyRosterItemButton.IsEnabled = false;
    }

    private void RefreshCharacterItemChoices(uint? selectedHash)
    {
        string query = RosterItemSearch.Text?.Trim() ?? "";
        var choices = ItemCatalog.Items.Select(item => new ItemChoice(item, item.Name(UiLanguage.Current)))
            .Where(choice => choice.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || choice.Definition.Hash == selectedHash)
            .OrderBy(choice => choice.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        bool refreshing = _refreshingRoster;
        _refreshingRoster = true;
        RosterItemType.ItemsSource = choices;
        RosterItemType.SelectedItem = choices.FirstOrDefault(choice => choice.Definition.Hash == selectedHash);
        _refreshingRoster = refreshing;
    }

    private void SetCharacterItemRanges(ItemDefinition? item)
    {
        RosterItemUses.Maximum = item?.MaxUses ?? 255;
        RosterItemUses.IsVisible = item is not { UnlimitedUses: true };
        RosterUnlimitedUses.IsVisible = item is { UnlimitedUses: true };
        RosterItemRefine.Maximum = item?.MaxRefine ?? 0;
        RosterItemRefine.IsEnabled = item is { MaxRefine: > 0 };
        ApplyRosterItemButton.IsEnabled = item is not null && _selectedCharacterItem.HasValue;
    }

    public bool ApplyRosterItem() => EditRoster(save =>
    {
        var choice = RosterItemType.SelectedItem as ItemChoice ?? throw new ArgumentException(UiLanguage.Get("SelectItem"));
        return save.WithRosterItem(_selectedCharacter!.Value, _selectedCharacterItem!.Value, choice.Definition.Id,
            choice.Definition.UnlimitedUses ? 255 : Amount(RosterItemUses), Amount(RosterItemRefine));
    }, refresh: false);

    private bool ApplyPendingRoster()
    {
        if (!CanEditRoster || SelectedCharacter is null)
            return true;
        return EditRoster(save =>
        {
            save = PendingCharacterValues(save);
            if (RosterItemType.SelectedItem is not ItemChoice choice || !_selectedCharacterItem.HasValue)
                return save;
            var old = SelectedCharacter.Items[_selectedCharacterItem.Value].Item;
            if (old?.ItemHash == choice.Definition.Hash && RosterItemUses.Text == old.Uses.ToString()
                && RosterItemRefine.Text == old.RefineLevel.ToString())
                return save;
            return save.WithRosterItem(_selectedCharacter!.Value, _selectedCharacterItem.Value, choice.Definition.Id,
                choice.Definition.UnlimitedUses ? 255 : Amount(RosterItemUses), Amount(RosterItemRefine));
        }, refresh: false);
    }

    private EngageSave PendingCharacterValues(EngageSave save)
    {
        var character = SelectedCharacter ?? throw new ArgumentException("Select a character.");
        var old = character.Values;
        var values = old;
        if (RosterLevel.Text != old.Level.ToString())
            values = values with { Level = Amount(RosterLevel) };
        if (RosterExperience.Text != old.Experience.ToString())
            values = values with { Experience = Amount(RosterExperience) };
        if (RosterSkillPoints.Text != old.SkillPoints.ToString())
            values = values with { SkillPoints = Amount(RosterSkillPoints) };
        save = save.WithRosterValues(character.Index, values);
        foreach (var (stat, field) in _rosterStats.Where(pair => pair.Value.Input.IsEnabled))
            save = save.WithRosterStat(character.Index, stat, Amount(field.Input));
        return save;
    }

    private void RosterSearch_Changed(object? sender, TextChangedEventArgs e) => RefreshRoster(selectEditor: false);
    private void RosterList_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingRoster)
            return;
        _selectedCharacter = (RosterList.SelectedItem as RosterRow)?.Index;
        SelectCharacter();
    }
    private void RosterTabs_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (RosterGeneralForm is null)
            return;
        RosterGeneralForm.IsVisible = RosterTabs.SelectedIndex == 0;
        RosterStatsForm.IsVisible = RosterTabs.SelectedIndex == 1;
        RosterItemsForm.IsVisible = RosterTabs.SelectedIndex == 2;
    }
    private void RosterLevel_Changed(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (!_refreshingRoster && RosterLevel.Value == RosterLevel.Maximum)
            RosterExperience.Text = "0";
    }
    private void RosterItemsList_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingRoster)
            return;
        _selectedCharacterItem = (RosterItemsList.SelectedItem as InventoryRow)?.Slot;
        SelectCharacterItem();
    }
    private void RosterItemSearch_Changed(object? sender, TextChangedEventArgs e) =>
        RefreshCharacterItemChoices((RosterItemType.SelectedItem as ItemChoice)?.Definition.Hash);
    private void RosterItemType_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingRoster)
            return;
        var item = (RosterItemType.SelectedItem as ItemChoice)?.Definition;
        SetCharacterItemRanges(item);
        RosterItemUses.Text = (item?.MaxUses ?? 1).ToString();
        RosterItemRefine.Text = "0";
    }
    private void ApplyRosterValues_Click(object? sender, RoutedEventArgs e) => ApplyRosterValues();
    private void ApplyRosterStats_Click(object? sender, RoutedEventArgs e) => ApplyRosterStats();
    private void ApplyRosterItem_Click(object? sender, RoutedEventArgs e) => ApplyRosterItem();
    private void DeleteRosterItem_Click(object? sender, RoutedEventArgs e) =>
        EditRoster(save => PendingCharacterValues(save).DeleteRosterItem(_selectedCharacter!.Value, _selectedCharacterItem!.Value), refresh: true);
    private void RestoreRosterUses_Click(object? sender, RoutedEventArgs e) =>
        EditRoster(save => PendingCharacterValues(save).RestoreRosterUses(_selectedCharacter!.Value), refresh: true);
}
