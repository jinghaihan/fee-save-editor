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
    public sealed record ClassChoice(ClassDefinition Definition, string Label);
    public sealed record WeaponChoice(uint Mask, string Label);
    public bool CanEditRoster { get; private set; }
    private int? _selectedCharacter;
    private int? _selectedCharacterItem;
    private bool _refreshingRoster;
    private readonly Dictionary<RosterStat, (TextBlock Label, NumericUpDown Input, TextBlock Preview)> _rosterStats = new();
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
        MaximizeAllRosterStatsButton.IsEnabled = MaximizeRosterStatsButton.IsEnabled = false;
        _selectedCharacter = null;
        _selectedCharacterItem = null;
        RosterGeneralForm.IsEnabled = RosterStatsForm.IsEnabled = RosterItemsForm.IsEnabled = false;
        RosterSkillsForm.IsEnabled = RosterProficienciesForm.IsEnabled = false;
        ClearRosterSkills();
        RosterList.ItemsSource = Array.Empty<RosterRow>();
        RosterItemsList.ItemsSource = Array.Empty<InventoryRow>();
        RosterName.Clear();
        RosterClass.ItemsSource = Array.Empty<ClassChoice>();
        RosterLevel.Value = RosterExperience.Value = RosterSkillPoints.Value = null;
        RosterInternalLevel.Value = RosterCurrentHP.Value = null;
        RosterStatsInputs.Children.Clear();
        RosterStatsInputs.RowDefinitions.Clear();
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
        var roster = Save.ReadRoster();
        MaximizeAllRosterStatsButton.IsEnabled = roster.Any(character => character.Force is not UnitForce.Enemy and not UnitForce.Temporary
            && RosterCatalog.Person(character.PersonHash) is not null);
        var rows = roster.Where(character => character.Force is not UnitForce.Enemy and not UnitForce.Temporary)
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
        MaximizeRosterStatsButton.IsEnabled = character is not null
            && character.Force is not UnitForce.Enemy and not UnitForce.Temporary
            && RosterCatalog.Person(character.PersonHash) is not null;
        RosterGeneralForm.IsEnabled = RosterStatsForm.IsEnabled = RosterItemsForm.IsEnabled = character is not null;
        RosterSkillsForm.IsEnabled = RosterProficienciesForm.IsEnabled = character is not null;
        if (character is null)
        {
            RosterName.Clear();
            RosterClass.ItemsSource = Array.Empty<ClassChoice>();
            RosterLevel.Value = RosterExperience.Value = RosterSkillPoints.Value = null;
            RosterInternalLevel.Value = RosterCurrentHP.Value = null;
            ClearRosterSkills();
            RosterStatsInputs.Children.Clear();
            _rosterStats.Clear();
            RosterItemsList.ItemsSource = Array.Empty<InventoryRow>();
            return;
        }
        _refreshingRoster = true;
        RosterClass.SelectedItem = null;
        RosterWeaponVariant.SelectedItem = null;
        RefreshCharacterNames(character);
        var job = RosterCatalog.Class(character.ClassHash);
        RosterLevel.Maximum = job?.MaxLevel ?? 255;
        RosterLevel.IsEnabled = RosterExperience.IsEnabled = job is not null;
        RosterLevel.Text = character.Values.Level.ToString();
        RosterExperience.Text = character.Values.Experience.ToString();
        RosterSkillPoints.Text = character.Values.SkillPoints.ToString();
        RosterInternalLevel.Text = character.Progress.InternalLevel.ToString();
        RosterCurrentHP.Maximum = Save!.RosterMaximumHP(character.Index);
        RosterCurrentHP.Text = character.Progress.CurrentHP.ToString();
        RefreshRosterSkills(character, preserveEdits: false);
        RosterStatsInputs.Children.Clear();
        RosterStatsInputs.RowDefinitions.Clear();
        _rosterStats.Clear();
        foreach (var stat in character.Stats.Where(stat => stat.Stat != RosterStat.Sight))
        {
            var label = new TextBlock { Text = UiLanguage.Get(stat.Stat.ToString()) };
            var range = job is null ? (Minimum: (int)sbyte.MinValue, Maximum: (int)sbyte.MaxValue) : RosterStats.PersonalRange(stat.Stat, job);
            var input = new NumericUpDown { Minimum = Math.Min(range.Minimum, stat.PersonalValue),
                Maximum = Math.Max(range.Maximum, stat.PersonalValue), Increment = 1, FormatString = "0", Height = 42,
                Name = $"RosterPersonal{stat.Stat}", HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = job is not null && RosterCatalog.Person(character.PersonHash) is not null, Value = stat.PersonalValue };
            input.Text = stat.PersonalValue.ToString();
            var preview = new TextBlock { Name = $"RosterPreview{stat.Stat}", HorizontalAlignment = HorizontalAlignment.Right };
            var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            Grid.SetColumn(preview, 1);
            heading.Children.Add(label);
            heading.Children.Add(preview);
            var field = new StackPanel { Spacing = 8 };
            field.Children.Add(heading);
            field.Children.Add(input);
            int index = RosterStatsInputs.Children.Count;
            if (index % 2 == 0) RosterStatsInputs.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(field, index % 2);
            Grid.SetRow(field, index / 2);
            RosterStatsInputs.Children.Add(field);
            _rosterStats.Add(stat.Stat, (label, input, preview));
            input.PropertyChanged += (_, e) =>
            {
                if (!_refreshingRoster && e.Property == NumericUpDown.TextProperty)
                    RefreshRosterStatPreviews();
            };
        }
        RefreshRosterStatPreviews();
        _selectedCharacterItem = null;
        RefreshCharacterItems(selectEditor: true);
        _refreshingRoster = false;
    }

    private void RefreshCharacterNames(RosterCharacter character)
    {
        RosterName.Text = character.Progress.CustomName ?? CharacterName(character);
        uint selected = (RosterClass.SelectedItem as ClassChoice)?.Definition.Hash ?? character.ClassHash;
        bool refreshing = _refreshingRoster;
        _refreshingRoster = true;
        var classes = RosterCatalog.Person(character.PersonHash) is null ? [] : Save!.ReadRosterClasses(character.Index).ToList();
        if (RosterCatalog.Class(character.ClassHash) is { } current && classes.All(job => job.Hash != current.Hash))
            classes.Add(current);
        var choices = classes.Select(job => new ClassChoice(job, job.Name(UiLanguage.Current)))
            .OrderBy(choice => choice.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
        RosterClass.ItemsSource = choices;
        RosterClass.SelectedItem = choices.FirstOrDefault(choice => choice.Definition.Hash == selected)
            ?? choices.FirstOrDefault(choice => choice.Definition.Hash == character.ClassHash);
        RefreshWeaponVariants(character);
        _refreshingRoster = refreshing;
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
        RefreshRosterStatPreviews();
        RefreshCharacterItems(selectEditor: false);
        RefreshCharacterItemChoices((RosterItemType.SelectedItem as ItemChoice)?.Definition.Hash);
        RefreshRosterSkills(character, preserveEdits: true);
    }

    public bool ApplyRosterValues()
    {
        return EditRoster(save =>
        {
            var character = SelectedCharacter ?? throw new ArgumentException("Select a character.");
            return PendingRosterCondition(PendingGeneralValues(save, character), character);
        }, refresh: true);
    }

    public bool ApplyRosterStats() => EditRoster(save =>
    {
        var character = SelectedCharacter ?? throw new ArgumentException("Select a character.");
        foreach (var (stat, field) in _rosterStats.Where(pair => pair.Value.Input.IsEnabled))
            save = save.WithRosterPersonalStat(character.Index, stat, Amount(field.Input));
        return PendingRosterCondition(save, character);
    }, refresh: true);

    public bool MaximizeSelectedRosterStats() => EditRoster(save => PendingCharacterValues(save)
        .MaximizeRosterStats(_selectedCharacter!.Value), refresh: true);

    public bool MaximizeAllRosterStats() => EditRoster(save =>
    {
        if (SelectedCharacter is not null)
            save = PendingCharacterValues(save);
        return save.MaximizeAllRosterStats();
    }, refresh: true, requireCharacter: false);

    private void RefreshRosterStatPreviews()
    {
        if (SelectedCharacter is not { } character)
            return;
        var job = (RosterClass.SelectedItem as ClassChoice)?.Definition ?? RosterCatalog.Class(character.ClassHash);
        var person = RosterCatalog.Person(character.PersonHash);
        foreach (var (stat, field) in _rosterStats)
        {
            if (job is null || person is null || !int.TryParse(field.Input.Text, out int value)
                || value < field.Input.Minimum || value > field.Input.Maximum)
            {
                field.Preview.Text = "—";
                continue;
            }
            var preview = RosterStats.Calculate(stat, value, job, person);
            field.Preview.Text = string.Format(UiLanguage.Get("CurrentClassStat"), preview.Value, preview.Maximum);
        }
    }

    private bool EditRoster(Func<EngageSave, EngageSave> edit, bool refresh, bool requireCharacter = true)
    {
        if (!CanEditRoster || Save is null || requireCharacter && SelectedCharacter is null)
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
        }, refresh: true);
    }

    private EngageSave PendingCharacterValues(EngageSave save)
    {
        var character = SelectedCharacter ?? throw new ArgumentException("Select a character.");
        save = PendingGeneralValues(save, character);
        foreach (var (stat, field) in _rosterStats.Where(pair => pair.Value.Input.IsEnabled))
            if (field.Input.Text != character.Stats[(int)stat].PersonalValue.ToString())
                save = save.WithRosterPersonalStat(character.Index, stat, Amount(field.Input));
        save = PendingRosterCondition(save, character);
        return PendingRosterSkills(save, character);
    }

    private EngageSave PendingGeneralValues(EngageSave save, RosterCharacter character)
    {
        save = PendingClass(save, character);
        var old = character.Values;
        var values = save.ReadRoster()[character.Index].Values;
        if (RosterLevel.Text != old.Level.ToString())
            values = values with { Level = Amount(RosterLevel) };
        if (RosterExperience.Text != old.Experience.ToString())
            values = values with { Experience = Amount(RosterExperience) };
        if (RosterSkillPoints.Text != old.SkillPoints.ToString())
            values = values with { SkillPoints = Amount(RosterSkillPoints) };
        save = save.WithRosterValues(character.Index, values);
        return save;
    }

    private EngageSave PendingClass(EngageSave save, RosterCharacter character)
    {
        if (RosterClass.SelectedItem is not ClassChoice choice || RosterWeaponVariant.SelectedItem is not WeaponChoice weapon)
            return save;
        if (choice.Definition.Hash == character.ClassHash && (weapon.Mask == character.Progress.SelectedWeapons
            || character.Progress.SelectedWeapons == 0 && choice.Definition.WeaponVariants().Count == 1))
            return save;
        return save.WithRosterClass(character.Index, choice.Definition.Id, weapon.Mask);
    }

    private void RefreshWeaponVariants(RosterCharacter character)
    {
        if (RosterClass.SelectedItem is not ClassChoice choice)
            return;
        uint selected = (RosterWeaponVariant.SelectedItem as WeaponChoice)?.Mask ?? character.Progress.SelectedWeapons;
        var variants = choice.Definition.WeaponVariants().Select(mask => new WeaponChoice(mask,
            string.Join(" / ", Enum.GetValues<WeaponType>().Where(kind => (mask & (1u << (int)kind)) != 0)
                .Select(kind => UiLanguage.Get(kind.ToString()))))).ToArray();
        RosterWeaponVariant.ItemsSource = variants;
        RosterWeaponVariant.SelectedItem = variants.FirstOrDefault(weapon => weapon.Mask == selected) ?? variants.FirstOrDefault();
        RosterWeaponVariantField.IsVisible = variants.Length > 1;
    }

    public bool ChangeRosterClass() => EditRoster(save => PendingClass(save,
        SelectedCharacter ?? throw new ArgumentException("Select a character.")), refresh: true);

    private void ChangeRosterClass_Click(object? sender, RoutedEventArgs e) => ChangeRosterClass();
    private void RosterClass_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingRoster && SelectedCharacter is { } character)
        {
            RefreshWeaponVariants(character);
            RefreshRosterStatPreviews();
        }
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
        MaximizeRosterStatsButton.IsVisible = RosterTabs.SelectedIndex == 1;
        RosterItemsForm.IsVisible = RosterTabs.SelectedIndex == 2;
        RosterSkillsForm.IsVisible = RosterTabs.SelectedIndex == 3;
        RosterProficienciesForm.IsVisible = RosterTabs.SelectedIndex == 4;
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
    private void MaximizeRosterStats_Click(object? sender, RoutedEventArgs e) => MaximizeSelectedRosterStats();
    private void MaximizeAllRosterStats_Click(object? sender, RoutedEventArgs e) => MaximizeAllRosterStats();
    private void ApplyRosterItem_Click(object? sender, RoutedEventArgs e) => ApplyRosterItem();
    private void DeleteRosterItem_Click(object? sender, RoutedEventArgs e) =>
        EditRoster(save => PendingCharacterValues(save).DeleteRosterItem(_selectedCharacter!.Value, _selectedCharacterItem!.Value), refresh: true);
    private void RestoreRosterUses_Click(object? sender, RoutedEventArgs e) =>
        EditRoster(save => PendingCharacterValues(save).RestoreRosterUses(_selectedCharacter!.Value), refresh: true);
}
