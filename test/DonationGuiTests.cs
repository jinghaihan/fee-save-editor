using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FeeEditor.Core;
using FeeEditor.Gui;

internal static class DonationGuiTests
{
    public static void Run(MainWindow window, string temporary)
    {
        byte[] original = DonationTests.Fixture();
        string source = Path.Combine(temporary, "donation-gui-source");
        File.WriteAllBytes(source, original);
        Check(window.LoadSave(source) && window.CanEditDonations, "Donation controls were not enabled.");
        var countries = DonationCatalog.Countries;
        var inputs = window.FindControl<StackPanel>("DonationInputs")!;
        var main = window.FindControl<Grid>("MainPanel")!;
        Check(((StackPanel)main.Children[0]).Children.Select(control => control.Name)
            .SequenceEqual(new[] { "SettingsCard", "ResourcesCard", "ActivitiesCard" })
            && ((StackPanel)main.Children[1]).Children.Single().Name == "DonationsCard",
            "Main must place settings/resources/activities on the left and only donations on the right.");
        Check(inputs.Children.Count == 4 && window.FindControl<ComboBox>("DonationCountryInput") is null,
            "Donations must show four countries in one card without a country picker.");
        var card = window.FindControl<SukiUI.Controls.GlassCard>("DonationsCard")!;
        Check(!inputs.GetLogicalDescendants().OfType<Button>().Any()
            && card.GetLogicalDescendants().OfType<Button>().Single() == window.FindControl<Button>("MaxAllDonationsButton"),
            "Donations must offer only one all-country max button, not individual country buttons.");
        Check(inputs.GetLogicalDescendants().All(control => control.GetType().Name != "GlassCard" && control is not ScrollViewer),
            "Donations created nested cards or internal scrolling.");
        double width = window.Width;
        foreach (double size in new[] { 1120d, 860d })
        {
            window.Width = size;
            window.FindControl<Avalonia.Controls.Primitives.TabStrip>("MainNavigation")!.SelectedIndex = 0;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            foreach (var country in countries)
            {
                var level = Input<ComboBox>(country.Id + "Level");
                var number = Input<NumericUpDown>(country.Id);
                var left = level.TranslatePoint(new Point(), inputs)!.Value;
                var right = number.TranslatePoint(new Point(), inputs)!.Value;
                Check(Math.Abs(left.Y - right.Y) < 1 && right.X - left.X - level.Bounds.Width >= 23,
                    $"Country fields lost alignment or spacing at {size}: {country.Id} level={left}/{level.Bounds}, amount={right}/{number.Bounds}.");
            }
        }
        window.Width = width;
        var amount = Input<NumericUpDown>(countries[0].Id);
        var levels = Input<ComboBox>(countries[0].Id + "Level");
        Check(amount.Minimum == 0 && amount.Maximum == DonationCatalog.MaximumAmount, "GUI donation range differs from the core.");
        Check(amount.Value == 5100 && levels.SelectedIndex == 1, "Loading clamped a partial donation.");
        Check(Input<NumericUpDown>(countries[3].Id).Value == 1_000_000 && Input<ComboBox>(countries[3].Id + "Level").SelectedIndex == 4,
            "Above-threshold saved amount was clamped.");
        levels.SelectedIndex = 3;
        Check(amount.Value == 40000, "Level dropdown did not write its cumulative amount.");
        amount.Text = "89999";
        Check(levels.SelectedIndex == 3, "Amount input did not update its level.");
        foreach (var language in LanguageCatalog.Languages)
        {
            window.SetLanguage(language.Code); Dispatcher.UIThread.RunJobs();
            foreach (var country in countries)
                Check(Input<TextBlock>(country.Id + "Title").Text == country.Name(language.Code), "Country title did not translate.");
            Check(amount.Text == "89999" && levels.SelectedIndex == 3, "Language switching discarded pending donations.");
        }
        window.SetLanguage("en");
        Check(window.MaximizeDonations(countries[0].Id), "Single-country max failed.");
        Check(window.Save!.ReadDonations().Select(row => row.Amount).SequenceEqual(new[] { 90000, 15000, 90000, 1_000_000 }),
            "Single-country max changed other countries.");
        Check(window.MaximizeDonations() && window.Save.ReadDonations().All(row => row.Amount == 90000), "All-country max failed.");
        Check(window.LoadSave(source), "Could not reload donation fixture.");
        amount.Text = "12345";
        Input<NumericUpDown>(countries[1].Id).Text = "45000";
        window.FindControl<TextBox>("SommieNameInput")!.Text = "Donation round-trip 索拉";
        window.ShowMinigames();
        string output = Path.Combine(temporary, "donation-gui-copy");
        Check(window.SaveCopy(output), "Save Copy did not apply pending donations.");
        var saved = EngageSave.Load(output);
        Check(saved.ReadDonations().Select(row => row.Amount).SequenceEqual(new[] { 12345, 45000, 90000, 1_000_000 }),
            "All country edits were not saved together.");
        Check(saved.ReadMainValues().SommieName == "Donation round-trip 索拉", "Donation saving lost Main name resizing.");
        Check(window.LoadSave(source), "Could not restore donation GUI fixture.");
        byte[] before = window.Save!.Serialize();
        window.FindControl<NumericUpDown>("MoneyInput")!.Value = 777;
        foreach (var country in countries)
        {
            var current = Input<NumericUpDown>(country.Id);
            string valid = current.Text!;
            foreach (string invalid in new[] { "", "abc", "-1", "10000000", "1.5", "2147483648" })
            {
                current.Text = invalid;
                Check(!window.ApplyMainValues(), "Invalid donation amount was accepted.");
                Check(window.Save.Serialize().AsSpan().SequenceEqual(before), "Invalid donation partially committed Main fields.");
                window.SetLanguage("ja"); window.SetLanguage("en");
                Check(current.Text == invalid, "Language switching discarded invalid input.");
            }
            current.Text = valid;
        }
        amount.Text = "";
        string rejected = Path.Combine(temporary, "donation-gui-rejected");
        Check(!window.SaveCopy(rejected) && !File.Exists(rejected), "Invalid donation created an output.");
        string bad = Path.Combine(temporary, "donation-gui-malformed");
        File.WriteAllBytes(bad, DonationTests.Fixture("duplicate"));
        Check(window.LoadSave(bad) && !window.CanEditDonations && window.CanEditMain, "Malformed donations disabled unrelated Main editing.");
        Check(!inputs.IsEnabled && countries.All(country => Input<NumericUpDown>(country.Id).Value is null),
            "Malformed donations left stale controls.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Donation GUI tests overwrote the source.");
        Console.WriteLine("Donation GUI: one card, four country rows, linked values, nine languages, max scopes and atomic validation passed.");

        T Input<T>(string tag) where T : Control => inputs.GetLogicalDescendants().OfType<T>()
            .Single(control => Equals(control.Tag, tag));
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
