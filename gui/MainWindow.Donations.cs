using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    private sealed record DonationEditor(DonationCountry Country, TextBlock Title, ComboBox Level,
        NumericUpDown Amount, TextBlock LevelLabel, TextBlock AmountLabel);
    private readonly Dictionary<string, DonationEditor> _donationEditors = new(StringComparer.Ordinal);
    private bool _refreshingDonations;
    public bool CanEditDonations { get; private set; }

    private void BuildDonationRows()
    {
        if (_donationEditors.Count != 0) return;
        foreach (var country in DonationCatalog.Countries)
        {
            var title = new TextBlock { Tag = country.Id + "Title", FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            var levelLabel = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var amountLabel = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var level = new ComboBox { Tag = country.Id + "Level", ItemsSource = Enumerable.Range(1, 5).ToArray(),
                SelectedIndex = -1, Height = 42, MinHeight = 42, MaxHeight = 42, Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch };
            var amount = new NumericUpDown { Tag = country.Id, Minimum = 0, Maximum = DonationCatalog.MaximumAmount,
                FormatString = "0", Height = 42, Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch };
            var fields = new Grid { ColumnDefinitions = new("*,*"), RowDefinitions = new("Auto,Auto"),
                ColumnSpacing = 24, RowSpacing = 8 };
            fields.Children.Add(levelLabel);
            Grid.SetColumn(amountLabel, 1);
            fields.Children.Add(amountLabel);
            Grid.SetRow(level, 1);
            fields.Children.Add(level);
            Grid.SetRow(amount, 1);
            Grid.SetColumn(amount, 1);
            fields.Children.Add(amount);
            var row = new StackPanel { Spacing = 8 };
            row.Children.Add(title);
            row.Children.Add(fields);
            DonationInputs.Children.Add(row);
            var editor = new DonationEditor(country, title, level, amount, levelLabel, amountLabel);
            _donationEditors.Add(country.Id, editor);
            level.SelectionChanged += (_, _) => ChangeDonationLevel(editor);
            amount.PropertyChanged += (_, change) =>
            {
                if (change.Property == NumericUpDown.TextProperty) UpdateDonationLevel(editor);
            };
        }
    }

    private void LoadDonations()
    {
        CanEditDonations = DonationInputs.IsEnabled = MaxAllDonationsButton.IsEnabled = false;
        _refreshingDonations = true;
        try
        {
            foreach (var editor in _donationEditors.Values)
            {
                editor.Amount.Value = null;
                editor.Amount.Text = "";
                editor.Level.SelectedIndex = -1;
            }
            if (Save is null || !CanEditMain) return;
            var donations = Save.ReadDonations();
            foreach (var donation in donations) SetDonationAmount(_donationEditors[donation.Country.Id], donation.Amount);
            CanEditDonations = DonationInputs.IsEnabled = MaxAllDonationsButton.IsEnabled = true;
        }
        catch (Exception error) when (IsFileError(error)) { ShowMessage("DonationsUnavailable", error.Message); }
        finally { _refreshingDonations = false; }
    }

    private void RefreshDonationLanguage()
    {
        BuildDonationRows();
        foreach (var editor in _donationEditors.Values)
        {
            string name = editor.Country.Name(UiLanguage.Current);
            editor.Title.Text = name;
            editor.LevelLabel.Text = UiLanguage.Get("Level");
            editor.AmountLabel.Text = UiLanguage.Get("DonatedGold");
            AutomationProperties.SetName(editor.Level, name + " — " + UiLanguage.Get("Level"));
            AutomationProperties.SetName(editor.Amount, name + " — " + UiLanguage.Get("DonatedGold"));
        }
    }

    private static void SetDonationAmount(DonationEditor editor, int amount)
    {
        editor.Amount.Value = amount;
        editor.Amount.Text = amount.ToString(editor.Amount.NumberFormat);
        editor.Level.SelectedIndex = editor.Country.LevelForAmount(amount) - 1;
    }

    private Dictionary<string, int> ReadDonationInputs() => _donationEditors.ToDictionary(
        entry => entry.Key, entry => Amount(entry.Value.Amount), StringComparer.Ordinal);

    private void ChangeDonationLevel(DonationEditor editor)
    {
        if (_refreshingDonations || !CanEditDonations || editor.Level.SelectedIndex < 0) return;
        _refreshingDonations = true;
        try { SetDonationAmount(editor, editor.Country.AmountForLevel(editor.Level.SelectedIndex + 1)); }
        finally { _refreshingDonations = false; }
    }

    private void UpdateDonationLevel(DonationEditor editor)
    {
        if (_refreshingDonations || !CanEditDonations) return;
        _refreshingDonations = true;
        try { editor.Level.SelectedIndex = editor.Country.LevelForAmount(Amount(editor.Amount)) - 1; }
        catch (ArgumentException) { editor.Level.SelectedIndex = -1; }
        finally { _refreshingDonations = false; }
    }

    public bool MaximizeDonations(string? countryId = null)
    {
        if (!CanEditDonations || (countryId is not null && !_donationEditors.ContainsKey(countryId))) return false;
        _refreshingDonations = true;
        try
        {
            foreach (var editor in _donationEditors.Values)
                if (countryId is null || editor.Country.Id == countryId)
                    SetDonationAmount(editor, editor.Country.AmountForLevel(editor.Country.MaximumLevel));
        }
        finally { _refreshingDonations = false; }
        return ApplyMainValues();
    }

    private void MaxAllDonations_Click(object? sender, RoutedEventArgs e) => MaximizeDonations();
}
