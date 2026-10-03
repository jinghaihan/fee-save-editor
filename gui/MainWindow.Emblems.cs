using Avalonia.Controls;
using Avalonia.Interactivity;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record EmblemChoice(uint Instance, string Label);
    public sealed record EmblemRow(string Key, string Label);
    public sealed record MissingEmblemChoice(string Id, string Label);
    public bool CanEditEmblems { get; private set; }
    public bool CanEditBondRings { get; private set; }
    public bool CanEditEmblemConditions { get; private set; }
    private uint? _selectedEmblem;
    private enum EmblemPage { Bonds, BondRings }
    private EmblemPage _emblemPage;
    private string? _selectedBondRecord;
    private string? _selectedRingRecord;
    private string? SelectedEmblemRecordKey
    {
        get => _emblemPage == EmblemPage.Bonds ? _selectedBondRecord : _selectedRingRecord;
        set
        {
            if (_emblemPage == EmblemPage.Bonds) _selectedBondRecord = value;
            else _selectedRingRecord = value;
        }
    }
    private ListBox ActiveEmblemList => _emblemPage == EmblemPage.Bonds ? EmblemRecordList : BondRingList;
    private TextBox ActiveEmblemSearch => _emblemPage == EmblemPage.Bonds ? EmblemSearch : BondRingSearch;
    private bool _refreshingEmblems;
    private bool _refreshingEmblemPages;

    public void ShowEmblems() => ShowEmblemPage(EmblemPage.Bonds);
    public void ShowBondRings() => ShowEmblemPage(EmblemPage.BondRings);

    private void ShowEmblemPage(EmblemPage page)
    {
        if (HasPendingSupportValues() && !ApplySupportValues())
        {
            SelectEmblemPageTab(_emblemPage);
            MainNavigation.SelectedIndex = 4;
            return;
        }
        if (_emblemPage != page && HasPendingEmblemValues() && !ApplyEmblemValues())
        {
            SelectEmblemPageTab(_emblemPage);
            MainNavigation.SelectedIndex = 3;
            return;
        }
        bool preserveEditor = _emblemPage == page && HasPendingEmblemValues();
        _emblemPage = page;
        MinigamesPanel.IsVisible = false;
        MainPanel.IsVisible = ItemsPanel.IsVisible = RosterPanel.IsVisible = InspectorPanel.IsVisible = false;
        SupportsPanel.IsVisible = AchievementsPanel.IsVisible = false;
        EmblemPagesPanel.IsVisible = true;
        EmblemsPanel.IsVisible = page == EmblemPage.Bonds;
        BondRingsPanel.IsVisible = page == EmblemPage.BondRings;
        SelectEmblemPageTab(page);
        MainNavigation.SelectedIndex = 3;
        RefreshEmblemRecords(preserveEditor);
        RefreshSupportRecords(preserveEditor: false);
        RefreshPageLayout();
    }

    private void EmblemPages_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingEmblemPages || EmblemPagesPanel is null || BondRingsPanel is null) return;
        ShowEmblemPage((EmblemPage)EmblemPages.SelectedIndex);
    }

    private void SelectEmblemPageTab(EmblemPage page)
    {
        _refreshingEmblemPages = true;
        EmblemPages.SelectedIndex = (int)page;
        _refreshingEmblemPages = false;
    }

    private EmblemBond? SelectedBond => CanEditEmblems && Save is not null
        ? Save.ReadEmblems().FirstOrDefault(row => row.InstanceId == _selectedEmblem)?.Bonds
            .FirstOrDefault(row => row.PersonId == SelectedEmblemRecordKey) : null;
    private BondRing? SelectedBondRing => CanEditBondRings && Save is not null
        ? Save.ReadBondRings().FirstOrDefault(row => row.InstanceId.ToString() == SelectedEmblemRecordKey) : null;
    private SavedEmblem? SelectedEmblem => CanEditEmblems && Save is not null
        ? Save.ReadEmblems().FirstOrDefault(row => row.InstanceId == _selectedEmblem) : null;
    private EmblemCondition? SelectedEmblemCondition
    {
        get
        {
            if (!CanEditEmblemConditions || Save is null || SelectedEmblem is not { } emblem) return null;
            var matches = Save.ReadEmblemConditions().Where(row => row.BondHolderId == emblem.InstanceId && row.EmblemId == emblem.EmblemId).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
    }

    private void LoadEmblems()
    {
        _refreshingEmblems = true;
        CanEditEmblems = CanEditBondRings = CanEditEmblemConditions = false;
        _selectedEmblem = null;
        _selectedBondRecord = _selectedRingRecord = null;
        EmblemChoiceInput.ItemsSource = Array.Empty<EmblemChoice>();
        EmblemRecordList.ItemsSource = Array.Empty<EmblemRow>();
        BondRingList.ItemsSource = Array.Empty<EmblemRow>();
        EmblemSearch.Clear();
        BondRingSearch.Clear();
        EmblemBondLevelInput.ItemsSource = Enumerable.Range(1, 20).ToArray();
        EmblemBondExpInput.Value = BondRingStockInput.Value = null;
        EmblemDirtinessInput.Value = null;
        EmblemConditionForm.IsEnabled = false;
        RemoveEmblemButton.IsEnabled = false;
        _refreshingEmblems = false;
        if (Save is not null && Save.Kind == SaveKind.Game)
        {
            try { Save.ReadEmblems(); CanEditEmblems = true; }
            catch (Exception error) when (IsFileError(error))
            {
                if (Save.Sections.Any(row => row.Name == "GDBD")) ShowMessage("EmblemsUnavailable", error.Message);
            }
            try { Save.ReadBondRings(); CanEditBondRings = true; }
            catch (Exception error) when (IsFileError(error))
            {
                if (Save.Sections.Any(row => row.Name == "RING")) ShowMessage("EmblemsUnavailable", error.Message);
            }
            if (Save.Sections.Any(row => row.Name == "GOD"))
            {
                try { Save.ReadEmblemConditions(); CanEditEmblemConditions = true; }
                catch (Exception error) when (IsFileError(error)) { ShowMessage("EmblemsUnavailable", error.Message); }
            }
        }
        RefreshEmblemChoices();
        RefreshEmblemRecords(preserveEditor: false);
        RefreshMissingEmblems();
    }

    private static string BondPersonName(string pid) => RosterCatalog.Person(ItemCatalog.Hash(pid))?.Name(UiLanguage.Current) ?? pid;
    private static string BondRingName(BondRing ring)
    {
        var definition = EmblemCatalog.Ring(ring.RingHash);
        return definition is null ? $"{UiLanguage.Get("UnknownRing")} (0x{ring.RingHash:X8})"
            : $"{definition.Name(UiLanguage.Current)} · {definition.RankName}";
    }

    private void RefreshEmblemChoices()
    {
        _refreshingEmblems = true;
        var choices = CanEditEmblems && Save is not null ? Save.ReadEmblems()
            .Select(row => new EmblemChoice(row.InstanceId, EmblemCatalog.Emblem(row.EmblemId)?.Name(UiLanguage.Current) ?? row.EmblemId))
            .ToArray() : Array.Empty<EmblemChoice>();
        EmblemChoiceInput.ItemsSource = choices;
        EmblemChoiceInput.SelectedItem = choices.FirstOrDefault(row => row.Instance == _selectedEmblem) ?? choices.FirstOrDefault();
        _selectedEmblem = (EmblemChoiceInput.SelectedItem as EmblemChoice)?.Instance;
        _refreshingEmblems = false;
    }

    private void RefreshEmblemRecords(bool preserveEditor)
    {
        if (ActiveEmblemList is null) return;
        _refreshingEmblems = true;

        CleanAllEmblemsButton.IsEnabled = CanEditEmblemConditions && Save is not null && Save.ReadEmblemConditions().Count > 0;
        bool available = _emblemPage == EmblemPage.Bonds ? CanEditEmblems : CanEditBondRings;
        var emblem = _emblemPage == EmblemPage.Bonds ? SelectedEmblem : null;
        MaxAllEmblemBondsButton.IsEnabled = emblem is not null && EmblemCatalog.Emblem(emblem.EmblemId) is not null;
        FillSBondRingsButton.IsEnabled = CanEditBondRings;
        var completion = _emblemPage == EmblemPage.Bonds ? EmblemCompletion : BondRingCompletion;
        var availability = _emblemPage == EmblemPage.Bonds ? EmblemListAvailability : BondRingAvailability;
        completion.Text = "";
        if (_emblemPage == EmblemPage.BondRings && CanEditBondRings && Save is not null)
        {
            var owned = Save.ReadBondRings().Where(ring => ring.StockCount > 0).Select(ring => ring.RingHash).ToHashSet();
            int complete = BondRingCatalog.SRings.Count(ring => owned.Contains(ring.Hash));
            completion.Text = $"S: {complete}/{BondRingCatalog.SRings.Count}";
            FillSBondRingsButton.IsEnabled = complete < BondRingCatalog.SRings.Count;
        }
        if (emblem is not null)
        {
            var knownBonds = emblem.Bonds.Where(bond => RosterCatalog.Person(ItemCatalog.Hash(bond.PersonId)) is not null).ToArray();
            int complete = knownBonds.Count(bond => bond.Level == EmblemCatalog.MaximumLevel(emblem, bond.PersonId)
                && bond.Experience == EmblemCatalog.ExperienceForLevel(emblem, bond.PersonId, bond.Level));
            completion.Text = $"{complete}/{knownBonds.Length}";
        }
        availability.Text = available || Save is null ? "" : UiLanguage.Get("Unavailable");
        availability.IsVisible = !string.IsNullOrEmpty(availability.Text);
        IEnumerable<EmblemRow> rows = Array.Empty<EmblemRow>();
        if (Save is not null && available)
        {
            if (_emblemPage == EmblemPage.Bonds)
                rows = (Save.ReadEmblems().FirstOrDefault(row => row.InstanceId == _selectedEmblem)?.Bonds ?? [])
                    .Select(row => new EmblemRow(row.PersonId, $"{BondPersonName(row.PersonId)} · Lv. {row.Level}"));
            else
                rows = Save.ReadBondRings().Select(row => new EmblemRow(row.InstanceId.ToString(), $"{BondRingName(row)} · ×{row.StockCount}"));
        }
        string query = ActiveEmblemSearch.Text?.Trim() ?? "";
        var filtered = rows.Where(row => row.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        ActiveEmblemList.ItemsSource = filtered;
        ActiveEmblemList.SelectedItem = filtered.FirstOrDefault(row => row.Key == SelectedEmblemRecordKey);
        if (!preserveEditor)
        {
            ActiveEmblemList.SelectedItem ??= filtered.FirstOrDefault();
            SelectedEmblemRecordKey = (ActiveEmblemList.SelectedItem as EmblemRow)?.Key;
            LoadEmblemEditor();
        }
        _refreshingEmblems = false;
        RefreshBondRingMeld();
    }

    private void LoadEmblemEditor()
    {
        var condition = _emblemPage == EmblemPage.Bonds ? SelectedEmblemCondition : null;
        RemoveEmblemButton.IsEnabled = condition is not null && Save!.CanRemoveEmblem(condition.InstanceId);
        EmblemConditionForm.IsEnabled = condition is not null;
        EmblemDirtinessInput.Value = condition?.Dirtiness;
        var bond = _emblemPage == EmblemPage.Bonds ? SelectedBond : null;
        var ring = _emblemPage == EmblemPage.BondRings ? SelectedBondRing : null;
        var emblem = SelectedEmblem;
        bool knownEmblem = emblem is not null && EmblemCatalog.Emblem(emblem.EmblemId) is not null;
        EmblemBondForm.IsEnabled = bond is not null && knownEmblem
            && bond.Level <= EmblemCatalog.MaximumLevel(emblem!, bond.PersonId)
            && RosterCatalog.Person(ItemCatalog.Hash(bond.PersonId)) is not null;
        int[] levels = emblem is not null && bond is not null
            ? EmblemCatalog.SelectableLevels(emblem, bond.PersonId) : Enumerable.Range(1, EmblemCatalog.MaxBondLevel).ToArray();
        EmblemBondLevelInput.ItemsSource = bond is not null && !levels.Contains(bond.Level) ? new[] { bond.Level } : levels;
        EmblemBondLevelInput.SelectedItem = bond?.Level;
        EmblemBondExpInput.IsReadOnly = emblem?.EmblemId == EmblemCatalog.AlearEmblemId;
        EmblemBondExpInput.Maximum = Math.Max(bond?.Experience ?? 0, EmblemCatalog.MaxBondExperience + 1);
        EmblemBondExpInput.Value = bond?.Experience;
        BondRingForm.IsEnabled = ring is not null && EmblemCatalog.Ring(ring.RingHash) is not null;
        BondRingStockInput.Minimum = ring?.OwnerIndex.HasValue == true ? 1 : 0;
        BondRingStockInput.Maximum = ring is not null && EmblemCatalog.Ring(ring.RingHash) is not null
            ? Save!.MaximumBondRingStock(ring.InstanceId) : 99;
        BondRingStockInput.Text = ring?.StockCount.ToString() ?? "";
        RefreshBondRingOwner(ring);
        RefreshBondRingEffects(ring);
    }

    private bool HasPendingEmblemValues()
    {
        if (_emblemPage == EmblemPage.Bonds)
        {
            return HasPendingBondValues() || (SelectedEmblemCondition is { } condition
                && EmblemDirtinessInput.Text != condition.Dirtiness.ToString());
        }
        var ring = SelectedBondRing;
        return ring is not null && BondRingStockInput.Text != ring.StockCount.ToString();
    }

    private bool HasPendingBondValues() => SelectedBond is { } bond &&
        (EmblemBondLevelInput.SelectedItem is not int level || level != bond.Level || EmblemBondExpInput.Text != bond.Experience.ToString());

    public bool ApplyEmblemValues()
    {
        if (Save is null) return false;
        try
        {
            Save = PendingEmblemValues(Save);
            RefreshOverview();
            RefreshSections();
            Message.IsVisible = false;
            RefreshEmblemRecords(preserveEditor: false);
            RefreshSupportRecords(preserveEditor: false);
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("EditFailed", error.Message);
            return false;
        }
    }

    private EngageSave PendingEmblemValues(EngageSave save)
    {
        if (!HasPendingEmblemValues()) return save;
        if (_emblemPage == EmblemPage.Bonds)
        {
            var edited = save;
            if (HasPendingBondValues())
            {
                var bond = SelectedBond!;
                int level = EmblemBondLevelInput.SelectedItem is int selected ? selected : throw new ArgumentException(UiLanguage.Get("InvalidAmount"));
                edited = edited.WithEmblemBond(_selectedEmblem!.Value, bond.PersonId, level, Amount(EmblemBondExpInput));
            }
            if (SelectedEmblemCondition is { } condition)
                edited = edited.WithEmblemDirtiness(condition.InstanceId, Amount(EmblemDirtinessInput));
            return edited;
        }
        var ring = SelectedBondRing ?? throw new ArgumentException("Select an existing bond ring.");
        return save.WithBondRingStock(ring.InstanceId, Amount(BondRingStockInput));
    }

    private void CleanEmblem_Click(object? sender, RoutedEventArgs e)
    {
        if (_emblemPage != EmblemPage.Bonds || SelectedEmblemCondition is null) return;
        EmblemDirtinessInput.Value = 0;
        ApplyEmblemValues();
    }

    private void CleanAllEmblems_Click(object? sender, RoutedEventArgs e)
    {
        if (!CanEditEmblemConditions || Save is null || _emblemPage != EmblemPage.Bonds) return;
        try
        {
            Save = PendingEmblemValues(Save).WithCleanedEmblems();
            RefreshOverview();
            RefreshSections();
            Message.IsVisible = false;
            RefreshEmblemRecords(preserveEditor: false);
            RefreshSupportRecords(preserveEditor: false);
        }
        catch (Exception error) when (IsFileError(error)) { ShowMessage("EditFailed", error.Message); }
    }

    private void RefreshEmblemLanguage()
    {
        RefreshEmblemChoices();
        RefreshEmblemRecords(preserveEditor: true);
        RefreshMissingEmblems();
        RefreshBondRingOwner(_emblemPage == EmblemPage.BondRings ? SelectedBondRing : null);
        RefreshBondRingEffects(_emblemPage == EmblemPage.BondRings ? SelectedBondRing : null);
    }

    private void RefreshMissingEmblems()
    {
        string? selected = (MissingEmblemInput.SelectedItem as MissingEmblemChoice)?.Id;
        MissingEmblemChoice[] choices = [];
        if (Save is not null && CanEditEmblems)
        {
            try
            {
                choices = Save.ReadMissingEmblems().Select(row => new MissingEmblemChoice(row.Id, row.Name(UiLanguage.Current))).ToArray();
            }
            catch (Exception error) when (IsFileError(error))
            {
                if (Save.Sections.Any(section => section.Name == "GOD")) ShowMessage("EmblemsUnavailable", error.Message);
            }
        }
        MissingEmblemInput.ItemsSource = choices;
        MissingEmblemInput.SelectedItem = choices.FirstOrDefault(row => row.Id == selected) ?? choices.FirstOrDefault();
        AddEmblemButton.IsEnabled = AddEmblemConfirmButton.IsEnabled = choices.Length > 0;
    }

    public bool AddEmblem(string emblemId)
    {
        if (Save is null || !CanEditEmblems || _emblemPage != EmblemPage.Bonds) return false;
        try
        {
            var edited = PendingEmblemValues(Save).WithAddedEmblem(emblemId);
            Save = edited;
            _selectedEmblem = edited.ReadEmblems().Single(row => row.EmblemId == emblemId).InstanceId;
            SelectedEmblemRecordKey = null;
            RefreshOverview();
            RefreshSections();
            Message.IsVisible = false;
            RefreshEmblemChoices();
            RefreshEmblemRecords(preserveEditor: false);
            RefreshMissingEmblems();
            RefreshRosterEquipment(preserveEdits: true);
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("EditFailed", error.Message);
            return false;
        }
    }

    private void AddEmblem_Click(object? sender, RoutedEventArgs e)
    {
        if (MissingEmblemInput.SelectedItem is MissingEmblemChoice choice && AddEmblem(choice.Id))
            AddEmblemButton.Flyout?.Hide();
    }

    private void MissingEmblem_Changed(object? sender, SelectionChangedEventArgs e) =>
        AddEmblemConfirmButton.IsEnabled = MissingEmblemInput.SelectedItem is MissingEmblemChoice;

    private void RefreshBondRingOwner(BondRing? ring)
    {
        var owner = ring?.OwnerIndex is int index ? Save!.ReadRoster().First(row => row.Index == index) : null;
        BondRingOwnerValue.Text = owner is null ? UiLanguage.Get("None") : owner.Progress.CustomName ?? CharacterName(owner);
    }

    private void RefreshBondRingMeld()
    {
        if (MeldBondRingButton is null) return;
        var ring = _emblemPage == EmblemPage.BondRings ? SelectedBondRing : null;
        var meld = ring is not null ? BondRingCatalog.Melding(ring.RingHash) : null;
        BondRingMeldForm.IsVisible = meld is not null;
        MeldBondRingButton.IsEnabled = false;
        if (meld is null || ring is null || Save is null) return;
        MeldBondRingButton.Content = string.Format(UiLanguage.Get("MeldBondRing"), meld.Result.RankName);
        BondRingMeldCost.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            UiLanguage.Get("BondRingMeldCost"), meld.RequiredRings, meld.Source.RankName, meld.BondFragments);
        if (ring.OwnerIndex.HasValue || !CanEditMain
            || !int.TryParse(BondRingStockInput.Text, out int stock) || stock is < 0 or > 99
            || !int.TryParse(BondFragmentsInput.Text, out int fragments) || fragments < meld.BondFragments) return;
        var rings = Save.ReadBondRings();
        if (stock > Save.MaximumBondRingStock(ring.InstanceId)) return;
        int materials = stock + rings.Where(row => row.InstanceId != ring.InstanceId
            && row.RingHash == ring.RingHash && !row.OwnerIndex.HasValue).Sum(row => row.StockCount);
        MeldBondRingButton.IsEnabled = materials >= meld.RequiredRings
            && rings.Where(row => row.RingHash == meld.Result.Hash).Sum(row => row.StockCount) < meld.Result.MaxStock;
    }

    private void FillSBondRings_Click(object? sender, RoutedEventArgs e) => ManageBondRings(meld: false);
    private void MeldBondRing_Click(object? sender, RoutedEventArgs e) => ManageBondRings(meld: true);

    public bool ManageBondRings(bool meld)
    {
        if (Save is null || !CanEditBondRings || _emblemPage != EmblemPage.BondRings) return false;
        try
        {
            var edited = Save;
            var selected = SelectedBondRing;
            if (selected is not null && HasPendingEmblemValues())
                edited = edited.WithBondRingStock(selected.InstanceId, Amount(BondRingStockInput));
            if (meld)
            {
                if (selected is null) throw new ArgumentException("Select an existing bond ring.");
                edited = edited.WithMainValues(ReadMainInputs()).WithMeldedBondRing(selected.InstanceId);
                var result = BondRingCatalog.Melding(selected.RingHash)!.Result;
                SelectedEmblemRecordKey = edited.ReadBondRings().First(ring => ring.RingHash == result.Hash && !ring.OwnerIndex.HasValue).InstanceId.ToString();
            }
            else edited = edited.WithMissingSBondRings();
            Save = edited;
            if (meld) BondFragmentsInput.Value = edited.ReadMainValues().BondFragments;
            RefreshOverview();
            RefreshSections();
            Message.IsVisible = false;
            RefreshEmblemRecords(preserveEditor: false);
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("EditFailed", error.Message);
            return false;
        }
    }

    private void EmblemChoice_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingEmblems || EmblemChoiceInput.SelectedItem is not EmblemChoice choice) return;
        if (HasPendingEmblemValues() && !ApplyEmblemValues()) { RefreshEmblemChoices(); return; }
        _selectedEmblem = choice.Instance;
        SelectedEmblemRecordKey = null;
        RefreshEmblemRecords(preserveEditor: false);
    }

    private void EmblemRecord_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingEmblems || sender != ActiveEmblemList || ActiveEmblemList.SelectedItem is not EmblemRow row) return;
        if (row.Key == SelectedEmblemRecordKey) return;
        if (HasPendingEmblemValues() && !ApplyEmblemValues()) { RefreshEmblemRecords(preserveEditor: true); return; }
        SelectedEmblemRecordKey = row.Key;
        RefreshEmblemRecords(preserveEditor: false);
    }

    private void EmblemBondLevel_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingEmblems || EmblemBondLevelInput.SelectedItem is not int level || SelectedEmblem is not { } emblem || SelectedBond is not { } bond) return;
        _refreshingEmblems = true;
        EmblemBondExpInput.Value = EmblemCatalog.ExperienceForLevel(emblem, bond.PersonId, level);
        _refreshingEmblems = false;
    }

    private void UpdateEmblemBondLevel()
    {
        if (_refreshingEmblems || SelectedEmblem?.EmblemId == EmblemCatalog.AlearEmblemId
            || !int.TryParse(EmblemBondExpInput.Text, out int experience) || experience < 0 || experience > EmblemCatalog.MaxBondExperience) return;
        _refreshingEmblems = true;
        EmblemBondLevelInput.SelectedItem = EmblemCatalog.LevelForExperience(experience);
        _refreshingEmblems = false;
    }

    private void MaxEmblemBond_Click(object? sender, RoutedEventArgs e)
    { MaximizeEmblemBonds(all: false); }
    private void MaxAllEmblemBonds_Click(object? sender, RoutedEventArgs e)
    { MaximizeEmblemBonds(all: true); }
    public bool MaximizeEmblemBonds(bool all)
    {
        if (Save is null || _selectedEmblem is not uint instance || _emblemPage != EmblemPage.Bonds) return false;
        try
        {
            string? personId = null;
            if (!all) personId = SelectedBond?.PersonId ?? throw new ArgumentException("Select an existing character bond.");
            var edited = Save;
            if (SelectedEmblemCondition is { } condition)
                edited = edited.WithEmblemDirtiness(condition.InstanceId, Amount(EmblemDirtinessInput));
            edited = edited.WithMaximumEmblemBonds(instance, personId);
            Save = edited;
            RefreshOverview();
            RefreshSections();
            Message.IsVisible = false;
            RefreshEmblemRecords(preserveEditor: false);
            RefreshSupportRecords(preserveEditor: false);
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("EditFailed", error.Message);
            return false;
        }
    }
    private void ApplyEmblem_Click(object? sender, RoutedEventArgs e) => ApplyEmblemValues();
    private void EmblemSearch_Changed(object? sender, TextChangedEventArgs e)
    { if (!_refreshingEmblems && sender == ActiveEmblemSearch) RefreshEmblemRecords(preserveEditor: true); }
}
