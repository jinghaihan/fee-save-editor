using Avalonia.Controls;
using Avalonia.Interactivity;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record DonationChoice(string Id, string Name);
    public bool CanEditDonations { get; private set; }
    private readonly Dictionary<string, int> _donationAmounts = new();
    private string? _donationCountryId;
    private bool _refreshingDonations;

    private void LoadDonations()
    {
        CanEditDonations = false;
        DonationInputs.IsEnabled = false;
        MaxAllDonationsButton.IsEnabled = false;
        _refreshingDonations = true;
        try
        {
            _donationAmounts.Clear();
            _donationCountryId = null;
            DonationCountryInput.SelectedIndex = -1;
            DonationLevelInput.SelectedIndex = -1;
            DonationAmountInput.Value = null;
            if (Save is null || !CanEditMain) return;
            foreach (var row in Save.ReadDonations()) _donationAmounts.Add(row.Country.Id, row.Amount);
            CanEditDonations = true;
            DonationInputs.IsEnabled = true;
            MaxAllDonationsButton.IsEnabled = true;
            _donationCountryId = DonationCatalog.Countries[0].Id;
            DonationCountryInput.SelectedIndex = 0;
            LoadDonationCountry();
        }
        catch (Exception error) when (IsFileError(error))
        {
            ShowMessage("DonationsUnavailable", error.Message);
        }
        finally { _refreshingDonations = false; }
    }

    private void RefreshDonationLanguage()
    {
        _refreshingDonations = true;
        try
        {
            int selected = DonationCountryInput.SelectedIndex;
            DonationCountryInput.ItemsSource = DonationCatalog.Countries.Select(country =>
                new DonationChoice(country.Id, country.Name(UiLanguage.Current))).ToArray();
            DonationCountryInput.SelectedIndex = selected;
            DonationLevelInput.ItemsSource = Enumerable.Range(1, 5).ToArray();
            if (_donationCountryId is not null && CanEditDonations) UpdateDonationLevelSelection();
        }
        finally { _refreshingDonations = false; }
    }

    private void LoadDonationCountry()
    {
        if (_donationCountryId is null) return;
        int amount = _donationAmounts[_donationCountryId];
        DonationAmountInput.Value = amount;
        DonationAmountInput.Text = amount.ToString(DonationAmountInput.NumberFormat);
        DonationLevelInput.SelectedIndex = DonationCatalog.Country(_donationCountryId).LevelForAmount(amount) - 1;
    }

    private Dictionary<string, int> ReadDonationInputs()
    {
        var values = new Dictionary<string, int>(_donationAmounts);
        if (_donationCountryId is null) throw new ArgumentException(UiLanguage.Get("SelectCountry"));
        values[_donationCountryId] = Amount(DonationAmountInput);
        return values;
    }

    private void DonationCountry_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingDonations || !CanEditDonations || DonationCountryInput.SelectedItem is not DonationChoice selected) return;
        _refreshingDonations = true;
        try
        {
            if (_donationCountryId is not null) _donationAmounts[_donationCountryId] = Amount(DonationAmountInput);
            _donationCountryId = selected.Id;
            LoadDonationCountry();
        }
        catch (Exception error) when (IsFileError(error))
        {
            DonationCountryInput.SelectedItem = DonationCountryInput.Items.Cast<DonationChoice>()
                .FirstOrDefault(row => row.Id == _donationCountryId);
            ShowMessage("EditFailed", error.Message);
        }
        finally { _refreshingDonations = false; }
    }

    private void DonationLevel_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingDonations || !CanEditDonations || _donationCountryId is null || DonationLevelInput.SelectedIndex < 0) return;
        _refreshingDonations = true;
        try
        {
            int amount = DonationCatalog.Country(_donationCountryId).AmountForLevel(DonationLevelInput.SelectedIndex + 1);
            DonationAmountInput.Value = amount;
            DonationAmountInput.Text = amount.ToString(DonationAmountInput.NumberFormat);
        }
        finally { _refreshingDonations = false; }
    }

    private void UpdateDonationLevelSelection()
    {
        if (_donationCountryId is null) return;
        try { DonationLevelInput.SelectedIndex = DonationCatalog.Country(_donationCountryId).LevelForAmount(Amount(DonationAmountInput)) - 1; }
        catch (ArgumentException) { DonationLevelInput.SelectedIndex = -1; }
    }

    private void UpdateDonationLevel()
    {
        if (_refreshingDonations || !CanEditDonations) return;
        _refreshingDonations = true;
        try { UpdateDonationLevelSelection(); }
        finally { _refreshingDonations = false; }
    }

    public bool MaximizeDonations(bool allCountries)
    {
        if (!CanEditDonations || _donationCountryId is null) return false;
        var targets = DonationCatalog.Countries.Where(country => allCountries || country.Id == _donationCountryId);
        foreach (var country in targets) _donationAmounts[country.Id] = country.AmountForLevel(country.MaximumLevel);
        _refreshingDonations = true;
        try { LoadDonationCountry(); }
        finally { _refreshingDonations = false; }
        return ApplyMainValues();
    }

    private void MaxDonation_Click(object? sender, RoutedEventArgs e) => MaximizeDonations(false);
    private void MaxAllDonations_Click(object? sender, RoutedEventArgs e) => MaximizeDonations(true);
}
