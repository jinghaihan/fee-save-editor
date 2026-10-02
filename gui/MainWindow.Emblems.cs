using Avalonia.Controls;
using Avalonia.Interactivity;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record EmblemChoice(uint Instance, string Label);
    public sealed record EmblemRow(string Key, string Label);
    public bool CanEditEmblems { get; private set; }
    public bool CanEditBondRings { get; private set; }
    private uint? _selectedEmblem;
    private string? _selectedEmblemRecord;
    private int _emblemTab;
    private bool _refreshingEmblems;

    public void ShowEmblems()
    {
        if (HasPendingSupportValues() && !ApplySupportValues())
        {
            MainNavigation.SelectedIndex = 4;
            return;
        }
        MainPanel.IsVisible = ItemsPanel.IsVisible = RosterPanel.IsVisible = InspectorPanel.IsVisible = false;
        SupportsPanel.IsVisible = false;
        AchievementsPanel.IsVisible = false;
        EmblemsPanel.IsVisible = true;
        ApplyMainButton.IsVisible = false;
        MainNavigation.SelectedIndex = 3;
        RefreshSupportRecords(preserveEditor: false);
        RefreshPageTitle();
    }

    private EmblemBond? SelectedBond => CanEditEmblems && Save is not null
        ? Save.ReadEmblems().FirstOrDefault(row => row.InstanceId == _selectedEmblem)?.Bonds
            .FirstOrDefault(row => row.PersonId == _selectedEmblemRecord) : null;
    private BondRing? SelectedBondRing => CanEditBondRings && Save is not null
        ? Save.ReadBondRings().FirstOrDefault(row => row.InstanceId.ToString() == _selectedEmblemRecord) : null;
    private SavedEmblem? SelectedEmblem => CanEditEmblems && Save is not null
        ? Save.ReadEmblems().FirstOrDefault(row => row.InstanceId == _selectedEmblem) : null;

    private void LoadEmblems()
    {
        _refreshingEmblems = true;
        CanEditEmblems = CanEditBondRings = false;
        _selectedEmblem = null;
        _selectedEmblemRecord = null;
        EmblemChoiceInput.ItemsSource = Array.Empty<EmblemChoice>();
        EmblemRecordList.ItemsSource = Array.Empty<EmblemRow>();
        EmblemSearch.Clear();
        EmblemBondLevelInput.ItemsSource = Enumerable.Range(1, 20).ToArray();
        EmblemBondExpInput.Value = BondRingStockInput.Value = null;
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
        }
        RefreshEmblemChoices();
        RefreshEmblemRecords(preserveEditor: false);
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
        if (EmblemRecordList is null) return;
        _refreshingEmblems = true;
        EmblemBondForm.IsVisible = EmblemChoiceInput.IsVisible = _emblemTab == 0;
        BondRingForm.IsVisible = _emblemTab == 1;
        EmblemListTitle.Text = UiLanguage.Get(_emblemTab == 0 ? "CharacterList" : "BondRings");
        bool available = _emblemTab == 0 ? CanEditEmblems : CanEditBondRings;
        var emblem = _emblemTab == 0 ? SelectedEmblem : null;
        MaxAllEmblemBondsButton.IsVisible = _emblemTab == 0;
        MaxAllEmblemBondsButton.IsEnabled = emblem is not null && EmblemCatalog.Emblem(emblem.EmblemId) is not null;
        FillSBondRingsButton.IsVisible = _emblemTab == 1;
        FillSBondRingsButton.IsEnabled = CanEditBondRings;
        EmblemCompletion.Text = "";
        if (_emblemTab == 1 && CanEditBondRings && Save is not null)
        {
            var owned = Save.ReadBondRings().Where(ring => ring.StockCount > 0).Select(ring => ring.RingHash).ToHashSet();
            int complete = BondRingCatalog.SRings.Count(ring => owned.Contains(ring.Hash));
            EmblemCompletion.Text = $"S: {complete}/{BondRingCatalog.SRings.Count}";
            FillSBondRingsButton.IsEnabled = complete < BondRingCatalog.SRings.Count;
        }
        if (emblem is not null)
        {
            var knownBonds = emblem.Bonds.Where(bond => RosterCatalog.Person(ItemCatalog.Hash(bond.PersonId)) is not null).ToArray();
            int complete = knownBonds.Count(bond => bond.Level == EmblemCatalog.MaximumLevel(emblem, bond.PersonId)
                && bond.Experience == EmblemCatalog.ExperienceForLevel(emblem, bond.PersonId, bond.Level));
            EmblemCompletion.Text = $"{complete}/{knownBonds.Length}";
        }
        EmblemListAvailability.Text = available || Save is null ? "" : UiLanguage.Get("Unavailable");
        EmblemListAvailability.IsVisible = !string.IsNullOrEmpty(EmblemListAvailability.Text);
        IEnumerable<EmblemRow> rows = Array.Empty<EmblemRow>();
        if (Save is not null && available)
        {
            if (_emblemTab == 0)
                rows = (Save.ReadEmblems().FirstOrDefault(row => row.InstanceId == _selectedEmblem)?.Bonds ?? [])
                    .Select(row => new EmblemRow(row.PersonId, $"{BondPersonName(row.PersonId)} · Lv. {row.Level}"));
            else
                rows = Save.ReadBondRings().Select(row => new EmblemRow(row.InstanceId.ToString(), $"{BondRingName(row)} · ×{row.StockCount}"));
        }
        string query = EmblemSearch.Text?.Trim() ?? "";
        var filtered = rows.Where(row => row.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        EmblemRecordList.ItemsSource = filtered;
        EmblemRecordList.SelectedItem = filtered.FirstOrDefault(row => row.Key == _selectedEmblemRecord);
        if (!preserveEditor)
        {
            EmblemRecordList.SelectedItem ??= filtered.FirstOrDefault();
            _selectedEmblemRecord = (EmblemRecordList.SelectedItem as EmblemRow)?.Key;
            LoadEmblemEditor();
        }
        _refreshingEmblems = false;
        RefreshBondRingMeld();
    }

    private void LoadEmblemEditor()
    {
        var bond = _emblemTab == 0 ? SelectedBond : null;
        var ring = _emblemTab == 1 ? SelectedBondRing : null;
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
        BondRingStockInput.Maximum = ring?.OwnerIndex.HasValue == true ? 1 : 99;
        BondRingStockInput.Value = ring?.StockCount;
        RefreshBondRingOwner(ring);
    }

    private bool HasPendingEmblemValues()
    {
        if (_emblemTab == 0)
        {
            var bond = SelectedBond;
            return bond is not null && (EmblemBondLevelInput.SelectedItem is not int level || level != bond.Level
                || EmblemBondExpInput.Text != bond.Experience.ToString());
        }
        var ring = SelectedBondRing;
        return ring is not null && BondRingStockInput.Text != ring.StockCount.ToString();
    }

    public bool ApplyEmblemValues()
    {
        if (Save is null) return false;
        try
        {
            if (_emblemTab == 0)
            {
                var bond = SelectedBond ?? throw new ArgumentException("Select an existing character bond.");
                if (!HasPendingEmblemValues()) return true;
                int level = EmblemBondLevelInput.SelectedItem is int selected ? selected : throw new ArgumentException(UiLanguage.Get("InvalidAmount"));
                Save = Save.WithEmblemBond(_selectedEmblem!.Value, bond.PersonId, level, Amount(EmblemBondExpInput));
            }
            else
            {
                var ring = SelectedBondRing ?? throw new ArgumentException("Select an existing bond ring.");
                Save = Save.WithBondRingStock(ring.InstanceId, Amount(BondRingStockInput));
            }
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

    private void RefreshEmblemLanguage()
    {
        RefreshEmblemChoices();
        RefreshEmblemRecords(preserveEditor: true);
        RefreshBondRingOwner(_emblemTab == 1 ? SelectedBondRing : null);
    }

    private void RefreshBondRingOwner(BondRing? ring)
    {
        var owner = ring?.OwnerIndex is int index ? Save!.ReadRoster().First(row => row.Index == index) : null;
        BondRingOwnerValue.Text = owner is null ? UiLanguage.Get("None") : owner.Progress.CustomName ?? CharacterName(owner);
    }

    private void RefreshBondRingMeld()
    {
        if (MeldBondRingButton is null) return;
        var ring = _emblemTab == 1 ? SelectedBondRing : null;
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
        int materials = stock + rings.Where(row => row.InstanceId != ring.InstanceId
            && row.RingHash == ring.RingHash && !row.OwnerIndex.HasValue).Sum(row => row.StockCount);
        MeldBondRingButton.IsEnabled = materials >= meld.RequiredRings
            && rings.Where(row => row.RingHash == meld.Result.Hash).Sum(row => row.StockCount) < meld.Result.MaxStock;
    }

    private void FillSBondRings_Click(object? sender, RoutedEventArgs e) => ManageBondRings(meld: false);
    private void MeldBondRing_Click(object? sender, RoutedEventArgs e) => ManageBondRings(meld: true);

    public bool ManageBondRings(bool meld)
    {
        if (Save is null || !CanEditBondRings || _emblemTab != 1) return false;
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
                _selectedEmblemRecord = edited.ReadBondRings().First(ring => ring.RingHash == result.Hash && !ring.OwnerIndex.HasValue).InstanceId.ToString();
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
        _selectedEmblemRecord = null;
        RefreshEmblemRecords(preserveEditor: false);
    }

    private void EmblemRecord_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingEmblems || EmblemRecordList.SelectedItem is not EmblemRow row) return;
        if (row.Key == _selectedEmblemRecord) return;
        if (HasPendingEmblemValues() && !ApplyEmblemValues()) { RefreshEmblemRecords(preserveEditor: true); return; }
        _selectedEmblemRecord = row.Key;
        RefreshEmblemRecords(preserveEditor: false);
    }

    private void EmblemTabs_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingEmblems || EmblemRecordList is null || EmblemTabs.SelectedIndex == _emblemTab) return;
        if (HasPendingEmblemValues() && !ApplyEmblemValues())
        {
            _refreshingEmblems = true; EmblemTabs.SelectedIndex = _emblemTab; _refreshingEmblems = false; return;
        }
        _emblemTab = EmblemTabs.SelectedIndex;
        _selectedEmblemRecord = null;
        _refreshingEmblems = true; EmblemSearch.Clear(); _refreshingEmblems = false;
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
        if (Save is null || _selectedEmblem is not uint instance || _emblemTab != 0) return false;
        try
        {
            string? personId = null;
            if (!all) personId = SelectedBond?.PersonId ?? throw new ArgumentException("Select an existing character bond.");
            var edited = Save.WithMaximumEmblemBonds(instance, personId);
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
    { if (!_refreshingEmblems) RefreshEmblemRecords(preserveEditor: true); }
}
