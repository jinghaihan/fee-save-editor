using Avalonia.Controls;
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
        var countries = window.FindControl<ComboBox>("DonationCountryInput")!;
        var levels = window.FindControl<ComboBox>("DonationLevelInput")!;
        var amount = window.FindControl<NumericUpDown>("DonationAmountInput")!;
        Check(amount.Minimum == 0 && amount.Maximum == DonationCatalog.MaximumAmount, "GUI donation range differs from the core.");
        Check(amount.Value == 5100 && levels.SelectedIndex == 1, "Loading clamped a partial donation.");
        levels.SelectedIndex = 3;
        Check(amount.Value == 40000, "Level dropdown did not write its cumulative amount.");
        amount.Text = "89999";
        Check(levels.SelectedIndex == 3, "Amount input did not update its level.");
        countries.SelectedIndex = 3;
        Check(amount.Value == 1_000_000 && levels.SelectedIndex == 4, "Above-threshold saved amount was clamped.");
        countries.SelectedIndex = 0;
        Check(amount.Text == "89999", "Country switching discarded pending edits.");
        for (int pass = 0; pass < 3; pass++)
        {
            window.SetLanguage("zh-Hans"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.DonationChoice)countries.SelectedItem!).Name == "费列聂", "Country names did not translate.");
            window.SetLanguage("en"); Dispatcher.UIThread.RunJobs();
            Check(((MainWindow.DonationChoice)countries.SelectedItem!).Name == "Firene" && amount.Text == "89999"
                && levels.SelectedIndex == 3, "Language switching discarded pending donations.");
        }
        Check(window.MaximizeDonations(false), "Single-country max failed.");
        Check(window.Save!.ReadDonations().Select(row => row.Amount).SequenceEqual(new[] { 90000, 15000, 90000, 1_000_000 }),
            "Single-country max changed other countries.");
        Check(window.MaximizeDonations(true) && window.Save.ReadDonations().All(row => row.Amount == 90000), "All-country max failed.");
        Check(window.LoadSave(source), "Could not reload donation fixture.");
        amount.Text = "12345";
        countries.SelectedIndex = 1;
        amount.Text = "45000";
        window.FindControl<TextBox>("SommieNameInput")!.Text = "Donation round-trip 索拉";
        string output = Path.Combine(temporary, "donation-gui-copy");
        Check(window.SaveCopy(output), "Save Copy did not apply pending donations.");
        var saved = EngageSave.Load(output);
        Check(saved.ReadDonations().Select(row => row.Amount).SequenceEqual(new[] { 12345, 45000, 90000, 1_000_000 }),
            "Pending country edits were not saved together.");
        Check(saved.ReadMainValues().SommieName == "Donation round-trip 索拉", "Donation saving lost Main name resizing.");
        Check(window.LoadSave(source), "Could not restore donation GUI fixture.");
        byte[] before = window.Save!.Serialize();
        window.FindControl<NumericUpDown>("MoneyInput")!.Value = 777;
        foreach (string invalid in new[] { "", "abc", "-1", "10000000", "1.5", "2147483648" })
        {
            amount.Text = invalid;
            Check(!window.ApplyMainValues(), "Invalid donation amount was accepted.");
            Check(window.Save.Serialize().AsSpan().SequenceEqual(before), "Invalid donation partially committed Main fields.");
        }
        amount.Text = "";
        countries.SelectedIndex = 2;
        Check(countries.SelectedIndex == 0 && amount.Text == "", "Switching country silently discarded invalid input.");
        string rejected = Path.Combine(temporary, "donation-gui-rejected");
        Check(!window.SaveCopy(rejected) && !File.Exists(rejected), "Invalid donation created an output.");
        string bad = Path.Combine(temporary, "donation-gui-malformed");
        File.WriteAllBytes(bad, DonationTests.Fixture("duplicate"));
        Check(window.LoadSave(bad) && !window.CanEditDonations && window.CanEditMain, "Malformed donation data disabled unrelated Main editing.");
        Check(amount.Value is null && !window.FindControl<StackPanel>("DonationInputs")!.IsEnabled,
            "Malformed donation data left stale editable controls.");
        Check(File.ReadAllBytes(source).AsSpan().SequenceEqual(original), "Donation GUI tests overwrote the source.");
        Console.WriteLine("Donation GUI: linked fields, pending countries/languages, max scopes and atomic validation passed.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
