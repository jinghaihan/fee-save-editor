using System.Globalization;
using Avalonia.Controls;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public bool CanEditMain { get; private set; }

    public bool ApplyMainValues()
    {
        if (!CanEditMain || Save is null)
            return false;
        try
        {
            var values = new MainValues(Amount(MoneyInput), Amount(BondFragmentsInput),
                Amount(IronIngotsInput), Amount(SteelIngotsInput), Amount(SilverIngotsInput),
                (Difficulty)DifficultyInput.SelectedIndex, (GameMode)ModeInput.SelectedIndex,
                SommieNameInput.Text ?? "");
            Save = Save.WithMainValues(values);
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
        ApplyMainButton.IsEnabled = false;
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
            ApplyMainButton.IsEnabled = true;
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
        MainPanel.IsVisible = !inspector;
        ItemsPanel.IsVisible = false;
        RosterPanel.IsVisible = false;
        EmblemsPanel.IsVisible = false;
        SupportsPanel.IsVisible = false;
        InspectorPanel.IsVisible = inspector;
        ApplyMainButton.IsVisible = !inspector;
        MainNavigation.SelectedIndex = inspector ? -1 : 0;
        RefreshPageTitle();
    }

    private void RefreshPageTitle()
    {
        PageTitle.Text = UiLanguage.Get("Inspector");
        PageTitle.IsVisible = InspectorPanel.IsVisible;
    }
}
