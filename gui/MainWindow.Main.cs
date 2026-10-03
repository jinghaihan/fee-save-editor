using System.Globalization;
using Avalonia.Controls;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public bool CanEditMain { get; private set; }
    private bool _canEditProtagonistName;

    private void LoadProtagonistName()
    {
        _canEditProtagonistName = PlayerNameInput.IsEnabled = false;
        PlayerNameInput.Clear();
        if (Save is null || !CanEditMain || !Save.Sections.Any(section => section.Name == "UNIT")) return;
        try
        {
            if (Save.ReadProtagonistName() is not { } name) return;
            PlayerNameInput.Text = name;
            _canEditProtagonistName = PlayerNameInput.IsEnabled = true;
        }
        catch (Exception error) when (IsFileError(error)) { ShowMessage("MainUnavailable", error.Message); }
    }

    public bool ApplyMainValues()
    {
        if (!CanEditMain || Save is null)
            return false;
        try
        {
            var edited = Save.WithMainValues(ReadMainInputs());
            if (CanEditDonations) edited = edited.WithDonations(ReadDonationInputs());
            if (CanEditMinigames) edited = edited.WithMinigameRecords(ReadMinigameInputs());
            if (CanEditActivities) edited = edited.WithSomnielActivities(ReadActivityInputs());
            if (_canEditProtagonistName) edited = edited.WithProtagonistName(PlayerNameInput.Text ?? "");
            Save = edited;
            if (CanEditRoster) RefreshRoster(selectEditor: false, preserveSelection: true);
            RefreshQuantityItems(selectEditor: !HasPendingQuantityItemValues());
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

    private MainValues ReadMainInputs() => new(Amount(MoneyInput), Amount(BondFragmentsInput),
        Amount(IronIngotsInput), Amount(SteelIngotsInput), Amount(SilverIngotsInput),
        (Difficulty)DifficultyInput.SelectedIndex, (GameMode)ModeInput.SelectedIndex, SommieNameInput.Text ?? "");

    private static int Amount(NumericUpDown input)
    {
        if (!int.TryParse(input.Text, NumberStyles.Integer, input.NumberFormat, out int value)
            || value < input.Minimum || value > input.Maximum)
            throw new ArgumentException(UiLanguage.Get("InvalidAmount"));
        return value;
    }

    private void LoadMainValues()
    {
        CanEditMain = false;
        SettingsInputs.IsEnabled = false;
        ResourceInputs.IsEnabled = false;
        DifficultyInput.SelectedIndex = -1;
        ModeInput.SelectedIndex = -1;
        SommieNameInput.Clear();
        foreach (var input in new[] { MoneyInput, BondFragmentsInput, IronIngotsInput, SteelIngotsInput, SilverIngotsInput })
            input.Value = null;
        if (Save is null)
            return;
        try
        {
            var values = Save.ReadMainValues();
            MainLimits.ValidateAmounts(values);
            MoneyInput.Value = values.Money;
            BondFragmentsInput.Value = values.BondFragments;
            IronIngotsInput.Value = values.IronIngots;
            SteelIngotsInput.Value = values.SteelIngots;
            SilverIngotsInput.Value = values.SilverIngots;
            DifficultyInput.SelectedIndex = (int)values.Difficulty;
            ModeInput.SelectedIndex = (int)values.GameMode;
            SommieNameInput.Text = values.SommieName;
            CanEditMain = true;
            SettingsInputs.IsEnabled = true;
            ResourceInputs.IsEnabled = true;
            ShowPage(inspector: false);
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowPage(inspector: true);
            if (Save.Kind == SaveKind.Game)
                ShowMessage("MainUnavailable", error.Message);
        }
    }

    private void RefreshOptions()
    {
        int difficulty = DifficultyInput.SelectedIndex;
        int mode = ModeInput.SelectedIndex;
        DifficultyInput.ItemsSource = new[] { "Normal", "Hard", "Maddening" }.Select(UiLanguage.Get).ToArray();
        ModeInput.ItemsSource = new[] { "Casual", "Classic" }.Select(UiLanguage.Get).ToArray();
        DifficultyInput.SelectedIndex = difficulty;
        ModeInput.SelectedIndex = mode;
    }

    private void ShowPage(bool inspector)
    {
        MinigamesPanel.IsVisible = false;
        MainPanel.IsVisible = !inspector;
        ItemsPanel.IsVisible = false;
        RosterPanel.IsVisible = false;
        EmblemsPanel.IsVisible = false;
        EmblemPagesPanel.IsVisible = false;
        BondRingsPanel.IsVisible = false;
        SupportsPanel.IsVisible = false;
        AchievementsPanel.IsVisible = false;
        InspectorPanel.IsVisible = inspector;
        MainNavigation.SelectedIndex = inspector ? 7 : 0;
        if (inspector && CanEditVariables && !HasPendingVariable()) RefreshVariables();
        RefreshPageLayout();
    }

    private void RefreshPageLayout()
    {
        WindowLayout.RowSpacing = ItemsPanel.IsVisible || EmblemPagesPanel.IsVisible || InspectorPanel.IsVisible ? 8 : 24;
    }
}
